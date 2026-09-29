#nullable enable

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Ergebnis des Ladens einer Projektdefinition (Result-Pattern): entweder eine vollständige,
/// existenzgeprüfte Definition (<see cref="Succeeded"/>) oder ein Fehler mit Code aus
/// <see cref="ProjectErrorCodes"/> oder <see cref="NavigationErrorCodes"/>.
/// </summary>
public sealed record ProjectDefinitionLoadResult(ProjectDefinition? Definition, string? ErrorCode, string? Message)
{
    public bool Succeeded => Definition is not null;

    public static ProjectDefinitionLoadResult Success(ProjectDefinition definition) => new(definition, null, null);

    public static ProjectDefinitionLoadResult Failure(string errorCode, string message) => new(null, errorCode, message);
}
