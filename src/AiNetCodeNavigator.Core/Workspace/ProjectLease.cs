#nullable enable

using System;
using System.Threading;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Represents active access to a resident project instance.
/// Disposal (<see cref="Dispose"/>) decrements the in-flight counter.
/// </summary>
public sealed class ProjectLease(
    string rootPath,
    ProjectDefinition definition,
    ResidentSolution residentSolution,
    Action<ProjectLease> release) : IDisposable
{
    private int released;
    private int loadFailedResponseEmitted;

    public ResidentSolution ResidentSolution { get; } = residentSolution;

    public string RootPath { get; } = rootPath;

    public ProjectDefinition Definition { get; } = definition;

    public bool LoadFailedResponseEmitted =>
        Volatile.Read(ref loadFailedResponseEmitted) == 1;

    public void MarkLoadFailedResponseEmitted()
    {
        Interlocked.Exchange(ref loadFailedResponseEmitted, 1);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref released, 1, 0) != 0)
        {
            return;
        }

        release(this);
    }
}
