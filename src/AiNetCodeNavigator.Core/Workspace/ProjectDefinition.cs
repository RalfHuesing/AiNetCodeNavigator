#nullable enable

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Immutable value type for a loaded project definition.
/// The path is absolute and has been verified to exist.
/// </summary>
public sealed record ProjectDefinition(string SolutionPath);
