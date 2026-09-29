#nullable enable

using System;
using System.IO;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Lädt die Definition für eine konkrete, bereits adressierte Solution-Datei (.sln oder .slnx).
/// </summary>
public static class ProjectDefinitionLoader
{
    public static ProjectDefinitionLoadResult LoadSolutionTarget(string? solutionPath)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return Fail(
                NavigationErrorCodes.InvalidArgument,
                "Der Parameter 'targetPath' ist erforderlich; übergib den absoluten Pfad einer vorhandenen .sln- oder .slnx-Datei.");
        }

        if (solutionPath.Contains('*') || solutionPath.Contains('?'))
        {
            return Fail(
                NavigationErrorCodes.InvalidArgument,
                $"Der Parameter 'targetPath' darf keine Wildcards oder Suchmasken enthalten: '{solutionPath}'.");
        }

        var canonicalSolutionPath = Canonicalize(solutionPath);
        if (canonicalSolutionPath is null
            || !File.Exists(canonicalSolutionPath)
            || !IsSolutionPath(canonicalSolutionPath))
        {
            return Fail(
                ProjectErrorCodes.SolutionNotFound,
                $"Solution-Datei nicht gefunden oder nicht unterstützt: '{solutionPath}'. " +
                "Erforderlich ist der absolute Pfad einer vorhandenen .sln- oder .slnx-Datei.");
        }

        return ProjectDefinitionLoadResult.Success(new ProjectDefinition(canonicalSolutionPath));
    }

    private static string? Canonicalize(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path)
                ? Path.GetFullPath(path)
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsSolutionPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".sln" or ".slnx";

    private static ProjectDefinitionLoadResult Fail(string errorCode, string message) =>
        ProjectDefinitionLoadResult.Failure(errorCode, message);
}
