#nullable enable

using System;
using System.IO;

namespace AiNetCodeNavigator.Core.Common;

/// <summary>
/// Normalizes absolute file paths to relative paths with consistent forward slashes.
/// </summary>
public static class PathNormalizer
{
    public static string ToRelative(string outputRoot, string absoluteFilePath)
    {
        if (string.IsNullOrEmpty(absoluteFilePath))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return Path.GetFileName(absoluteFilePath).Replace('\\', '/');
        }

        var normalizedRoot = Path.GetFullPath(outputRoot);
        var normalizedFile = Path.GetFullPath(absoluteFilePath);

        if (!normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(normalizedFile).Replace('\\', '/');
        }

        var relative = Path.GetRelativePath(normalizedRoot, normalizedFile);
        return relative.Replace('\\', '/');
    }

    public static string NormalizeSeparators(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        return path.Replace('\\', '/');
    }

    public static bool MatchesScope(string? filePath, string? scopeFilter)
    {
        if (string.IsNullOrWhiteSpace(scopeFilter)) return true;
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        var normalizedPath = NormalizeSeparators(filePath);
        var normalizedFilter = NormalizeSeparators(scopeFilter.Trim());

        if (normalizedPath.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase)) return true;

        if (Path.IsPathRooted(normalizedFilter))
        {
            var fileName = Path.GetFileName(normalizedFilter);
            if (!string.IsNullOrEmpty(fileName) && normalizedPath.Contains(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
