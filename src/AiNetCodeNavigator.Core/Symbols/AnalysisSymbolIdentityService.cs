#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Core.Models;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Runtime-owned single-flight source identity service. Completed values contain scalar evidence only;
/// the weak Solution key and request-owned reference projection are never copied into memo values.
/// </summary>
internal sealed class AnalysisSymbolIdentityService : IAsyncDisposable
{
    private sealed class Entry
    {
        internal readonly object Gate = new();
        internal SourceIdentityFingerprintData? Completed;
        internal Task<Result<SourceIdentityFingerprintData>>? InFlight;
    }

    private readonly ConditionalWeakTable<Solution, Entry> entries = new();
    private readonly ConcurrentDictionary<long, Task> activeComputations = new();
    private readonly CancellationTokenSource shutdown;
    private readonly Func<SourceIdentityValidatedSnapshot, CancellationToken, Task<Result<SourceIdentityFingerprintData>>> compute;
    private readonly object lifetimeGate = new();
    private long nextTicket;
    private long nextComputation;
    private bool disposeStarted;

    internal AnalysisSymbolIdentityService(CancellationToken runtimeLifetime = default)
        : this(SourceAnalysisIdentityEncoder.ComputeAsync, runtimeLifetime)
    {
    }

    internal AnalysisSymbolIdentityService(
        Func<SourceIdentityValidatedSnapshot, CancellationToken, Task<Result<SourceIdentityFingerprintData>>> compute,
        CancellationToken runtimeLifetime = default)
    {
        this.compute = compute ?? throw new ArgumentNullException(nameof(compute));
        shutdown = CancellationTokenSource.CreateLinkedTokenSource(runtimeLifetime);
    }

    internal async Task<Result<SourceIdentityRequest>> GetForSourceAsync(
        SourceIdentityValidatedSnapshot validatedSnapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validatedSnapshot);
        ArgumentNullException.ThrowIfNull(validatedSnapshot.Solution);
        ArgumentNullException.ThrowIfNull(validatedSnapshot.Inputs);

        foreach (var projectInputs in validatedSnapshot.Inputs.Projects)
        {
            if (projectInputs.IsSupported) continue;
            var project = validatedSnapshot.Solution.GetProject(projectInputs.OwnerProjectId);
            var projectName = project?.Name ?? projectInputs.OwnerProjectId.ToString();
            var reason = projectInputs.UnsupportedReason
                ?? "The workspace owner could not establish supported source identity provenance.";
            return Result<SourceIdentityRequest>.Failure(NavigationErrorCodes.WorkspaceDiagnostic,
                $"Project '{projectName}' has unsupported source identity provenance: {reason}");
        }

        Task<Result<SourceIdentityFingerprintData>>? task = null;
        SourceIdentityFingerprintData? completed = null;
        lock (lifetimeGate)
        {
            if (disposeStarted || shutdown.IsCancellationRequested)
                return Result<SourceIdentityRequest>.Failure(NavigationErrorCodes.WorkspaceDiagnostic,
                    "The source identity service is shutting down.");

            var entry = entries.GetValue(validatedSnapshot.Solution, static _ => new Entry());
            lock (entry.Gate)
            {
                if (entry.Completed is { } completedValue)
                    completed = completedValue;
                else if (entry.InFlight is null)
                {
                    var computationId = Interlocked.Increment(ref nextComputation);
                    var completion = new TaskCompletionSource<Result<SourceIdentityFingerprintData>>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    entry.InFlight = completion.Task;

                    var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var work = Task.Run(async () =>
                    {
                        await start.Task.ConfigureAwait(false);
                        await ComputeAndPublishAsync(validatedSnapshot, entry, completion).ConfigureAwait(false);
                    }, CancellationToken.None);
                    activeComputations.TryAdd(computationId, work);
                    _ = work.ContinueWith(
                        static (completedWork, state) =>
                        {
                            var (owner, id) = ((AnalysisSymbolIdentityService Owner, long Id))state!;
                            owner.activeComputations.TryRemove(id, out var removed);
                            _ = completedWork;
                            _ = removed;
                        },
                        (this, computationId), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    start.SetResult();
                }

                if (completed is null) task = entry.InFlight;
            }
        }

        if (completed is not null)
            return await CreateRequestAsync(validatedSnapshot.Solution, completed, cancellationToken).ConfigureAwait(false);

        Result<SourceIdentityFingerprintData> result;
        try
        {
            result = await task!.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Result<SourceIdentityRequest>.Failure(NavigationErrorCodes.WorkspaceDiagnostic,
                "Source identity computation was cancelled during runtime shutdown.");
        }

        if (!result.IsSuccess)
            return Result<SourceIdentityRequest>.Failure(result.Error!.Value);
        return await CreateRequestAsync(validatedSnapshot.Solution, result.Value!, cancellationToken).ConfigureAwait(false);
    }

    private async Task ComputeAndPublishAsync(
        SourceIdentityValidatedSnapshot validatedSnapshot,
        Entry entry,
        TaskCompletionSource<Result<SourceIdentityFingerprintData>> completion)
    {
        Result<SourceIdentityFingerprintData> result;
        try
        {
            result = await compute(validatedSnapshot, shutdown.Token).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                var value = result.Value!;
                result = Result<SourceIdentityFingerprintData>.Success(value with { Ticket = Interlocked.Increment(ref nextTicket) });
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            result = Result<SourceIdentityFingerprintData>.Failure(NavigationErrorCodes.WorkspaceDiagnostic,
                "Source identity computation was cancelled during runtime shutdown.");
        }
        catch (Exception exception)
        {
            result = Result<SourceIdentityFingerprintData>.Failure(NavigationErrorCodes.WorkspaceDiagnostic,
                $"Source snapshot identity failed: {exception.Message}");
        }

        if (result.IsSuccess)
        {
            lock (entry.Gate)
            {
                entry.InFlight = null;
                entry.Completed = result.Value;
            }
        }
        else
        {
            lock (lifetimeGate)
            lock (entry.Gate)
            {
                entry.InFlight = null;
                if (entries.TryGetValue(validatedSnapshot.Solution, out var current) && ReferenceEquals(current, entry))
                    entries.Remove(validatedSnapshot.Solution);
            }
        }
        completion.TrySetResult(result);
    }

    private static async Task<Result<SourceIdentityRequest>> CreateRequestAsync(
        Solution solution,
        SourceIdentityFingerprintData fingerprint,
        CancellationToken cancellationToken)
    {
        var formatter = await SourceReferenceFormattingContext.CreateAsync(solution, cancellationToken).ConfigureAwait(false);
        var markers = BuildRequestProjectMarkers(solution, fingerprint.ProjectMarkers);
        var ownerContexts = BuildOwnerContextFingerprints(solution, fingerprint.ProjectMarkers);
        var identity = AnalysisSymbolIdentity.ForSource(fingerprint.CanonicalPath, fingerprint.ContentHash) with
        {
            SourceProjectMarkers = markers,
        };
        return Result<SourceIdentityRequest>.Success(new SourceIdentityRequest(identity, formatter, fingerprint.Ticket, ownerContexts));
    }

    private static IReadOnlyDictionary<ProjectId, string> BuildRequestProjectMarkers(
        Solution solution,
        IReadOnlyList<SourceIdentityProjectMarker> candidates)
    {
        // Entries are scalar-only and aligned with this exact immutable Solution's project order.
        // No Roslyn object or workspace ProjectId is retained in the completed memo value.
        if (candidates.Count != solution.Projects.Count()) return new Dictionary<ProjectId, string>();
        var markerGroups = solution.Projects.Select((project, index) =>
                (project.Id, Marker: candidates[index].Marker))
            .Where(item => item.Marker is not null)
            .Select(item => (item.Id, Marker: item.Marker!))
            .ToList();

        return markerGroups.GroupBy(item => item.Marker, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Single().Id, group => group.Key);
    }

    private static ImmutableDictionary<ProjectId, string> BuildOwnerContextFingerprints(
        Solution solution,
        IReadOnlyList<SourceIdentityProjectMarker> candidates)
    {
        if (candidates.Count != solution.Projects.Count()) return ImmutableDictionary<ProjectId, string>.Empty;
        return solution.Projects.Select((project, index) => (project.Id, candidates[index].ContextFingerprint))
            .ToImmutableDictionary(item => item.Id, item => item.ContextFingerprint);
    }

    public async ValueTask DisposeAsync()
    {
        Task[] active;
        lock (lifetimeGate)
        {
            if (disposeStarted) return;
            disposeStarted = true;
            active = activeComputations.Values.ToArray();
        }
        await shutdown.CancelAsync().ConfigureAwait(false);
        try { await Task.WhenAll(active).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        shutdown.Dispose();
    }
}

internal sealed record SourceIdentityRequest(
    AnalysisSymbolIdentity Identity,
    SourceReferenceFormattingContext SourceReferences,
    long SnapshotTicket,
    ImmutableDictionary<ProjectId, string> OwnerContextFingerprints)
{
    internal bool IsForSolution(Solution solution) => SourceReferences.IsForSolution(solution);

    internal string? FormatHandoff(ISymbol symbol, Solution solution) =>
        SourceReferences.IsForSolution(solution) ? SourceReferences.Format(symbol) : null;
}
