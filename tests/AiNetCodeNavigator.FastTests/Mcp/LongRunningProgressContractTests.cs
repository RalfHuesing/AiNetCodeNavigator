using System.Diagnostics;
using System.Globalization;
using System.Text;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Protocol;

namespace AiNetCodeNavigator.FastTests.Mcp;

[Trait("Category", "Unit")]
public sealed class LongRunningProgressContractTests
{
    private const string NextAction = "Wait at least 1000 ms, then repeat the same tool, target and query with this operationToken; preserve an active resultCursor and omit continuationToken.";

    [Fact]
    public async Task FirstWindowOverrideRetainsTheProductionOneSecondPollWindow()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), NavigatorHostRuntime.ResponseWindow);
        Assert.Equal(TimeSpan.FromSeconds(1), NavigatorHostRuntime.PollResponseWindow);
        var release = NewSignal();
        var started = NewSignal();
        var starts = 0;
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(20));
        var request = Request(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref starts);
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return Result("finished");
        });
        try
        {
            var first = await store.RunAsync(request).WaitAsync(TimeSpan.FromSeconds(5));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var token = TokenOf(first);
            var stopwatch = Stopwatch.StartNew();
            var poll = await store.RunAsync(request with { OperationToken = token }).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(900), $"Poll waited only {stopwatch.Elapsed}.");
            AssertRunning(poll, token);
            Assert.Equal(1, starts);
            release.TrySetResult();
            var final = await store.RunAsync(request with { OperationToken = token }).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("finished", TextOf(final), StringComparison.Ordinal);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public void AdmissionPairCoversEveryPhaseAtInt64MaximumWithFixedWidthTokenCost()
    {
        var phases = new[] { "loading", "refreshing", "identifying", "analyzing", "formatting" };
        var tokens = Enumerable.Range(0, 16).Select(_ => McpResponseContinuationStore.CreateOpaqueToken()).ToArray();
        var reference = MeasureRequiredControls(tokens[0], phases);
        foreach (var token in tokens)
        {
            Assert.Equal(39, token.Length);
            Assert.All(token, character => Assert.InRange(character, '0', '9'));
            Assert.Equal(reference, MeasureRequiredControls(token, phases));
            Assert.Equal(reference, McpToolResults.RunningAdmissionMinimum(token));
            foreach (var phase in Enum.GetValues<NavigationAnalysisPhase>())
            {
                var result = McpToolResults.Running(token, new(long.MaxValue, phase), reference.Bytes, reference.Tokens, reference);
                AssertRunning(result, token);
                Assert.Contains($"elapsedMilliseconds: {long.MaxValue}", TextOf(result), StringComparison.Ordinal);
                Assert.Contains($"phase: {phase.ToString().ToLowerInvariant()}", TextOf(result), StringComparison.Ordinal);
                AssertFits(result, reference.Bytes, reference.Tokens);
            }
        }
    }

    [Fact]
    public async Task AdmissionAndPollBudgetRecoveryOfferExecutableFixedPairWithoutRestarting()
    {
        var clock = new TimestampClock();
        var release = NewSignal();
        var ready = new TaskCompletionSource<NavigationOperationProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(20),
            pollResponseWindow: TimeSpan.FromMilliseconds(20), timeProvider: clock);
        var request = Request(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref starts);
            var progress = NavigationOperationProgress.Current!;
            progress.Report(new DependencyRequiredDocumentsKnown(int.MaxValue));
            ready.TrySetResult(progress);
            await release.Task.WaitAsync(cancellationToken);
            return Result("completed once");
        });
        var expected = MeasureRequiredControls(McpResponseContinuationStore.CreateOpaqueToken(),
            ["loading", "refreshing", "identifying", "analyzing", "formatting"]);
        try
        {
            var rejected = await store.RunAsync(request with { MaxResponseTokens = expected.Tokens - 1 });
            AssertBudgetError(rejected, expected, isPoll: false);
            Assert.Equal(0, starts);
            Assert.False(ready.Task.IsCompleted);

            var exactRequest = request with { MaxResponseBytes = expected.Bytes, MaxResponseTokens = expected.Tokens };
            var first = await store.RunAsync(exactRequest).WaitAsync(TimeSpan.FromSeconds(5));
            var token = TokenOf(first);
            var progress = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            AssertRunning(first, token);
            Assert.Equal(1, starts);
            progress.Advance(NavigationAnalysisPhase.Formatting);
            clock.SetTimestamp(long.MaxValue);
            var requiredNow = McpResponseFormatter.CountTokens(RequiredControl(token, "formatting"));
            var tightPoll = await store.RunAsync(exactRequest with { OperationToken = token, MaxResponseTokens = requiredNow - 1 })
                .WaitAsync(TimeSpan.FromSeconds(5));
            AssertBudgetError(tightPoll, expected, isPoll: true);
            Assert.Equal(1, starts);

            var recovered = await store.RunAsync(exactRequest with { OperationToken = token }).WaitAsync(TimeSpan.FromSeconds(5));
            AssertRunning(recovered, token);
            AssertFits(recovered, expected.Bytes, expected.Tokens);
            Assert.Contains($"elapsedMilliseconds: {long.MaxValue}", TextOf(recovered), StringComparison.Ordinal);
            Assert.DoesNotContain("totalDocuments:", TextOf(recovered), StringComparison.Ordinal);
            Assert.Equal(1, starts);
            release.TrySetResult();
            var final = await store.RunAsync(request with { OperationToken = token }).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("completed once", TextOf(final), StringComparison.Ordinal);
            Assert.Equal(1, starts);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task TokenReservationRetriesALiveCollisionBeforeAdmittingTheSecondJob()
    {
        var firstToken = McpResponseContinuationStore.CreateOpaqueToken();
        var secondToken = McpResponseContinuationStore.CreateOpaqueToken();
        var candidates = new Queue<string>([firstToken, firstToken, secondToken]);
        var release = NewSignal();
        var starts = 0;
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(20),
            operationTokenFactory: () => candidates.Dequeue());
        var request = Request(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref starts);
            await release.Task.WaitAsync(cancellationToken);
            return Result("done");
        });
        try
        {
            var first = await store.RunAsync(request).WaitAsync(TimeSpan.FromSeconds(5));
            var second = await store.RunAsync(request with { ArgumentsKey = "name=Other" }).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(firstToken, TokenOf(first));
            Assert.Equal(secondToken, TokenOf(second));
            Assert.Empty(candidates);
            Assert.Equal(2, starts);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task UnrepresentableBudgetErrorNeverStartsAnOperation()
    {
        var starts = 0;
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(20));
        var request = Request((_, _) =>
        {
            Interlocked.Increment(ref starts);
            return Task.FromResult(Result("must not start"));
        }) with { MaxResponseTokens = 1 };
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.RunAsync(request));
        Assert.Equal(0, starts);
    }

    [Fact]
    public async Task SnapshotsRemainImmutableWithConcurrentPhaseReportsAndSaturatingElapsedTime()
    {
        var clock = new TimestampClock();
        var progress = new NavigationOperationProgress(clock);
        var initial = progress.Snapshot();
        Assert.Equal(NavigationAnalysisPhase.Loading, initial.Phase);
        Assert.Null(initial.ProcessedDocuments);
        Assert.Null(initial.TotalDocuments);
        progress.Advance(NavigationAnalysisPhase.Formatting);
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            progress.Advance(NavigationAnalysisPhase.Loading);
            progress.Advance(NavigationAnalysisPhase.Identifying);
            Assert.Equal(NavigationAnalysisPhase.Formatting, progress.Snapshot().Phase);
        })));
        clock.SetTimestamp(2);
        var measured = progress.Snapshot();
        Assert.Equal(2_000L, measured.ElapsedMilliseconds);
        clock.SetTimestamp(1);
        Assert.Equal(2_000L, progress.Snapshot().ElapsedMilliseconds);
        clock.SetTimestamp(long.MaxValue);
        Assert.Equal(long.MaxValue, progress.Snapshot().ElapsedMilliseconds);
        Assert.Equal(0L, initial.ElapsedMilliseconds);
        Assert.Equal(NavigationAnalysisPhase.Loading, initial.Phase);
        Assert.Equal(2_000L, measured.ElapsedMilliseconds);
    }

    [Fact]
    public async Task CoveredNeedsUseExactTicketProjectDocumentIdentityAndNeverInventAGrowingTotal()
    {
        var progress = new NavigationOperationProgress(TimeProvider.System);
        var project = ProjectId.CreateNewId();
        var otherProject = ProjectId.CreateNewId();
        var document = DocumentId.CreateNewId(project);
        var otherDocument = DocumentId.CreateNewId(project);
        var needs = new[]
        {
            new DependencyDocumentSatisfied(1, project, document),
            new DependencyDocumentSatisfied(2, project, document),
            new DependencyDocumentSatisfied(1, otherProject, document),
            new DependencyDocumentSatisfied(1, project, otherDocument)
        };
        progress.Report(new DependencyCollectionStarted());
        var unknown = progress.Snapshot();
        Assert.Equal(0L, unknown.ProcessedDocuments);
        Assert.Null(unknown.TotalDocuments);
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            foreach (var need in needs) progress.Report(need);
        })));
        Assert.Equal(4L, progress.Snapshot().ProcessedDocuments);
        Assert.Null(progress.Snapshot().TotalDocuments);
        progress.Report(new DependencyRequiredDocumentsKnown(4));
        progress.Advance(NavigationAnalysisPhase.Formatting);
        foreach (var need in needs) progress.Report(need);
        var known = progress.Snapshot();
        Assert.Equal(4L, known.ProcessedDocuments);
        Assert.Equal(4L, known.TotalDocuments);
        Assert.Throws<InvalidOperationException>(() => progress.Report(new DependencyRequiredDocumentsKnown(5)));
        Assert.Equal(known.TotalDocuments, progress.Snapshot().TotalDocuments);
        Assert.Throws<InvalidOperationException>(() => progress.Report(
            new DependencyDocumentSatisfied(3, project, document)));
        Assert.Equal(4L, progress.Snapshot().ProcessedDocuments);
        Assert.Equal(4L, progress.Snapshot().TotalDocuments);
        Assert.Equal(0L, unknown.ProcessedDocuments);
        Assert.Null(unknown.TotalDocuments);
    }

    [Fact]
    public async Task CacheProgressCountsNewSharedAndRetainedFulfillmentForEachRequest()
    {
        using var fixture = CreateFixture();
        var releaseA = NewSignal();
        var releaseB = NewSignal();
        var aStarted = NewSignal();
        var bothSubscribed = NewSignal();
        var scans = 0;
        var subscriptions = 0;
        var observer = new DependencyGraphCollectionObserver(
            DocumentCollected: document =>
            {
                Interlocked.Increment(ref scans);
                if (document.Name == "a.cs")
                {
                    aStarted.TrySetResult();
                    releaseA.Task.GetAwaiter().GetResult();
                }
                else releaseB.Task.GetAwaiter().GetResult();
            },
            SubscriptionAdded: _ =>
            {
                if (Interlocked.Increment(ref subscriptions) == 4) bothSubscribed.TrySetResult();
            });
        await using var cache = new DependencyGraphCache(observer: observer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var progressA = new NavigationOperationProgress(TimeProvider.System);
        var progressB = new NavigationOperationProgress(TimeProvider.System);
        var coveredA = NewSignal();
        var coveredB = NewSignal();
        try
        {
            var first = CollectAsync(cache, fixture.Solution, progressA, coveredA, timeout.Token);
            await aStarted.Task.WaitAsync(timeout.Token);
            var second = CollectAsync(cache, fixture.Solution, progressB, coveredB, timeout.Token);
            await bothSubscribed.Task.WaitAsync(timeout.Token);
            Assert.Equal(0L, progressA.Snapshot().ProcessedDocuments);
            Assert.Equal(0L, progressB.Snapshot().ProcessedDocuments);
            releaseA.TrySetResult();
            await Task.WhenAll(coveredA.Task, coveredB.Task).WaitAsync(timeout.Token);
            Assert.Equal(1L, progressA.Snapshot().ProcessedDocuments);
            Assert.Equal(1L, progressB.Snapshot().ProcessedDocuments);
            Assert.Equal(2L, progressA.Snapshot().TotalDocuments);
            Assert.Equal(2L, progressB.Snapshot().TotalDocuments);
            releaseB.TrySetResult();
            var collections = await Task.WhenAll(first, second).WaitAsync(timeout.Token);
            Assert.All(collections, collection => Assert.Equal(2, collection.CoveredDocumentCount));
            Assert.Equal(2, scans);
            Assert.Equal(2L, progressA.Snapshot().ProcessedDocuments);
            Assert.Equal(2L, progressB.Snapshot().ProcessedDocuments);

            var cachedProgress = new NavigationOperationProgress(TimeProvider.System);
            var cached = await CollectAsync(cache, fixture.Solution, cachedProgress, NewSignal(), timeout.Token).WaitAsync(timeout.Token);
            Assert.Equal(0, cached.NewSemanticScanCount);
            Assert.Equal(2L, cachedProgress.Snapshot().ProcessedDocuments);
            Assert.Equal(2L, cachedProgress.Snapshot().TotalDocuments);
            Assert.Equal(2, scans);
        }
        finally
        {
            releaseA.TrySetResult();
            releaseB.TrySetResult();
        }
    }

    [Fact]
    public async Task CacheProgressExcludesFailedNeedsAndCountsSuccessfulRetryAndReuse()
    {
        using var fixture = CreateFixture();
        var failures = 0;
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(
            DocumentCollected: document =>
            {
                if (document.Name == "a.cs" && Interlocked.Increment(ref failures) == 1)
                    throw new IOException("Synthetic transient collection failure.");
            }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var partialProgress = new NavigationOperationProgress(TimeProvider.System);
        var partial = await CollectAsync(cache, fixture.Solution, partialProgress, NewSignal(), timeout.Token).WaitAsync(timeout.Token);
        Assert.Single(partial.Errors);
        Assert.Equal(1, partial.CoveredDocumentCount);
        Assert.Equal(1L, partialProgress.Snapshot().ProcessedDocuments);
        Assert.Equal(2L, partialProgress.Snapshot().TotalDocuments);
        var retryProgress = new NavigationOperationProgress(TimeProvider.System);
        var retry = await CollectAsync(cache, fixture.Solution, retryProgress, NewSignal(), timeout.Token).WaitAsync(timeout.Token);
        Assert.Empty(retry.Errors);
        Assert.Equal(1, retry.NewSemanticScanCount);
        Assert.Equal(2L, retryProgress.Snapshot().ProcessedDocuments);
        Assert.Equal(2L, retryProgress.Snapshot().TotalDocuments);
        Assert.Equal(1L, partialProgress.Snapshot().ProcessedDocuments);
    }

    private static LongRunningToolCallRequest Request(Func<string?, CancellationToken, Task<CallToolResult>> operation) =>
        new("find_symbol", "target", "name=Example", operation);

    private static CallToolResult Result(string text) => new() { Content = [new TextContentBlock { Text = text }] };
    private static string TextOf(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    private static string TokenOf(CallToolResult result) => TextOf(result).Split('\n')
        .Single(line => line.StartsWith("operationToken=", StringComparison.Ordinal))["operationToken=".Length..];
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string RequiredControl(string token, string phase) =>
        "Status: operation=running, completeness=not_applicable\n"
        + $"operationToken={token}\nelapsedMilliseconds: {long.MaxValue.ToString(CultureInfo.InvariantCulture)}\n"
        + $"phase: {phase}\nretryAfterMilliseconds: 1000\nnextAction: {NextAction}";

    private static (int Bytes, int Tokens) MeasureRequiredControls(string token, string[] phases)
    {
        var controls = phases.Select(phase => RequiredControl(token, phase)).ToArray();
        return (Math.Max(McpResponseBudgetLimits.MinimumBytes, controls.Max(Encoding.UTF8.GetByteCount)),
            controls.Max(McpResponseFormatter.CountTokens));
    }

    private static void AssertRunning(CallToolResult result, string token)
    {
        Assert.False(result.IsError ?? false, TextOf(result));
        Assert.StartsWith("Status: operation=running, completeness=not_applicable\n", TextOf(result), StringComparison.Ordinal);
        Assert.Equal(token, TokenOf(result));
        Assert.Contains("elapsedMilliseconds: ", TextOf(result), StringComparison.Ordinal);
        Assert.Contains("phase: ", TextOf(result), StringComparison.Ordinal);
        Assert.Contains("retryAfterMilliseconds: 1000\n", TextOf(result), StringComparison.Ordinal);
        Assert.EndsWith($"nextAction: {NextAction}", TextOf(result), StringComparison.Ordinal);
    }

    private static void AssertBudgetError(CallToolResult result, (int Bytes, int Tokens) expected, bool isPoll)
    {
        Assert.True(result.IsError, TextOf(result));
        var action = isPoll
            ? $"Repeat the unchanged poll with maxResponseBytes={expected.Bytes} and maxResponseTokens={expected.Tokens}, the same operationToken and active resultCursor; omit continuationToken."
            : $"Repeat the unchanged fresh call with maxResponseBytes={expected.Bytes} and maxResponseTokens={expected.Tokens}.";
        Assert.Equal("Status: operation=error, completeness=not_applicable\nRESPONSE_BUDGET_TOO_SMALL\n"
            + $"minimumResponseBytes: {expected.Bytes}\nminimumResponseTokens: {expected.Tokens}\nnextAction: {action}", TextOf(result));
    }

    private static void AssertFits(CallToolResult result, int bytes, int tokens)
    {
        Assert.True(Encoding.UTF8.GetByteCount(TextOf(result)) <= bytes);
        Assert.True(McpResponseFormatter.CountTokens(TextOf(result)) <= tokens);
    }

    private static TestSolutionHandle CreateFixture() => TestWorkspaceBuilder.CreateSolution(
        @"C:\VirtualRepo\Progress.slnx",
        new ProjectSpec("App", [
            ("a.cs", "namespace App; public class A { public B Value = new(); }"),
            ("b.cs", "namespace App; public class B { }")], VirtualProjectDirectory: "src/App"));

    private static Task<DependencyGraphCollection> CollectAsync(DependencyGraphCache cache, Solution solution,
        NavigationOperationProgress progress, TaskCompletionSource firstCovered, CancellationToken cancellationToken) =>
        cache.CollectAsync(solution, new DependencyGraphCollectionOptions(), @"C:\cache\progress.slnx", 1,
            cancellationToken: cancellationToken, progress: value =>
            {
                progress.Report(value);
                if (value is DependencyDocumentSatisfied) firstCovered.TrySetResult();
            });

    private sealed class TimestampClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Volatile.Read(ref timestamp);
        internal void SetTimestamp(long value) => Interlocked.Exchange(ref timestamp, value);
    }
}
