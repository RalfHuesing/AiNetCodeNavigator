#nullable enable

using System;

namespace AiNetCodeNavigator.Core.Workspace;

public static class ProjectRegistryDefaults
{
    public const int MaxProjects = 4;
    public static readonly TimeSpan IdleTtl = TimeSpan.FromMinutes(45);
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);
}

public sealed record ProjectRegistryOptions(
    Func<ProjectDefinition, ResidentSolutionCreation> InstanceFactory,
    TimeProvider Clock,
    int MaxProjects = ProjectRegistryDefaults.MaxProjects,
    TimeSpan IdleTtl = default,
    TimeSpan TickInterval = default)
{
    internal Action? BeforeLeaseRelease { get; init; }

    internal Action? BeforeCreationReservation { get; init; }

    internal Func<string, ProjectCreationAttempt, ProjectCreationAttempt?>? BeforePublishCreation { get; init; }
}

public sealed record ProjectSnapshot(
    string RootPath,
    ProjectDefinition Definition,
    DateTime LastUsedUtc,
    ResidentSolution ResidentSolution);
