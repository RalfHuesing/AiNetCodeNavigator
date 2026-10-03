using System;
using System.IO;
using System.Linq;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Symbols;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Mcp.Tools.Relationships;

/// <summary>Formats exact assembly references for declarations in a leased owner context.</summary>
internal static class AssemblyReferenceFormatting
{
    internal static Func<ISymbol, string?> Create(AssemblyNavigationSessionScope ownerScope) =>
        Create(ownerScope.Solution, ownerScope.Context);

    internal static Func<ISymbol, string?> Create(Solution solution, AssemblyContext context) => symbol =>
    {
        if (!HasSourceDeclaration(symbol, solution, context.DecompiledProjectPaths?.DecompiledSourceRoot)) return null;
        var reference = ExactAssemblySymbolResolver.CreateReference(
            context.Identity?.Name ?? context.Assembly.Identity.Name,
            context.Assembly,
            context.Compilation,
            symbol);
        return reference.IsSuccess ? StableSymbolReferenceCodec.Format(reference.Value!) : null;
    };

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
