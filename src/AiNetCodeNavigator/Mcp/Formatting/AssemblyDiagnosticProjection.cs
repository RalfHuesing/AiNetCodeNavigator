namespace AiNetCodeNavigator.Mcp.Formatting;

internal static class AssemblyDiagnosticProjection
{
    internal static IReadOnlyList<string> Project(IReadOnlyList<string> diagnostics, bool includeDiagnostics)
    {
        if (includeDiagnostics || diagnostics.Count == 0) return diagnostics;
        return diagnostics
            .GroupBy(DiagnosticCategory, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}: {group.Count()}")
            .ToArray();
    }

    private static string DiagnosticCategory(string diagnostic)
    {
        var value = diagnostic.ToLowerInvariant();
        if (value.Contains("incompleterelationships", StringComparison.Ordinal)
            || value.Contains("incomplete relationship", StringComparison.Ordinal)
            || value.Contains("relationship closure", StringComparison.Ordinal)) return "incompleteRelationships";
        if (value.Contains("version_mismatch", StringComparison.Ordinal)
            || value.Contains("version mismatch", StringComparison.Ordinal)
            || value.Contains("version conflict", StringComparison.Ordinal)) return "versionConflict";
        if (value.Contains("closure", StringComparison.Ordinal) || value.Contains("limit", StringComparison.Ordinal)) return "analysisLimit";
        if (value.Contains("reference", StringComparison.Ordinal) || value.Contains("resolve", StringComparison.Ordinal)) return "unresolvedReference";
        if (value.Contains("decompil", StringComparison.Ordinal)) return "decompilation";
        return "navigation";
    }

}
