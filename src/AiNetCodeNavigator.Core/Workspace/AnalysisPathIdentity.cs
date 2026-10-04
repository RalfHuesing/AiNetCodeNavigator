#nullable enable

using System;
using AiNetCodeNavigator.Core.Symbols;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>Canonical lexical paths for analysis fingerprints, separate from public target-token encoding.</summary>
internal static class AnalysisPathIdentity
{
    internal static bool TryNormalize(string? path, out string canonicalPath)
    {
        canonicalPath = string.Empty;
        if (!StableTargetPath.TryNormalizeTargetPath(path, out var targetPath))
            return false;

        var slashPath = targetPath.Replace('\\', '/');
        canonicalPath = OperatingSystem.IsWindows() ? slashPath.ToUpperInvariant() : slashPath;
        return true;
    }
}
