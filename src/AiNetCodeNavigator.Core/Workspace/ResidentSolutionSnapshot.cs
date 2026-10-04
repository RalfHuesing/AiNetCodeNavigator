#nullable enable

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using AiNetCodeNavigator.Core.Symbols;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Result of asking a resident solution for its latest snapshot.
/// </summary>
public sealed record ResidentSolutionSnapshot(
    Solution? Solution,
    ResidentSolutionLoadError? Error,
    IReadOnlyDictionary<string, ConfiguredTargetFrameworks>? ConfiguredTargetFrameworks = null)
{
    internal SourceIdentityValidatedInputs? IdentityInputs { get; init; }

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
