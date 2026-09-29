#nullable enable

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Ergebnis der Instanz-Erzeugung im Registry-Pfad: entweder eine konfigurierte
/// residente Solution oder Fehlercode plus Ursprungsmeldung ohne Eintrag in der Registry.
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
