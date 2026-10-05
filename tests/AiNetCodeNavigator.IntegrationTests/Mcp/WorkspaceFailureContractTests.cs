using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.Tools;
using AiNetCodeNavigator.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Globalization;
using System.Text;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class WorkspaceFailureContractTests
{
    [Theory]
    [InlineData(300, 16_384, null)]
    [InlineData(2_000, 512, null)]
    [InlineData(2_000, 512, 120)]
    [InlineData(70_000, 16_384, null)]
    public async Task BrowseTarget_FailedLoadReturnsCompleteErrorAndAllowsFreshRetry(int diagnosticLength, int bytes, int? tokens)
    {
        using var fixture = TestTempDirectory.Create("workspace-failure-");
        var target = fixture.CreateFile("Source.slnx", string.Empty);
        var cause = new IOException("Missing build targets: " + new string('x', diagnosticLength) + " diagnostic end.");
        var failure = new InvalidOperationException("Design-time loading failed.", cause);
        var creations = 0;
        var loads = 0;
        await using var registry = new ProjectRegistry(new ProjectRegistryOptions(_ =>
        {
            Interlocked.Increment(ref creations);
            failure = new InvalidOperationException("Design-time loading failed.", cause);
            return ResidentSolutionCreation.Resident(new ResidentSolution(
                _ =>
                {
                    Interlocked.Increment(ref loads);
                    return Task.FromException<ResidentLoadedState?>(failure);
                }, target));
        }, TimeProvider.System));
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        var sink = new ErrorSink();
        await using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>(),
            projectRegistry: registry, workspaceLogger: logger);
        var tools = new StructureTools(runtime);

        var response = await tools.BrowseTarget(target, "scope", maxResponseBytes: bytes, maxResponseTokens: tokens);
        var text = TextOf(response);
        Assert.True(response.IsError, text);
        Assert.True(Encoding.UTF8.GetByteCount(text) <= bytes);
        if (tokens is { } cap) Assert.True(McpResponseFormatter.CountTokens(text) <= cap);
        var logged = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Error, logged.Level);
        Assert.Equal(failure.ToString(), Assert.IsType<ScalarValue>(logged.Properties["DiagnosticDetails"]).Value);
        Assert.Equal(target, Assert.IsType<ScalarValue>(logged.Properties["TargetPath"]).Value);

        if (text.Contains("RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
        {
            var minimumBytes = Minimum("minimumResponseBytes: ");
            var minimumTokens = Minimum("minimumResponseTokens: ");
            response = await tools.BrowseTarget(target, "scope", maxResponseBytes: minimumBytes, maxResponseTokens: minimumTokens);
            text = TextOf(response);
            Assert.True(response.IsError, text);
            Assert.DoesNotContain("RESPONSE_BUDGET_TOO_SMALL", text, StringComparison.Ordinal);
            Assert.True(Encoding.UTF8.GetByteCount(text) <= minimumBytes);
            Assert.True(McpResponseFormatter.CountTokens(text) <= minimumTokens);
        }
        Assert.Contains("PROJECT_LOAD_FAILED", text, StringComparison.Ordinal);
        if (diagnosticLength < 65_536) Assert.Contains(failure.ToString(), text, StringComparison.Ordinal);
        else Assert.Contains("Read the complete diagnostic in the server log", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Wait briefly", text, StringComparison.Ordinal);

        var previousCreations = creations;
        var retry = await tools.BrowseTarget(target, "scope");
        Assert.True(retry.IsError);
        Assert.Equal(previousCreations + 1, creations);
        Assert.Equal(creations, loads);

        int Minimum(string prefix) => int.Parse(text.Split('\n').Single(line => line.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..], CultureInfo.InvariantCulture);
    }

    private static string TextOf(CallToolResult response) => Assert.IsType<TextContentBlock>(Assert.Single(response.Content)).Text;

    private sealed class ErrorSink : ILogEventSink
    {
        internal List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
