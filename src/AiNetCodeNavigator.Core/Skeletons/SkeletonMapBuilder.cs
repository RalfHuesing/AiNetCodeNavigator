#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Skeletons;

/// <summary>
/// Creates type skeletons for individual documents or entire projects.
/// </summary>
public static class SkeletonMapBuilder
{
    public static async Task<IReadOnlyList<SkeletonTypeInfo>> BuildForDocumentAsync(
        Document document,
        string solutionDir,
        Func<string?, string?>? formatSymbolId = null,
        Func<ISymbol, string?>? formatSymbol = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(solutionDir);

        var syntaxTree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (syntaxTree is null) return Array.Empty<SkeletonTypeInfo>();

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel is null) return Array.Empty<SkeletonTypeInfo>();

        var relativePath = PathNormalizer.ToRelative(solutionDir, document.FilePath ?? document.Name);
        var walker = new SkeletonSyntaxWalker(semanticModel, relativePath, formatSymbolId, formatSymbol);

        var root = await syntaxTree.GetRootAsync(ct).ConfigureAwait(false);
        walker.Visit(root);

        return walker.Types;
    }

    public static async Task<IReadOnlyList<SkeletonTypeInfo>> BuildForProjectAsync(
        Project project,
        string solutionDir,
        Func<string?, string?>? formatSymbolId = null,
        Func<ISymbol, string?>? formatSymbol = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(solutionDir);

        var result = new List<SkeletonTypeInfo>();
        foreach (var doc in project.Documents)
        {
            ct.ThrowIfCancellationRequested();
            var types = await BuildForDocumentAsync(doc, solutionDir, formatSymbolId, formatSymbol, ct).ConfigureAwait(false);
            result.AddRange(types);
        }

        return result;
    }
}
