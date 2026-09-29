#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

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
        var types = await SkeletonMapBuilder.BuildForDocumentAsync(document, solutionDir, formatSymbolId, ct).ConfigureAwait(false);
        return SkeletonMarkdownRenderer.Render(types, solutionPath);
    }
}
