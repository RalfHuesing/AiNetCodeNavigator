using System.Text;
using System.Text.Json;
using AiNetCodeNavigator.Configuration;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.TrafficCapture;

namespace AiNetCodeNavigator.FastTests.Mcp;

public sealed class TrafficCaptureSessionTests
{
    [Fact]
    public async Task CorrelatesOutOfOrderResponsesAndPreservesRawFramesAndVisibleTextMetrics()
    {
        using var fixture = new CaptureFixture();
        await using var session = fixture.CreateSession();
        var initialize = Frame("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"test-agent\",\"version\":\"4.2\"}}}");
        session.RecordInbound(initialize);

        var firstInput = Frame("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"find_symbol\",\"arguments\":{\"pattern\":\"Run\"}}}");
        var secondInput = Frame("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"get_symbol_body\",\"arguments\":{\"operationToken\":\"op-3\",\"resultCursor\":\"cur-in\"}}}");
        var secondText = "Status: operation=running, completeness=not_applicable\noperationToken=op-4\nresultCursor=cur-out\nretry: poll with the same operationToken.";
        var secondOutput = Frame("{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":{\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(secondText) + "}]}}");
        var firstText = "Symbol \nfound.";
        var firstOutput = Frame("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize("Symbol ") + "},{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize("found.") + "},{\"type\":\"image\",\"data\":\"ignored\"}]}}");

        session.RecordInbound(firstInput);
        session.RecordInbound(secondInput);
        session.RecordOutbound(secondOutput);
        session.RecordOutbound(firstOutput);
        await session.DisposeAsync();

        using var sessionMetadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.SessionDirectory(session), "session.json")));
        Assert.Equal("test-agent", sessionMetadata.RootElement.GetProperty("client").GetProperty("name").GetString());
        Assert.Equal("4.2", sessionMetadata.RootElement.GetProperty("client").GetProperty("version").GetString());
        Assert.Equal("AiNetCodeNavigator", sessionMetadata.RootElement.GetProperty("server").GetProperty("name").GetString());

        var firstCall = FindCallDirectory(fixture.SessionDirectory(session), "find_symbol");
        var secondCall = FindCallDirectory(fixture.SessionDirectory(session), "get_symbol_body");
        Assert.Equal(firstInput, await File.ReadAllBytesAsync(Path.Combine(firstCall, "input.json")));
        Assert.Equal(firstOutput, await File.ReadAllBytesAsync(Path.Combine(firstCall, "output.json")));
        Assert.Equal(secondInput, await File.ReadAllBytesAsync(Path.Combine(secondCall, "input.json")));
        Assert.Equal(secondOutput, await File.ReadAllBytesAsync(Path.Combine(secondCall, "output.json")));

        using var firstSummary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(firstCall, "summary.json")));
        Assert.Equal(2, firstSummary.RootElement.GetProperty("requestId").GetInt32());
        Assert.Equal("find_symbol", firstSummary.RootElement.GetProperty("tool").GetString());
        Assert.Equal("success", firstSummary.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(firstInput.Length, firstSummary.RootElement.GetProperty("inputBytes").GetInt32());
        Assert.Equal(firstOutput.Length, firstSummary.RootElement.GetProperty("outputBytes").GetInt32());
        Assert.Equal(Encoding.UTF8.GetByteCount(firstText), firstSummary.RootElement.GetProperty("visibleTextBytes").GetInt32());
        Assert.Equal(McpResponseFormatter.CountTokens(firstText), firstSummary.RootElement.GetProperty("visibleTextTokens").GetInt32());
        Assert.Equal("cl100k_base", firstSummary.RootElement.GetProperty("tokenEncoding").GetString());

        using var secondSummary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(secondCall, "summary.json")));
        Assert.Equal("op-3", secondSummary.RootElement.GetProperty("operationToken").GetString());
        Assert.Equal("cur-in", secondSummary.RootElement.GetProperty("resultCursor").GetString());
        Assert.Equal("running", secondSummary.RootElement.GetProperty("operation").GetString());
        Assert.Equal("op-4", secondSummary.RootElement.GetProperty("outgoingOperationToken").GetString());
        Assert.Equal("cur-out", secondSummary.RootElement.GetProperty("outgoingResultCursor").GetString());

        var lines = await File.ReadAllLinesAsync(Path.Combine(fixture.SessionDirectory(session), "calls.jsonl"));
        Assert.Equal(2, lines.Length);
        var journalRequestIds = lines.Select(line =>
        {
            using var item = JsonDocument.Parse(line);
            return item.RootElement.GetProperty("requestId").GetInt32();
        }).ToArray();
        Assert.Equal(new[] { 3, 2 }, journalRequestIds);
    }

    [Fact]
    public async Task CapturesToolErrorsAndRpcErrorsAsCorrelatedOutcomes()
    {
        using var fixture = new CaptureFixture();
        await using var session = fixture.CreateSession();
        session.RecordInbound(ToolCall(10, "invalid_tool", "{}"));
        session.RecordOutbound(Frame("{\"jsonrpc\":\"2.0\",\"id\":10,\"result\":{\"isError\":true,\"content\":[{\"type\":\"text\",\"text\":\"ARGUMENT_INVALID: missing pattern\"}]}}"));
        session.RecordInbound(ToolCall(11, "find_symbol", "{}"));
        session.RecordOutbound(Frame("{\"jsonrpc\":\"2.0\",\"id\":11,\"error\":{\"code\":-32602,\"message\":\"Invalid params\"}}"));
        await session.DisposeAsync();

        var toolErrorCall = FindCallDirectory(fixture.SessionDirectory(session), "invalid_tool");
        var rpcErrorCall = FindCallDirectory(fixture.SessionDirectory(session), "find_symbol");
        using var toolSummary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(toolErrorCall, "summary.json")));
        using var rpcSummary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(rpcErrorCall, "summary.json")));
        Assert.Equal("error", toolSummary.RootElement.GetProperty("outcome").GetString());
        Assert.Contains("ARGUMENT_INVALID", toolSummary.RootElement.GetProperty("errorMessage").GetString(), StringComparison.Ordinal);
        Assert.Equal("error", rpcSummary.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("-32602", rpcSummary.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal("Invalid params", rpcSummary.RootElement.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task IgnoresMalformedFramesAndNonToolNotifications()
    {
        using var fixture = new CaptureFixture();
        await using var session = fixture.CreateSession();
        session.RecordInbound(Frame("not-json"));
        session.RecordInbound(Frame("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
        session.RecordInbound(Frame("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":1}}"));
        session.RecordOutbound(Frame("{\"jsonrpc\":\"2.0\",\"id\":999,\"result\":{}}"));
        await session.DisposeAsync();

        var sessionDirectory = fixture.SessionDirectory(session);
        Assert.Empty(Directory.EnumerateDirectories(sessionDirectory));
        Assert.False(File.Exists(Path.Combine(sessionDirectory, "calls.jsonl")));
    }

    [Fact]
    public async Task CapturesToolsCallRequestsWithMissingParametersOrNameAsUnknownTool()
    {
        using var fixture = new CaptureFixture();
        await using var session = fixture.CreateSession();
        session.RecordInbound(Frame("{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"tools/call\"}"));
        session.RecordOutbound(Frame("{\"jsonrpc\":\"2.0\",\"id\":8,\"error\":{\"code\":-32602,\"message\":\"Missing params\"}}"));
        session.RecordInbound(Frame("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":{}}"));
        session.RecordOutbound(Frame("{\"jsonrpc\":\"2.0\",\"id\":9,\"result\":{\"isError\":true,\"content\":[{\"type\":\"text\",\"text\":\"Tool name is required\"}]}}"));
        await session.DisposeAsync();

        var unknownToolCalls = Directory.EnumerateDirectories(Path.Combine(fixture.SessionDirectory(session), "unknown_tool"));
        Assert.Equal(2, unknownToolCalls.Count());
        var summaries = new List<JsonDocument>();
        foreach (var callDirectory in unknownToolCalls)
        {
            Assert.True(File.Exists(Path.Combine(callDirectory, "input.json")));
            Assert.True(File.Exists(Path.Combine(callDirectory, "output.json")));
            summaries.Add(JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(callDirectory, "summary.json"))));
        }
        try
        {
            Assert.Equal(new[] { 8, 9 }, summaries.Select(summary => summary.RootElement.GetProperty("requestId").GetInt32()).Order().ToArray());
            Assert.All(summaries, summary => Assert.Equal(JsonValueKind.Null, summary.RootElement.GetProperty("tool").ValueKind));
        }
        finally
        {
            foreach (var summary in summaries) summary.Dispose();
        }
    }

    [Fact]
    public async Task MatchesSemanticallyEquivalentEscapedJsonRpcIdsAndNormalizesOperationMetadata()
    {
        using var fixture = new CaptureFixture();
        await using var session = fixture.CreateSession();
        var input = Frame("{\"jsonrpc\":\"2.0\",\"id\":\"\\u0061\",\"method\":\"tools/call\",\"params\":{\"name\":\"find_symbol\",\"arguments\":{}}}");
        var visibleText = "useful body text";
        var output = Frame("{\"jsonrpc\":\"2.0\",\"id\":\"a\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(visibleText) + "}]}}");
        session.RecordInbound(input);
        session.RecordOutbound(output);
        await session.DisposeAsync();

        var callDirectory = FindCallDirectory(fixture.SessionDirectory(session), "find_symbol");
        Assert.Equal(input, await File.ReadAllBytesAsync(Path.Combine(callDirectory, "input.json")));
        Assert.Equal(output, await File.ReadAllBytesAsync(Path.Combine(callDirectory, "output.json")));
        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(callDirectory, "summary.json")));
        Assert.Equal("ok", summary.RootElement.GetProperty("operation").GetString());
        Assert.Equal(Encoding.UTF8.GetByteCount(visibleText), summary.RootElement.GetProperty("visibleTextBytes").GetInt32());
        Assert.Equal(McpResponseFormatter.CountTokens(visibleText), summary.RootElement.GetProperty("visibleTextTokens").GetInt32());
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("../../outside")]
    [InlineData("CON")]
    public async Task ToolNamesCannotCreateCallDirectoriesOutsideSessionRoot(string toolName)
    {
        using var fixture = new CaptureFixture();
        await using var session = fixture.CreateSession();
        session.RecordInbound(ToolCall(22, toolName, "{}"));
        session.RecordOutbound(Frame("{\"jsonrpc\":\"2.0\",\"id\":22,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}}"));
        await session.DisposeAsync();

        var sessionRoot = Path.GetFullPath(fixture.SessionDirectory(session)) + Path.DirectorySeparatorChar;
        Assert.Single(Directory.EnumerateFiles(fixture.SessionDirectory(session), "input.json", SearchOption.AllDirectories));
        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(fixture.Root, "traffic"), "*", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(directory);
            Assert.StartsWith(sessionRoot, fullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        Assert.True(File.Exists(Path.Combine(fixture.SessionDirectory(session), "session.json")));
        var journal = (await File.ReadAllLinesAsync(Path.Combine(fixture.SessionDirectory(session), "calls.jsonl"))).Single();
        using var summary = JsonDocument.Parse(journal);
        Assert.Equal(toolName, summary.RootElement.GetProperty("tool").GetString());
    }

    [Fact]
    public async Task RetentionPrunesExpiredInactiveSessions()
    {
        using var fixture = new CaptureFixture();
        var expired = Path.Combine(fixture.Root, "traffic", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(expired);
        await File.WriteAllTextAsync(Path.Combine(expired, "old.txt"), "expired");
        Directory.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-3));

        await using var session = fixture.CreateSession(new TrafficCaptureOptions(true, 1, 1_000_000));

        Assert.False(Directory.Exists(expired));
        Assert.True(File.Exists(Path.Combine(fixture.SessionDirectory(session), "session.json")));
    }

    [Fact]
    public async Task TotalByteQuotaPrunesOldInactiveSessionBeforeStartingCapture()
    {
        using var fixture = new CaptureFixture();
        var oldSession = Path.Combine(fixture.Root, "traffic", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(oldSession);
        await File.WriteAllTextAsync(Path.Combine(oldSession, "large.json"), new string('x', 2_000));
        Directory.SetLastWriteTimeUtc(oldSession, DateTime.UtcNow.AddMinutes(-1));

        await using var session = fixture.CreateSession(new TrafficCaptureOptions(true, 7, 1_000));

        Assert.False(Directory.Exists(oldSession));
    }

    [Fact]
    public void FailedSessionInitializationImmediatelyReleasesExclusiveSessionLock()
    {
        using var fixture = new CaptureFixture();
        string? sessionDirectory = null;

        Assert.Throws<IOException>(() => new TrafficCaptureSession(
            fixture.Root,
            new TrafficCaptureOptions(true, 7, 1_000_000),
            "AiNetCodeNavigator",
            "1.2.3",
            directory =>
            {
                sessionDirectory = directory;
                throw new IOException("Injected initialization failure after lock acquisition.");
            }));

        Assert.NotNull(sessionDirectory);
        using var exclusiveLock = new FileStream(Path.Combine(sessionDirectory!, ".active.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task ReparsePointInToolDirectoryStopsCaptureBeforeWritingOutsideTrafficRoot()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new CaptureFixture();
        await using var session = fixture.CreateSession();
        var toolDirectory = Path.Combine(fixture.SessionDirectory(session), "find_symbol");
        var outsideDirectory = Path.Combine(Path.GetDirectoryName(fixture.Root)!, "outside-capture");
        Directory.CreateDirectory(outsideDirectory);
        using var junction = DirectoryJunction.Create(toolDirectory, outsideDirectory);

        session.RecordInbound(ToolCall(52, "find_symbol", "{}"));
        session.RecordOutbound(Frame("{\"jsonrpc\":\"2.0\",\"id\":52,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}}"));

        Assert.False(session.IsCapturing);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outsideDirectory));
    }

    [Fact]
    public async Task RetentionPreservesAnExpiredSessionWhileItsLockIsHeld()
    {
        using var fixture = new CaptureFixture();
        var active = Path.Combine(fixture.Root, "traffic", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(active);
        await File.WriteAllTextAsync(Path.Combine(active, "payload.txt"), "keep while active");
        await using var activeLock = new FileStream(Path.Combine(active, ".active.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        Directory.SetLastWriteTimeUtc(active, DateTime.UtcNow.AddDays(-3));

        await using var session = fixture.CreateSession(new TrafficCaptureOptions(true, 1, 1_000_000));

        Assert.True(Directory.Exists(active));
    }

    [Fact]
    public async Task CaptureQuotaFailureDoesNotInterruptWrappedTransport()
    {
        using var fixture = new CaptureFixture();
        await using var session = fixture.CreateSession(new TrafficCaptureOptions(true, 7, 1));
        var wire = ToolCall(31, "find_symbol", "{}");
        await using var inner = new MemoryStream();
        await using var stream = new TrafficCaptureStream(inner, session.RecordInbound, session.MaximumFrameBytes);

        await stream.WriteAsync(wire);
        await stream.FlushAsync();

        Assert.Equal(wire, inner.ToArray());
        Assert.False(session.IsCapturing);
    }

    [Fact]
    public async Task CancellationAndUnansweredCallsAreSummarizedWhenSessionCloses()
    {
        using var fixture = new CaptureFixture();
        await using var session = fixture.CreateSession();
        session.RecordInbound(ToolCall(40, "find_symbol", "{}"));
        session.RecordInbound(Frame("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":40}}"));
        session.RecordOutbound(Frame("{\"jsonrpc\":\"2.0\",\"id\":40,\"error\":{\"code\":-32603,\"message\":\"Operation failed while cancelling\"}}"));
        session.RecordInbound(ToolCall(41, "get_symbol_body", "{}"));
        session.RecordInbound(ToolCall(42, "find_symbol", "{}"));
        session.RecordInbound(Frame("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":42}}"));
        session.RecordOutbound(Frame("{\"jsonrpc\":\"2.0\",\"id\":42,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"Completed despite cancellation notification\"}]}}"));
        session.RecordInbound(ToolCall(43, "find_symbol", "{}"));
        session.RecordInbound(Frame("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":43}}"));
        session.RecordOutbound(Frame("{\"jsonrpc\":\"2.0\",\"id\":43,\"error\":{\"code\":-32800,\"message\":\"Request cancelled\"}}"));
        await session.DisposeAsync();

        var incomplete = FindCallDirectory(fixture.SessionDirectory(session), "get_symbol_body");
        var summaries = new List<(int RequestId, JsonDocument Summary)>();
        foreach (var callDirectory in Directory.EnumerateDirectories(Path.Combine(fixture.SessionDirectory(session), "find_symbol")))
        {
            var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(callDirectory, "summary.json")));
            summaries.Add((summary.RootElement.GetProperty("requestId").GetInt32(), summary));
        }
        using var errorSummary = summaries.Single(item => item.RequestId == 40).Summary;
        using var successSummary = summaries.Single(item => item.RequestId == 42).Summary;
        using var cancellationSummary = summaries.Single(item => item.RequestId == 43).Summary;
        using var incompleteSummary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(incomplete, "summary.json")));
        Assert.True(errorSummary.RootElement.GetProperty("cancellationRequested").GetBoolean());
        Assert.Equal("error", errorSummary.RootElement.GetProperty("outcome").GetString());
        Assert.True(successSummary.RootElement.GetProperty("cancellationRequested").GetBoolean());
        Assert.Equal("success", successSummary.RootElement.GetProperty("outcome").GetString());
        Assert.True(cancellationSummary.RootElement.GetProperty("cancellationRequested").GetBoolean());
        Assert.Equal("cancelled", cancellationSummary.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("incomplete", incompleteSummary.RootElement.GetProperty("outcome").GetString());
    }

    private static byte[] ToolCall(int id, string toolName, string arguments) =>
        Frame("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":{\"name\":" + JsonSerializer.Serialize(toolName) + ",\"arguments\":" + arguments + "}}");

    private static byte[] Frame(string json) => Encoding.UTF8.GetBytes(json + "\n");

    private static string FindCallDirectory(string sessionDirectory, string toolName) =>
        Directory.EnumerateDirectories(Path.Combine(sessionDirectory, toolName)).Single();

    private sealed class CaptureFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ainet-traffic-capture-" + Guid.NewGuid().ToString("N"));

        internal CaptureFixture() => Directory.CreateDirectory(_directory);
        internal string Root => Path.Combine(_directory, "logs");
        internal TrafficCaptureSession CreateSession(TrafficCaptureOptions? options = null) =>
            new(Root, options ?? new TrafficCaptureOptions(true, 7, 1_000_000), "AiNetCodeNavigator", "1.2.3");
        internal string SessionDirectory(TrafficCaptureSession session) => Path.Combine(Root, "traffic", session.SessionId.ToString("D"));

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class DirectoryJunction(string junctionPath) : IDisposable
    {
        internal static DirectoryJunction Create(string junctionPath, string targetPath)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(junctionPath);
            startInfo.ArgumentList.Add(targetPath);
            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new Xunit.Sdk.XunitException("Could not start cmd.exe to create a temporary directory junction.");
            process.WaitForExit();
            Assert.True(process.ExitCode == 0 && Directory.Exists(junctionPath), "Could not create a temporary directory junction.");
            return new DirectoryJunction(junctionPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(junctionPath)) Directory.Delete(junctionPath);
        }
    }
}
