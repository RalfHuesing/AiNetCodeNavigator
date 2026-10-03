#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;

namespace AiNetCodeNavigator.Core.Assemblies;

/// <summary>Keeps recently used assembly workspaces resident so handoffs can reuse their snapshot.</summary>
internal sealed class AssemblyAnalysisSessionRegistry : IAsyncDisposable
{
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(10);
    internal const int MaxResidentSessions = 32;
    private static readonly Lazy<AssemblyAnalysisSessionRegistry> DefaultRegistry = new(() => new());

    private readonly object gate = new();
    private readonly Dictionary<string, Entry> sessions = new(StringComparer.OrdinalIgnoreCase);

    internal static AssemblyAnalysisSessionRegistry Default => DefaultRegistry.Value;

    /// <summary>Returns the active access count without acquiring or refreshing a session.</summary>
    internal int GetActiveAccessCount(string assemblyPath)
    {
        var canonicalPath = Path.GetFullPath(assemblyPath);
        lock (gate)
        {
            return sessions.TryGetValue(canonicalPath, out var entry) ? entry.ActiveAccesses : 0;
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Successful accesses transfer ownership to the caller; idle session ownership stays with this registry until eviction.")]
    internal Task<Result<AssemblySessionAccess>> AcquireAsync(string assemblyPath, CancellationToken cancellationToken)
        => AcquireAsync(assemblyPath, cancellationToken, requireResident: false);

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "A successful access transfers ownership of the resident snapshot lease to the caller.")]
    internal Task<Result<AssemblySessionAccess>> AcquireResidentAsync(string assemblyPath, CancellationToken cancellationToken)
        => AcquireAsync(assemblyPath, cancellationToken, requireResident: true);

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "A successful session access transfers its lease to the caller; created sessions remain owned by this registry until eviction.")]
    private async Task<Result<AssemblySessionAccess>> AcquireAsync(string assemblyPath, CancellationToken cancellationToken, bool requireResident)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        Entry entry;
        List<Entry> retired;
        var capacityExceeded = false;
        var ownerNotResident = false;
        lock (gate)
        {
            retired = RetireIdleSessions(DateTime.UtcNow);
            if (!sessions.TryGetValue(fullPath, out entry!))
            {
                if (requireResident)
                {
                    entry = null!;
                    ownerNotResident = true;
                }
                else
                {
                    while (sessions.Count >= MaxResidentSessions)
                    {
                        var oldest = sessions.Values
                            .Where(candidate => candidate.ActiveAccesses == 0)
                            .OrderBy(candidate => candidate.LastAccessUtc)
                            .FirstOrDefault();
                        if (oldest is null) break;
                        sessions.Remove(oldest.Path);
                        retired.Add(oldest);
                    }

                    if (sessions.Count >= MaxResidentSessions)
                    {
                        entry = null!;
                        capacityExceeded = true;
                    }
                    else
                    {
                        entry = new Entry(fullPath, new AssemblyAnalysisSession(fullPath));
                        sessions.Add(fullPath, entry);
                    }
                }
            }

            if (!capacityExceeded && !ownerNotResident)
            {
                entry.ActiveAccesses++;
                entry.LastAccessUtc = DateTime.UtcNow;
            }
        }

        await DisposeEntriesAsync(retired).ConfigureAwait(false);
        if (ownerNotResident)
        {
            return Result<AssemblySessionAccess>.Failure(
                NavigationErrorCodes.HandoffOwnerUnresident,
                "The assembly owner for this handoff is no longer resident in this server process.",
                "Repeat the original discovery query on the owner target, then use its current ownerTargetPath and handoff.");
        }
        if (capacityExceeded)
        {
            return Result<AssemblySessionAccess>.Failure(
                NavigationErrorCodes.AssemblySessionLimit,
                $"All {MaxResidentSessions} assembly session slots are active.",
                "Retry after an active assembly navigation operation completes.");
        }

        AssemblySessionRefreshResult refresh;
        try
        {
            refresh = await entry.Session.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(entry);
            throw;
        }
        if (entry.Session.CurrentGeneration is null
            || refresh.Status is AssemblySessionStatus.Failed or AssemblySessionStatus.Degraded)
        {
            Release(entry);
            var message = refresh.Diagnostics.Count == 0
                ? "The assembly could not be analyzed."
                : string.Join(" ", refresh.Diagnostics.Select(diagnostic => diagnostic.Message));
            if (refresh.Failure?.Kind == AssemblySessionFailureKind.MetadataUnavailable
                || message.Contains(AssemblyReferenceResolver.NativeMetadataFailureMessage, StringComparison.Ordinal)
                || message.Contains("BadImageFormatException", StringComparison.OrdinalIgnoreCase))
            {
                return Result<AssemblySessionAccess>.Failure(
                    NavigationErrorCodes.InvalidAssembly,
                    $"'{Path.GetFileName(fullPath)}' is not a valid managed .NET assembly.",
                    "assemblyPath must point to a managed .NET .dll or .exe containing IL.");
            }

            return Result<AssemblySessionAccess>.Failure(NavigationErrorCodes.WorkspaceDiagnostic, message, fullPath);
        }

        var snapshotLease = entry.Session.AcquireSnapshot();
        if (snapshotLease is null)
        {
            Release(entry);
            return Result<AssemblySessionAccess>.Failure(NavigationErrorCodes.WorkspaceDiagnostic, "The assembly snapshot is no longer available.", fullPath);
        }

        var generation = snapshotLease.Generation;
        return Result<AssemblySessionAccess>.Success(new AssemblySessionAccess(this, entry, snapshotLease, generation));
    }

    internal async Task<Result<AssemblySessionAccess>> AcquireByTargetTokenAsync(
        string targetToken,
        CancellationToken cancellationToken)
    {
        await ExpireIdleSessionsAsync(DateTime.UtcNow).ConfigureAwait(false);
        string? matchingPath;
        lock (gate)
        {
            matchingPath = sessions.Keys.FirstOrDefault(path =>
                SymbolHandoffToken.TryCreateTarget(path, out var candidate)
                && string.Equals(candidate, targetToken, StringComparison.Ordinal));
        }

        if (matchingPath is null)
        {
            return Result<AssemblySessionAccess>.Failure(
                NavigationErrorCodes.HandoffOwnerUnresident,
                "The assembly owner for this handoff is no longer resident in this server process.",
                "Repeat the original discovery query on the owner target, then use its current ownerTargetPath and handoff.");
        }

        return await AcquireResidentAsync(matchingPath, cancellationToken).ConfigureAwait(false);
    }

    internal async Task ExpireIdleSessionsAsync(DateTime nowUtc, string? assemblyPath = null)
    {
        List<Entry> retired;
        lock (gate) retired = RetireIdleSessions(nowUtc, assemblyPath is null ? null : Path.GetFullPath(assemblyPath));
        await DisposeEntriesAsync(retired).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        List<Entry> retired;
        lock (gate)
        {
            retired = sessions.Values.ToList();
            sessions.Clear();
        }

        await DisposeEntriesAsync(retired).ConfigureAwait(false);
    }

    private List<Entry> RetireIdleSessions(DateTime nowUtc, string? assemblyPath = null)
    {
        var cutoff = nowUtc - IdleLifetime;
        var retired = sessions
            .Where(pair => pair.Value.ActiveAccesses == 0 && pair.Value.LastAccessUtc < cutoff
                && (assemblyPath is null || string.Equals(pair.Key, assemblyPath, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => pair.Value)
            .ToList();
        foreach (var entry in retired) sessions.Remove(entry.Path);
        return retired;
    }

    private void Release(Entry entry)
    {
        lock (gate)
        {
            entry.ActiveAccesses = Math.Max(0, entry.ActiveAccesses - 1);
            entry.LastAccessUtc = DateTime.UtcNow;
        }
    }

    private static async Task DisposeEntriesAsync(IEnumerable<Entry> entries)
    {
        foreach (var entry in entries) await entry.Session.DisposeAsync().ConfigureAwait(false);
    }

    internal sealed class Entry(string path, AssemblyAnalysisSession session)
    {
        internal string Path { get; } = path;
        internal AssemblyAnalysisSession Session { get; } = session;
        internal DateTime LastAccessUtc { get; set; } = DateTime.UtcNow;
        internal int ActiveAccesses { get; set; }
    }

    internal sealed class AssemblySessionAccess : IAsyncDisposable
    {
        private readonly AssemblyAnalysisSessionRegistry owner;
        private readonly Entry entry;
        private readonly AssemblyAnalysisSnapshotLease snapshotLease;
        private int disposed;

        internal AssemblySessionAccess(
            AssemblyAnalysisSessionRegistry owner,
            Entry entry,
            AssemblyAnalysisSnapshotLease snapshotLease,
            AssemblySessionGeneration generation)
        {
            this.owner = owner;
            this.entry = entry;
            this.snapshotLease = snapshotLease;
            Generation = generation;
        }

        internal AssemblySessionGeneration Generation { get; }
        internal string Path => entry.Path;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                snapshotLease.Dispose();
                owner.Release(entry);
            }

            return ValueTask.CompletedTask;
        }
    }
}
