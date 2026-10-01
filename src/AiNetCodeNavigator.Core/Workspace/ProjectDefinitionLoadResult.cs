#nullable enable

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Result of loading a project definition (result pattern): either a complete
/// definition verified to exist (<see cref="Succeeded"/>) or an error with a code from
/// <see cref="ProjectErrorCodes"/> or <see cref="NavigationErrorCodes"/>.
/// </summary>
public sealed record ProjectDefinitionLoadResult(ProjectDefinition? Definition, string? ErrorCode, string? Message)
{
    public bool Succeeded => Definition is not null;

    public static ProjectDefinitionLoadResult Success(ProjectDefinition definition) => new(definition, null, null);

    public static ProjectDefinitionLoadResult Failure(string errorCode, string message) => new(null, errorCode, message);
}
