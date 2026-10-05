using System.Text;
using System.Text.Json;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Formatting;
using ModelContextProtocol.Protocol;

namespace AiNetCodeNavigator.FastTests.Mcp;

public sealed class LongRunningToolCallStoreTests
{
    [Fact]
    public async Task AnalysisScopeHeaderEscapesMultilineQueryWithoutCreatingExtraHeaders()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1));
        var metadata = new NavigationAnalysisMetadata(
            "source:0123456789abcdef01234567",
            "findSymbol(pattern=Run\\n snapshotId=forged, kind=method)",
            Array.Empty<string>());
        var payload = JsonSerializer.Serialize(new
        {
            analysis = metadata,
            items = new string('x', 1_000),
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });

        var result = await store.RunAsync(Request("find_symbol", "target", "pattern=Run", _ =>
            Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = payload }] }), maxResponseBytes: 1024));

        var text = TextOf(result);
        Assert.Equal(1, text.Split('\n').Count(line => line.StartsWith("snapshotId=", StringComparison.Ordinal)));
        Assert.Contains("analyzedScope=findSymbol(pattern=Run\\\\n snapshotId=forged, kind=method)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisteredDomainCursorKeepsCompactJsonPropertyBoundaries()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1));
        var metadata = new NavigationAnalysisMetadata(
            "assembly:0123456789abcdef01234567",
            "inspectAssembly(maxResults=50)",
            Array.Empty<string>());
        var result = await store.RunAsync(new LongRunningToolCallRequest(
            "inspect_assembly", "target", "query=members", (_, _) => Task.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = "{\n\"types\":[\n{\n\"name\":\"Widget\",\n\"members\":[\n{\n\"name\":\"Run\"}\n],\n\"resultCursor\":\"core-cursor\"}\n]\n}" }],
                Meta = new System.Text.Json.Nodes.JsonObject
                {
                    ["navigationAnalysis"] = JsonSerializer.SerializeToNode(metadata,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                },
            })));

        var text = TextOf(result);
        var body = BodyOf(result);
        using var parsed = JsonDocument.Parse(body);

        Assert.False(result.IsError ?? false, text);
        Assert.Contains("\n\"types\":[\n", body, StringComparison.Ordinal);
        Assert.Contains("\n\"resultCursor\":", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  \"types\": [", body, StringComparison.Ordinal);
        var cursor = parsed.RootElement.GetProperty("types")[0].GetProperty("resultCursor").GetString();
        Assert.NotNull(cursor);
        Assert.Equal(39, cursor.Length);
        Assert.All(cursor, character => Assert.InRange(character, '0', '9'));
        Assert.Contains("snapshotId=assembly:0123456789abcdef01234567", text, StringComparison.Ordinal);
        Assert.Contains("analyzedScope=inspectAssembly(maxResults=50)", text, StringComparison.Ordinal);
        Assert.Contains("analysisCompleteness=complete", text, StringComparison.Ordinal);
        Assert.Equal("Widget", parsed.RootElement.GetProperty("types")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task CompleteAnalysisHeaderOmitsNegativeDefaults()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1));
        var metadata = new NavigationAnalysisMetadata(
            "assembly:0123456789abcdef01234567",
            "inspectAssembly(maxResults=50)",
            Array.Empty<string>());
        var result = await store.RunAsync(new LongRunningToolCallRequest(
            "inspect_assembly", "target", "query=members", (_, _) => Task.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = "{\"types\":[]}" }],
                Meta = new System.Text.Json.Nodes.JsonObject
                {
                    ["navigationAnalysis"] = JsonSerializer.SerializeToNode(metadata,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                },
            })));
        var text = TextOf(result);

        Assert.Contains("snapshotId=assembly:0123456789abcdef01234567", text, StringComparison.Ordinal);
        Assert.Contains("analyzedScope=inspectAssembly(maxResults=50)", text, StringComparison.Ordinal);
        Assert.Contains("analysisCompleteness=complete", text, StringComparison.Ordinal);
        Assert.DoesNotContain("resultContinuation=none", text, StringComparison.Ordinal);
        Assert.DoesNotContain("omissions=none", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialAnalysisHeaderKeepsPositiveCursorAndOmissionFields()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1));
        var metadata = new NavigationAnalysisMetadata(
            "assembly:0123456789abcdef01234567",
            "inspectAssembly(maxFiles=3)",
            ["maxFiles"],
            "partial",
            ResultContinuationAvailable: true);
        var response = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "{\"types\":[],\"resultCursor\":\"core-cursor\"}" }],
            Meta = new System.Text.Json.Nodes.JsonObject
            {
                ["navigationAnalysis"] = JsonSerializer.SerializeToNode(metadata,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            },
        };
        var result = await store.RunAsync(new LongRunningToolCallRequest(
            "inspect_assembly", "target", "query=partial", (_, _) => Task.FromResult(response)));
        var text = TextOf(result);

        Assert.Contains("analysisCompleteness=partial", text, StringComparison.Ordinal);
        Assert.Contains("resultContinuation=available", text, StringComparison.Ordinal);
        Assert.Contains("omissions=maxFiles", text, StringComparison.Ordinal);
        Assert.Contains("resultCursor", text, StringComparison.Ordinal);
    }

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
    public async Task DomainCursorMayAccompanyOperationPollAndIsPartOfOperationIdentity()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(20));
        var cursor = "v1.1.bound-query";
        var cursorPage = await store.RunAsync(Request("inspect_assembly", "target", "query=maxResults:1", _ =>
            Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = "{\"resultCursor\":\"v1.1.bound-query\"}" }] })));
        Assert.False(cursorPage.IsError ?? false, TextOf(cursorPage));
        cursor = DomainCursorOf(cursorPage);
        var release = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        Task<CallToolResult> Start(string? domainCursor, CancellationToken token) { Interlocked.Increment(ref starts); return release.Task; }
        var original = new LongRunningToolCallRequest("inspect_assembly", "target", "query=maxResults:1", Start,
            DomainCursor: cursor);

        var pending = await store.RunAsync(original);
        var operationToken = TokenOf(pending, "operationToken");
        Assert.Contains("operation=running", TextOf(pending), StringComparison.Ordinal);

        var wrongCursor = await store.RunAsync(original with { OperationToken = operationToken, DomainCursor = "v1.2.other-query" });
        Assert.True(wrongCursor.IsError);
        Assert.Contains("OPERATION_EXPIRED", TextOf(wrongCursor), StringComparison.Ordinal);

        release.SetResult(new CallToolResult { Content = [new TextContentBlock { Text = "Second domain page." }] });
        var completed = await store.RunAsync(original with { OperationToken = operationToken });

        Assert.Contains("Second domain page.", TextOf(completed), StringComparison.Ordinal);
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task DomainCursorUsesSharedRetentionAndBindsToolTargetAndQuery()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1), maxContinuationPages: 2);
        const string cursor = "opaque-domain-cursor";
        var issued = await store.RunAsync(Request("find_references", "target-a", "scope=production", _ =>
            Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = $"{{\"resultCursor\":\"{cursor}\"}}" }] })));
        Assert.False(issued.IsError ?? false, TextOf(issued));
        var publicCursor = DomainCursorOf(issued);

        var changedQuery = await store.RunAsync(new LongRunningToolCallRequest(
            "find_references", "target-a", "scope=all", (_, _) => Task.FromResult(new CallToolResult()), DomainCursor: publicCursor));
        Assert.True(changedQuery.IsError);
        Assert.Contains("RESULT_CURSOR_ARGUMENT_MISMATCH", TextOf(changedQuery), StringComparison.Ordinal);

        ExpireDomainCursor(store, publicCursor);
        var expired = await store.RunAsync(new LongRunningToolCallRequest(
            "find_references", "target-a", "scope=production", (_, _) => Task.FromResult(new CallToolResult()), DomainCursor: publicCursor));
        Assert.True(expired.IsError);
        Assert.Contains("RESULT_CURSOR_EXPIRED", TextOf(expired), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DomainCursorCapacityIsBoundedByTheSharedContinuationStore()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1), maxContinuationPages: 1);
        const string firstCursor = "cursor-one";
        const string secondCursor = "cursor-two";
        var first = await store.RunAsync(Request("inspect_assembly", "target", "query=all", _ =>
            Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = $"{{\"resultCursor\":\"{firstCursor}\"}}" }] })));
        Assert.False(first.IsError ?? false, TextOf(first));
        var publicCursor = DomainCursorOf(first);

        var second = await store.RunAsync(new LongRunningToolCallRequest("inspect_assembly", "target", "query=all",
            (_, _) => Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = $"{{\"resultCursor\":\"{secondCursor}\"}}" }] }),
            DomainCursor: publicCursor));
        Assert.True(second.IsError);
        Assert.Contains("RESULT_CURSOR_CAPACITY", TextOf(second), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CachedFinalPollReissuesAnExpiredResultCursorWithoutRevivingIt()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(20), maxContinuationPages: 4);
        var release = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        Task<CallToolResult> Start(CancellationToken token)
        {
            Interlocked.Increment(ref starts);
            return release.Task;
        }

        var pending = await store.RunAsync(Request("search_assembly", "target", "query=x", Start));
        var operationToken = TokenOf(pending, "operationToken");
        release.SetResult(new CallToolResult { Content = [new TextContentBlock { Text = "{\"resultCursor\":\"core-cursor\"}" }] });
        var first = await store.RunAsync(Request("search_assembly", "target", "query=x", Start, operationToken: operationToken));
        var expiredPublicCursor = DomainCursorOf(first);
        ExpireDomainCursor(store, expiredPublicCursor);

        var replay = await store.RunAsync(Request("search_assembly", "target", "query=x", Start, operationToken: operationToken));
        var refreshedPublicCursor = DomainCursorOf(replay);
        Assert.NotEqual(expiredPublicCursor, refreshedPublicCursor);
        Assert.Equal(1, starts);

        var stale = await store.RunAsync(new LongRunningToolCallRequest("search_assembly", "target", "query=x",
            (_, _) => Task.FromResult(new CallToolResult()), DomainCursor: expiredPublicCursor));
        Assert.True(stale.IsError);
        Assert.Contains("RESULT_CURSOR_EXPIRED", TextOf(stale), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OuterPageReplayKeepsItsExpiredDomainCursorImmutable()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1), maxContinuationPages: 64);
        var rows = string.Join(",\n", Enumerable.Range(0, 40).Select(index => index == 12
            ? $"{{\"resultCursor\":\"core-cursor\",\"value\":\"row-{index:D3}-{new string('x', 180)}\"}}"
            : $"{{\"value\":\"row-{index:D3}-{new string('x', 180)}\"}}"));
        var source = $"{{\n  \"rows\": [\n{rows}\n  ]\n}}";
        Task<CallToolResult> Produce(CancellationToken _) => Task.FromResult(new CallToolResult
        {
            Content = [new TextContentBlock { Text = source }],
        });
        var request = Request("search_assembly", "target", "query=x", Produce, maxResponseBytes: 512, maxResponseTokens: 300);
        string? incomingOuterToken = null;
        var page = await store.RunAsync(request);
        for (var index = 0; index < 40; index++)
        {
            Assert.False(page.IsError ?? false, TextOf(page));
            var pageText = TextOf(page);
            const string cursorMarker = "\"resultCursor\":\"";
            var cursorStart = pageText.IndexOf(cursorMarker, StringComparison.Ordinal);
            if (cursorStart >= 0)
            {
                cursorStart += cursorMarker.Length;
                var cursorEnd = pageText.IndexOf('"', cursorStart);
                Assert.True(cursorEnd > cursorStart, pageText);
                var publicCursor = pageText[cursorStart..cursorEnd];
                Assert.Equal(39, publicCursor.Length);
                Assert.All(publicCursor, character => Assert.True(char.IsAsciiDigit(character)));
                var pageOuterToken = incomingOuterToken ?? throw new Xunit.Sdk.XunitException("The cursor page must be an outer continuation page.");
                ExpireDomainCursor(store, publicCursor);

                var replay = await store.RunAsync(request with { ContinuationToken = pageOuterToken });
                Assert.Equal(pageText, TextOf(replay));
                var stale = await store.RunAsync(new LongRunningToolCallRequest("search_assembly", "target", "query=x",
                    (_, _) => Task.FromResult(new CallToolResult()), DomainCursor: publicCursor));
                Assert.True(stale.IsError);
                Assert.Contains("RESULT_CURSOR_EXPIRED", TextOf(stale), StringComparison.Ordinal);
                return;
            }

            incomingOuterToken = TokenOf(page, "continuationToken");
            page = await store.RunAsync(request with { ContinuationToken = incomingOuterToken });
        }
        Assert.Fail("The stored outer pages did not reach the domain cursor.");
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
        var tokens = Enumerable.Range(0, 5).Select(_ => McpResponseContinuationStore.CreateOpaqueToken()).ToArray();
        var candidates = new Queue<string>(tokens.Append(tokens[4]));
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(10), operationTokenFactory: () => candidates.Dequeue());
        var releases = Enumerable.Range(0, 5).Select(_ => new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var started = Enumerable.Range(0, 5).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var starts = new int[5];
        var requests = Enumerable.Range(0, 5).Select(index => Request("long_tool", "target", $"query={index}", async cancellationToken =>
        {
            Interlocked.Increment(ref starts[index]);
            started[index].TrySetResult();
            return await releases[index].Task.WaitAsync(cancellationToken);
        }) with { MaxResponseBytes = 512, MaxResponseTokens = 256 }).ToArray();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                var pending = await store.RunAsync(requests[i]);
                Assert.Equal(tokens[i], TokenOf(pending, "operationToken"));
                await started[i].Task.WaitAsync(TimeSpan.FromSeconds(1));
            }
            var fifth = await store.RunAsync(requests[4]);
            Assert.True(fifth.IsError);
            Assert.Contains("TOO_MANY_OPERATIONS", TextOf(fifth), StringComparison.Ordinal);
            Assert.Equal(1000, IntField(fifth, "retryAfterMilliseconds"));
            Assert.Contains("Wait at least 1000 milliseconds", TextOf(fifth), StringComparison.Ordinal);
            Assert.Contains("retry the original request", TextOf(fifth), StringComparison.Ordinal);
            Assert.Null(TryTokenOf(fifth, "operationToken"));
            Assert.True(Encoding.UTF8.GetByteCount(TextOf(fifth)) <= 512);
            Assert.True(McpResponseFormatter.CountTokens(TextOf(fifth)) <= 256);
            Assert.Equal(new[] { 1, 1, 1, 1, 0 }, starts);
            Assert.Equal(4, OperationCount(store));
            Assert.Equal(4, RunningCount(store));

            releases[0].SetResult(new CallToolResult { Content = [new TextContentBlock { Text = "done-0" }] });
            var completed = await store.RunAsync(requests[0] with { OperationToken = tokens[0] });
            Assert.Contains("done-0", TextOf(completed), StringComparison.Ordinal);
            Assert.Equal(3, RunningCount(store));
            var retry = await store.RunAsync(requests[4]);
            Assert.Equal(tokens[4], TokenOf(retry, "operationToken"));
            await started[4].Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Empty(candidates); // The rejected preflight reservation was released and reused.
            Assert.Equal(new[] { 1, 1, 1, 1, 1 }, starts);
            Assert.Equal(5, OperationCount(store));
            Assert.Equal(4, RunningCount(store));
            for (var i = 1; i < 5; i++)
            {
                releases[i].SetResult(new CallToolResult { Content = [new TextContentBlock { Text = $"done-{i}" }] });
                var final = await store.RunAsync(requests[i] with { OperationToken = tokens[i] });
                Assert.Contains($"done-{i}", TextOf(final), StringComparison.Ordinal);
            }
            Assert.Equal(0, RunningCount(store));
            Assert.Equal(5, OperationCount(store)); // Only the five completed admitted results are retained.
            Assert.Equal(new[] { 1, 1, 1, 1, 1 }, starts);
        }
        finally
        {
            foreach (var release in releases) release.TrySetResult(new CallToolResult());
        }
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
    public async Task DomainTruncationRemainsTruncatedAcrossEveryBudgetPageAndReplay()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1));
        var source = string.Join("\n", Enumerable.Range(0, 120).Select(index => $"symbol-{index:D3}: [handoff: src:src/App/App.csproj|M:Run{index:D3}]"));
        var calls = 0;
        Task<CallToolResult> Work(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(McpToolResults.DomainTruncated(source, "Increase maxResults and repeat the query."));
        }

        var current = await store.RunAsync(Request("find_symbol", "target", "pattern=Symbol", Work, maxResponseBytes: 512));
        var collected = new StringBuilder();
        var pageNumber = 0;
        while (true)
        {
            var text = TextOf(current);
            Assert.Contains("completeness=truncated", text, StringComparison.Ordinal);
            Assert.Contains("nextAction: Increase maxResults and repeat the query.", text, StringComparison.Ordinal);
            Assert.Null(current.StructuredContent);
            Assert.True(Encoding.UTF8.GetByteCount(text) <= 512);
            collected.Append(BodyOf(current));
            pageNumber++;
            var token = TryTokenOf(current, "continuationToken");
            if (token is null) break;

            current = await store.RunAsync(Request("find_symbol", "target", "pattern=Symbol", Work,
                continuationToken: token, maxResponseBytes: 512));
        }

        Assert.True(pageNumber > 1);
        Assert.Contains("symbol-119", collected.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, calls);
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
    public async Task ExactContinuationMinimumRetryUsesTheSameProspectiveTokenProjection()
    {
        var source = $"short row\n{new string('x', 600)}\n{new string('y', 1_000)}\ntail row\n";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1), maxContinuationSnapshots: 1);
            var first = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", _ => Task.FromResult(
                new CallToolResult { Content = [new TextContentBlock { Text = source }] }), maxResponseBytes: 512, maxResponseTokens: 120));
            var continuation = TokenOf(first, "continuationToken");
            var tooSmall = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", _ => Task.FromResult(new CallToolResult()),
                continuationToken: continuation, maxResponseBytes: 512, maxResponseTokens: 120));
            var retryBytes = IntField(tooSmall, "minimumResponseBytes");
            var retryTokens = IntField(tooSmall, "minimumResponseTokens");
            var retry = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", _ => Task.FromResult(new CallToolResult()),
                continuationToken: continuation, maxResponseBytes: retryBytes, maxResponseTokens: retryTokens));

            Assert.True(tooSmall.IsError);
            Assert.False(retry.IsError ?? false);
            Assert.True(Encoding.UTF8.GetByteCount(TextOf(retry)) <= retryBytes);
            Assert.True(McpResponseFormatter.CountTokens(TextOf(retry)) <= retryTokens);
        }
    }

    [Fact]
    public async Task BudgetFailureDoesNotConsumeTheOnlyContinuationBudgetVariant()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1), maxContinuationBudgetVariants: 1);
        var source = $"short row\n{new string('x', 600)}\n{new string('y', 1_000)}\ntail row\n";
        Task<CallToolResult> Work(CancellationToken _) => Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = source }] });
        var first = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work, maxResponseBytes: 512, maxResponseTokens: 120));
        var continuation = TokenOf(first, "continuationToken");
        var tooSmall = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work,
            continuationToken: continuation, maxResponseBytes: 512, maxResponseTokens: 120));
        var retry = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work,
            continuationToken: continuation, maxResponseBytes: IntField(tooSmall, "minimumResponseBytes"),
            maxResponseTokens: IntField(tooSmall, "minimumResponseTokens")));

        Assert.True(tooSmall.IsError);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", TextOf(tooSmall), StringComparison.Ordinal);
        Assert.False(retry.IsError ?? false);
        Assert.DoesNotContain("CONTINUATION_CAPACITY", TextOf(retry), StringComparison.Ordinal);
    }

    [Fact]
    public void OpaqueContinuationTokensHaveStableBudgetProjection()
    {
        var prefixCosts = new HashSet<int>();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < 500; index++)
        {
            var token = McpResponseContinuationStore.CreateOpaqueToken();
            Assert.Equal(39, token.Length);
            Assert.All(token, character => Assert.InRange(character, '0', '9'));
            Assert.True(tokens.Add(token));
            var completePrefix = McpToolResults.TruncatedSuccessStatusPrefix + $"continuationToken={token}\n";
            prefixCosts.Add(McpResponseFormatter.CountTokens(completePrefix));
        }

        Assert.Single(prefixCosts);
    }

    [Fact]
    public async Task RepeatedFinalOperationPollsReuseOneSnapshotAndStablePage()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(10), maxContinuationSnapshots: 1);
        var release = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = string.Join("\n", Enumerable.Range(0, 100).Select(index => $"row-{index:D3}: value"));
        var starts = 0;
        Task<CallToolResult> Work(CancellationToken _)
        {
            Interlocked.Increment(ref starts);
            return release.Task;
        }

        var pending = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work, maxResponseBytes: 512));
        var operationToken = TokenOf(pending, "operationToken");
        release.SetResult(new CallToolResult { Content = [new TextContentBlock { Text = source }] });
        var pollRequest = Request("get_call_tree", "target", "symbol=Foo", Work, operationToken: operationToken, maxResponseBytes: 512);
        var concurrent = await Task.WhenAll(store.RunAsync(pollRequest), store.RunAsync(pollRequest));
        var later = await store.RunAsync(pollRequest);

        Assert.Equal(TextOf(concurrent[0]), TextOf(concurrent[1]));
        Assert.Equal(TextOf(concurrent[0]), TextOf(later));
        Assert.Equal(TokenOf(concurrent[0], "continuationToken"), TokenOf(later, "continuationToken"));
        Assert.Equal(1, starts);
        var changedBudget = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work,
            operationToken: operationToken, maxResponseBytes: 513));
        Assert.False(changedBudget.IsError ?? false);
        Assert.Contains("continuationToken=", TextOf(changedBudget), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayedFinalPageRenewsExpiredContinuationWithoutRebindingOldToken()
    {
        await using var store = new LongRunningToolCallStore(
            TimeSpan.FromMilliseconds(10), completedIdleTtl: TimeSpan.FromMinutes(5), continuationIdleTtl: TimeSpan.FromSeconds(1));
        var release = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = string.Join("\n", Enumerable.Range(0, 100).Select(index => $"row-{index:D3}: {new string('x', 70)}"));
        var work = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", _ => release.Task, maxResponseBytes: 512));
        var operationToken = TokenOf(work, "operationToken");
        release.SetResult(new CallToolResult { Content = [new TextContentBlock { Text = source }] });
        var poll = Request("get_call_tree", "target", "symbol=Foo", _ => release.Task, operationToken: operationToken, maxResponseBytes: 512);
        var original = await store.RunAsync(poll);
        var expiredContinuation = TokenOf(original, "continuationToken");
        ExpireContinuationPage(store, expiredContinuation);

        var changedBudget = await store.RunAsync(poll with { MaxResponseBytes = 513 });
        var changedBudgetContinuation = TokenOf(changedBudget, "continuationToken");
        var replay = await store.RunAsync(poll);
        var renewedContinuation = TokenOf(replay, "continuationToken");
        var oldPage = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", _ => release.Task,
            continuationToken: expiredContinuation, maxResponseBytes: 512));
        var newPage = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", _ => release.Task,
            continuationToken: renewedContinuation, maxResponseBytes: 512));

        Assert.False(replay.IsError ?? false);
        Assert.NotEqual(expiredContinuation, renewedContinuation);
        Assert.NotEqual(expiredContinuation, changedBudgetContinuation);
        Assert.Contains("completeness=truncated", TextOf(replay), StringComparison.Ordinal);
        Assert.Contains("CONTINUATION_EXPIRED", TextOf(oldPage), StringComparison.Ordinal);
        Assert.False(newPage.IsError ?? false);
        Assert.False((await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", _ => release.Task,
            continuationToken: changedBudgetContinuation, maxResponseBytes: 513))).IsError ?? false);
    }

    [Fact]
    public async Task ReplayedContinuationPageDoesNotReturnExpiredSuccessorToken()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1), continuationIdleTtl: TimeSpan.FromMinutes(5));
        var source = string.Join("\n", Enumerable.Range(0, 150).Select(index => $"row-{index:D3}: {new string('x', 70)}"));
        Task<CallToolResult> Work(CancellationToken _) => Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = source }] });
        var first = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work, maxResponseBytes: 512));
        var firstToken = TokenOf(first, "continuationToken");
        var pageTwo = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work,
            continuationToken: firstToken, maxResponseBytes: 512));
        var expiredSuccessor = TokenOf(pageTwo, "continuationToken");
        ExpireContinuationPage(store, expiredSuccessor);

        var changedBudgetPage = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work,
            continuationToken: firstToken, maxResponseBytes: 513));
        var changedBudgetSuccessor = TokenOf(changedBudgetPage, "continuationToken");
        var replay = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work,
            continuationToken: firstToken, maxResponseBytes: 512));
        var renewedSuccessor = TokenOf(replay, "continuationToken");
        var oldPage = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work,
            continuationToken: expiredSuccessor, maxResponseBytes: 512));
        var nextPage = await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work,
            continuationToken: renewedSuccessor, maxResponseBytes: 512));

        Assert.False(replay.IsError ?? false);
        Assert.NotEqual(expiredSuccessor, renewedSuccessor);
        Assert.NotEqual(expiredSuccessor, changedBudgetSuccessor);
        Assert.Equal(BodyOf(pageTwo), BodyOf(replay));
        Assert.Contains("CONTINUATION_EXPIRED", TextOf(oldPage), StringComparison.Ordinal);
        Assert.False(nextPage.IsError ?? false);
        Assert.False((await store.RunAsync(Request("get_call_tree", "target", "symbol=Foo", Work,
            continuationToken: changedBudgetSuccessor, maxResponseBytes: 513))).IsError ?? false);
        var lastReplayedRow = int.Parse(BodyOf(replay).Split('\n').Last(static line => line.Length > 0).AsSpan(4, 3));
        Assert.StartsWith($"row-{lastReplayedRow + 1:D3}:", BodyOf(nextPage), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DelegateLoadingControlsRemainRetryResultsWithinRequestedBudgets()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1));
        var loading = McpToolResults.Loading();
        var direct = await store.RunAsync(Request("load_workspace", "target", "query=Foo", _ => Task.FromResult(loading),
            maxResponseBytes: 512, maxResponseTokens: 256));

        var customLoading = McpToolResults.Loading("Still indexing the workspace.", "Wait for the index and retry.", 512, 100);
        var custom = await store.RunAsync(Request("load_workspace", "target", "query=Bar", _ => Task.FromResult(customLoading),
            maxResponseBytes: 512, maxResponseTokens: 256));

        Assert.False(direct.IsError ?? false);
        Assert.Equal(TextOf(loading), TextOf(direct));
        Assert.StartsWith(McpToolResults.LoadingStatusPrefix, TextOf(direct), StringComparison.Ordinal);
        Assert.Contains("nextAction: Wait briefly and repeat the same call.", TextOf(direct), StringComparison.Ordinal);
        Assert.DoesNotContain("operation=ok", TextOf(direct), StringComparison.Ordinal);
        Assert.False(custom.IsError ?? false);
        Assert.Equal(TextOf(customLoading), TextOf(custom));
        Assert.StartsWith(McpToolResults.LoadingStatusPrefix, TextOf(custom), StringComparison.Ordinal);
        Assert.Contains("nextAction: Wait for the index and retry.", TextOf(custom), StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(TextOf(custom)) <= 512);
        Assert.True(McpResponseFormatter.CountTokens(TextOf(custom)) <= 100);
    }

    [Fact]
    public async Task OversizedDelegateLoadingControlsReturnAtomicBudgetErrors()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(20));
        var longAction = new string('R', 450);
        var oversized = McpToolResults.Loading("Still loading.", longAction, 1_024, 500);
        var byteLimited = await store.RunAsync(Request("load_workspace", "target", "bytes", _ => Task.FromResult(oversized),
            maxResponseBytes: 512, maxResponseTokens: 500));
        var release = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = await store.RunAsync(Request("load_workspace", "target", "tokens", _ => release.Task,
            maxResponseBytes: 1_024, maxResponseTokens: 500));
        release.SetResult(oversized);
        var tokenLimited = await store.RunAsync(Request("load_workspace", "target", "tokens", _ => release.Task,
            operationToken: TokenOf(pending, "operationToken"), maxResponseBytes: 1_024, maxResponseTokens: 80));

        Assert.True(byteLimited.IsError);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", TextOf(byteLimited), StringComparison.Ordinal);
        Assert.DoesNotContain(McpToolResults.LoadingStatusPrefix, TextOf(byteLimited), StringComparison.Ordinal);
        Assert.True(IntField(byteLimited, "minimumResponseBytes") > 512);
        Assert.True(tokenLimited.IsError);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", TextOf(tokenLimited), StringComparison.Ordinal);
        Assert.DoesNotContain(McpToolResults.LoadingStatusPrefix, TextOf(tokenLimited), StringComparison.Ordinal);
        Assert.True(IntField(tokenLimited, "minimumResponseTokens") > 80);
        Assert.True(McpResponseFormatter.CountTokens(TextOf(tokenLimited)) <= 80);
    }

    [Fact]
    public async Task DelegateRunningControlIsRejectedInsteadOfWrappedAsSuccess()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1));
        var nestedRunning = McpToolResults.Running("foreign-token", 512, 256);
        var actual = await store.RunAsync(Request("nested_call", "target", "query=Foo", _ => Task.FromResult(nestedRunning),
            maxResponseBytes: 512, maxResponseTokens: 256));

        Assert.True(actual.IsError);
        Assert.Contains("OPERATION_CONTROL_UNSUPPORTED", TextOf(actual), StringComparison.Ordinal);
        Assert.DoesNotContain("operation=ok", TextOf(actual), StringComparison.Ordinal);
        Assert.DoesNotContain("operation=running", TextOf(actual), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PendingOperationCanFinishWithLoadingControlAndReplayItWithoutSuccessSnapshot()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(10));
        var release = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        Task<CallToolResult> Work(CancellationToken _) { Interlocked.Increment(ref starts); return release.Task; }
        var pending = await store.RunAsync(Request("load_workspace", "target", "query=Foo", Work, maxResponseBytes: 512, maxResponseTokens: 256));
        var operationToken = TokenOf(pending, "operationToken");
        release.SetResult(McpToolResults.Loading("Workspace remains unavailable.", "Wait for loading to finish.", 512, 100));
        var poll = Request("load_workspace", "target", "query=Foo", Work, operationToken: operationToken, maxResponseBytes: 512, maxResponseTokens: 256);
        var firstPoll = await store.RunAsync(poll);
        var replay = await store.RunAsync(poll);

        Assert.False(firstPoll.IsError ?? false);
        Assert.StartsWith(McpToolResults.LoadingStatusPrefix, TextOf(firstPoll), StringComparison.Ordinal);
        Assert.Equal(TextOf(firstPoll), TextOf(replay));
        Assert.DoesNotContain("operation=ok", TextOf(replay), StringComparison.Ordinal);
        Assert.DoesNotContain("continuationToken=", TextOf(replay), StringComparison.Ordinal);
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task PendingLoadingControlBudgetRecoveryOffersExecutableMinimumPair()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(10));
        var release = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        Task<CallToolResult> Work(CancellationToken _) { Interlocked.Increment(ref starts); return release.Task; }
        var pending = await store.RunAsync(Request("load_workspace", "target", "loading-recovery", Work,
            maxResponseBytes: 512, maxResponseTokens: 256));
        var operationToken = TokenOf(pending, "operationToken");
        var loading = McpToolResults.Loading("Still indexing.", $"Wait and retry: {string.Concat(Enumerable.Repeat("x ", 100))}",
            maxResponseBytes: 1_024, maxResponseTokens: 256);
        var loadingText = TextOf(loading);
        var loadingBytes = Encoding.UTF8.GetByteCount(loadingText);
        var loadingTokens = McpResponseFormatter.CountTokens(loadingText);
        Assert.InRange(loadingBytes, 1, 511);
        Assert.True(loadingTokens > 80);
        release.SetResult(loading);

        var poll = Request("load_workspace", "target", "loading-recovery", Work, operationToken: operationToken,
            maxResponseBytes: 512, maxResponseTokens: loadingTokens - 1);
        var tooSmall = await store.RunAsync(poll);
        Assert.True(tooSmall.IsError);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", TextOf(tooSmall), StringComparison.Ordinal);
        Assert.Equal(512, IntField(tooSmall, "minimumResponseBytes"));
        Assert.Equal(loadingTokens, IntField(tooSmall, "minimumResponseTokens"));
        var retry = await store.RunAsync(poll with
        {
            MaxResponseBytes = IntField(tooSmall, "minimumResponseBytes"),
            MaxResponseTokens = IntField(tooSmall, "minimumResponseTokens")
        });

        Assert.True(Encoding.UTF8.GetByteCount(TextOf(tooSmall)) <= poll.MaxResponseBytes);
        Assert.True(McpResponseFormatter.CountTokens(TextOf(tooSmall)) <= poll.MaxResponseTokens);
        Assert.False(retry.IsError ?? false);
        Assert.Equal(loadingText, TextOf(retry));
        Assert.True(Encoding.UTF8.GetByteCount(TextOf(retry)) <= 512);
        Assert.True(McpResponseFormatter.CountTokens(TextOf(retry)) <= loadingTokens);
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task PendingTypedErrorBudgetRecoveryPreservesOriginalErrorAndOffersExactPair()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(10));
        var release = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        Task<CallToolResult> Work(CancellationToken _) { Interlocked.Increment(ref starts); return release.Task; }
        var pending = await store.RunAsync(Request("find_symbol", "target", "typed-error-recovery", Work,
            maxResponseBytes: 512, maxResponseTokens: 256));
        var operationToken = TokenOf(pending, "operationToken");
        var original = McpToolResults.Recoverable("SYMBOL_NOT_FOUND", $"No match. {string.Concat(Enumerable.Repeat("detail ", 90))}",
            "Choose another symbol.", fieldPath: "symbolIdentifier", maxResponseBytes: 2_048, maxResponseTokens: 512);
        var originalText = TextOf(original);
        var originalBytes = Encoding.UTF8.GetByteCount(originalText);
        var originalTokens = McpResponseFormatter.CountTokens(originalText);
        Assert.True(originalBytes > 512);
        Assert.True(originalTokens > 80);
        release.SetResult(original);

        var bytePoll = Request("find_symbol", "target", "typed-error-recovery", Work, operationToken: operationToken,
            maxResponseBytes: 512, maxResponseTokens: 256);
        var byteError = await store.RunAsync(bytePoll);
        Assert.True(byteError.IsError);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", TextOf(byteError), StringComparison.Ordinal);
        Assert.Equal(Math.Max(512, originalBytes), IntField(byteError, "minimumResponseBytes"));
        Assert.Equal(originalTokens, IntField(byteError, "minimumResponseTokens"));
        var byteRetry = await store.RunAsync(bytePoll with
        {
            MaxResponseBytes = IntField(byteError, "minimumResponseBytes"),
            MaxResponseTokens = IntField(byteError, "minimumResponseTokens")
        });
        var tokenPoll = bytePoll with { MaxResponseBytes = 2_048, MaxResponseTokens = 80 };
        var tokenError = await store.RunAsync(tokenPoll);
        Assert.True(tokenError.IsError);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", TextOf(tokenError), StringComparison.Ordinal);
        Assert.Equal(Math.Max(512, originalBytes), IntField(tokenError, "minimumResponseBytes"));
        Assert.Equal(originalTokens, IntField(tokenError, "minimumResponseTokens"));
        var tokenRetry = await store.RunAsync(tokenPoll with
        {
            MaxResponseBytes = IntField(tokenError, "minimumResponseBytes"),
            MaxResponseTokens = IntField(tokenError, "minimumResponseTokens")
        });

        Assert.True(Encoding.UTF8.GetByteCount(TextOf(byteError)) <= bytePoll.MaxResponseBytes);
        Assert.True(McpResponseFormatter.CountTokens(TextOf(byteError)) <= bytePoll.MaxResponseTokens);
        Assert.True(Encoding.UTF8.GetByteCount(TextOf(tokenError)) <= tokenPoll.MaxResponseBytes);
        Assert.True(McpResponseFormatter.CountTokens(TextOf(tokenError)) <= tokenPoll.MaxResponseTokens);
        Assert.True(byteRetry.IsError);
        Assert.True(tokenRetry.IsError);
        Assert.Equal(originalText, TextOf(byteRetry));
        Assert.Equal(originalText, TextOf(tokenRetry));
        Assert.True(Encoding.UTF8.GetByteCount(TextOf(byteRetry)) <= Math.Max(512, originalBytes));
        Assert.True(McpResponseFormatter.CountTokens(TextOf(byteRetry)) <= originalTokens);
        Assert.True(Encoding.UTF8.GetByteCount(TextOf(tokenRetry)) <= Math.Max(512, originalBytes));
        Assert.True(McpResponseFormatter.CountTokens(TextOf(tokenRetry)) <= originalTokens);
        Assert.Contains("SYMBOL_NOT_FOUND", TextOf(byteRetry), StringComparison.Ordinal);
        Assert.Contains("fieldPath: symbolIdentifier", TextOf(byteRetry), StringComparison.Ordinal);
        Assert.Contains("nextAction: Choose another symbol.", TextOf(byteRetry), StringComparison.Ordinal);
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task AtomicProjectionsAboveMaximumRequireNarrowingWithoutByteRetry()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromSeconds(1));
        var oversizedAction = new string('x', McpResponseBudgetLimits.MaximumBytes + 1);
        var loading = McpToolResults.TextResult(
            $"{McpToolResults.LoadingStatusPrefix}nextAction: {oversizedAction}\n[INFO]: Still loading.", isError: false);
        var error = McpToolResults.TextResult(
            $"{McpToolResults.ErrorStatusPrefix}OVERSIZED_ERROR\nmessage: {oversizedAction}\nfieldPath: targetPath\nnextAction: Narrow the request.", isError: true);

        var loadingResult = await store.RunAsync(Request("load_workspace", "target", "oversized-loading",
            _ => Task.FromResult(loading), maxResponseBytes: McpResponseBudgetLimits.MaximumBytes, maxResponseTokens: 256));
        var errorResult = await store.RunAsync(Request("find_symbol", "target", "oversized-error",
            _ => Task.FromResult(error), maxResponseBytes: McpResponseBudgetLimits.MaximumBytes, maxResponseTokens: 256));

        foreach (var result in new[] { loadingResult, errorResult })
        {
            Assert.True(result.IsError);
            Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", TextOf(result), StringComparison.Ordinal);
            Assert.True(IntField(result, "minimumResponseBytes") > McpResponseBudgetLimits.MaximumBytes);
            Assert.Contains("exceeds the 65536-byte limit; narrow it", TextOf(result), StringComparison.Ordinal);
            Assert.DoesNotContain("retry: repeat with maxResponseBytes=", TextOf(result), StringComparison.Ordinal);
            Assert.True(Encoding.UTF8.GetByteCount(TextOf(result)) <= McpResponseBudgetLimits.MaximumBytes);
            Assert.True(McpResponseFormatter.CountTokens(TextOf(result)) <= 256);
        }
    }

    [Fact]
    public async Task UnpolledBackgroundCompletionsRespectCompletedCapacity()
    {
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(2), maxCompleted: 1);
        var tokens = new string[5];
        for (var index = 0; index < tokens.Length; index++)
        {
            var captured = index;
            var release = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<CallToolResult> Work(CancellationToken _)
            {
                var result = await release.Task;
                completed.TrySetResult();
                return result;
            }

            var pending = await store.RunAsync(Request("long_tool", "target", $"query={index}", Work));
            tokens[index] = TokenOf(pending, "operationToken");
            release.SetResult(new CallToolResult { Content = [new TextContentBlock { Text = $"done-{captured}" }] });
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await SpinWaitAsync(() => RunningCount(store) == 0, TimeSpan.FromSeconds(1));
        }

        Task<CallToolResult> IgnoredWork(CancellationToken _) => Task.FromResult(new CallToolResult());
        var evicted = await store.RunAsync(Request("long_tool", "target", "query=0", IgnoredWork, operationToken: tokens[0]));
        var retained = await store.RunAsync(Request("long_tool", "target", "query=4", IgnoredWork, operationToken: tokens[4]));

        Assert.True(evicted.IsError);
        Assert.Contains("OPERATION_EXPIRED", TextOf(evicted), StringComparison.Ordinal);
        Assert.Contains("done-4", TextOf(retained), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpirationAndConcurrentCompletionDoNotCancelDisposedSources()
    {
        using var releaseFirstCancellation = new ManualResetEventSlim();
        await using var store = new LongRunningToolCallStore(
            TimeSpan.FromMilliseconds(10), runningIdleTtl: TimeSpan.FromMinutes(30));
        var firstCancellationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<CallToolResult> First(CancellationToken token)
        {
            await using var registration = token.Register(() =>
            {
                firstCancellationStarted.TrySetResult();
                releaseFirstCancellation.Wait();
            });
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new CallToolResult();
        }

        async Task<CallToolResult> Second(CancellationToken _)
        {
            secondStarted.TrySetResult();
            var result = await releaseSecond.Task;
            secondCompleted.TrySetResult();
            return result;
        }

        try
        {
            var firstPending = await store.RunAsync(Request("long_tool", "target", "first", First));
            Assert.NotNull(TryTokenOf(firstPending, "operationToken"));
            var secondPending = await store.RunAsync(Request("long_tool", "target", "second", Second));
            Assert.NotNull(TryTokenOf(secondPending, "operationToken"));
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            FreezeOperationEntriesForExpiry(store);

            var expireAndRun = Task.Run(() => store.RunAsync(Request("quick_tool", "target", "unrelated", _ =>
                Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = "unrelated" }] }))));
            await SpinWaitAsync(() => OperationCount(store) == 0, TimeSpan.FromSeconds(1));
            await firstCancellationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            releaseSecond.SetResult(new CallToolResult { Content = [new TextContentBlock { Text = "second done" }] });
            await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await SpinWaitAsync(() => RunningCount(store) <= 1, TimeSpan.FromSeconds(1));
            releaseFirstCancellation.Set();

            var unrelated = await expireAndRun.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Contains("unrelated", TextOf(unrelated), StringComparison.Ordinal);
        }
        finally
        {
            releaseFirstCancellation.Set();
            releaseSecond.TrySetResult(new CallToolResult());
        }
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
            TimeSpan.FromMilliseconds(15), runningIdleTtl: TimeSpan.FromMilliseconds(800),
            pollResponseWindow: TimeSpan.FromMilliseconds(15));
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
        new(tool, target, arguments, (_, token) => operation(token), operationToken, continuationToken, maxResponseBytes, maxResponseTokens);

    private static string TextOf(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static string TokenOf(CallToolResult result, string field) =>
        TryTokenOf(result, field) ?? throw new Xunit.Sdk.XunitException($"Missing {field}.");

    private static string DomainCursorOf(CallToolResult result)
    {
        var text = TextOf(result);
        using var document = JsonDocument.Parse(text[text.IndexOf('{')..]);
        return document.RootElement.GetProperty("resultCursor").GetString()!;
    }

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

    private static int OperationCount(LongRunningToolCallStore store) =>
        ((System.Collections.IDictionary)typeof(LongRunningToolCallStore).GetField("_operations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!).Count;

    private static void ExpireContinuationPage(LongRunningToolCallStore store, string token)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var continuationStore = typeof(LongRunningToolCallStore).GetField("_continuations", flags)!.GetValue(store)!;
        var pages = (System.Collections.IDictionary)continuationStore.GetType().GetField("_pages", flags)!.GetValue(continuationStore)!;
        var page = pages[token] ?? throw new Xunit.Sdk.XunitException("Continuation page was not stored.");
        page.GetType().GetProperty("LastAccess", flags)!.SetValue(page, DateTimeOffset.UtcNow - TimeSpan.FromHours(1));
    }

    private static void ExpireDomainCursor(LongRunningToolCallStore store, string token)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var continuationStore = typeof(LongRunningToolCallStore).GetField("_continuations", flags)!.GetValue(store)!;
        var cursors = (System.Collections.IDictionary)continuationStore.GetType().GetField("_domainCursors", flags)!.GetValue(continuationStore)!;
        var cursor = cursors[token] ?? throw new Xunit.Sdk.XunitException("Domain cursor was not stored.");
        cursor.GetType().GetProperty("LastAccess", flags)!.SetValue(cursor, DateTimeOffset.UtcNow - TimeSpan.FromHours(1));
    }

    private static int RunningCount(LongRunningToolCallStore store) =>
        (int)typeof(LongRunningToolCallStore).GetField("_running", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(store)!.GetType().GetProperty("Count")!.GetValue(typeof(LongRunningToolCallStore)
                .GetField("_running", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!)!;

    private static void FreezeOperationEntriesForExpiry(LongRunningToolCallStore store)
    {
        var entries = (System.Collections.IDictionary)typeof(LongRunningToolCallStore)
            .GetField("_operations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(store)!;
        foreach (System.Collections.DictionaryEntry pair in entries)
        {
            var entry = pair.Value!;
            var entryType = entry.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            ((CancellationTokenSource)entryType.GetProperty("Cancellation", flags)!.GetValue(entry)!).CancelAfter(Timeout.InfiniteTimeSpan);
            entryType.GetProperty("LastAccess", flags)!.SetValue(entry, DateTimeOffset.UtcNow - TimeSpan.FromHours(1));
        }
    }

    private static async Task SpinWaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("Condition did not become true in time.");
            await Task.Delay(5);
        }
    }

    private static string BodyOf(CallToolResult result)
    {
        var lines = TextOf(result).Split('\n');
        var firstContentLine = lines.Length > 0 && lines[0].StartsWith("Status:", StringComparison.Ordinal) ? 1 : 0;
        while (firstContentLine < lines.Length && (lines[firstContentLine].StartsWith("snapshotId=", StringComparison.Ordinal)
            || lines[firstContentLine].StartsWith("analyzedScope=", StringComparison.Ordinal)
            || lines[firstContentLine].StartsWith("analysisCompleteness=", StringComparison.Ordinal)
            || lines[firstContentLine].StartsWith("resultContinuation=", StringComparison.Ordinal)
            || lines[firstContentLine].StartsWith("omissions=", StringComparison.Ordinal)
            || lines[firstContentLine].StartsWith("nextAction: ", StringComparison.Ordinal)
            || lines[firstContentLine].StartsWith("continuationToken=", StringComparison.Ordinal))) firstContentLine++;
        return string.Join("\n", lines.Skip(firstContentLine));
    }
}
