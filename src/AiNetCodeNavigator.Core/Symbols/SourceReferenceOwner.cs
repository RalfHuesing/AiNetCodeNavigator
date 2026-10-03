#nullable enable

using System;
using System.IO;
using System.Text;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>Computes lexical repository-relative coordinates without consulting Git.</summary>
internal static class SourceReferenceOwner
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static bool TryCreateProjectCoordinate(string? solutionPath, string? projectPath, out string coordinate, out string reason)
    {
        coordinate = string.Empty;
        reason = "A stable source owner path could not be established.";
        if (string.IsNullOrWhiteSpace(solutionPath) || string.IsNullOrWhiteSpace(projectPath)
            || !Path.IsPathFullyQualified(solutionPath) || !Path.IsPathFullyQualified(projectPath))
        {
            reason = "The selected solution and loaded project must have stable absolute paths.";
            return false;
        }

        try
        {
            var solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath));
            var canonicalProjectPath = Path.GetFullPath(projectPath);
            if (solutionDirectory is null || !TryFindReferenceBase(Path.GetFullPath(solutionDirectory), out var referenceBase, out reason)) return false;

            var relative = Path.GetRelativePath(referenceBase, canonicalProjectPath);
            if (Path.IsPathRooted(relative))
            {
                reason = "The loaded project is on a different filesystem volume from the source reference base.";
                return false;
            }

            relative = relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            if (!StableSymbolReferenceCodec.IsCanonicalSourcePath(relative))
            {
                reason = "The loaded project path does not produce a canonical lexical relative path.";
                return false;
            }

            coordinate = relative;
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            reason = $"Source owner path discovery failed: {exception.Message}";
            return false;
        }
    }

    internal static bool TryResolveLoadedProjectPath(string? solutionPath, string coordinate, out string canonicalPath, out string reason)
    {
        canonicalPath = string.Empty;
        reason = "A stable source owner path could not be established.";
        if (!StableSymbolReferenceCodec.IsCanonicalSourcePath(coordinate))
        {
            reason = "The source owner coordinate is not canonical.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(solutionPath) || !Path.IsPathFullyQualified(solutionPath))
        {
            reason = "The selected solution has no stable absolute path.";
            return false;
        }

        try
        {
            var solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath));
            if (solutionDirectory is null || !TryFindReferenceBase(Path.GetFullPath(solutionDirectory), out var referenceBase, out reason)) return false;
            canonicalPath = Path.GetFullPath(Path.Combine(referenceBase, coordinate.Replace('/', Path.DirectorySeparatorChar)));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            reason = $"Source owner path discovery failed: {exception.Message}";
            return false;
        }
    }

    private static bool TryFindReferenceBase(string startDirectory, out string referenceBase, out string reason)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            var marker = Path.Combine(directory.FullName, ".git");
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(marker);
            }
            catch (FileNotFoundException)
            {
                directory = directory.Parent;
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                directory = directory.Parent;
                continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                referenceBase = string.Empty;
                reason = $"The nearest .git marker is unreadable: {exception.Message}";
                return false;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                referenceBase = directory.FullName;
                reason = string.Empty;
                return true;
            }

            if (!TryValidatePointer(marker, directory.FullName, out reason))
            {
                referenceBase = string.Empty;
                return false;
            }

            referenceBase = directory.FullName;
            reason = string.Empty;
            return true;
        }

        referenceBase = startDirectory;
        reason = string.Empty;
        return true;
    }

    private static bool TryValidatePointer(string markerPath, string markerDirectory, out string reason)
    {
        reason = string.Empty;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(markerPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reason = $"The nearest .git pointer is unreadable: {exception.Message}";
            return false;
        }

        string content;
        try
        {
            content = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            reason = "The nearest .git pointer is not valid UTF-8.";
            return false;
        }

        if (content.Length > 0 && content[0] == '\uFEFF') content = content[1..];
        if (content.EndsWith("\r\n", StringComparison.Ordinal)) content = content[..^2];
        else if (content.EndsWith('\n')) content = content[..^1];
        if (content.Contains('\r') || content.Contains('\n') || !content.StartsWith("gitdir: ", StringComparison.Ordinal))
        {
            reason = "The nearest .git pointer must contain exactly one 'gitdir: <path>' line.";
            return false;
        }

        var path = content[8..];
        if (path.Length == 0 || !string.Equals(path, path.Trim(), StringComparison.Ordinal))
        {
            reason = "The nearest .git pointer has an empty path or surrounding whitespace.";
            return false;
        }

        try
        {
            var destination = Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(markerDirectory, path));
            if (Directory.Exists(destination)) return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            reason = $"The nearest .git pointer destination is invalid: {exception.Message}";
            return false;
        }

        reason = "The nearest .git pointer destination is not an existing directory.";
        return false;
    }
}
