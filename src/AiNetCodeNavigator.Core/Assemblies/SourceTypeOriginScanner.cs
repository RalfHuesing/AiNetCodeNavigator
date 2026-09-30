#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Assemblies;

/// <summary>Resolves a source or metadata type origin without losing the owning project identity.</summary>
public static class SourceTypeOriginScanner
{
    public const int MaxCandidates = 100;

    public static async Task<Result<SourceTypeOriginPayload>> ResolveAsync(
        Solution solution,
        string targetPath,
        ISymbol? sourceSymbol,
        string? typeName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        if ((sourceSymbol is null) == string.IsNullOrWhiteSpace(typeName))
            return Result<SourceTypeOriginPayload>.Failure(NavigationErrorCodes.InvalidArgument,
                "Specify exactly one resolved source symbol or type name.");

        var symbolType = sourceSymbol as INamedTypeSymbol ?? sourceSymbol?.ContainingType;
        if (symbolType is not null)
        {
            var locations = new List<SourceTypeOriginLocation>();
            var owningProjects = new HashSet<ProjectId>();
            foreach (var location in symbolType.Locations.Where(location => location.IsInSource && location.SourceTree is not null
                         && !string.IsNullOrWhiteSpace(location.SourceTree.FilePath)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = solution.GetDocument(location.SourceTree!);
                if (document is null || document.Project.AssemblyName != symbolType.ContainingAssembly?.Identity.Name) continue;
                owningProjects.Add(document.Project.Id);
                var lineSpan = location.GetLineSpan();
                var sourceFilePath = Path.GetFullPath(location.SourceTree!.FilePath);
                locations.Add(new SourceTypeOriginLocation(sourceFilePath, lineSpan.StartLinePosition.Line + 1, lineSpan.StartLinePosition.Character + 1));
            }

            if (locations.Count > 0 && owningProjects.Count == 1)
            {
                var project = solution.GetProject(owningProjects.Single());
                return Result<SourceTypeOriginPayload>.Success(new SourceTypeOriginPayload(
                    symbolType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), true,
                    Path.GetFullPath(targetPath), project?.Name, locations
                        .DistinctBy(location => (location.FilePath, location.Line, location.Column))
                        .OrderBy(location => location.FilePath, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(location => location.Line).ThenBy(location => location.Column).ToArray(),
                    "source", project?.OutputFilePath, symbolType.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                    [symbolType.ContainingAssembly?.Identity.Name ?? string.Empty]));
            }
        }

        var requestedName = typeName ?? sourceSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? string.Empty;
        var normalized = requestedName.Trim().Replace("global::", string.Empty, StringComparison.Ordinal);
        var searched = new List<string>();
        var candidates = new Dictionary<string, (ITypeSymbol Type, string? Path)> (StringComparer.Ordinal);
        foreach (var project in solution.Projects.OrderBy(project => project.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null) continue;
            foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                searched.Add(assembly.Identity.Name);
                foreach (var type in FindTypes(assembly, normalized))
                {
                    var paths = compilation.References.OfType<PortableExecutableReference>()
                        .Where(reference => reference.FilePath is not null)
                        .Where(reference => compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol referenced
                            && referenced.Identity.Equals(type.ContainingAssembly?.Identity))
                        .Select(reference => Path.GetFullPath(reference.FilePath!))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var key = $"{type.ContainingAssembly?.Identity}|{type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}";
                    var candidatePath = paths.Length == 1 ? paths[0] : null;
                    if (!candidates.TryGetValue(key, out var existing)) candidates.Add(key, (type, candidatePath));
                    else if (candidatePath is null || !string.Equals(existing.Path, candidatePath, StringComparison.OrdinalIgnoreCase))
                        candidates[key] = (existing.Type, null);
                }
            }
        }

        var candidatePaths = candidates.Values.Select(candidate => candidate.Path)
            .Where(path => path is not null).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var isAmbiguous = candidates.Count > 1 || candidates.Values.Any(candidate => candidate.Path is null);
        if (candidates.Count == 0)
        {
            return Result<SourceTypeOriginPayload>.Success(new SourceTypeOriginPayload(
                requestedName.Trim(), false, Path.GetFullPath(targetPath), null, Array.Empty<SourceTypeOriginLocation>(),
                null, null, string.Empty, searched.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray()));
        }

        if (isAmbiguous)
        {
            return Result<SourceTypeOriginPayload>.Success(new SourceTypeOriginPayload(
                requestedName.Trim(), true, Path.GetFullPath(targetPath), null, Array.Empty<SourceTypeOriginLocation>(),
                "ambiguous", null, string.Empty, searched.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                true, candidatePaths.Take(MaxCandidates).ToArray()));
        }

        var candidate = candidates.Values.Single();
        var path = candidate.Path!;
        return Result<SourceTypeOriginPayload>.Success(new SourceTypeOriginPayload(
            candidate.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), true, Path.GetFullPath(targetPath), null,
            Array.Empty<SourceTypeOriginLocation>(), "reference",
            path, candidate.Type.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            searched.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            CandidatePaths: candidatePaths));
    }

    private static IReadOnlyList<ITypeSymbol> FindTypes(IAssemblySymbol assembly, string name)
    {
        var metadataName = name.StartsWith("T:", StringComparison.Ordinal) ? name[2..] : name;
        var exact = assembly.GetTypeByMetadataName(metadataName);
        if (exact is not null) return [exact];
        if (metadataName.Contains('.')) return Array.Empty<ITypeSymbol>();
        return AssemblyAnalysisSymbolTraversal.GetAllTypes(assembly.GlobalNamespace)
            .Where(type => string.Equals(type.Name, metadataName, StringComparison.Ordinal))
            .OrderBy(type => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
            .Cast<ITypeSymbol>()
            .Take(MaxCandidates)
            .ToArray();
    }

}
