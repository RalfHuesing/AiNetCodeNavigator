#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Hierarchy;

/// <summary>
/// Service für Typ-Hierarchien und formatierte Ausgabe.
/// </summary>
public static class TypeHierarchyService
{
    public static async Task<string> GetFormattedHierarchyAsync(
        INamedTypeSymbol type,
        Solution solution,
        int maxResults = 50,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(solution);
        var payload = await TypeHierarchyScanner.ScanAsync(type, solution, maxResults, ct).ConfigureAwait(false);
        return GetTypeHierarchyFormatter.FormatText(payload);
    }
}
