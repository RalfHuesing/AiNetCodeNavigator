namespace AiNetCodeNavigator.AssemblyExport;

internal static class ExportFilenamePattern
{
    internal static bool IsValid(string pattern) => !string.IsNullOrWhiteSpace(pattern)
        && !pattern.Contains("**", StringComparison.Ordinal)
        && pattern.IndexOfAny(['/', '\\', ':', '<', '>', '|', '"']) < 0
        && !pattern.Any(char.IsControl);
}
