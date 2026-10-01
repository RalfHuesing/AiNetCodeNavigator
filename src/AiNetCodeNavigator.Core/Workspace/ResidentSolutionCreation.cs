#nullable enable

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Result of instance creation through the registry: either a configured
/// resident solution or an error code and original message without a registry entry.
/// </summary>
public sealed record ResidentSolutionCreation(
    ResidentSolution? Solution,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool Succeeded => Solution is not null;

    public static ResidentSolutionCreation Resident(ResidentSolution solution) => new(solution);

    public static ResidentSolutionCreation Failed(string errorCode, string errorMessage) =>
        new(null, errorCode, errorMessage);
}
