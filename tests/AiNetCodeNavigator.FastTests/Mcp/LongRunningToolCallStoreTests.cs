using System.Text;
using System.Text.Json;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Formatting;
using ModelContextProtocol.Protocol;

namespace AiNetCodeNavigator.FastTests.Mcp;

public sealed class LongRunningToolCallStoreTests
{
    [Fact]
    public async Task FastSuccessReturnsFormattedResultWithoutStartingAgain()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1));
        var starts = 0;
        var result = new CallToolResult { Content = [new TextContentBlock { Text = "Found it." }] };

        var actual = await store.RunAsync(Request("find_symbol", "target-a", "name=Foo", _ =>
        {
            Interlocked.Increment(ref starts);
            return Task.FromResult(result);
        }));

        Assert.False(actual.IsError ?? false);
        Assert.Contains("Found it.", TextOf(actual), StringComparison.Ordinal);
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task PendingOperationReturnsFinalResultBySameTokenWithoutRestarting()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(20));
        var release = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        Task<CallToolResult> Start(CancellationToken _) { Interlocked.Increment(ref starts); return release.Task; }

        var pending = await store.RunAsync(Request("find_symbol", "target-a", "name=Foo", Start));
        var operationToken = TokenOf(pending, "operationToken");
        Assert.Contains("operation=running", TextOf(pending), StringComparison.Ordinal);
        Assert.False(pending.IsError ?? false);

        release.SetResult(new CallToolResult { Content = [new TextContentBlock { Text = "Found it." }] });
        var final = await store.RunAsync(Request("find_symbol", "target-a", "name=Foo", Start, operationToken: operationToken));

        Assert.Contains("Found it.", TextOf(final), StringComparison.Ordinal);
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task SynchronouslyBlockingDelegateDoesNotHoldStoreLockOrResponseWindow()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(30));
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<CallToolResult> Blocking(CancellationToken token)
        {
            started.TrySetResult();
            release.Wait(token);
            return Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = "blocked completed" }] });
        }

        try
        {
            var pendingCall = store.RunAsync(Request("slow_tool", "target", "blocked", Blocking));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var pending = await pendingCall.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Contains("operation=running", TextOf(pending), StringComparison.Ordinal);

            var quick = await store.RunAsync(Request("quick_tool", "target", "quick", _ =>
                Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = "quick completed" }] })));
            Assert.Contains("quick completed", TextOf(quick), StringComparison.Ordinal);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task OperationTokenIsBoundToToolTargetAndArguments()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(10));
        var release = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        Task<CallToolResult> Start(CancellationToken _) { Interlocked.Increment(ref starts); return release.Task; }
        var pending = await store.RunAsync(Request("find_symbol", "target-a", "name=Foo", Start));
        var token = TokenOf(pending, "operationToken");

        var wrongTarget = await store.RunAsync(Request("find_symbol", "target-b", "name=Foo", Start, operationToken: token));
        var wrongQuery = await store.RunAsync(Request("find_symbol", "target-a", "name=Bar", Start, operationToken: token));
        var unknown = await store.RunAsync(Request("find_symbol", "target-a", "name=Foo", Start, operationToken: "unknown"));

        Assert.True(wrongTarget.IsError);
        Assert.True(wrongQuery.IsError);
        Assert.True(unknown.IsError);
        Assert.Equal(1, starts);
        release.SetResult(new CallToolResult { Content = [new TextContentBlock { Text = "done" }] });
    }

    [Fact]
    public async Task FourthRunningOperationIsAllowedAndFifthIsRejected()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(10));
        var release = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        for (var i = 0; i < 4; i++)
        {
            var pending = await store.RunAsync(Request("long_tool", "target", $"query={i}", _ => release.Task));
            Assert.Contains("operation=running", TextOf(pending), StringComparison.Ordinal);
        }

        var fifth = await store.RunAsync(Request("long_tool", "target", "query=5", _ => release.Task));

        Assert.True(fifth.IsError);
        Assert.Contains("TOO_MANY_OPERATIONS", TextOf(fifth), StringComparison.Ordinal);
        release.SetResult(new CallToolResult { Content = [new TextContentBlock { Text = "done" }] });
    }

    [Fact]
    public async Task InitialRequestCancellationStopsWorkButPollingCancellationDoesNot()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(2));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<CallToolResult> WaitForCancellation(CancellationToken token)
        {
            started.TrySetResult();
            await using var registration = token.Register(() => cancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new CallToolResult();
        }

        using var initialCancellation = new CancellationTokenSource();
        var initial = store.RunAsync(Request("long_tool", "target", "initial", WaitForCancellation), initialCancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await initialCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initial);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var pollingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pollingCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<CallToolResult> PollWork(CancellationToken token)
        {
            pollingStarted.TrySetResult();
            await using var registration = token.Register(() => pollingCancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new CallToolResult();
        }

        var pending = await store.RunAsync(Request("long_tool", "target", "poll", PollWork));
        var operationToken = TokenOf(pending, "operationToken");
        await pollingStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        using var pollCancellation = new CancellationTokenSource();
        var poll = store.RunAsync(Request("long_tool", "target", "poll", PollWork, operationToken: operationToken), pollCancellation.Token);
        await pollCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poll);
        Assert.False(pollingCancelled.Task.IsCompleted);
    }

    [Fact]
    public async Task DisposeCancelsAndAwaitsOwnedWork()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(10));
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<CallToolResult> Work(CancellationToken token)
        {
            await using var registration = token.Register(() => cancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new CallToolResult();
        }

        await store.RunAsync(Request("long_tool", "target", "query", Work));
        await store.DisposeAsync();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.RunAsync(Request("long_tool", "target", "query", Work)));
    }

    [Fact]
    public async Task ExternalLifetimeCancellationStopsStoreOwnedWork()
    {
        using var lifetime = new CancellationTokenSource();
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(10), lifetime.Token);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<CallToolResult> Work(CancellationToken token)
        {
            await using var registration = token.Register(() => cancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new CallToolResult();
        }

        await store.RunAsync(Request("long_tool", "target", "lifetime", Work));
        await lifetime.CancelAsync();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task OperationExceptionBecomesRecoverableError()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1));

        var result = await store.RunAsync(Request("long_tool", "target", "query", _ => throw new InvalidOperationException("private detail")));

        Assert.True(result.IsError);
        Assert.Contains("OPERATION_FAILED", TextOf(result), StringComparison.Ordinal);
        Assert.DoesNotContain("private detail", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContinuationPagesAreStableBoundToQueryAndPreserveUnicodeUnitsWithinBudgets()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1));
        var source = string.Join("\n", Enumerable.Range(0, 120).Select(index => $"row-{index:D3}: Grüße 🌍")) + "\n";
        var starts = 0;
        Task<CallToolResult> Work(CancellationToken _)
        {
            Interlocked.Increment(ref starts);
            return Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = source }] });
        }

        var first = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work, maxResponseBytes: 512, maxResponseTokens: 120));
        var allRows = new StringBuilder();
        var current = first;
        var pageCount = 0;
        while (true)
        {
            var text = TextOf(current);
            Assert.True(Encoding.UTF8.GetByteCount(text) <= 512);
            Assert.True(McpResponseFormatter.CountTokens(text) <= 120);
            allRows.Append(BodyOf(current));
            pageCount++;
            var continuation = TryTokenOf(current, "continuationToken");
            if (continuation is null) break;

            var replay = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work,
                continuationToken: continuation, maxResponseBytes: 512, maxResponseTokens: 120));
            var replayAgain = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work,
                continuationToken: continuation, maxResponseBytes: 512, maxResponseTokens: 120));
            Assert.Equal(TextOf(replay), TextOf(replayAgain));
            var wrongQuery = await store.RunAsync(Request("get_call_tree", "target", "symbol=Bar", Work,
                continuationToken: continuation, maxResponseBytes: 512, maxResponseTokens: 120));
            var wrongTarget = await store.RunAsync(Request("get_call_tree", "other-target", "symbol=Foo", Work,
                continuationToken: continuation, maxResponseBytes: 512, maxResponseTokens: 120));
            var wrongBudget = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work,
                continuationToken: continuation, maxResponseBytes: 512, maxResponseTokens: 119));
            Assert.True(wrongQuery.IsError);
            Assert.True(wrongTarget.IsError);
            Assert.False(wrongBudget.IsError ?? false);
            Assert.True(Encoding.UTF8.GetByteCount(TextOf(wrongBudget)) <= 512);
            Assert.True(McpResponseFormatter.CountTokens(TextOf(wrongBudget)) <= 119);
            Assert.Contains("CONTINUATION", TextOf(wrongQuery), StringComparison.Ordinal);
            current = replay;
        }

        Assert.True(pageCount > 1);
        Assert.Equal(source, allRows.ToString());
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task OversizedAtomicUnitAndStructuredPartialResultsAreRejectedClearly()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1));
        var tooLong = await store.RunAsync(Request("find_symbol", "target", "long-line", _ => Task.FromResult(
            new CallToolResult { Content = [new TextContentBlock { Text = new string('x', 2_000) }] }), maxResponseBytes: 512));

        using var structured = JsonDocument.Parse("{\"complete\":true}");
        var paginatedStructured = await store.RunAsync(Request("find_symbol", "target", "structured", _ => Task.FromResult(
            new CallToolResult
            {
                Content = [new TextContentBlock { Text = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"item-{i}")) }],
                StructuredContent = structured.RootElement.Clone(),
            }), maxResponseBytes: 512));

        Assert.True(tooLong.IsError);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", TextOf(tooLong), StringComparison.Ordinal);
        Assert.True(paginatedStructured.IsError);
        Assert.Null(paginatedStructured.StructuredContent);
        Assert.Contains("STRUCTURED_RESULT_TOO_LARGE", TextOf(paginatedStructured), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpiredOperationAndContinuationTokensReturnRecoverableErrors()
    {
        await using var operationStore = new LongRunningToolCallStore(
            TimeSpan.FromMilliseconds(10), runningIdleTtl: TimeSpan.FromMilliseconds(40));
        var pendingWork = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = await operationStore.RunAsync(Request("long_tool", "target", "slow", _ => pendingWork.Task));
        var operationToken = TokenOf(pending, "operationToken");
        await Task.Delay(100);
        var expiredOperation = await operationStore.RunAsync(Request("long_tool", "target", "slow", _ => pendingWork.Task, operationToken: operationToken));

        await using var continuationStore = new LongRunningToolCallStore(TimeSpan.FromSeconds(1), continuationIdleTtl: TimeSpan.FromMilliseconds(40));
        var source = string.Join("\n", Enumerable.Range(0, 100).Select(index => $"line-{index:D3}: value"));
        var paged = await continuationStore.RunAsync(Request("find_symbol", "target", "name=Foo", _ => Task.FromResult(
            new CallToolResult { Content = [new TextContentBlock { Text = source }] }), maxResponseBytes: 512));
        var continuationToken = TokenOf(paged, "continuationToken");
        await Task.Delay(100);
        var expiredContinuation = await continuationStore.RunAsync(Request("find_symbol", "target", "name=Foo", _ => Task.FromResult(new CallToolResult()), continuationToken: continuationToken, maxResponseBytes: 512));

        Assert.True(expiredOperation.IsError);
        Assert.Contains("OPERATION_EXPIRED", TextOf(expiredOperation), StringComparison.Ordinal);
        Assert.True(expiredContinuation.IsError);
        Assert.Contains("CONTINUATION_EXPIRED", TextOf(expiredContinuation), StringComparison.Ordinal);
        pendingWork.TrySetResult(new CallToolResult { Content = [new TextContentBlock { Text = "done" }] });
    }

    [Fact]
    public async Task ContinuationSnapshotCapacityIsBounded()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1), maxContinuationSnapshots: 1);
        var source = string.Join("\n", Enumerable.Range(0, 100).Select(index => $"line-{index:D3}: value"));
        Task<CallToolResult> Work(CancellationToken _) => Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = source }] });

        var first = await store.RunAsync(Request("find_symbol", "target", "name=Foo", Work, maxResponseBytes: 512));
        Assert.NotNull(TryTokenOf(first, "continuationToken"));
        var second = await store.RunAsync(Request("find_symbol", "target", "name=Bar", Work, maxResponseBytes: 512));

        Assert.True(second.IsError);
        Assert.Contains("CONTINUATION_CAPACITY", TextOf(second), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContinuationBudgetRecoveryKeepsTheSameSnapshotAndToken()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1));
        var oversizedRow = new string('x', 600);
        var source = $"short row\n{oversizedRow}\ntail row\n";
        var first = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", _ => Task.FromResult(
            new CallToolResult { Content = [new TextContentBlock { Text = source }] }), maxResponseBytes: 512, maxResponseTokens: 120));
        var token = TokenOf(first, "continuationToken");

        var tooSmall = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", _ => Task.FromResult(new CallToolResult()),
            continuationToken: token, maxResponseBytes: 512, maxResponseTokens: 120));
        var retryBytes = IntField(tooSmall, "minimumResponseBytes");
        var retryTokens = IntField(tooSmall, "minimumResponseTokens");
        var retried = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", _ => Task.FromResult(new CallToolResult()),
            continuationToken: token, maxResponseBytes: retryBytes, maxResponseTokens: retryTokens));

        Assert.True(tooSmall.IsError);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", TextOf(tooSmall), StringComparison.Ordinal);
        Assert.False(retried.IsError ?? false);
        Assert.Contains(oversizedRow, TextOf(retried), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContinuationBudgetVariantCacheIsBounded()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1), maxContinuationBudgetVariants: 1);
        var source = string.Join("\n", Enumerable.Range(0, 100).Select(index => $"row-{index:D3}: value"));
        var first = await store.RunAsync(Request("find_symbol", "target", "name=Foo", _ => Task.FromResult(
            new CallToolResult { Content = [new TextContentBlock { Text = source }] }), maxResponseBytes: 512));
        var token = TokenOf(first, "continuationToken");
        var firstBudgetResult = await store.RunAsync(Request("find_symbol", "target", "name=Foo", _ => Task.FromResult(new CallToolResult()),
            continuationToken: token, maxResponseBytes: 512));
        var excessBudgetResult = await store.RunAsync(Request("find_symbol", "target", "name=Foo", _ => Task.FromResult(new CallToolResult()),
            continuationToken: token, maxResponseBytes: 513));

        Assert.False(firstBudgetResult.IsError ?? false);
        Assert.True(excessBudgetResult.IsError);
        Assert.Contains("CONTINUATION_CAPACITY", TextOf(excessBudgetResult), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunningOperationExpiresWithoutAnotherStoreCall()
    {
        await using var store = new LongRunningToolCallStore(
            TimeSpan.FromMilliseconds(10), runningIdleTtl: TimeSpan.FromMilliseconds(60));
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<CallToolResult> Work(CancellationToken token)
        {
            await using var registration = token.Register(() => cancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new CallToolResult();
        }

        var pending = await store.RunAsync(Request("long_tool", "target", "idle", Work));
        Assert.NotNull(TryTokenOf(pending, "operationToken"));

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ValidPollResetsRunningIdleDeadline()
    {
        await using var store = new LongRunningToolCallStore(
            TimeSpan.FromMilliseconds(15), runningIdleTtl: TimeSpan.FromMilliseconds(800));
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<CallToolResult> Work(CancellationToken token)
        {
            await using var registration = token.Register(() => cancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new CallToolResult();
        }

        var pending = await store.RunAsync(Request("long_tool", "target", "poll-reset", Work));
        var token = TokenOf(pending, "operationToken");
        await Task.Delay(500);
        var repolled = await store.RunAsync(Request("long_tool", "target", "poll-reset", Work, operationToken: token));
        Assert.Contains("operation=running", TextOf(repolled), StringComparison.Ordinal);

        await Task.Delay(500);
        Assert.False(cancelled.Task.IsCompleted);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    private static LongRunningToolCallRequest Request(
        string tool,
        string target,
        string arguments,
        Func<CancellationToken, Task<CallToolResult>> operation,
        string? operationToken = null,
        string? continuationToken = null,
        int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        int? maxResponseTokens = null) =>
        new(tool, target, arguments, operation, operationToken, continuationToken, maxResponseBytes, maxResponseTokens);

    private static string TextOf(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static string TokenOf(CallToolResult result, string field) =>
        TryTokenOf(result, field) ?? throw new Xunit.Sdk.XunitException($"Missing {field}.");

    private static string? TryTokenOf(CallToolResult result, string field)
    {
        var prefix = field + "=";
        return TextOf(result).Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    }

    private static int IntField(CallToolResult result, string field)
    {
        var prefix = field + ": ";
        var value = TextOf(result).Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return value is not null && int.TryParse(value[prefix.Length..], out var parsed)
            ? parsed
            : throw new Xunit.Sdk.XunitException($"Missing {field}.");
    }

    private static string BodyOf(CallToolResult result)
    {
        var lines = TextOf(result).Split('\n');
        var firstContentLine = lines[1].StartsWith("continuationToken=", StringComparison.Ordinal) ? 2 : 1;
        return string.Join("\n", lines.Skip(firstContentLine));
    }
}
