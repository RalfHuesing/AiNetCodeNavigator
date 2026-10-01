#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.FastTests.Workspace;

[Trait("Category", "Unit")]
internal sealed class FakeClock : TimeProvider
{
    private long utcTicks = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks;

    public override DateTimeOffset GetUtcNow() => new(Volatile.Read(ref utcTicks), TimeSpan.Zero);

    public void Advance(TimeSpan delta) => Interlocked.Add(ref utcTicks, delta.Ticks);

    public void AdvanceMinutes(int minutes) => Advance(TimeSpan.FromMinutes(minutes));
}

[Trait("Category", "Unit")]
internal sealed class TrackingSolutionFactory
{
    private int instancesCreated;
    private int loadsStarted;
    private int loadsCancelled;
    private int solutionsDisposed;
    private int failLoads;
    private readonly Dictionary<ResidentSolution, int> disposalCounts = new();

    internal int InstancesCreated => instancesCreated;

    internal int LoadsCancelled => loadsCancelled;

    internal int LoadsStarted => Volatile.Read(ref loadsStarted);

    internal int SolutionsDisposed => Volatile.Read(ref solutionsDisposed);

    internal Action<ResidentSolution>? OnSolutionDisposed { get; set; }

    internal int DisposalsFor(ResidentSolution solution)
    {
        lock (disposalCounts)
        {
            return disposalCounts.TryGetValue(solution, out var count) ? count : 0;
        }
    }

    internal bool FailLoads
    {
        get => Volatile.Read(ref failLoads) == 1;
        set => Volatile.Write(ref failLoads, value ? 1 : 0);
    }

    internal Func<ProjectDefinition, ResidentSolutionCreation> Factory =>
        definition => ResidentSolutionCreation.Resident(CreateSolution(definition));

    internal ResidentSolution CreateSolution(ProjectDefinition definition)
    {
        Interlocked.Increment(ref instancesCreated);
        return FailLoads ? CreateFailedLoadSolution() : CreatePendingLoadSolution();
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ResidentSolution is disposed by caller or registry")]
    internal ResidentSolution CreatePendingLoadSolution()
    {
        ResidentSolution? solution = null;
        solution = new ResidentSolution(token =>
        {
            Interlocked.Increment(ref loadsStarted);
            var pending = new TaskCompletionSource<Solution?>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() =>
            {
                Interlocked.Increment(ref loadsCancelled);
                RecordDisposal(solution!);
                pending.TrySetCanceled(token);
            });
            return pending.Task;
        });
        return solution;
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ResidentSolution is disposed by caller or registry")]
    private ResidentSolution CreateFailedLoadSolution()
    {
        ResidentSolution? solution = null;
        solution = new ResidentSolution(token =>
        {
            Interlocked.Increment(ref loadsStarted);
            token.Register(() =>
            {
                Interlocked.Increment(ref loadsCancelled);
                RecordDisposal(solution!);
            });
            return Task.FromException<Solution?>(new InvalidOperationException("Solution cannot be loaded."));
        });
        return solution;
    }

    private void RecordDisposal(ResidentSolution solution)
    {
        Interlocked.Increment(ref solutionsDisposed);
        lock (disposalCounts)
        {
            disposalCounts[solution] = (disposalCounts.TryGetValue(solution, out var count) ? count : 0) + 1;
        }

        OnSolutionDisposed?.Invoke(solution);
    }
}
