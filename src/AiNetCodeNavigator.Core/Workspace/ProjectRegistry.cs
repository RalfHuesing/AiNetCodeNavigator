#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Manages resident solution instances in memory with least-recently-used (LRU) eviction,
/// TTL checks and concurrency-safe deduplication of parallel requests.
/// </summary>
public sealed class ProjectRegistry : IAsyncDisposable, IDisposable
{
    private readonly record struct RetiredSolution(
        string? RetiredTarget,
        long MaximumSourceSnapshotTicket,
        ResidentSolution ResidentSolution);

    private readonly Lock gate = new();
    private readonly Dictionary<string, ProjectEntry> projects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProjectCreationReservation> reservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ProjectRegistryOptions options;
    private readonly TimeSpan idleTtl;
    private readonly CancellationTokenSource tickSource = new();
    private readonly Task tickTask;
    private TaskCompletionSource? leaseOperationsDrained;
    private int activeLeaseOperations;
    private int disposed;

    internal Func<string, long, Task>? SourceOwnerRetiring { get; set; }

    public ProjectRegistry(ProjectRegistryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        idleTtl = ResolvePositive(options.IdleTtl, ProjectRegistryDefaults.IdleTtl);
        var tickInterval = ResolvePositive(options.TickInterval, ProjectRegistryDefaults.TickInterval);
        tickTask = Task.Run(() => MonitorLoopAsync(tickInterval));
    }

    internal CancellationToken ShutdownToken => tickSource.Token;

    public ProjectLeaseResult Lease(string solutionPath)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            activeLeaseOperations++;
        }

        try
        {
            var key = Canonicalize(solutionPath);
            var retired = new List<RetiredSolution>();
            var result = TryAdoptOrCreate(key, retired);
            foreach (var retiredSolution in retired)
            {
                RetireAndDisposeAsync(retiredSolution).AsTask().GetAwaiter().GetResult();
            }

            return result;
        }
        finally
        {
            CompleteLeaseOperation();
        }
    }

    public int ActiveLoadCount
    {
        get
        {
            lock (gate)
            {
                return projects.Values.Count(entry => entry.ResidentSolution.LoadState == ServerLoadState.Loading);
            }
        }
    }

    public int PendingCreationWaiters(string solutionPath)
    {
        var key = Canonicalize(solutionPath);
        lock (gate)
        {
            return reservations.TryGetValue(key, out var reservation) ? reservation.WaiterCount : 0;
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            Volatile.Write(ref disposed, 1);
        }

        await tickSource.CancelAsync().ConfigureAwait(false);
        try
        {
            await tickTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (tickSource.IsCancellationRequested)
        {
        }

        Task? pendingLeaseOperations;
        lock (gate)
        {
            if (activeLeaseOperations == 0)
            {
                pendingLeaseOperations = null;
            }
            else
            {
                leaseOperationsDrained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                pendingLeaseOperations = leaseOperationsDrained.Task;
            }
        }

        if (pendingLeaseOperations is not null)
        {
            await pendingLeaseOperations.ConfigureAwait(false);
        }

        List<RetiredSolution> remaining;
        lock (gate)
        {
            remaining = projects.Values
                .Select(entry => RetiredSolutionFor(entry))
                .ToList();
            projects.Clear();
            reservations.Clear();
        }

        foreach (var retiredSolution in remaining)
        {
            await RetireAndDisposeAsync(retiredSolution).ConfigureAwait(false);
        }

        tickSource.Dispose();
    }

    public async Task RunEvictionTickAsync()
    {
        List<ProjectEntry> expired;
        lock (gate)
        {
            expired = CollectExpired(UtcNow());
            foreach (var entry in expired)
            {
                projects.Remove(entry.RootPath);
            }
        }

        foreach (var entry in expired)
        {
            await RetireAndDisposeAsync(RetiredSolutionFor(entry)).ConfigureAwait(false);
        }
    }

    private async Task MonitorLoopAsync(TimeSpan tickInterval)
    {
        try
        {
            while (!tickSource.IsCancellationRequested)
            {
                await Task.Delay(tickInterval, tickSource.Token).ConfigureAwait(false);
                await RunEvictionTickAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (tickSource.IsCancellationRequested)
        {
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ProjectLease ownership is transferred to the caller via ProjectLeaseResult")]
    private ProjectLeaseResult TryAdoptOrCreate(string key, List<RetiredSolution> retired)
    {
        if (options.BeforeCreationReservation is not null)
        {
            var observed = FindResidentBeforeCreationBarrier(key, retired);
            if (observed is not null)
            {
                return ProjectLeaseResult.Success(observed);
            }

            options.BeforeCreationReservation();
        }

        ProjectCreationReservation reservation;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            var resident = FindAdoptable(key, retired);
            if (resident is not null)
            {
                return ProjectLeaseResult.Success(resident);
            }

            reservation = ReserveCreationUnderLock(key);
        }

        ProjectCreationAttempt attempt;
        try
        {
            attempt = reservation.GetValue();
        }
        catch
        {
            RemoveReservation(key, reservation);
            throw;
        }

        if (options.BeforePublishCreation is { } beforePublishCreation)
        {
            var winnerAttempt = beforePublishCreation(key, attempt);
            if (winnerAttempt is not null)
            {
                var winner = PublishCreation(key, reservation, winnerAttempt, retired);
                winner.Lease?.Dispose();
            }
        }

        return PublishCreation(key, reservation, attempt, retired);
    }

    private ProjectLease? FindResidentBeforeCreationBarrier(string key, List<RetiredSolution> retired)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            return FindAdoptable(key, retired);
        }
    }

    private ProjectCreationReservation ReserveCreationUnderLock(string key)
    {
        if (reservations.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var reservation = new ProjectCreationReservation(() => CreateInstance(key));
        reservations.Add(key, reservation);
        return reservation;
    }

    private ProjectCreationAttempt CreateInstance(string key)
    {
        var definition = ProjectDefinitionLoader.LoadSolutionTarget(key);
        if (!definition.Succeeded)
        {
            return new ProjectCreationAttempt(null, ResidentSolutionCreation.Failed(definition.ErrorCode!, definition.Message!));
        }

        return new ProjectCreationAttempt(definition.Definition, options.InstanceFactory(definition.Definition!));
    }

    private ProjectLeaseResult PublishCreation(
        string key,
        ProjectCreationReservation reservation,
        ProjectCreationAttempt attempt,
        List<RetiredSolution> retired)
    {
        var created = attempt.Creation;
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                RemoveReservationUnderLock(key, reservation);
                if (created.Solution is not null)
                {
                    retired.Add(new RetiredSolution(null, 0, created.Solution));
                }

                return ProjectLeaseResult.Failure(
                    ProjectErrorCodes.RegistryDisposed,
                    "The project registry was shut down before the new solution could be published.");
            }

            if (!created.Succeeded)
            {
                RemoveReservationUnderLock(key, reservation);
                return ProjectLeaseResult.Failure(created.ErrorCode!, created.ErrorMessage!);
            }

            if (projects.TryGetValue(key, out var raced))
            {
                RemoveReservationUnderLock(key, reservation);
                if (created.Solution is not null && !ReferenceEquals(created.Solution, raced.ResidentSolution))
                {
                    retired.Add(new RetiredSolution(null, 0, created.Solution));
                }

                return ProjectLeaseResult.Success(Adopt(raced));
            }

            EvictLeastRecentlyUsed(retired);
            var entry = new ProjectEntry(key, attempt.Definition!, created.Solution!, UtcNow());
            projects.Add(key, entry);
            RemoveReservationUnderLock(key, reservation);
            return ProjectLeaseResult.Success(Adopt(entry));
        }
    }

    private void RemoveReservation(string key, ProjectCreationReservation reservation)
    {
        lock (gate)
        {
            RemoveReservationUnderLock(key, reservation);
        }
    }

    private void CompleteLeaseOperation()
    {
        TaskCompletionSource? drained = null;
        lock (gate)
        {
            activeLeaseOperations--;
            if (Volatile.Read(ref disposed) != 0 && activeLeaseOperations == 0)
            {
                drained = leaseOperationsDrained;
            }
        }

        drained?.TrySetResult();
    }

    private void RemoveReservationUnderLock(string key, ProjectCreationReservation reservation)
    {
        if (reservations.TryGetValue(key, out var current) && ReferenceEquals(current, reservation))
        {
            reservations.Remove(key);
        }
    }

    private ProjectLease? FindAdoptable(string key, List<RetiredSolution> retired)
    {
        if (!projects.TryGetValue(key, out var entry))
        {
            return null;
        }

        if (entry.ResidentSolution.LoadState == ServerLoadState.LoadFailed
            && entry.FailureLeaseReleased
            && entry.InFlightCount == 0)
        {
            projects.Remove(key);
            retired.Add(RetiredSolutionFor(entry));
            return null;
        }

        return Adopt(entry);
    }

    public IReadOnlyList<ProjectSnapshot> Snapshots()
    {
        lock (gate)
        {
            return projects.Values.Select(SnapshotOf).ToList();
        }
    }

    public ProjectSnapshot? FindSnapshot(string solutionPath)
    {
        var key = Canonicalize(solutionPath);
        lock (gate)
        {
            return projects.TryGetValue(key, out var entry) ? SnapshotOf(entry) : null;
        }
    }

    public ProjectSnapshot SnapshotFor(ProjectLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (gate)
        {
            if (!projects.TryGetValue(lease.RootPath, out var entry)
                || !ReferenceEquals(entry.ResidentSolution, lease.ResidentSolution))
            {
                throw new InvalidOperationException("The project lease is no longer resident.");
            }

            return SnapshotOf(entry);
        }
    }

    internal void RecordValidatedSourceSnapshot(ProjectLease lease, long snapshotTicket)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (snapshotTicket <= 0) throw new ArgumentOutOfRangeException(nameof(snapshotTicket));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (!projects.TryGetValue(lease.RootPath, out var entry)
                || !ReferenceEquals(entry.ResidentSolution, lease.ResidentSolution))
            {
                throw new InvalidOperationException("The source snapshot lease is no longer resident.");
            }

            entry.MaximumSourceSnapshotTicket = Math.Max(entry.MaximumSourceSnapshotTicket, snapshotTicket);
        }
    }

    private static ProjectSnapshot SnapshotOf(ProjectEntry entry) =>
        new(entry.RootPath, entry.Definition, entry.LastUsedUtc, entry.ResidentSolution);

    private ProjectLease Adopt(ProjectEntry entry)
    {
        entry.PendingEviction = false;
        entry.LastUsedUtc = UtcNow();
        return entry.OpenLease(lease =>
        {
            options.BeforeLeaseRelease?.Invoke();
            ReleaseEntry(entry, lease.LoadFailedResponseEmitted);
        });
    }

    private void ReleaseEntry(ProjectEntry entry, bool loadFailedResponseEmitted)
    {
        lock (gate)
        {
            if (loadFailedResponseEmitted
                && entry.ResidentSolution.LoadState == ServerLoadState.LoadFailed
                && projects.TryGetValue(entry.RootPath, out var current)
                && ReferenceEquals(current, entry))
            {
                entry.FailureLeaseReleased = true;
            }
        }
    }

    private void EvictLeastRecentlyUsed(List<RetiredSolution> retired)
    {
        while (projects.Count >= options.MaxProjects)
        {
            ProjectEntry? victim = null;
            foreach (var candidate in projects.Values)
            {
                if (candidate.InFlightCount == 0
                    && (candidate.ResidentSolution.LoadState != ServerLoadState.LoadFailed || candidate.FailureLeaseReleased)
                    && (victim is null || candidate.LastUsedUtc < victim.LastUsedUtc))
                {
                    victim = candidate;
                }
            }

            if (victim is null)
            {
                break;
            }

            projects.Remove(victim.RootPath);
            retired.Add(RetiredSolutionFor(victim));
        }
    }

    private static RetiredSolution RetiredSolutionFor(ProjectEntry entry) =>
        new(entry.RootPath, entry.MaximumSourceSnapshotTicket, entry.ResidentSolution);

    private async ValueTask RetireAndDisposeAsync(RetiredSolution retiredSolution)
    {
        try
        {
            if (retiredSolution.RetiredTarget is { } targetPath
                && retiredSolution.MaximumSourceSnapshotTicket > 0
                && SourceOwnerRetiring is { } retireOwner)
            {
                await retireOwner(targetPath, retiredSolution.MaximumSourceSnapshotTicket).ConfigureAwait(false);
            }
        }
        finally
        {
            await retiredSolution.ResidentSolution.DisposeAsync().ConfigureAwait(false);
        }
    }

    private List<ProjectEntry> CollectExpired(DateTime now)
    {
        var expired = new List<ProjectEntry>();
        foreach (var entry in projects.Values)
        {
            if (IsExpired(entry, now))
            {
                expired.Add(entry);
            }
        }

        return expired;
    }

    private bool IsExpired(ProjectEntry entry, DateTime now)
    {
        if (entry.ResidentSolution.LoadState == ServerLoadState.LoadFailed)
        {
            return entry.InFlightCount == 0 && entry.FailureLeaseReleased;
        }

        var idleBeyondTtl = now - entry.LastUsedUtc > idleTtl;
        if (entry.InFlightCount > 0)
        {
            if (idleBeyondTtl)
            {
                entry.PendingEviction = true;
            }

            return false;
        }

        return idleBeyondTtl || entry.PendingEviction;
    }

    private DateTime UtcNow() => options.Clock.GetUtcNow().UtcDateTime;

    private static TimeSpan ResolvePositive(TimeSpan value, TimeSpan fallback) =>
        value > TimeSpan.Zero ? value : fallback;

    private static string Canonicalize(string solutionPath)
    {
        if (!Path.IsPathFullyQualified(solutionPath))
        {
            throw new ArgumentException("The solution path must be absolute.", nameof(solutionPath));
        }

        return Path.GetFullPath(solutionPath);
    }
}
