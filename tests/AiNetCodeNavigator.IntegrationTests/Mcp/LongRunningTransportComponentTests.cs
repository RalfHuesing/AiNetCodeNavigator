using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AiNetCodeNavigator.Configuration;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.TrafficCapture;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class LongRunningTransportComponentTests
{
    [Fact]
    public async Task SdkFrames_RepeatedRunningPollsRetrieveOneRetainedOperationAndReplayItsResult()
    {
        var runId = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        var artifacts = Path.Combine(TestTempDirectory.RootTempDirectory, "agentic-navigation-evolution", "R07", runId);
        Directory.CreateDirectory(artifacts);
        var identity = ServerBuildIdentity.Create();
        await using var capture = new TrafficCaptureSession(artifacts, new TrafficCaptureOptions(true, 7, 1_000_000), identity.Name, identity.Version);
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(20));
        var state = new OperationFixtureState(store);
        var input = new Pipe();
        using var output = new FramedOutputStream();
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(state);
        var server = builder.Services.AddMcpServer(options => options.ServerInfo = identity);
        McpServerHost.ConfigureTransport(server, capture, input.Reader.AsStream(leaveOpen: true), output).WithTools<OperationFixtureTools>();
        using var host = builder.Build();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var timings = new List<FrameTiming>();
        var frames = new List<string>();
        var clock = Stopwatch.StartNew();
        var hostTask = host.RunAsync(lifetime.Token);
        var completed = false;
        try
        {
            var initialize = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{},\"clientInfo\":{\"name\":\"bounded-operation-frame-fixture\",\"version\":\"1.0\"}}}";
            using var initialized = await ExchangeAsync(initialize, 1, TimeSpan.FromSeconds(20));
            Assert.Equal(1, initialized.RootElement.GetProperty("id").GetInt32());
            await SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", lifetime.Token);

            using var first = await ExchangeAsync(RequestFrame(2), 2, TimeSpan.FromSeconds(20));
            var control = ReadControl(first, expectedToken: null, previousElapsed: 0);
            await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
            Assert.Equal(1, state.Starts);
            var lastReceived = timings[^1].ReceivedMilliseconds;
            var elapsed = control.Elapsed;
            for (var id = 3; id <= 4; id++)
            {
                if (id == 4)
                {
                    state.Progress!.Advance(NavigationAnalysisPhase.Formatting);
                    state.Progress.Advance(NavigationAnalysisPhase.Loading);
                }
                await Task.Delay(control.RetryAfter, lifetime.Token);
                using var poll = await ExchangeAsync(RequestFrame(id, control.Token), id, TimeSpan.FromSeconds(5));
                var timing = timings[^1];
                Assert.True(timing.SentMilliseconds - lastReceived >= control.RetryAfter, JsonSerializer.Serialize(timing));
                // A small first-response override must leave the production one-second poll window intact.
                Assert.InRange(timing.ReceivedMilliseconds - timing.SentMilliseconds, 900, 4999);
                var next = ReadControl(poll, control.Token, elapsed);
                Assert.Equal(id == 3 ? "analyzing" : "formatting", next.Phase);
                elapsed = next.Elapsed;
                lastReceived = timing.ReceivedMilliseconds;
                Assert.Equal(1, state.Starts);
            }

            state.Release.TrySetResult();
            await Task.Delay(control.RetryAfter, lifetime.Token);
            using var final = await ExchangeAsync(RequestFrame(5, control.Token), 5, TimeSpan.FromSeconds(5));
            Assert.True(timings[^1].SentMilliseconds - lastReceived >= control.RetryAfter);
            var finalText = ResponseText(final);
            Assert.Contains("fixture-complete:target-a:fixed-query", finalText, StringComparison.Ordinal);
            Assert.DoesNotContain("operation=running", finalText, StringComparison.Ordinal);
            Assert.False(final.RootElement.GetProperty("result").TryGetProperty("isError", out var error) && error.GetBoolean());
            using var replay = await ExchangeAsync(RequestFrame(6, control.Token), 6, TimeSpan.FromSeconds(5));
            Assert.Equal(finalText, ResponseText(replay));
            Assert.Equal(1, state.Starts);
            var capturedInputs = Directory.EnumerateFiles(Path.Combine(artifacts, "traffic", capture.SessionId.ToString("D")), "input.json", SearchOption.AllDirectories).ToArray();
            Assert.Equal(5, capturedInputs.Length);
            foreach (var path in capturedInputs)
            {
                using var captured = JsonDocument.Parse(await File.ReadAllTextAsync(path));
                var request = captured.RootElement;
                var arguments = request.GetProperty("params").GetProperty("arguments");
                Assert.Equal("bounded_operation", request.GetProperty("params").GetProperty("name").GetString());
                Assert.Equal("target-a", arguments.GetProperty("targetPath").GetString());
                Assert.Equal("fixed-query", arguments.GetProperty("query").GetString());
                Assert.False(arguments.TryGetProperty("resultCursor", out _));
                Assert.False(arguments.TryGetProperty("continuationToken", out _));
                if (request.GetProperty("id").GetInt32() == 2) Assert.False(arguments.TryGetProperty("operationToken", out _));
                else Assert.Equal(control.Token, arguments.GetProperty("operationToken").GetString());
            }
            completed = true;
        }
        finally
        {
            state.Release.TrySetResult();
            await input.Writer.CompleteAsync();
            try
            {
                await hostTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                await lifetime.CancelAsync();
                await capture.DisposeAsync();
                await File.WriteAllLinesAsync(Path.Combine(artifacts, "frames.jsonl"), frames);
                await File.WriteAllTextAsync(Path.Combine(artifacts, "transport-observations.json"), JsonSerializer.Serialize(new
                {
                    completed,
                    artifactDirectory = artifacts,
                    server = identity,
                    sdkAssembly = typeof(McpServerToolAttribute).Assembly.FullName,
                    client = "bounded-operation-frame-fixture/1.0 (in-process explicit frame writer)",
                    transport = "in-memory SDK component; no child process or connected product client",
                    firstFrameTimeoutMilliseconds = 20000,
                    pollFrameTimeoutMilliseconds = 5000,
                    firstOperationWindowMilliseconds = 20,
                    pollOperationWindow = "production default",
                    stopwatchFrequency = Stopwatch.Frequency,
                    executionCount = state.Starts,
                    timings
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        async Task SendAsync(string frame, CancellationToken cancellationToken)
        {
            frames.Add(JsonSerializer.Serialize(new { direction = "request", timestampMilliseconds = clock.ElapsedMilliseconds, frame }));
            await input.Writer.WriteAsync(Encoding.UTF8.GetBytes(frame + "\n"), cancellationToken);
            var flush = await input.Writer.FlushAsync(cancellationToken);
            Assert.False(flush.IsCompleted, "The SDK transport closed before the request was sent.");
        }

        async Task<JsonDocument> ExchangeAsync(string frame, int id, TimeSpan timeout)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(timeout);
            var sent = clock.ElapsedMilliseconds;
            await SendAsync(frame, deadline.Token);
            var response = await output.ReadFrameAsync(deadline.Token);
            var received = clock.ElapsedMilliseconds;
            frames.Add(JsonSerializer.Serialize(new { direction = "response", timestampMilliseconds = received, frame = response }));
            timings.Add(new FrameTiming(id, sent, received, (long)timeout.TotalMilliseconds));
            var parsed = JsonDocument.Parse(response);
            Assert.Equal(id, parsed.RootElement.GetProperty("id").GetInt32());
            Assert.False(parsed.RootElement.TryGetProperty("error", out _), response);
            return parsed;
        }
    }

    private sealed record FrameTiming(int RequestId, long SentMilliseconds, long ReceivedMilliseconds, long TimeoutMilliseconds);

    private static string RequestFrame(int id, string? token = null)
    {
        var arguments = new Dictionary<string, object> { ["targetPath"] = "target-a", ["query"] = "fixed-query" };
        if (token is not null) arguments.Add("operationToken", token);
        return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name = "bounded_operation", arguments } });
    }

    private static (string Token, long Elapsed, string Phase, int RetryAfter) ReadControl(JsonDocument response, string? expectedToken, long previousElapsed)
    {
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean(), response.RootElement.GetRawText());
        var text = ResponseText(response);
        Assert.StartsWith("Status: operation=running, completeness=not_applicable\n", text, StringComparison.Ordinal);
        var token = ReadHeader(text, "operationToken");
        Assert.Equal(39, token.Length);
        Assert.All(token, character => Assert.True(char.IsAsciiDigit(character)));
        if (expectedToken is not null) Assert.Equal(expectedToken, token);
        var elapsed = long.Parse(Field(text, "elapsedMilliseconds"), CultureInfo.InvariantCulture);
        Assert.True(elapsed >= previousElapsed, text);
        var phase = Field(text, "phase");
        Assert.Contains(phase, new[] { "loading", "refreshing", "identifying", "analyzing", "formatting" });
        var retry = int.Parse(Field(text, "retryAfterMilliseconds"), CultureInfo.InvariantCulture);
        Assert.Equal(1000, retry);
        Assert.Equal("Wait at least 1000 ms, then repeat the same tool, target and query with this operationToken; preserve an active resultCursor and omit continuationToken.", Field(text, "nextAction"));
        Assert.DoesNotContain("continuationToken=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("resultCursor=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("processedDocuments:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("totalDocuments:", text, StringComparison.Ordinal);
        return (token, elapsed, phase, retry);
    }

    private static string Field(string text, string name) => text.Split('\n').Single(line => line.StartsWith(name + ": ", StringComparison.Ordinal))[(name.Length + 2)..];
    private static string ResponseText(JsonDocument response) => response.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;

    public sealed class OperationFixtureState
    {
        internal OperationFixtureState(LongRunningToolCallStore store) => Store = store;
        internal LongRunningToolCallStore Store { get; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal NavigationOperationProgress? Progress { get; set; }
        internal int Starts;
    }

    [McpServerToolType]
    public sealed class OperationFixtureTools(OperationFixtureState state)
    {
        [McpServerTool(Name = "bounded_operation", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        public Task<CallToolResult> Run(string targetPath, string query, string? operationToken = null, CancellationToken cancellationToken = default) =>
            state.Store.RunAsync(new LongRunningToolCallRequest("bounded_operation", targetPath, query, async (cursor, token) =>
            {
                Assert.Null(cursor);
                Interlocked.Increment(ref state.Starts);
                state.Progress = NavigationOperationProgress.Current;
                Assert.NotNull(state.Progress);
                state.Started.TrySetResult();
                await state.Release.Task.WaitAsync(token);
                return McpToolResults.Success($"fixture-complete:{targetPath}:{query}");
            }, OperationToken: operationToken), cancellationToken);
    }

    private sealed class FramedOutputStream : Stream
    {
        private readonly Channel<string> frames = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private readonly StringBuilder current = new();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Write(byte[] buffer, int offset, int count) => Append(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) => Append(buffer);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(buffer.Span);
            return ValueTask.CompletedTask;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        internal ValueTask<string> ReadFrameAsync(CancellationToken cancellationToken) => frames.Reader.ReadAsync(cancellationToken);
        private void Append(ReadOnlySpan<byte> bytes)
        {
            lock (current)
            {
                foreach (var character in Encoding.UTF8.GetString(bytes))
                {
                    if (character == '\n')
                    {
                        frames.Writer.TryWrite(current.ToString() + "\n");
                        current.Clear();
                    }
                    else current.Append(character);
                }
            }
        }
    }
}
