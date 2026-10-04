using System.Globalization;
using System.Text.Json;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Validation;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class LongRunningNavigationProgressContractTests
{
    [Fact]
    public async Task DependencyGraph_InitialMaterializationRemainsLoadingUntilAnalysisStarts()
    {
        using var fixture = TestTempDirectory.Create("ainet-initial-load-progress-");
        var target = fixture.CreateFile("Loading.slnx", string.Empty);
        const string source = "namespace LoadingProbe; public class Root { }";
        var document = fixture.CreateFile("src/App/Root.cs", source);
        using var workspace = TestWorkspaceBuilder.CreateSolution(target,
            new ProjectSpec("App", [(document, source)], VirtualProjectDirectory: "src/App"));
        var loadStarted = Signal();
        var releaseLoad = Signal();
        var analysisStarted = Signal();
        var releaseAnalysis = Signal();
        var creations = 0;
        var scans = 0;
        await using var registry = new ProjectRegistry(new ProjectRegistryOptions(_ =>
        {
            Interlocked.Increment(ref creations);
            return ResidentSolutionCreation.Resident(new ResidentSolution(async cancellationToken =>
            {
                loadStarted.TrySetResult();
                await releaseLoad.Task.WaitAsync(cancellationToken);
                return workspace.Solution;
            }));
        }, TimeProvider.System));
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(DocumentCollected: _ =>
        {
            Interlocked.Increment(ref scans);
            analysisStarted.TrySetResult();
            releaseAnalysis.Task.GetAwaiter().GetResult();
        }));
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>(),
            operationResponseWindow: TimeSpan.FromMilliseconds(10), projectRegistry: registry,
            dependencyGraphCache: cache, operationPollResponseWindow: TimeSpan.FromMilliseconds(20));
        var tools = new RelationshipTools(runtime);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var first = await CallAsync(null);
            AssertRunning(first);
            var token = OperationToken(first);
            await loadStarted.Task.WaitAsync(timeout.Token);
            var loading = await PollAsync(token);
            Assert.Equal("loading", StringField(loading, "phase"));
            Assert.DoesNotContain("processedDocuments:", TextOf(loading), StringComparison.Ordinal);
            Assert.DoesNotContain("totalDocuments:", TextOf(loading), StringComparison.Ordinal);
            Assert.Equal(1, creations);
            Assert.Equal(0, scans);

            releaseLoad.TrySetResult();
            await analysisStarted.Task.WaitAsync(timeout.Token);
            var analyzing = await PollAsync(token);
            Assert.Equal("analyzing", StringField(analyzing, "phase"));
            Assert.Equal(0, Field(analyzing, "processedDocuments"));
            Assert.Equal(1, Field(analyzing, "totalDocuments"));
            Assert.True(Field(analyzing, "elapsedMilliseconds") >= Field(loading, "elapsedMilliseconds"));
            releaseAnalysis.TrySetResult();
            await Task.Delay(1000, timeout.Token);
            var final = await CallAsync(token);
            AssertSuccessWithinBudget(final, 65536, 4096);
            Assert.Equal(1, creations);
            Assert.Equal(1, scans);
        }
        finally
        {
            releaseLoad.TrySetResult();
            releaseAnalysis.TrySetResult();
        }

        Task<CallToolResult> CallAsync(string? token) => tools.DependencyGraph(target,
            symbolIdentifier: "T:LoadingProbe.Root", direction: "outgoing", depth: 1,
            operationToken: token, maxResponseBytes: 65536, maxResponseTokens: 4096, cancellationToken: timeout.Token);

        async Task<CallToolResult> PollAsync(string token)
        {
            await Task.Delay(1000, timeout.Token);
            var result = await CallAsync(token);
            AssertRunning(result);
            Assert.Equal(token, OperationToken(result));
            return result;
        }
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public async Task DependencyGraph_ActualRoutePreflightsThenPollsOneJobWithMeasuredRequiredCoverage(int depth, int expectedScans)
    {
        using var fixture = TestTempDirectory.Create("ainet-running-progress-");
        var target = fixture.CreateFile("Progress.slnx", string.Empty);
        var texts = new Dictionary<string, string>
        {
            ["Root.cs"] = "namespace ProgressProbe; public partial class Root { public Node Value = new(); }",
            ["Root.Partial.cs"] = "namespace ProgressProbe; public partial class Root { }",
            ["Node.cs"] = "namespace ProgressProbe; public class Node { public Leaf Value = new(); }",
            ["Leaf.cs"] = "namespace ProgressProbe; public class Leaf { }",
            ["Unused.cs"] = "namespace ProgressProbe; public class Unused { public Root Value = new(); }"
        };
        var documents = texts.Select(pair => (fixture.CreateFile("src/App/" + pair.Key, pair.Value), pair.Value)).ToArray();
        using var workspace = TestWorkspaceBuilder.CreateSolution(target,
            new ProjectSpec("App", documents, VirtualProjectDirectory: "src/App"));
        var creations = 0;
        await using var registry = new ProjectRegistry(new ProjectRegistryOptions(_ =>
        {
            Interlocked.Increment(ref creations);
            return ResidentSolutionCreation.Resident(new ResidentSolution(workspace.Solution));
        }, TimeProvider.System));
        var rootStarted = Signal();
        var partialStarted = Signal();
        var nodeStarted = Signal();
        var releaseRoot = Signal();
        var releasePartial = Signal();
        var releaseNode = Signal();
        var scans = 0;
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(DocumentCollected: document =>
        {
            Interlocked.Increment(ref scans);
            if (document.Name == "Root.cs") { rootStarted.TrySetResult(); releaseRoot.Task.GetAwaiter().GetResult(); }
            if (document.Name == "Root.Partial.cs") { partialStarted.TrySetResult(); releasePartial.Task.GetAwaiter().GetResult(); }
            if (document.Name == "Node.cs") { nodeStarted.TrySetResult(); releaseNode.Task.GetAwaiter().GetResult(); }
        }));
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>(),
            operationResponseWindow: TimeSpan.FromMilliseconds(10), projectRegistry: registry,
            dependencyGraphCache: cache, operationPollResponseWindow: TimeSpan.FromMilliseconds(20));
        var tools = new RelationshipTools(runtime);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var minimum = McpToolResults.RunningAdmissionMinimum(new string('1', 39));
            var rejected = await CallAsync(null, minimum.Bytes, minimum.Tokens - 1);
            AssertErrorWithinBudget(rejected, "RESPONSE_BUDGET_TOO_SMALL", minimum.Bytes, minimum.Tokens - 1);
            Assert.Equal(0, creations);
            Assert.Equal(0, scans);
            var bytes = (int)Field(rejected, "minimumResponseBytes");
            var tokens = (int)Field(rejected, "minimumResponseTokens");
            Assert.Contains($"maxResponseBytes={bytes}", TextOf(rejected), StringComparison.Ordinal);
            Assert.Contains($"maxResponseTokens={tokens}", TextOf(rejected), StringComparison.Ordinal);

            var first = await CallAsync(null, bytes, tokens);
            var token = OperationToken(first);
            Assert.Matches("^[0-9]{39}$", token);
            AssertRunning(first);
            await rootStarted.Task.WaitAsync(timeout.Token);
            var initialProgress = await PollAsync(token);
            Assert.Equal("analyzing", StringField(initialProgress, "phase"));
            Assert.Equal(0, Field(initialProgress, "processedDocuments"));
            AssertTotal(initialProgress);
            releaseRoot.TrySetResult();
            await partialStarted.Task.WaitAsync(timeout.Token);
            var partialProgress = await PollAsync(token);
            Assert.Equal(1, Field(partialProgress, "processedDocuments"));
            AssertTotal(partialProgress);
            Assert.True(Field(partialProgress, "elapsedMilliseconds") >= Field(initialProgress, "elapsedMilliseconds"));
            Assert.Equal(token, OperationToken(partialProgress));
            Assert.Equal(1, creations);
            releasePartial.TrySetResult();

            if (depth == 2)
            {
                await nodeStarted.Task.WaitAsync(timeout.Token);
                var expanded = await PollAsync(token);
                Assert.Equal(2, Field(expanded, "processedDocuments"));
                AssertTotal(expanded);
            }
            releaseNode.TrySetResult();
            await Task.Delay(1000, timeout.Token);
            var final = await CallAsync(token, 65536, 4096);
            for (var attempt = 0; TextOf(final).StartsWith(McpToolResults.RunningStatusPrefix, StringComparison.Ordinal) && attempt < 3; attempt++)
            {
                AssertTotal(final);
                await Task.Delay(1000, timeout.Token);
                final = await CallAsync(token, 65536, 4096);
            }
            AssertSuccessWithinBudget(final, 65536, 4096);
            using var payload = JsonDocument.Parse(BodyOf(TextOf(final)));
            Assert.Equal(expectedScans, payload.RootElement.GetProperty("scannedDocumentCount").GetInt32());
            Assert.Equal(5, payload.RootElement.GetProperty("totalDocumentCount").GetInt32());
            Assert.True(payload.RootElement.GetProperty("isComplete").GetBoolean(), TextOf(final));
            Assert.Equal(expectedScans, scans);
            var replay = await CallAsync(token, 65536, 4096);
            Assert.Equal(TextOf(final), TextOf(replay));
            Assert.Equal(expectedScans, scans);
            Assert.Equal(1, creations);
        }
        finally
        {
            releaseRoot.TrySetResult();
            releasePartial.TrySetResult();
            releaseNode.TrySetResult();
        }

        Task<CallToolResult> CallAsync(string? token, int bytes, int tokens) => tools.DependencyGraph(target,
            symbolIdentifier: "T:ProgressProbe.Root", direction: "outgoing", depth: depth,
            operationToken: token, maxResponseBytes: bytes, maxResponseTokens: tokens, cancellationToken: timeout.Token);

        async Task<CallToolResult> PollAsync(string token)
        {
            await Task.Delay(1000, timeout.Token);
            var result = await CallAsync(token, 65536, 4096);
            AssertRunning(result);
            Assert.Equal(token, OperationToken(result));
            return result;
        }

        void AssertTotal(CallToolResult result)
        {
            if (depth == 1) Assert.Equal(2, Field(result, "totalDocuments"));
            else Assert.DoesNotContain("totalDocuments:", TextOf(result), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DependencyGraph_SdkSchemaKeepsPollingOpaqueAndInternalWindowsOutOfArguments()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var tools = new RelationshipTools(runtime);
        Func<string, string?, string?, string, int, int, string, bool, int, int?, string?, string?, CancellationToken, Task<CallToolResult>> handler = tools.DependencyGraph;
        var tool = McpServerTool.Create(handler, new McpServerToolCreateOptions { Name = "dependency_graph" });
        var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
        Assert.Equal(new[] { "string", "null" }, properties.GetProperty("operationToken").GetProperty("type")
            .EnumerateArray().Select(type => type.GetString()).ToArray());
        Assert.False(properties.TryGetProperty("pollResponseWindow", out _));
        Assert.False(properties.TryGetProperty("responseWindow", out _));
        Assert.False(properties.TryGetProperty("cancellationToken", out _));
        Assert.False(properties.TryGetProperty("progressToken", out _));
        using var arguments = JsonDocument.Parse("""{"targetPath":"C:\\Source.slnx","symbolIdentifier":"T:Probe.Root","operationToken":"opaque-token"}""");
        Assert.Null(await McpArgumentValidationFilter.ValidateArgumentsAsync(tool,
            arguments.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)));
        using var malformed = JsonDocument.Parse("""{"targetPath":"C:\\Source.slnx","symbolIdentifier":"T:Probe.Root","operationToken":17}""");
        Assert.NotNull(await McpArgumentValidationFilter.ValidateArgumentsAsync(tool,
            malformed.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static string OperationToken(CallToolResult result) => TextOf(result).Split('\n')
        .Single(line => line.StartsWith("operationToken=", StringComparison.Ordinal))["operationToken=".Length..];
    private static string StringField(CallToolResult result, string name) => TextOf(result).Split('\n')
        .Single(line => line.StartsWith(name + ": ", StringComparison.Ordinal))[(name.Length + 2)..];
    private static long Field(CallToolResult result, string name) => long.Parse(StringField(result, name), CultureInfo.InvariantCulture);
    private static void AssertRunning(CallToolResult result)
    {
        Assert.False(result.IsError ?? false, TextOf(result));
        Assert.StartsWith(McpToolResults.RunningStatusPrefix, TextOf(result), StringComparison.Ordinal);
        Assert.Equal(1000, Field(result, "retryAfterMilliseconds"));
        Assert.True(Field(result, "elapsedMilliseconds") >= 0);
        Assert.Contains("nextAction: " + McpToolResults.RunningNextAction, TextOf(result), StringComparison.Ordinal);
    }
}
