#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using AiNetCodeNavigator.Core.Common;

namespace AiNetCodeNavigator.Core.FileStructure;

/// <summary>
/// Filtert Dateien und Ordner anhand von Globs, Regexen und Standard-Ausschlüssen (bin, obj, .git usw.).
/// </summary>
public static class FileTreeFilter
{
    private static readonly HashSet<string> DefaultExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules", "TestResults", "artifacts", "temp"
    };

    public static bool IsExcludedDirectory(string dirName) =>
        DefaultExcludedDirectories.Contains(dirName);

    public static bool MatchesFileFilter(string fileName, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || filter == "*") return true;

        if (RegexAutoDetector.TryCreateFilterRegex(filter, out var regex, out var isNegated, out _))
        {
            if (regex is null) return true;
            var isMatch = regex.IsMatch(fileName);
            return isNegated ? !isMatch : isMatch;
        }

        return fileName.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesFolderFilter(string folderPath, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || filter == "*") return true;

        var normalized = PathNormalizer.NormalizeSeparators(folderPath);

        if (RegexAutoDetector.TryCreateFilterRegex(filter, out var regex, out var isNegated, out _))
        {
            if (regex is null) return true;
            var isMatch = regex.IsMatch(normalized);
            return isNegated ? !isMatch : isMatch;
        }

        return normalized.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
