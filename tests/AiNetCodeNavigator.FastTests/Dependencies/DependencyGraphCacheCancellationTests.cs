#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;

namespace AiNetCodeNavigator.FastTests.Dependencies;

[Trait("Category", "Unit")]
public sealed class DependencyGraphCacheCancellationTests
{
    [Fact]
    public async Task CollectAsync_CancelsOnlyUnsharedNeedAndRetriesItAfterQuiescence()
    {
        using var fixture = CreateThreeDocumentFixture();
        using var canceledRequest = new CancellationTokenSource();
        var scans = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var aStarted = NewSignal();
        var releaseFirstA = NewSignal();
        var bStarted = NewSignal();
        var releaseB = NewSignal();
        var retryAStarted = NewSignal();
        var releaseRetryA = NewSignal();
        var firstSubscriptions = NewSignal();
        var bSubscribed = NewSignal();
        var cSubscribed = NewSignal();
        var aQuiescing = NewSignal();
        var subscriptionCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var observer = new DependencyGraphCollectionObserver(
            DocumentCollected: document =>
            {
                var count = scans.AddOrUpdate(document.Name, 1, (_, current) => current + 1);
                if (document.Name == "a.cs" && count == 1)
                {
                    aStarted.TrySetResult();
                    releaseFirstA.Task.GetAwaiter().GetResult();
                }
                else if (document.Name == "b.cs")
                {
                    bStarted.TrySetResult();
                    releaseB.Task.GetAwaiter().GetResult();
                }
                else if (document.Name == "a.cs" && count == 2)
                {
                    retryAStarted.TrySetResult();
                    releaseRetryA.Task.GetAwaiter().GetResult();
                }
            },
            SubscriptionAdded: document =>
            {
                var count = subscriptionCounts.AddOrUpdate(document.Name, 1, (_, current) => current + 1);
                if (document.Name == "a.cs" && count == 1) firstSubscriptions.TrySetResult();
                if (document.Name == "b.cs") bSubscribed.TrySetResult();
                if (document.Name == "c.cs") cSubscribed.TrySetResult();
            },
            QuiescingWait: document =>
            {
                if (document.Name == "a.cs") aQuiescing.TrySetResult();
            });
        await using var cache = new DependencyGraphCache(observer: observer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            var requestA = CollectWindowAsync(cache, fixture.Solution, offset: 0, maxDocuments: 2,
                canceledRequest.Token);
            await Task.WhenAll(firstSubscriptions.Task, aStarted.Task).WaitAsync(timeout.Token);
            var requestB = CollectWindowAsync(cache, fixture.Solution, offset: 1, maxDocuments: 2,
                CancellationToken.None);
            await Task.WhenAll(bSubscribed.Task, cSubscribed.Task).WaitAsync(timeout.Token);

            await canceledRequest.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestA);

            var replacementA = CollectWindowAsync(cache, fixture.Solution, offset: 0, maxDocuments: 2,
                CancellationToken.None);
            await aQuiescing.Task.WaitAsync(timeout.Token);
            releaseFirstA.TrySetResult();
            await Task.WhenAll(retryAStarted.Task, bStarted.Task).WaitAsync(timeout.Token);
            Assert.False(requestB.IsCompleted);
            Assert.False(replacementA.IsCompleted);
            releaseB.TrySetResult();

            var sharedRequest = await requestB.WaitAsync(timeout.Token);
            Assert.Equal(2, sharedRequest.CoveredDocumentCount);
            Assert.Equal(1, sharedRequest.NewSemanticScanCount);
            Assert.Contains(sharedRequest.TypeDependencies,
                edge => edge.FromTypeName == "B" && edge.ToTypeName == "C");
            Assert.Equal(1, scans.GetValueOrDefault("b.cs"));
            Assert.Equal(1, scans.GetValueOrDefault("c.cs"));
            Assert.Equal(2, scans.GetValueOrDefault("a.cs"));

            releaseRetryA.TrySetResult();
            var replacementResult = await replacementA.WaitAsync(timeout.Token);
            Assert.Equal(2, replacementResult.CoveredDocumentCount);
            Assert.Equal(1, replacementResult.NewSemanticScanCount);
            Assert.Contains(replacementResult.TypeDependencies,
                edge => edge.FromTypeName == "A" && edge.ToTypeName == "B");
            Assert.Contains(replacementResult.TypeDependencies,
                edge => edge.FromTypeName == "B" && edge.ToTypeName == "C");
            Assert.Equal(1, scans.GetValueOrDefault("b.cs"));
            Assert.Equal(1, scans.GetValueOrDefault("c.cs"));
        }
        finally
        {
            releaseFirstA.TrySetResult();
            releaseB.TrySetResult();
            releaseRetryA.TrySetResult();
            await canceledRequest.CancelAsync();
        }
    }

    [Fact]
    public async Task CollectAsync_AllSubscribersCancelSkipsUnstartedNeedsAndRetriesTheWindow()
    {
        using var fixture = CreateThreeDocumentFixture();
        using var canceledRequest = new CancellationTokenSource();
        var scans = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var subscribed = 0;
        var allSubscribed = NewSignal();
        var firstStarted = NewSignal();
        var releaseFirst = NewSignal();
        var quiescing = NewSignal();
        var observer = new DependencyGraphCollectionObserver(
            DocumentCollected: document =>
            {
                scans.AddOrUpdate(document.Name, 1, (_, current) => current + 1);
                if (document.Name == "a.cs")
                {
                    firstStarted.TrySetResult();
                    releaseFirst.Task.GetAwaiter().GetResult();
                }
            },
            SubscriptionAdded: _ =>
            {
                if (Interlocked.Increment(ref subscribed) == 3) allSubscribed.TrySetResult();
            },
            QuiescingWait: _ => quiescing.TrySetResult());
        await using var cache = new DependencyGraphCache(observer: observer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            var canceled = CollectWindowAsync(cache, fixture.Solution, offset: 0, maxDocuments: null,
                canceledRequest.Token);
            await Task.WhenAll(allSubscribed.Task, firstStarted.Task).WaitAsync(timeout.Token);
            await canceledRequest.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);

            var retry = CollectWindowAsync(cache, fixture.Solution, offset: 0, maxDocuments: null,
                CancellationToken.None);
            await quiescing.Task.WaitAsync(timeout.Token);
            releaseFirst.TrySetResult();

            var result = await retry.WaitAsync(timeout.Token);
            Assert.Equal(3, result.CoveredDocumentCount);
            Assert.Equal(3, result.NewSemanticScanCount);
            Assert.Equal(1, scans.GetValueOrDefault("b.cs"));
            Assert.Equal(1, scans.GetValueOrDefault("c.cs"));
            Assert.Equal(2, scans.GetValueOrDefault("a.cs"));
        }
        finally
        {
            releaseFirst.TrySetResult();
            await canceledRequest.CancelAsync();
        }
    }

    [Fact]
    public async Task DisposeAsync_AwaitsBlockedCollectionWithoutHoldingCacheLock()
    {
        using var fixture = CreateThreeDocumentFixture();
        var started = NewSignal();
        var releaseScan = NewSignal();
        var observer = new DependencyGraphCollectionObserver(DocumentCollected: _ =>
        {
            started.TrySetResult();
            releaseScan.Task.GetAwaiter().GetResult();
        });
        var cache = new DependencyGraphCache(observer: observer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        Task? dispose = null;
        try
        {
            var operation = CollectWindowAsync(cache, fixture.Solution, offset: 0, maxDocuments: 1,
                CancellationToken.None);
            await started.Task.WaitAsync(timeout.Token);
            dispose = cache.DisposeAsync().AsTask();
            var retainedBucketCount = await Task.Run(() => cache.RetainedBucketCount).WaitAsync(timeout.Token);
            Assert.Equal(0, retainedBucketCount);
            Assert.False(dispose.IsCompleted);

            releaseScan.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            await dispose.WaitAsync(timeout.Token);
        }
        finally
        {
            releaseScan.TrySetResult();
            if (dispose is null) dispose = cache.DisposeAsync().AsTask();
            await dispose;
        }
        Assert.Equal(0, cache.RetainedBucketCount);
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task<DependencyGraphCollection> CollectWindowAsync(
        DependencyGraphCache cache,
        Microsoft.CodeAnalysis.Solution solution,
        int offset,
        int? maxDocuments,
        CancellationToken cancellationToken) =>
        cache.CollectAsync(solution,
            new DependencyGraphCollectionOptions(DocumentOffset: offset, MaxDocuments: maxDocuments),
            "C:\\cache\\overlap.slnx", snapshotTicket: 1, cancellationToken);

    private static TestSolutionHandle CreateThreeDocumentFixture() => TestWorkspaceBuilder.CreateSolution(
        @"C:\VirtualRepo\CacheCancellation.slnx",
        new ProjectSpec("App", [
            ("a.cs", "namespace App; public sealed class A { public B Value { get; set; } = new(); }"),
            ("b.cs", "namespace App; public sealed class B { public C Value { get; set; } = new(); }"),
            ("c.cs", "namespace App; public sealed class C { }")],
            VirtualProjectDirectory: "src/App"));
}
