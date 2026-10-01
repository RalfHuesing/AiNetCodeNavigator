using System.IO;
using System.Linq;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Mcp.Tools;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Mcp.Tools.Relationships;

/// <summary>Creates generation-bound assembly handoffs for declarations backed by the owner's source.</summary>
internal static class AssemblyHandoffFormatting
{
    internal static Func<ISymbol, string?> CreateInternal(AssemblyNavigationSessionScope ownerScope) =>
        CreateInternal(ownerScope.Solution, ownerScope.Context);

    internal static Func<ISymbol, string?> CreateInternal(Solution solution, AssemblyContext context) =>
        CreateInternal(solution, context.Origin.CanonicalPath, context.Origin.ContentHash, context.Generation,
            context.ReferenceSnapshotHash, context.DecompiledProjectPaths?.DecompiledSourceRoot,
            context.Assembly, context.Compilation);

    internal static Func<ISymbol, string?> CreateInternal(Solution solution, string canonicalPath, string contentHash,
        long generation, string referenceSnapshotHash, string? sourceRoot, IAssemblySymbol assembly, Compilation compilation)
    {
        var identity = AnalysisSymbolIdentity.ForAssembly(canonicalPath, contentHash, generation, referenceSnapshotHash);
        return symbol =>
        {
            if (!HasSourceDeclaration(symbol, solution, sourceRoot)) return null;
            var declarationId = DocumentationCommentId.CreateDeclarationId(symbol);
            if (string.IsNullOrWhiteSpace(declarationId)) return null;
            var owned = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, compilation)
                .Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, assembly))
                .Distinct(SymbolEqualityComparer.Default).Take(2).ToArray();
            if (owned.Length != 1) return null;
            return identity.FormatHandoff(owned[0]);
        };
    }

    internal static string? Externalize(string? internalHandoff) =>
        string.IsNullOrWhiteSpace(internalHandoff)
            ? null
            : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalHandoff);

    internal static bool HasSourceDeclaration(ISymbol symbol, Solution solution, string? sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot)) return false;
        var root = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var location in symbol.Locations.Where(item => item.IsInSource && item.SourceTree is not null))
        {
            var document = solution.GetDocument(location.SourceTree!);
            if (document?.FilePath is not { } filePath) continue;
            var fullPath = Path.GetFullPath(filePath);
            if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

}
