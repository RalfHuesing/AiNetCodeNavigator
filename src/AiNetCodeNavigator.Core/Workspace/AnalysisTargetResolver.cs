#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Löst den releaseweiten Kernvertrag auf: genau ein absoluter, vorhandener
/// Dateipfad. Die Dateiendung bestimmt Source- oder Decompiled-Assembly-Modus.
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
            ?? "Operation akzeptiert nur den absoluten Pfad einer vorhandenen .sln- oder .slnx-Datei.";
        throw new InvalidOperationException(message);
    }

    public static AnalysisTargetResolution ResolveTargetPathOnly(AnalysisTargetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TargetPath))
        {
            return Invalid(
                "Der Parameter 'targetPath' ist erforderlich.",
                "Den absoluten Pfad einer vorhandenen .sln, .slnx, .dll oder .exe übergeben.",
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
                $"Der Parameter 'targetPath' hat eine nicht unterstützte Endung: '{canonicalPath}'.",
                "Eine vorhandene Datei mit Endung .sln, .slnx, .dll oder .exe übergeben.",
                canonicalPath);
        }

        var analysisRoot = Path.GetDirectoryName(canonicalPath)!;
        var fingerprint = CreateFingerprint(canonicalPath);
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
                $"Der Parameter 'targetPath' darf keine Wildcards oder Suchmasken enthalten: '{targetPath}'.",
                "Eine konkrete vorhandene .sln, .slnx, .dll oder .exe angeben; keine Globs verwenden.");
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return new PathResolution(
                null,
                "Der Parameter 'targetPath' muss ein absoluter Pfad sein.",
                "targetPath mit einem absoluten Dateipfad angeben.");
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
                $"Der Parameter 'targetPath' ist kein gültiger Pfad: '{targetPath}'.",
                "Einen gültigen absoluten Dateipfad angeben.");
        }

        if (Directory.Exists(canonicalPath))
        {
            return new PathResolution(
                null,
                $"Der Parameter 'targetPath' muss auf eine Datei zeigen, kein Verzeichnis: '{canonicalPath}'.",
                "Eine konkrete vorhandene .sln/.slnx/.dll/.exe-Datei angeben.");
        }

        if (!File.Exists(canonicalPath))
        {
            return new PathResolution(
                null,
                $"Der Parameter 'targetPath' muss auf eine vorhandene Datei zeigen: '{canonicalPath}'.",
                "Eine vorhandene .sln/.slnx/.dll/.exe-Datei angeben.");
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
                hint ?? "targetPath mit dem absoluten Pfad einer vorhandenen .sln/.slnx/.dll/.exe-Datei übergeben.",
                Context: targetPath,
                FieldPath: "$.targetPath"));

    private sealed record PathResolution(string? CanonicalPath, string? Error, string? Hint = null);
}
