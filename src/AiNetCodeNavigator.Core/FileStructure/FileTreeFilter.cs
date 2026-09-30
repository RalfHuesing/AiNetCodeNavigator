#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AiNetCodeNavigator.Core.Common;

namespace AiNetCodeNavigator.Core.FileStructure;

/// <summary>Validates and applies relative file tree filters.</summary>
public static class FileTreeFilter
{
    private static readonly HashSet<string> DefaultExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", ".vs", ".idea", "obj", "bin", "node_modules", "worktrees", ".worktrees",
        "TestResults", "artifacts", "coverage", "temp", "packages",
    };

    public static bool IsExcludedDirectory(string dirName) => DefaultExcludedDirectories.Contains(dirName);

    public static string[] NormalizeExtensions(IReadOnlyList<string>? extensions)
    {
        if (extensions is null || extensions.Count == 0 || extensions.Any(extension => extension.Trim() == "*")) return [];
        return extensions.Select(extension => extension.Trim().ToLowerInvariant())
            .Select(extension => extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool MatchesFileFilter(string fileName, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || filter == "*") return true;
        var target = filter.Contains('/') || filter.Contains('\\') ? fileName.Replace('\\', '/') : Path.GetFileName(fileName);
        return MatchesGlob(target, filter);
    }

    public static bool MatchesFolderFilter(string folderPath, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || filter == "*") return true;
        var normalized = PathNormalizer.NormalizeSeparators(folderPath);
        return filter.Contains('*') || filter.Contains('?')
            ? MatchesGlob(normalized, filter)
            : normalized.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool Matches(string fullPath, string relativePath, FileTreeScanRequest request)
    {
        var extension = Path.GetExtension(fullPath);
        var extensions = NormalizeExtensions(request.IncludeExtensions);
        if (extensions.Length > 0 && !extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(request.FileFilter))
        {
            var filterTarget = request.FileFilter.Contains('/') || request.FileFilter.Contains('\\')
                ? relativePath
                : Path.GetFileName(relativePath);
            if (!MatchesGlob(filterTarget, request.FileFilter)) return false;
        }
        if (!string.IsNullOrWhiteSpace(request.FolderFilter) &&
            !MatchesFolderFilter(Path.GetDirectoryName(relativePath) ?? string.Empty, request.FolderFilter)) return false;
        return !IsExcluded(relativePath, request.ExcludePatterns ?? []);
    }

    internal static bool IsExcluded(string relativePath, IReadOnlyList<string> patterns) =>
        patterns.Any(pattern => MatchesPathOrName(relativePath, pattern));

    internal static bool IsValidExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return false;
        var normalized = extension.Trim();
        if (normalized == "*") return true;
        if (!normalized.StartsWith(".", StringComparison.Ordinal)) normalized = $".{normalized}";
        return normalized.Length > 1 && normalized.IndexOfAny(['/', '\\', ':', '*', '?', '\0']) < 0;
    }

    internal static bool IsValidRelativeGlob(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var normalized = PathNormalizer.NormalizeSeparators(pattern.Trim());
        if (normalized.IndexOf('\0') >= 0 || Path.IsPathRooted(normalized)) return false;
        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).All(segment => segment is not ".." and not ".");
    }

    private static bool MatchesPathOrName(string path, string pattern)
    {
        var normalized = PathNormalizer.NormalizeSeparators(pattern.Trim());
        var target = normalized.Contains('/') ? PathNormalizer.NormalizeSeparators(path) : Path.GetFileName(path);
        return MatchesGlob(target, normalized);
    }

    private static bool MatchesGlob(string target, string pattern)
    {
        var normalizedTarget = PathNormalizer.NormalizeSeparators(target);
        var normalizedPattern = PathNormalizer.NormalizeSeparators(pattern.Trim());
        var expression = new System.Text.StringBuilder("^");
        for (var index = 0; index < normalizedPattern.Length; index++)
        {
            var character = normalizedPattern[index];
            if (character == '*')
            {
                var isDouble = index + 1 < normalizedPattern.Length && normalizedPattern[index + 1] == '*';
                if (isDouble) index++;
                if (isDouble && index + 1 < normalizedPattern.Length && normalizedPattern[index + 1] == '/')
                {
                    index++;
                    expression.Append("(?:.*/)?");
                }
                else
                {
                    expression.Append(isDouble ? ".*" : "[^/]*");
                }
            }
            else if (character == '?') expression.Append("[^/]");
            else expression.Append(Regex.Escape(character.ToString()));
        }
        expression.Append('$');
        return Regex.IsMatch(normalizedTarget, expression.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}
