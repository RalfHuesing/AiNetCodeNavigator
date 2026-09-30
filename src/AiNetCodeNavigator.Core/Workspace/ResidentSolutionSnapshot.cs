#nullable enable

using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Result of asking a resident solution for its latest snapshot.
/// </summary>
public sealed record ResidentSolutionSnapshot(
    Solution? Solution,
    ResidentSolutionLoadError? Error)
{
    public bool Succeeded => Solution is not null && Error is null;
}

/// <summary>
/// A retryable failure while loading or reloading a solution.
/// </summary>
public sealed record ResidentSolutionLoadError(
    string ErrorCode,
    string? TargetPath,
    string Message,
    bool Retryable);
