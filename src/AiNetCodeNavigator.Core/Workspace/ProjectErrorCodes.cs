#nullable enable

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Error codes for managing and loading projects and solutions.
/// </summary>
public static class ProjectErrorCodes
{
    public const string SolutionNotFound = "SOLUTION_NOT_FOUND";
    public const string ProjectLoadFailed = "PROJECT_LOAD_FAILED";
    public const string ProjectNotInitialized = "PROJECT_NOT_INITIALIZED";
    public const string ProjectDefinitionInvalid = "PROJECT_DEFINITION_INVALID";
    public const string RegistryDisposed = "PROJECT_REGISTRY_DISPOSED";
}
