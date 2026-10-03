using System.IO;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Assemblies;

/// <summary>Searches a managed assembly and, when requested, its resolved references while preserving each binary's origin.</summary>
public static class AssemblyFindSymbolScanner
{
    public static async Task<FindSymbolScanResult> FindAsync(
        string assemblyPath,
        string namePattern,
        SymbolKindFilter kind = SymbolKindFilter.All,
        SymbolScopeType scope = SymbolScopeType.All,
        int maxResults = 50,
        bool includeReferences = false,
        CancellationToken cancellationToken = default,
        AssemblyNavigationSessionScope? pinnedRootScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(namePattern);
        AssemblyNavigationSessionScope? ownedRootScope = null;
        if (pinnedRootScope is null)
        {
            var opened = await AssemblyNavigationSessionScope.OpenAsync(assemblyPath, cancellationToken).ConfigureAwait(false);
            if (!opened.IsSuccess)
            {
                return Failure(opened.Error!.Value);
            }

            ownedRootScope = opened.Value!;
        }

        await using var ownedRootLease = ownedRootScope;
        var rootScope = pinnedRootScope ?? ownedRootScope!;
        if (!string.Equals(
                Path.GetFullPath(rootScope.Context.Origin.CanonicalPath),
                Path.GetFullPath(assemblyPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure(new ResultError(
                NavigationErrorCodes.TargetMismatch,
                "The pinned assembly scope belongs to a different root target.",
                "Acquire the analysis scope for the requested assembly path."));
        }

        var rootContext = rootScope.Context;
        var candidates = new List<string> { rootContext.Origin.CanonicalPath };
        var incompleteReferences = false;
        if (includeReferences)
        {
            incompleteReferences = rootContext.References.Any(reference =>
                !reference.Resolved || string.IsNullOrWhiteSpace(reference.ResolvedPath)
                || reference.ResolutionState is "depth_limit" or "invalid");
            candidates.AddRange(rootContext.References
                .Where(reference => reference.Resolved && !string.IsNullOrWhiteSpace(reference.ResolvedPath))
                .Select(reference => Path.GetFullPath(reference.ResolvedPath!))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        var entries = new List<SymbolLocationEntry>();
        var kindAlternatives = new SortedSet<string>(StringComparer.Ordinal);
        var nameFound = false;
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(candidate, rootContext.Origin.CanonicalPath, StringComparison.OrdinalIgnoreCase))
            {
                var rootProject = rootScope.Solution.Projects.FirstOrDefault();
                var rootIsTest = rootProject is not null
                    && TestDetector.IsTestProject(rootProject, classificationPath: rootContext.Origin.CanonicalPath);
                if (!(scope == SymbolScopeType.Tests && !rootIsTest || scope == SymbolScopeType.Production && rootIsTest))
                {
                    ScanContext(rootContext, candidate);
                }

                continue;
            }

            var result = await AssemblyNavigationSessionScope.OpenAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                var openError = result.Error!.Value;
                return Failure(new ResultError(
                    openError.Code,
                    $"A referenced assembly could not be scanned: {openError.Message}",
                    openError.Hint));
            }
            await using var scopeAccess = result.Value!;
            var context = scopeAccess.Context;
            var ownerStatus = AssemblyReferenceSnapshotValidator.ValidateOwner(
                rootContext, candidate, context, out var staleError);
            if (ownerStatus == AssemblyReferenceSnapshotValidator.OwnerValidationStatus.Stale)
                return Failure(staleError!.Value);
            if (ownerStatus == AssemblyReferenceSnapshotValidator.OwnerValidationStatus.Incomplete)
            {
                incompleteReferences = true;
                continue;
            }

            var project = scopeAccess.Solution.Projects.FirstOrDefault();
            var isTest = project is not null
                && TestDetector.IsTestProject(project, classificationPath: context.Origin.CanonicalPath);
            if (scope == SymbolScopeType.Tests && !isTest || scope == SymbolScopeType.Production && isTest) continue;

            ScanContext(context, candidate);
        }

        void ScanContext(AssemblyContext context, string candidatePath)
        {

            var identity = AnalysisSymbolIdentity.ForAssembly(
                context.Origin.CanonicalPath,
                context.Origin.ContentHash,
                context.Generation,
                context.ReferenceSnapshotHash);
            foreach (var symbol in EnumerateSymbols(context.Assembly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SymbolNameMatcher.MatchesSymbol(symbol, namePattern)) continue;
                nameFound = true;
                var symbolKind = DescribeKind(symbol);
                if (kind != SymbolKindFilter.All && !MatchesKind(symbol, kind))
                {
                    kindAlternatives.Add(symbolKind);
                    continue;
                }

                var locations = symbol.Locations
                    .Where(location => location.IsInSource && location.SourceTree is not null)
                    .Select(location =>
                    {
                        var span = location.GetLineSpan();
                        var relativePath = PathNormalizer.ToRelative(Path.GetDirectoryName(context.Origin.CanonicalPath) ?? string.Empty, span.Path);
                        return new SymbolLocationItem(relativePath, span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1, context.Identity?.Name ?? Path.GetFileNameWithoutExtension(candidatePath));
                    })
                    .Distinct()
                    .OrderBy(location => location.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(location => location.Line)
                    .ToArray();
                if (locations.Length == 0) continue;

                var internalId = identity.FormatHandoff(symbol);
                var handoff = internalId is null ? null : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalId);
                entries.Add(new SymbolLocationEntry(
                    symbol.Name,
                    symbolKind,
                    symbol.GetDocumentationCommentId(),
                    handoff,
                    locations[0].FilePath,
                    locations[0].Line,
                    locations[0].EndLine,
                    locations[0].ProjectName,
                    symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    locations,
                    context.Origin.CanonicalPath));
            }
        }

        var ordered = entries
            .DistinctBy(entry => entry.HandoffId ?? $"{entry.ProjectName}|{entry.FilePath}|{entry.Line}|{entry.Signature}", StringComparer.Ordinal)
            .OrderBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Line)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();
        if (!nameFound)
        {
            return new FindSymbolScanResult(
                $"No symbols matched '{namePattern}' in the selected assembly scope.",
                [],
                0,
                0,
                incompleteReferences,
                incompleteReferences ? ["unresolvedReferences"] : [],
                []);
        }

        var filtered = kind == SymbolKindFilter.All ? ordered : ordered.Where(entry => KindNameMatches(entry.Kind, kind)).ToList();
        if (filtered.Count == 0)
        {
            return new FindSymbolScanResult($"No symbols matched '{namePattern}' and kind '{kind}'.", [], 0, 0, false, [], kindAlternatives.ToArray());
        }

        var limit = Math.Max(1, maxResults);
        var shown = filtered.Take(limit).ToArray();
        return new FindSymbolScanResult(
            $"Found {filtered.Count} matching assembly symbol(s); returned {shown.Length}.",
            shown,
            filtered.Count,
            shown.Length,
            filtered.Count > shown.Length || incompleteReferences,
            filtered.Count > shown.Length
                ? incompleteReferences ? ["maxResults", "unresolvedReferences"] : ["maxResults"]
                : incompleteReferences ? ["unresolvedReferences"] : [],
            kindAlternatives.ToArray());
    }

    private static FindSymbolScanResult Failure(ResultError error) =>
        new(error.Message, [], 0, 0, false, [], [], error);

    private static IEnumerable<ISymbol> EnumerateSymbols(IAssemblySymbol assembly)
    {
        foreach (var type in AssemblyAnalysisSymbolTraversal.GetAllTypes(assembly.GlobalNamespace))
        {
            yield return type;
            foreach (var member in type.GetMembers())
            {
                if (!member.IsImplicitlyDeclared) yield return member;
            }
        }
    }

    private static bool MatchesKind(ISymbol symbol, SymbolKindFilter kind) => kind switch
    {
        SymbolKindFilter.Class => symbol is INamedTypeSymbol { TypeKind: TypeKind.Class, IsRecord: false },
        SymbolKindFilter.Record or SymbolKindFilter.RecordClass => symbol is INamedTypeSymbol { IsRecord: true, TypeKind: TypeKind.Class },
        SymbolKindFilter.RecordStruct => symbol is INamedTypeSymbol { IsRecord: true, TypeKind: TypeKind.Struct },
        SymbolKindFilter.Struct => symbol is INamedTypeSymbol { TypeKind: TypeKind.Struct, IsRecord: false },
        SymbolKindFilter.Interface => symbol is INamedTypeSymbol { TypeKind: TypeKind.Interface },
        SymbolKindFilter.Enum => symbol is INamedTypeSymbol { TypeKind: TypeKind.Enum },
        SymbolKindFilter.Delegate => symbol is INamedTypeSymbol { TypeKind: TypeKind.Delegate },
        SymbolKindFilter.Method => symbol is IMethodSymbol,
        SymbolKindFilter.Property => symbol is IPropertySymbol,
        SymbolKindFilter.Field => symbol is IFieldSymbol,
        SymbolKindFilter.Event => symbol is IEventSymbol,
        _ => true,
    };

    private static bool KindNameMatches(string name, SymbolKindFilter kind) => kind switch
    {
        SymbolKindFilter.Record or SymbolKindFilter.RecordClass => name is "record class" or "record struct",
        SymbolKindFilter.RecordStruct => name == "record struct",
        SymbolKindFilter.Class => name == "class",
        SymbolKindFilter.Struct => name == "struct",
        SymbolKindFilter.Interface => name == "interface",
        SymbolKindFilter.Enum => name == "enum",
        SymbolKindFilter.Delegate => name == "delegate",
        SymbolKindFilter.Method => name == "method",
        SymbolKindFilter.Property => name == "property",
        SymbolKindFilter.Field => name == "field",
        SymbolKindFilter.Event => name == "event",
        _ => true,
    };

    private static string DescribeKind(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
        INamedTypeSymbol { IsRecord: true } => "record class",
        INamedTypeSymbol type => type.TypeKind.ToString().ToLowerInvariant(),
        IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => "constructor",
        IMethodSymbol => "method",
        IPropertySymbol => "property",
        IFieldSymbol => "field",
        IEventSymbol => "event",
        _ => symbol.Kind.ToString().ToLowerInvariant(),
    };
}
