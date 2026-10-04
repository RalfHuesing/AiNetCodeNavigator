#nullable enable

using System;
using System.Threading;

namespace AiNetCodeNavigator.Core.Workspace;

internal sealed class ProjectEntry(
    string rootPath,
    ProjectDefinition definition,
    ResidentSolution residentSolution,
    DateTime lastUsedUtc)
{
    private int inFlightCount;

    internal string RootPath { get; } = rootPath;

    internal ProjectDefinition Definition { get; } = definition;

    internal ResidentSolution ResidentSolution { get; } = residentSolution;

    internal DateTime LastUsedUtc { get; set; } = lastUsedUtc;

    internal bool PendingEviction { get; set; }

    internal bool FailureLeaseReleased { get; set; }

    internal long MaximumSourceSnapshotTicket { get; set; }

    internal int InFlightCount => Interlocked.CompareExchange(ref inFlightCount, 0, 0);

    internal ProjectLease OpenLease(Action<ProjectLease>? onReleased = null)
    {
        Interlocked.Increment(ref inFlightCount);
        return new ProjectLease(
            RootPath,
            Definition,
            ResidentSolution,
            lease =>
            {
                Interlocked.Decrement(ref inFlightCount);
                onReleased?.Invoke(lease);
            });
    }
}
