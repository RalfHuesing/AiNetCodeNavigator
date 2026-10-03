using System.Text;
using System.Text.Json;
using System.IO.Pipelines;
using System.Threading.Channels;
using AiNetCodeNavigator.Configuration;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.TrafficCapture;
using AiNetCodeNavigator.Mcp.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

public sealed class TrafficCaptureTransportIntegrationTests
{
    [Fact]
    public async Task SdkStreamTransportCapturesSerializedToolRequestsAndValidationErrors()
    {
        using var fixture = new CaptureFixture();
        await using var capture = new TrafficCaptureSession(fixture.LogsDirectory, new TrafficCaptureOptions(true, 7, 1_000_000), "test-server", "1.0");
        var (input, output, inputWriter) = CreateTransportStreams();
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        var mcpBuilder = builder.Services.AddMcpServer(options => options.ServerInfo = ServerBuildIdentity.Create());
        McpServerHost.ConfigureTransport(mcpBuilder, capture, input, output)
            .WithTools<CaptureFixtureTools>()
            .WithRequestFilters(McpArgumentValidationFilter.Configure);
        string[] responseLines = [];

        using (var host = builder.Build())
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var hostTask = host.RunAsync(timeout.Token);
            await SendFrameAsync(inputWriter, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{},\"clientInfo\":{\"name\":\"stream-test\",\"version\":\"2.1\"}}}", timeout.Token);
            using var initializeResponse = JsonDocument.Parse(await output.ReadFrameAsync(timeout.Token));
            Assert.Equal(1, initializeResponse.RootElement.GetProperty("id").GetInt32());
            await SendFrameAsync(inputWriter, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", timeout.Token);
            await SendFrameAsync(inputWriter, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"capture_echo\",\"arguments\":{\"value\":\"hello\"}}}", timeout.Token);
            var successLine = await output.ReadFrameAsync(timeout.Token);
            await SendFrameAsync(inputWriter, "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"capture_echo\",\"arguments\":{}}}", timeout.Token);
            var invalidLine = await output.ReadFrameAsync(timeout.Token);
            await SendFrameAsync(inputWriter, "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"capture_budgeted\",\"arguments\":{\"maxResponseBytes\":512}}}", timeout.Token);
            var budgetedLine = await output.ReadFrameAsync(timeout.Token);
            responseLines = new[] { successLine, invalidLine, budgetedLine };

            using var id2Response = FindResponse(responseLines, 2);
            Assert.Contains("echo:hello", id2Response.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
            using var id3Response = FindResponse(responseLines, 3);
            var result = id3Response.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("isError").GetBoolean());
            Assert.Contains("value", result.GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.OrdinalIgnoreCase);
            using var budgetedResponse = FindResponse(responseLines, 4);
            var finalText = budgetedResponse.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
            var expectedFormatted = McpResponseFormatter.Format(CaptureFixtureTools.BudgetSource, 512);
            Assert.True(expectedFormatted.IsTruncated);
            Assert.Equal(expectedFormatted.Text, finalText);
            Assert.DoesNotContain("item-119", finalText, StringComparison.Ordinal);

            await inputWriter.CompleteAsync();
            await hostTask;
        }
        await capture.DisposeAsync();

        var sessionDirectory = Path.Combine(fixture.LogsDirectory, "traffic", capture.SessionId.ToString("D"));
        var client = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(sessionDirectory, "session.json")));
        using (client)
        {
            Assert.Equal("stream-test", client.RootElement.GetProperty("client").GetProperty("name").GetString());
        }

        var callFiles = Directory.EnumerateFiles(sessionDirectory, "input.json", SearchOption.AllDirectories).ToArray();
        Assert.Equal(3, callFiles.Length);
        foreach (var inputPath in callFiles)
        {
            var capturedInput = await File.ReadAllBytesAsync(inputPath);
            Assert.Contains("\"method\":\"tools/call\"", Encoding.UTF8.GetString(capturedInput), StringComparison.Ordinal);
            var outputPath = Path.Combine(Path.GetDirectoryName(inputPath)!, "output.json");
            var capturedOutput = await File.ReadAllBytesAsync(outputPath);
            Assert.Contains(Encoding.UTF8.GetString(capturedOutput), responseLines);
            using var inputDocument = JsonDocument.Parse(capturedInput);
            var requestId = inputDocument.RootElement.GetProperty("id").GetInt32();
            if (requestId == 4)
            {
                using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(inputPath)!, "summary.json")));
                using var response = FindResponse(responseLines, 4);
                var finalText = response.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
                Assert.Equal(Encoding.UTF8.GetByteCount(finalText), summary.RootElement.GetProperty("visibleTextBytes").GetInt32());
                Assert.Equal(McpResponseFormatter.CountTokens(finalText), summary.RootElement.GetProperty("visibleTextTokens").GetInt32());
            }
        }
        Assert.Equal(3, (await File.ReadAllLinesAsync(Path.Combine(sessionDirectory, "calls.jsonl"))).Length);
    }

    [Fact]
    public async Task DisabledCaptureUsesBareSdkStreamsAndCreatesNoCaptureFiles()
    {
        using var fixture = new CaptureFixture();
        var (input, output, inputWriter) = CreateTransportStreams();
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        var mcpBuilder = builder.Services.AddMcpServer(options => options.ServerInfo = ServerBuildIdentity.Create());
        McpServerHost.ConfigureTransport(mcpBuilder, captureSession: null, input, output)
            .WithTools<CaptureFixtureTools>();

        using (var host = builder.Build())
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var hostTask = host.RunAsync(timeout.Token);
            await SendFrameAsync(inputWriter, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{},\"clientInfo\":{\"name\":\"disabled-test\",\"version\":\"1.0\"}}}", timeout.Token);
            using var initializeResponse = JsonDocument.Parse(await output.ReadFrameAsync(timeout.Token));
            Assert.Equal(1, initializeResponse.RootElement.GetProperty("id").GetInt32());
            await SendFrameAsync(inputWriter, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", timeout.Token);
            await SendFrameAsync(inputWriter, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"capture_echo\",\"arguments\":{\"value\":\"plain\"}}}", timeout.Token);
            var resultLine = await output.ReadFrameAsync(timeout.Token);
            using var result = JsonDocument.Parse(resultLine);
            Assert.Contains("echo:plain", result.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
            await inputWriter.CompleteAsync();
            await hostTask;
        }

        Assert.False(Directory.Exists(Path.Combine(fixture.LogsDirectory, "traffic")));
    }

    private static (Stream Input, FramedOutputStream Output, PipeWriter Writer) CreateTransportStreams()
    {
        var pipe = new Pipe();
        return (pipe.Reader.AsStream(leaveOpen: true), new FramedOutputStream(), pipe.Writer);
    }

    private static async Task SendFrameAsync(PipeWriter writer, string json, CancellationToken cancellationToken)
    {
        await writer.WriteAsync(Encoding.UTF8.GetBytes(json + "\n"), cancellationToken);
        var flush = await writer.FlushAsync(cancellationToken);
        if (flush.IsCompleted) throw new EndOfStreamException("The in-memory MCP transport closed before receiving a request.");
    }

    private static JsonDocument FindResponse(IEnumerable<string> lines, int id)
    {
        foreach (var line in lines)
        {
            var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var idElement) && idElement.GetInt32() == id)
            {
                return document;
            }

            document.Dispose();
        }

        throw new Xunit.Sdk.XunitException($"MCP server did not return a response for request {id}.");
    }

    private sealed class FramedOutputStream : Stream
    {
        private readonly Channel<string> _frames = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private readonly StringBuilder _currentFrame = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        internal ValueTask<string> ReadFrameAsync(CancellationToken cancellationToken) => _frames.Reader.ReadAsync(cancellationToken);

        private void Append(ReadOnlySpan<byte> bytes)
        {
            var text = Encoding.UTF8.GetString(bytes);
            foreach (var character in text)
            {
                if (character == '\n')
                {
                    _frames.Writer.TryWrite(_currentFrame.ToString() + "\n");
                    _currentFrame.Clear();
                }
                else
                {
                    _currentFrame.Append(character);
                }
            }
        }
    }

    [McpServerToolType]
    public sealed class CaptureFixtureTools
    {
        internal static string BudgetSource { get; } = string.Join('\n', Enumerable.Range(0, 120).Select(index => $"item-{index:D3}: {new string('x', 40)}"));

        [McpServerTool(Name = "capture_echo", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        public string Echo(string value) => "echo:" + value;

        [McpServerTool(Name = "capture_budgeted", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        public string Budgeted(int maxResponseBytes) => McpResponseFormatter.Format(BudgetSource, maxResponseBytes).Text;
    }

    private sealed class CaptureFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ainet-traffic-transport-" + Guid.NewGuid().ToString("N"));

        internal string LogsDirectory => Path.Combine(_root, "logs");

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
