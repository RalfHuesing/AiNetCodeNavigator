#nullable enable

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Fehlercodes beim Verwalten und Laden von Projekten und Solutions.
/// </summary>
public static class ProjectErrorCodes
{
    public const string SolutionNotFound = "SOLUTION_NOT_FOUND";
    public const string ProjectLoadFailed = "PROJECT_LOAD_FAILED";
    public const string ProjectNotInitialized = "PROJECT_NOT_INITIALIZED";
    public const string ProjectDefinitionInvalid = "PROJECT_DEFINITION_INVALID";
}
