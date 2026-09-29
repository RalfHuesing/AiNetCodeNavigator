#nullable enable

using System;
using System.Threading;

namespace AiNetCodeNavigator.Core.Workspace;

internal sealed record ProjectCreationAttempt(
    ProjectDefinition? Definition,
    ResidentSolutionCreation Creation);

internal sealed class ProjectCreationReservation(Func<ProjectCreationAttempt> factory)
{
    private readonly Lazy<ProjectCreationAttempt> creation =
        new(factory, LazyThreadSafetyMode.ExecutionAndPublication);
    private int waiterCount;

    internal int WaiterCount => Volatile.Read(ref waiterCount);

    internal ProjectCreationAttempt GetValue()
    {
        Interlocked.Increment(ref waiterCount);
        try
        {
            return creation.Value;
        }
        finally
        {
            Interlocked.Decrement(ref waiterCount);
        }
    }
}
