#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AiNetCodeNavigator.Core.Symbols;

public static class StableTargetPath
{
    private const int TargetHashBytes = 16;
    public static bool TryCreateTarget(string canonicalPath, out string token)
    {
        token = string.Empty;
        if (!TryNormalizeTargetPath(canonicalPath, out var normalizedPath))
        {
            return false;
        }

        token = Encode128(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
        return true;
    }

    public static bool TryNormalizeTargetPath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (OperatingSystem.IsWindows())
            {
                normalizedPath = normalizedPath.ToUpperInvariant();
            }

            return normalizedPath.Length > 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string Encode128(ReadOnlySpan<byte> hash) =>
        Convert.ToBase64String(hash[..TargetHashBytes])
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
