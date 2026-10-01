#nullable enable

using System;
using System.IO;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Loads the definition for a concrete, already specified solution file (.sln or .slnx).
/// </summary>
public static class ProjectDefinitionLoader
{
    public static ProjectDefinitionLoadResult LoadSolutionTarget(string? solutionPath)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return Fail(
                NavigationErrorCodes.InvalidArgument,
                "The parameter 'targetPath' is required; provide the absolute path of an existing .sln or .slnx file.");
        }

        if (solutionPath.Contains('*') || solutionPath.Contains('?'))
        {
            return Fail(
                NavigationErrorCodes.InvalidArgument,
                $"The parameter 'targetPath' must not contain wildcards or search patterns: '{solutionPath}'.");
        }

        var canonicalSolutionPath = Canonicalize(solutionPath);
        if (canonicalSolutionPath is null
            || !File.Exists(canonicalSolutionPath)
            || !IsSolutionPath(canonicalSolutionPath))
        {
            return Fail(
                ProjectErrorCodes.SolutionNotFound,
                $"Solution file not found or not supported: '{solutionPath}'. " +
                "The absolute path of an existing .sln or .slnx file is required.");
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
