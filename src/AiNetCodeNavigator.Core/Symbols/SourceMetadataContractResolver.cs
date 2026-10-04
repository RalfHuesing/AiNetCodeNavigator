#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Core.Models;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Resolves metadata declarations only against references paired with the validated immutable solution.
/// The result is request-lived: occurrences retain their actual compilation/reference, never just an assembly name.
/// Source-first routing and relationship traversal belong to the caller.
/// </summary>
internal static class SourceMetadataContractResolver
{
    internal static async Task<SourceMetadataContractResolution> ResolveAsync(
        SourceIdentityValidatedSnapshot snapshot, string identifier, string? metadataOwnerPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var requested = identifier.Trim();
        if (!IsSupportedIdentifier(requested))
            return Failure(NavigationErrorCodes.SymbolNotFound, "Use a qualified metadata type name or an exact T:/M:/P:/E: declaration ID.");
        if (metadataOwnerPath is not null && !IsCanonicalDllPath(metadataOwnerPath))
            return Failure(NavigationErrorCodes.InvalidArgument, "metadataOwnerPath must be an absolute canonical DLL path from the loaded metadata candidates.", "metadataOwnerPath");

        var occurrences = new List<SourceMetadataContractOccurrence>();
        var evidence = snapshot.Inputs.MetadataReferences.ToDictionary(item => (item.OwnerProjectId, item.ReferenceOrdinal));
        var selectedOwnerIsLoaded = false;
        foreach (var project in snapshot.Solution.Projects.Where(project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
                return Failure(NavigationErrorCodes.WorkspaceDiagnostic, "A loaded C# project has no available compilation for metadata lookup.");
            var ordinal = 0;
            foreach (var reference in project.MetadataReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!evidence.TryGetValue((project.Id, ordinal++), out var captured) || captured.Images.IsDefaultOrEmpty)
                    return Failure(NavigationErrorCodes.WorkspaceDiagnostic, "The loaded metadata reference has no paired captured image evidence.");
                var ownerPath = GetProvenOwnerPath(reference, captured.Images);
                var selected = metadataOwnerPath is null || PathComparer.Equals(ownerPath, metadataOwnerPath);
                if (metadataOwnerPath is not null && selected) selectedOwnerIsLoaded = true;
                if (!selected) continue;
                var assembly = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                var isBoundInProject = assembly is not null && ReferenceEquals(compilation.GetMetadataReference(assembly), reference);
                var ownerCompilation = compilation;
                if (!isBoundInProject)
                {
                    // Roslyn can suppress a second reference with an equal assembly identity. Inspect that captured
                    // image independently, but never present this lookup-only symbol as a project binding proof.
                    ownerCompilation = compilation.RemoveAllReferences().AddReferences(reference);
                    assembly = ownerCompilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                }
                if (assembly is null) continue;
                foreach (var symbol in ResolveInOwner(assembly, requested))
                {
                    var declarationId = DocumentationCommentId.CreateDeclarationId(symbol);
                    if (declarationId is null || !ReferenceEquals(symbol.ContainingAssembly, assembly)) continue;
                    occurrences.Add(new SourceMetadataContractOccurrence(project.Id, compilation, ownerCompilation, reference,
                        captured.ReferenceOrdinal, assembly, symbol, captured.Images, ownerPath, declarationId, isBoundInProject));
                }
            }
        }
        if (metadataOwnerPath is not null && !selectedOwnerIsLoaded)
            return Failure(NavigationErrorCodes.InvalidArgument, "metadataOwnerPath is not a proven DLL owner in the currently loaded reference set.", "metadataOwnerPath");

        // Image keys include physical paths (or captured in-memory hashes). Assembly identity alone is insufficient.
        var candidates = occurrences.GroupBy(OccurrenceKey, StringComparer.Ordinal)
            .Select(group => new SourceMetadataContractCandidate(group.First().Assembly.Identity.ToString(),
                group.First().DeclarationId, group.First().OwnerPath, group.ToImmutableArray()))
            .OrderBy(candidate => candidate.OwnerPath, PathComparer)
            .ThenBy(candidate => candidate.AssemblyIdentity, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.DeclarationId, StringComparer.Ordinal).ToImmutableArray();
        return candidates.Length switch
        {
            0 => Failure(NavigationErrorCodes.SymbolNotFound, "The exact metadata declaration was not found in the loaded references."),
            1 => new(candidates, null, null),
            _ => new(candidates, new ResultError(NavigationErrorCodes.AmbiguousSymbol,
                "Multiple loaded metadata owners contain this exact declaration.",
                "Repeat the same raw identifier with one returned proven metadataOwnerPath; unproven owners cannot be selected by guessing."), null),
        };
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static SourceMetadataContractResolution Failure(string code, string message, string? argument = null) =>
        new([], new ResultError(code, message), argument);

    internal static bool IsSupportedIdentifier(string identifier) =>
        identifier.Length > 2 && identifier[1] == ':' && identifier[0] is 'T' or 'M' or 'P' or 'E'
        || !identifier.Contains(':') && identifier.Contains('.') && !identifier.Any(char.IsWhiteSpace);

    private static bool IsCanonicalDllPath(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path) && Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)
                && PathComparer.Equals(Path.GetFullPath(path), path);
        }
        catch (ArgumentException) { return false; }
    }

    private static string? GetProvenOwnerPath(MetadataReference reference, ImmutableArray<SourceIdentityImageEvidence> images)
    {
        if (reference is not PortableExecutableReference { FilePath: { } path } || !IsCanonicalDllPath(path)) return null;
        var key = Path.GetFullPath(path).Replace('\\', '/');
        if (OperatingSystem.IsWindows()) key = key.ToUpperInvariant();
        return StringComparer.Ordinal.Equals(images[0].CanonicalImageKey, key) ? Path.GetFullPath(path) : null;
    }

    private static string OccurrenceKey(SourceMetadataContractOccurrence occurrence) =>
        occurrence.Assembly.Identity + "\0" + occurrence.DeclarationId + "\0" +
        string.Join("\0", occurrence.Images.Select(image => image.CanonicalImageKey + "\0" + image.Sha256));

    private static IEnumerable<ISymbol> ResolveInOwner(IAssemblySymbol assembly, string requested)
    {
        var isId = requested.Length > 2 && requested[1] == ':';
        var name = isId ? requested[2..] : requested;
        if (!isId || requested[0] == 'T')
        {
            foreach (var type in FindTypes(assembly, name))
                if (!isId || DocumentationCommentId.CreateDeclarationId(type) == requested) yield return type;
            yield break;
        }

        // Locate the declaring type using prefixes of the ID stem, then compare Roslyn's complete ID.
        // Parameter/return-type dots are never treated as declaring-type separators. No overload name guess is used.
        var end = name.IndexOfAny(['(', '~']);
        var stem = end < 0 ? name : name[..end];
        for (var separator = stem.LastIndexOf('.'); separator > 0; separator = stem.LastIndexOf('.', separator - 1))
            foreach (var type in FindTypes(assembly, stem[..separator]))
                foreach (var member in type.GetMembers())
                    if (DocumentationCommentId.CreateDeclarationId(member) == requested) yield return member;
    }

    private static IEnumerable<INamedTypeSymbol> FindTypes(IAssemblySymbol assembly, string name)
    {
        var normalized = ResolveTypeOriginScanner.NormalizeMetadataName(name);
        var exact = assembly.GetTypeByMetadataName(normalized);
        if (exact is not null) { yield return exact; yield break; }
        var nested = new List<ITypeSymbol>();
        ResolveTypeOriginScanner.TryAddNestedMatches(assembly, normalized, nested);
        foreach (var type in nested.OfType<INamedTypeSymbol>()) yield return type;
    }
}

internal sealed record SourceMetadataContractResolution(
    ImmutableArray<SourceMetadataContractCandidate> Candidates, ResultError? Error, string? ArgumentName)
{
    internal bool IsSuccess => Error is null;
    internal SourceMetadataContractCandidate? Selected => IsSuccess && Candidates.Length == 1 ? Candidates[0] : null;
}

internal sealed record SourceMetadataContractCandidate(string AssemblyIdentity, string DeclarationId, string? OwnerPath,
    ImmutableArray<SourceMetadataContractOccurrence> Occurrences);

/// <summary>
/// ProjectCompilation is the actual loaded source binding. OwnerCompilation may instead be a lookup-only clone;
/// when IsBoundInProject is false, Symbol proves a loaded owner declaration, never a source implementation relationship.
/// </summary>
internal sealed record SourceMetadataContractOccurrence(ProjectId ProjectId, Compilation ProjectCompilation, Compilation OwnerCompilation,
    MetadataReference Reference, int ReferenceOrdinal, IAssemblySymbol Assembly, ISymbol Symbol,
    ImmutableArray<SourceIdentityImageEvidence> Images, string? OwnerPath, string DeclarationId, bool IsBoundInProject);
