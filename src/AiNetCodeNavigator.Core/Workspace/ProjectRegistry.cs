#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Verwaltet residente Solution-Instanzen im Arbeitsspeicher mit Least-Recently-Used (LRU)-Verdrängung,
/// TTL-Prüfung und nebenläufigkeitssicherer Deduplizierung paralleler Anfragen.
/// </summary>
public sealed class ProjectRegistry : IAsyncDisposable, IDisposable
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, ProjectEntry> projects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProjectCreationReservation> reservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ProjectRegistryOptions options;
    private readonly TimeSpan idleTtl;
    private readonly CancellationTokenSource tickSource = new();
    private readonly Task tickTask;
    private int disposed;

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
        var key = Canonicalize(solutionPath);
        var retired = new List<ResidentSolution>();
        var result = TryAdoptOrCreate(key, retired);
        foreach (var server in retired)
        {
            server.Dispose();
        }

        return result;
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
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await tickSource.CancelAsync().ConfigureAwait(false);
        try
        {
            await tickTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (tickSource.IsCancellationRequested)
        {
        }

        List<ResidentSolution> remaining;
        lock (gate)
        {
            remaining = projects.Values.Select(entry => entry.ResidentSolution).ToList();
            projects.Clear();
        }

        foreach (var server in remaining)
        {
            await server.DisposeAsync().ConfigureAwait(false);
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
            await entry.ResidentSolution.DisposeAsync().ConfigureAwait(false);
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
    private ProjectLeaseResult TryAdoptOrCreate(string key, List<ResidentSolution> retired)
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

    private ProjectLease? FindResidentBeforeCreationBarrier(string key, List<ResidentSolution> retired)
    {
        lock (gate)
        {
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
        List<ResidentSolution> retired)
    {
        var created = attempt.Creation;
        if (!created.Succeeded)
        {
            RemoveReservation(key, reservation);
            return ProjectLeaseResult.Failure(created.ErrorCode!, created.ErrorMessage!);
        }

        lock (gate)
        {
            if (projects.TryGetValue(key, out var raced))
            {
                RemoveReservationUnderLock(key, reservation);
                if (created.Solution is not null && !ReferenceEquals(created.Solution, raced.ResidentSolution))
                {
                    retired.Add(created.Solution);
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

    private void RemoveReservationUnderLock(string key, ProjectCreationReservation reservation)
    {
        if (reservations.TryGetValue(key, out var current) && ReferenceEquals(current, reservation))
        {
            reservations.Remove(key);
        }
    }

    private ProjectLease? FindAdoptable(string key, List<ResidentSolution> retired)
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
            retired.Add(entry.ResidentSolution);
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
                throw new InvalidOperationException("Der Projekt-Lease ist nicht mehr resident.");
            }

            return SnapshotOf(entry);
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

    private void EvictLeastRecentlyUsed(List<ResidentSolution> retired)
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
            retired.Add(victim.ResidentSolution);
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
            throw new ArgumentException("Der Solution-Pfad muss absolut sein.", nameof(solutionPath));
        }

        return Path.GetFullPath(solutionPath);
    }
}
