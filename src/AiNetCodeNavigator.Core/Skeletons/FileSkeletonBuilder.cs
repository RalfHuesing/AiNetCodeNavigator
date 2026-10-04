#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using AiNetCodeNavigator.Core.Symbols;

namespace AiNetCodeNavigator.Core.Skeletons;

/// <summary>
/// Convenient entry point for creating file and project skeletons as Markdown.
/// </summary>
public static class FileSkeletonBuilder
{
    public static async Task<string> BuildMarkdownForDocumentAsync(
        Document document,
        string solutionPath,
        Func<string?, string?>? formatSymbolId = null,
        CancellationToken ct = default,
        Func<ISymbol, string?>? formatSymbol = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(solutionPath);

        var solutionDir = Path.GetDirectoryName(solutionPath) ?? string.Empty;
        if (formatSymbolId is null && formatSymbol is null)
        {
            formatSymbol = await SourceReferenceFormattingContext.CreateFormatterAsync(document.Project.Solution, ct).ConfigureAwait(false);
        }

        var types = await SkeletonMapBuilder.BuildForDocumentAsync(document, solutionDir, formatSymbolId, formatSymbol, ct).ConfigureAwait(false);
        return SkeletonMarkdownRenderer.Render(types, solutionPath);
    }
}
