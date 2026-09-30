#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using AiNetCodeNavigator.Core.Symbols;

namespace AiNetCodeNavigator.Core.Skeletons;

/// <summary>
/// Bequemer Einstiegspunkt zur Erstellung von Datei- und Projekt-Skeletons als Markdown.
/// </summary>
public static class FileSkeletonBuilder
{
    public static async Task<string> BuildMarkdownForDocumentAsync(
        Document document,
        string solutionPath,
        Func<string?, string?>? formatSymbolId = null,
        CancellationToken ct = default)
    {
        var solutionDir = Path.GetDirectoryName(solutionPath) ?? string.Empty;
        Func<ISymbol, string?>? formatSymbol = null;
        if (formatSymbolId is null)
        {
            var identity = await AnalysisSymbolIdentity.ForSourceAsync(document.Project.Solution, ct).ConfigureAwait(false);
            if (identity is not null)
            {
                formatSymbol = symbol =>
                {
                    var internalId = identity.FormatHandoff(symbol, document.Project.Solution);
                    return internalId is null ? null : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalId);
                };
            }
        }

        var types = await SkeletonMapBuilder.BuildForDocumentAsync(document, solutionDir, formatSymbolId, formatSymbol, ct).ConfigureAwait(false);
        return SkeletonMarkdownRenderer.Render(types, solutionPath);
    }
}
