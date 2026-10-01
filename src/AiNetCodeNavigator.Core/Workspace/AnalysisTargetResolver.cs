#nullable enable

using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Resolves the release-wide core contract: exactly one absolute path to an existing
/// file. The extension determines source or decompiled-assembly mode.
/// </summary>
public static class AnalysisTargetResolver
{
    public static AnalysisTargetResolution Resolve(AnalysisTargetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolveTargetPathOnly(request);
    }

    public static AnalysisTargetResolution ResolveOptional(AnalysisTargetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TargetPath is null)
        {
            return new AnalysisTargetResolution(null, null);
        }

        return Resolve(request);
    }

    public static AnalysisTarget ResolveRequiredSourceTarget(string? targetPath)
    {
        var resolution = ResolveTargetPathOnly(new AnalysisTargetRequest(targetPath));
        if (resolution.Target is { TargetType: AnalysisTargetType.Project } target)
        {
            return target;
        }

        var message = resolution.Error?.FormattedMessage
            ?? "The operation accepts only the absolute path of an existing .sln or .slnx file.";
        throw new InvalidOperationException(message);
    }

    public static AnalysisTargetResolution ResolveTargetPathOnly(AnalysisTargetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TargetPath))
        {
            return Invalid(
                "The parameter 'targetPath' is required.",
                "Provide the absolute path of an existing .sln, .slnx, .dll or .exe file.",
                request.TargetPath);
        }

        var path = ResolveCanonicalFilePath(request.TargetPath);
        if (path.Error is not null)
        {
            return Invalid(path.Error, path.Hint, request.TargetPath);
        }

        var canonicalPath = path.CanonicalPath!;
        var targetKind = ResolveTargetTypeFromExtension(canonicalPath);

        if (targetKind is null)
        {
            return Invalid(
                $"The parameter 'targetPath' has an unsupported extension: '{canonicalPath}'.",
                "Provide an existing file with the extension .sln, .slnx, .dll or .exe.",
                canonicalPath);
        }

        var analysisRoot = Path.GetDirectoryName(canonicalPath)!;
        string fingerprint;
        try
        {
            fingerprint = CreateFingerprint(canonicalPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Unreadable(canonicalPath, exception);
        }

        var target = new AnalysisTarget(targetKind.Value, canonicalPath, request)
        {
            AnalysisRoot = analysisRoot,
            Fingerprint = fingerprint,
        };

        return new AnalysisTargetResolution(target, null);
    }

    private static AnalysisTargetType? ResolveTargetTypeFromExtension(string canonicalPath) =>
        Path.GetExtension(canonicalPath).ToLowerInvariant() switch
        {
            ".sln" or ".slnx" => AnalysisTargetType.Project,
            ".dll" or ".exe" => AnalysisTargetType.Assembly,
            _ => null,
        };

    private static PathResolution ResolveCanonicalFilePath(string targetPath)
    {
        var path = targetPath.Trim();
        if (path.Contains('*') || path.Contains('?'))
        {
            return new PathResolution(
                null,
                $"The parameter 'targetPath' must not contain wildcards or search patterns: '{targetPath}'.",
                "Specify a concrete existing .sln, .slnx, .dll or .exe file; do not use globs.");
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return new PathResolution(
                null,
                "The parameter 'targetPath' must be an absolute path.",
                "Specify targetPath as an absolute file path.");
        }

        string canonicalPath;
        try
        {
            canonicalPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return new PathResolution(
                null,
                $"The parameter 'targetPath' is not a valid path: '{targetPath}'.",
                "Specify a valid absolute file path.");
        }

        if (Directory.Exists(canonicalPath))
        {
            return new PathResolution(
                null,
                $"The parameter 'targetPath' must point to a file, not a directory: '{canonicalPath}'.",
                "Specify a concrete existing .sln/.slnx/.dll/.exe file.");
        }

        if (!File.Exists(canonicalPath))
        {
            return new PathResolution(
                null,
                $"The parameter 'targetPath' must point to an existing file: '{canonicalPath}'.",
                "Specify an existing .sln/.slnx/.dll/.exe file.");
        }

        return new PathResolution(canonicalPath, null, null);
    }

    private static string CreateFingerprint(string canonicalPath)
    {
        using var stream = new FileStream(
            canonicalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static AnalysisTargetResolution Invalid(string message, string? hint = null, string? targetPath = null) =>
        new(
            null,
            new AnalysisTargetError(
                NavigationErrorCodes.InvalidArgument,
                message,
                hint ?? "Provide targetPath as the absolute path of an existing .sln/.slnx/.dll/.exe file.",
                Context: targetPath,
                FieldPath: "$.targetPath"));

    private static AnalysisTargetResolution Unreadable(string canonicalPath, Exception exception) =>
        new(
            null,
            new AnalysisTargetError(
                NavigationErrorCodes.TargetUnreadable,
                $"The file could not be read to compute the fingerprint: '{exception.Message}'.",
                "Check read permissions and retry the call once the file is available.",
                Context: canonicalPath,
                FieldPath: "$.targetPath"));

    private sealed record PathResolution(string? CanonicalPath, string? Error, string? Hint = null);
}
