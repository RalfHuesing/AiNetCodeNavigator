#nullable enable

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class AnalysisSymbolIdentityServiceTests
{
    [Fact]
    public async Task ConcurrentRequestsForExactSolutionShareOneComputationAndTicket()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = EmptySnapshot(workspace.CurrentSolution);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var computeCount = 0;
        await using var service = new AnalysisSymbolIdentityService(async (_, token) =>
        {
            Interlocked.Increment(ref computeCount);
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return SuccessFingerprint();
        });

        var first = service.GetForSourceAsync(snapshot);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service.GetForSourceAsync(snapshot);
        release.TrySetResult();
        var results = await Task.WhenAll(first, second);
        var completedHit = await service.GetForSourceAsync(snapshot);

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(results[0].Value!.SnapshotTicket, results[1].Value!.SnapshotTicket);
        Assert.True(completedHit.IsSuccess);
        Assert.Equal(results[0].Value!.SnapshotTicket, completedHit.Value!.SnapshotTicket);
        Assert.Equal(1, computeCount);
    }

    [Fact]
    public async Task CancellingOneWaiterDoesNotCancelSharedRuntimeComputation()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = EmptySnapshot(workspace.CurrentSolution);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var computeCount = 0;
        CancellationToken observedComputeToken = default;
        await using var service = new AnalysisSymbolIdentityService(async (_, token) =>
        {
            Interlocked.Increment(ref computeCount);
            observedComputeToken = token;
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return SuccessFingerprint();
        });

        using var waiterCancellation = new CancellationTokenSource();
        var cancelledWaiter = service.GetForSourceAsync(snapshot, waiterCancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var remainingWaiter = service.GetForSourceAsync(snapshot);
        await waiterCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter);
        Assert.False(observedComputeToken.IsCancellationRequested);
        release.TrySetResult();
        var result = await remainingWaiter;

        Assert.True(result.IsSuccess);
        Assert.Equal(1, computeCount);
    }

    [Fact]
    public async Task FailedFlightIsRemovedAndLaterRequestRetries()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = EmptySnapshot(workspace.CurrentSolution);
        var computeCount = 0;
        await using var service = new AnalysisSymbolIdentityService((_, _) =>
        {
            var count = Interlocked.Increment(ref computeCount);
            return Task.FromResult(count == 1
                ? Result<SourceIdentityFingerprintData>.Failure(NavigationErrorCodes.WorkspaceDiagnostic, "fixture failure")
                : SuccessFingerprint());
        });

        var failed = await service.GetForSourceAsync(snapshot);
        var retried = await service.GetForSourceAsync(snapshot);

        Assert.False(failed.IsSuccess);
        Assert.Equal(NavigationErrorCodes.WorkspaceDiagnostic, failed.Error!.Value.Code);
        Assert.True(retried.IsSuccess);
        Assert.Equal(2, computeCount);
        Assert.True(retried.Value!.SnapshotTicket > 0);
    }

    [Fact]
    public async Task CancelledComputationIsRemovedAndLaterRequestRetries()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = EmptySnapshot(workspace.CurrentSolution);
        var computeCount = 0;
        await using var service = new AnalysisSymbolIdentityService((_, _) =>
        {
            var count = Interlocked.Increment(ref computeCount);
            if (count == 1)
                return Task.FromCanceled<Result<SourceIdentityFingerprintData>>(new CancellationToken(canceled: true));
            return Task.FromResult(SuccessFingerprint());
        });

        var cancelled = await service.GetForSourceAsync(snapshot);
        var retried = await service.GetForSourceAsync(snapshot);

        Assert.False(cancelled.IsSuccess);
        Assert.Equal(NavigationErrorCodes.WorkspaceDiagnostic, cancelled.Error!.Value.Code);
        Assert.True(retried.IsSuccess);
        Assert.Equal(2, computeCount);
        Assert.True(retried.Value!.SnapshotTicket > 0);
    }

    [Fact]
    public async Task ValidatedNewSolutionsReceiveMonotonicTickets()
    {
        using var firstWorkspace = new AdhocWorkspace();
        using var secondWorkspace = new AdhocWorkspace();
        await using var service = new AnalysisSymbolIdentityService((_, _) => Task.FromResult(SuccessFingerprint()));

        var first = await service.GetForSourceAsync(EmptySnapshot(firstWorkspace.CurrentSolution));
        var second = await service.GetForSourceAsync(EmptySnapshot(secondWorkspace.CurrentSolution));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(second.Value!.SnapshotTicket > first.Value!.SnapshotTicket);
    }

    [Fact]
    public async Task RuntimeDisposalCancelsAndAwaitsAnActiveComputation()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = EmptySnapshot(workspace.CurrentSolution);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new AnalysisSymbolIdentityService(async (_, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { finished.TrySetResult(); }
            return SuccessFingerprint();
        });

        var request = service.GetForSourceAsync(snapshot);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DisposeAsync();

        Assert.True(finished.Task.IsCompleted);
        var result = await request;
        Assert.False(result.IsSuccess);
        Assert.Equal(NavigationErrorCodes.WorkspaceDiagnostic, result.Error!.Value.Code);
    }

    [Fact]
    public async Task CompletedMemoDoesNotKeepWeakSolutionKeyAlive()
    {
        var service = new AnalysisSymbolIdentityService((_, _) => Task.FromResult(SuccessFingerprint()));
        var weakSolution = await CreateCompletedWeakSolutionAsync(service);

        for (var attempt = 0; attempt < 8 && weakSolution.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(10);
        }

        Assert.False(weakSolution.IsAlive);
        await service.DisposeAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> CreateCompletedWeakSolutionAsync(AnalysisSymbolIdentityService service)
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var weak = new WeakReference(solution);
        var result = await service.GetForSourceAsync(EmptySnapshot(solution));
        Assert.True(result.IsSuccess);
        result = default;
        solution = null!;
        workspace.Dispose();
        return weak;
    }

    private static SourceIdentityValidatedSnapshot EmptySnapshot(Solution solution) => new(
        solution,
        new SourceIdentityValidatedInputs(ImmutableArray<SourceIdentityProjectProvenance>.Empty,
            ImmutableArray<SourceIdentityMetadataReferenceEvidence>.Empty));

    private static Result<SourceIdentityFingerprintData> SuccessFingerprint() =>
        Result<SourceIdentityFingerprintData>.Success(new SourceIdentityFingerprintData(
            @"C:\VirtualRepo\Identity.slnx", new string('A', 64), ImmutableArray<SourceIdentityProjectMarker>.Empty, 0));
}
