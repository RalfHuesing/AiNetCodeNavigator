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

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Successful accesses transfer ownership to the caller; idle session ownership stays with this registry until eviction.")]
    internal async Task<Result<AssemblySessionAccess>> AcquireAsync(string assemblyPath, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        Entry entry;
        List<Entry> retired;
        var capacityExceeded = false;
        lock (gate)
        {
            retired = RetireIdleSessions(DateTime.UtcNow);
            if (!sessions.TryGetValue(fullPath, out entry!))
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

            if (!capacityExceeded)
            {
                entry.ActiveAccesses++;
                entry.LastAccessUtc = DateTime.UtcNow;
            }
        }

        await DisposeEntriesAsync(retired).ConfigureAwait(false);
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
                NavigationErrorCodes.TargetMismatch,
                "The assembly handoff belongs to an assembly that is not resident in this server process.");
        }

        return await AcquireAsync(matchingPath, cancellationToken).ConfigureAwait(false);
    }

    internal async Task ExpireIdleSessionsAsync(DateTime nowUtc)
    {
        List<Entry> retired;
        lock (gate) retired = RetireIdleSessions(nowUtc);
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

    private List<Entry> RetireIdleSessions(DateTime nowUtc)
    {
        var cutoff = nowUtc - IdleLifetime;
        var retired = sessions
            .Where(pair => pair.Value.ActiveAccesses == 0 && pair.Value.LastAccessUtc < cutoff)
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
