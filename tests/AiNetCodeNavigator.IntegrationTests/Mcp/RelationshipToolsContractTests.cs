using System.Linq;
using System.Text.Json;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.Mcp.Validation;
using AiNetCodeNavigator.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class RelationshipToolsContractTests
{
    [Fact]
    public async Task NavigationSdkMetadataExplainsMatchingTraversalAndDependencyScope()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var symbols = new SymbolTools(runtime);
        var relationships = new RelationshipTools(runtime);
        var find = McpServerTool.Create(typeof(SymbolTools).GetMethod(nameof(SymbolTools.FindSymbol))!, symbols);
        var references = McpServerTool.Create(typeof(RelationshipTools).GetMethod(nameof(RelationshipTools.FindReferences))!, relationships);
        var dependencies = McpServerTool.Create(typeof(RelationshipTools).GetMethod(nameof(RelationshipTools.DependencyGraph))!, relationships);

        Assert.Equal("find_symbol", find.ProtocolTool.Name);
        Assert.Contains("source or assembly", find.ProtocolTool.Description, StringComparison.Ordinal);
        foreach (var description in new[] { find.ProtocolTool.Description, ParameterDescription(find, "pattern"), ParameterDescription(find, "namePatterns") })
        {
            Assert.Contains("case-insensitive substring", description, StringComparison.Ordinal);
            Assert.Contains("anchored * / ? wildcards", description, StringComparison.Ordinal);
            Assert.Contains("automatically detected regex", description, StringComparison.Ordinal);
            Assert.Contains("qualified", description, StringComparison.Ordinal);
        }

        Assert.Equal("find_references", references.ProtocolTool.Name);
        Assert.Contains("decompiled", references.ProtocolTool.Description, StringComparison.Ordinal);
        Assert.Contains("bounded resolved owner closure", references.ProtocolTool.Description, StringComparison.Ordinal);
        var referenceDepth = ParameterDescription(references, "depth");
        Assert.Contains("Depth 1 returns direct references", referenceDepth, StringComparison.Ordinal);
        Assert.Contains("depths 2–3 follow caller symbols breadth-first", referenceDepth, StringComparison.Ordinal);
        Assert.Contains("fixed symbol-visit budget", referenceDepth, StringComparison.Ordinal);
        Assert.Contains("pages do not remove", referenceDepth, StringComparison.Ordinal);

        Assert.Equal("dependency_graph", dependencies.ProtocolTool.Name);
        Assert.Contains("not a call graph", dependencies.ProtocolTool.Description, StringComparison.Ordinal);
        Assert.Contains("shallow outgoing", dependencies.ProtocolTool.Description, StringComparison.Ordinal);
        Assert.Contains("materialized decompiled source", dependencies.ProtocolTool.Description, StringComparison.Ordinal);
        Assert.Contains("output pages do not remove analysis bounds", dependencies.ProtocolTool.Description, StringComparison.Ordinal);
        Assert.Contains("all declared types", ParameterDescription(dependencies, "filePath"), StringComparison.Ordinal);
        Assert.Contains("containing type", ParameterDescription(dependencies, "symbolIdentifier"), StringComparison.Ordinal);
        Assert.Contains("source project owner", ParameterDescription(dependencies, "symbolIdentifier"), StringComparison.Ordinal);
        Assert.Contains("scans all eligible documents", ParameterDescription(dependencies, "direction"), StringComparison.Ordinal);
        Assert.Contains("from admitted type edges", ParameterDescription(dependencies, "level"), StringComparison.Ordinal);
        Assert.Contains("ProjectReference facts without semantic document scanning", ParameterDescription(dependencies, "level"), StringComparison.Ordinal);

        static string ParameterDescription(McpServerTool tool, string parameter) =>
            tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty(parameter).GetProperty("description").GetString()!;
    }

    [Fact]
    public async Task DependencySdkContractPublishesSelectedLevelAndNullableSourceOptions()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var tools = new RelationshipTools(runtime);
        var sdk = McpServerTool.Create(typeof(RelationshipTools).GetMethod(nameof(RelationshipTools.DependencyGraph))!, tools,
            new McpServerToolCreateOptions { Name = "dependency_graph" });
        var properties = sdk.ProtocolTool.InputSchema.GetProperty("properties");
        Assert.Equal("type", properties.GetProperty("level").GetProperty("default").GetString());
        Assert.Contains("namespace", properties.GetProperty("level").GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, properties.GetProperty("scopeType").GetProperty("default").ValueKind);
        Assert.Equal(JsonValueKind.Null, properties.GetProperty("includeGenerated").GetProperty("default").ValueKind);
        Assert.Contains("project", properties.GetProperty("level").GetProperty("description").GetString());
        Assert.Contains("$.level", TextOf(await tools.DependencyGraph("C:/missing.slnx", filePath: "Root.cs", level: "invalid")));
        Assert.Contains("$.depth", TextOf(await tools.DependencyGraph("C:/missing.slnx", filePath: "Root.cs", depth: 0)));
    }

    [Fact]
    public async Task TypeRelationsSdkContractRequiresExplicitModeAndSymbol()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var tools = new RelationshipTools(runtime);
        var sdk = McpServerTool.Create(typeof(RelationshipTools).GetMethod(nameof(RelationshipTools.GetTypeRelations))!, tools,
            new McpServerToolCreateOptions { Name = "get_type_relations" });
        var required = sdk.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Contains("relation", required);
        Assert.Contains("symbolIdentifier", required);
        Assert.Contains("metadataOwnerPath", sdk.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(value => value.Name));
        using var missing = JsonDocument.Parse("""{"targetPath":"C:/missing.slnx","symbolIdentifier":"Probe.Target"}""");
        var error = await McpArgumentValidationFilter.ValidateArgumentsAsync(sdk,
            missing.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal));
        Assert.NotNull(error);
        Assert.Contains("$.relation", TextOf(error!), StringComparison.Ordinal);
        Assert.Contains("$.relation", TextOf(await tools.GetTypeRelations("C:/missing.slnx", "Probe.Target", "unknown")), StringComparison.Ordinal);
        Assert.Contains("$.scopeType", TextOf(await tools.GetTypeRelations("C:/missing.slnx", "Probe.Target", "hierarchy", scopeType: "unknown")), StringComparison.Ordinal);
        Assert.Contains("$.maxResults", TextOf(await tools.GetTypeRelations("C:/missing.slnx", "Probe.Target", "implementations", maxResults: 0)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContextSdkContractPublishesUsesAndRejectsLegacyAndIneffectiveScope()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var tools = new RelationshipTools(runtime);
        var sdkTool = McpServerTool.Create(typeof(RelationshipTools).GetMethod(nameof(RelationshipTools.GetContext))!, tools,
            new McpServerToolCreateOptions { Name = "get_context" });
        var properties = sdkTool.ProtocolTool.InputSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("usageScope", out _));
        Assert.False(properties.TryGetProperty("callerScope", out _));
        foreach (var option in new[] { "memberNameFilter", "memberKindFilter", "memberSortBy", "memberScope" })
            Assert.True(properties.TryGetProperty(option, out _));
        Assert.Contains("$.memberNameFilter", TextOf(await tools.GetContext(@"C:\missing.slnx", "Probe.Target", ["body"], memberNameFilter: "")), StringComparison.Ordinal);
        Assert.Contains("$.memberKindFilter", TextOf(await tools.GetContext(@"C:\missing.slnx", "Probe.Target", ["body"], memberKindFilter: "all")), StringComparison.Ordinal);
        Assert.Contains("$.memberSortBy", TextOf(await tools.GetContext(@"C:\missing.slnx", "Probe.Target", ["body"], memberSortBy: "lines")), StringComparison.Ordinal);
        Assert.Contains("$.memberScope", TextOf(await tools.GetContext(@"C:\missing.slnx", "Probe.Target", ["body"], memberScope: "all")), StringComparison.Ordinal);
        Assert.Contains("$.memberScope", TextOf(await tools.GetContext(@"C:\missing.dll", "Probe.Target", ["members"], memberScope: "all")), StringComparison.Ordinal);
        Assert.Contains("$.memberSortBy", TextOf(await tools.GetContext(@"C:\missing.slnx", "Probe.Target", ["members"], memberSortBy: "unknown")), StringComparison.Ordinal);

        Assert.Contains("uses", properties.GetProperty("sections").GetProperty("description").GetString());
        Assert.DoesNotContain("callers", properties.GetProperty("sections").GetProperty("description").GetString());
        using var legacy = JsonDocument.Parse("""{"targetPath":"C:\\missing.slnx","symbolIdentifier":"Probe.Target","sections":["uses"],"callerScope":"all"}""");
        var arguments = legacy.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        var invalidLegacyScope = await McpArgumentValidationFilter.ValidateArgumentsAsync(sdkTool, arguments);
        Assert.NotNull(invalidLegacyScope);
        Assert.Contains("$.callerScope", TextOf(invalidLegacyScope!), StringComparison.Ordinal);
        Assert.Contains("$.sections", TextOf(await tools.GetContext(@"C:\missing.slnx", "Probe.Target", ["callers"])), StringComparison.Ordinal);
        Assert.Contains("$.usageScope", TextOf(await tools.GetContext(@"C:\missing.slnx", "Probe.Target", ["body"], usageScope: "all")), StringComparison.Ordinal);
        Assert.Contains("$.usageScope", TextOf(await tools.GetContext(@"C:\missing.dll", "Probe.Target", ["uses"], usageScope: "all")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindReferences_SummarySdkContractRequiresSymbolAndRoutesSourceAndAssemblySymbols()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var tools = new RelationshipTools(runtime);
        Func<string, string, int, int, string, bool, bool, int, int?, string?, string?, string?, bool, CancellationToken, Task<ModelContextProtocol.Protocol.CallToolResult>> handler = tools.FindReferences;
        var sdkTool = McpServerTool.Create(handler, new McpServerToolCreateOptions { Name = "find_references" });
        var schema = sdkTool.ProtocolTool.InputSchema;
        var properties = schema.GetProperty("properties");
        var required = schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();

        Assert.Contains("symbolIdentifier", required);
        Assert.Equal("boolean", properties.GetProperty("includeSummary").GetProperty("type").GetString());
        Assert.False(properties.GetProperty("includeSummary").GetProperty("default").GetBoolean());
        Assert.DoesNotContain("includeSummary", required);
        Assert.DoesNotContain("gitRef", properties.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("detailLevel", properties.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("maxChangedSymbols", properties.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("maxTestsPerSymbol", properties.EnumerateObject().Select(property => property.Name));
        using var fixture = TestTempDirectory.Create("ainet-impact-contract-");
        var sourcePath = await CreateSourceFixtureAsync(fixture.DirectoryPath);
        foreach (var removedField in new[] { "gitRef", "detailLevel", "maxChangedSymbols", "maxTestsPerSymbol" })
        {
            using var argumentsDocument = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["targetPath"] = sourcePath,
                ["symbolIdentifier"] = "M:ImpactContractProbe.Target.Read",
                [removedField] = "legacy",
            }));
            var arguments = argumentsDocument.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
            var validation = await McpArgumentValidationFilter.ValidateArgumentsAsync(sdkTool, arguments);
            Assert.NotNull(validation);
            Assert.True(validation!.IsError);
            Assert.Contains("INVALID_ARGUMENT", TextOf(validation), StringComparison.Ordinal);
        }
        foreach (var invalidSymbol in new[] { (Present: false, Value: (object?)null), (Present: true, Value: null) })
        {
            var values = new Dictionary<string, object?> { ["targetPath"] = sourcePath };
            if (invalidSymbol.Present) values["symbolIdentifier"] = invalidSymbol.Value;
            using var argumentsDocument = JsonDocument.Parse(JsonSerializer.Serialize(values));
            var arguments = argumentsDocument.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
            var validation = await McpArgumentValidationFilter.ValidateArgumentsAsync(sdkTool, arguments);
            Assert.NotNull(validation);
            Assert.Contains("INVALID_ARGUMENT", TextOf(validation!), StringComparison.Ordinal);
        }
        Assert.True((await tools.FindReferences(sourcePath, null!, maxResponseBytes: 32768, includeSummary: true)).IsError == true);
        Assert.True((await tools.FindReferences(sourcePath, " ", maxResponseBytes: 32768, includeSummary: true)).IsError == true);

        var defaultReferences = await tools.FindReferences(sourcePath, "M:ImpactContractProbe.CompactTarget.Read", maxResults: 1, maxResponseBytes: 32768);
        using (var defaultDocument = JsonDocument.Parse(BodyOf(TextOf(defaultReferences))))
            Assert.False(defaultDocument.RootElement.TryGetProperty("summary", out _));

        var sourceResult = await tools.FindReferences(sourcePath,
            "M:ImpactContractProbe.CompactTarget.Read", maxResponseBytes: 32768, includeSummary: true);
        Assert.False(sourceResult.IsError ?? false, TextOf(sourceResult));
        await AssertCallerHandoffBodyAsync(new SymbolTools(runtime), sourcePath, sourceResult, "Caller029",
            (bytes, tokens, continuation) => tools.FindReferences(sourcePath, "M:ImpactContractProbe.CompactTarget.Read",
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation, includeSummary: true));
        await AssertImpactBudgetRecoveryAsync(tools, sourcePath, "M:ImpactContractProbe.Target.Read");
        await AssertImpactPagesReconstructAsync(tools, sourcePath, "M:ImpactContractProbe.CompactTarget.Read");

        var assemblySource = "namespace ImpactContractProbe;\n"
            + "public static class Target { public static int Read() => 1; }\n"
            + "public static class LongCaller { public static int LongCaller"
            + new string('X', 700) + "() => Target.Read(); }\n"
            + "public static class CompactTarget { public static int Read() => 2; }\n"
            + "public static class CompactCallers {\n"
            + string.Join("\n", Enumerable.Range(0, 30).Select(index => $"public static int Caller{index:D3}() => CompactTarget.Read();"))
            + "\n}";
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "ImpactContractProbe", assemblySource);
        var assemblyResult = await tools.FindReferences(assemblyPath, "M:ImpactContractProbe.CompactTarget.Read", maxResponseBytes: 32768, includeSummary: true);
        Assert.False(assemblyResult.IsError ?? false, TextOf(assemblyResult));
        await AssertCallerHandoffBodyAsync(new SymbolTools(runtime), assemblyPath, assemblyResult, "Caller029",
            (bytes, tokens, continuation) => tools.FindReferences(assemblyPath, "M:ImpactContractProbe.CompactTarget.Read",
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation, includeSummary: true));
        var omittedReferences = TextOf(assemblyResult);
        var explicitFalse = await tools.FindReferences(assemblyPath, "M:ImpactContractProbe.CompactTarget.Read", includeReferences: false, maxResponseBytes: 32768, includeSummary: true);
        Assert.Equal(omittedReferences, TextOf(explicitFalse));
        await AssertImpactBudgetRecoveryAsync(tools, assemblyPath, "M:ImpactContractProbe.Target.Read");
        await AssertImpactPagesReconstructAsync(tools, assemblyPath, "M:ImpactContractProbe.CompactTarget.Read",
            responseBytes: 4096, responseTokens: 4096);
    }

    private static string TextOf(ModelContextProtocol.Protocol.CallToolResult result) =>
        Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(result.Content)).Text;

    private static async Task AssertCallerHandoffBodyAsync(
        SymbolTools symbolTools,
        string targetPath,
        ModelContextProtocol.Protocol.CallToolResult impact,
        string expectedCaller,
        Func<int, int?, string?, Task<ModelContextProtocol.Protocol.CallToolResult>> readContinuation)
    {
        const int responseBytes = 32768;
        var pages = await ReadOuterResponsePagesAsync(readContinuation, responseBytes, null);
        Assert.Equal(BodyOf(TextOf(impact)), BodyOf(pages.FirstPage));
        Assert.Contains(expectedCaller, pages.Text, StringComparison.Ordinal);
        using var impactJson = ParseJsonWithDiagnostics(BodyOf(pages.Text),
            $"FindReferences caller extraction for {expectedCaller} (outerPages={pages.Pages}, bytes={responseBytes})");
        var selectedCaller = impactJson.RootElement.GetProperty("references").EnumerateArray()
            .Select(site => new
            {
                CallingMember = site.GetProperty("enclosingSymbolName").GetString(),
                Handoff = site.GetProperty("enclosingSymbolHandoffId").GetString(),
            })
            .FirstOrDefault(site => site.CallingMember?.Contains(expectedCaller, StringComparison.Ordinal) == true
                && !string.IsNullOrWhiteSpace(site.Handoff));
        var handoff = selectedCaller?.Handoff;
        Assert.False(string.IsNullOrWhiteSpace(handoff), "Impact call sites should expose a caller handoff.");
        var bodyPages = await ReadOuterResponsePagesAsync(
            (bytes, tokens, continuation) => symbolTools.GetSymbolBody(targetPath, [handoff!],
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation),
            32768, null);
        Assert.Contains(expectedCaller, bodyPages.Text, StringComparison.Ordinal);
        Assert.Contains("CompactTarget.Read", bodyPages.Text, StringComparison.Ordinal);
    }

    private static async Task AssertImpactPagesReconstructAsync(
        RelationshipTools tools,
        string targetPath,
        string symbolIdentifier,
        int responseBytes = 512,
        int responseTokens = 512)
    {
        var expectedPages = await ReadOuterResponsePagesAsync(
            (bytes, tokens, continuation) => tools.FindReferences(targetPath, symbolIdentifier,
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation, includeSummary: true),
            65536, 4096);
        var expectedBody = expectedPages.Text;
        var reconstructed = new System.Text.StringBuilder();
        var pageCount = 0;
        var complete = false;
        var seenContinuationTokens = new HashSet<string>(StringComparer.Ordinal);
        var requestCount = 0;
        var operationToken = (string?)null;
        var continuationToken = (string?)null;
        var result = await tools.FindReferences(targetPath, symbolIdentifier, maxResponseBytes: responseBytes, maxResponseTokens: responseTokens, includeSummary: true);
        for (; requestCount < 1_024; requestCount++)
        {
            var text = TextOf(result);
            if (result.IsError == true && text.Contains("RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
            {
                var minBytes = ReadBudget(text, "minimumResponseBytes");
                var minTokens = ReadBudget(text, "minimumResponseTokens");
                result = await tools.FindReferences(targetPath, symbolIdentifier, maxResponseBytes: minBytes, maxResponseTokens: minTokens,
                    operationToken: operationToken, continuationToken: continuationToken, includeSummary: true);
                continue;
            }

            if (TryReadToken(text, "operationToken", out var pendingOperation))
            {
                operationToken = pendingOperation;
                await Task.Delay(50);
                result = await tools.FindReferences(targetPath, symbolIdentifier, maxResponseBytes: responseBytes, maxResponseTokens: responseTokens,
                    operationToken: operationToken, continuationToken: continuationToken, includeSummary: true);
                continue;
            }

            if (text.Contains("operation=retry", StringComparison.Ordinal))
            {
                await Task.Delay(50);
                result = await tools.FindReferences(targetPath, symbolIdentifier, maxResponseBytes: responseBytes, maxResponseTokens: responseTokens,
                    operationToken: operationToken, continuationToken: continuationToken, includeSummary: true);
                continue;
            }

            var pageBody = BodyOf(text);
            Assert.NotEmpty(pageBody);
            reconstructed.Append(pageBody);
            pageCount++;
            operationToken = null;
            if (!TryReadToken(text, "continuationToken", out var nextContinuation))
            {
                complete = true;
                break;
            }
            continuationToken = nextContinuation;
            Assert.True(seenContinuationTokens.Add(continuationToken), "An outer page repeated a continuation token instead of advancing.");
            result = await tools.FindReferences(targetPath, symbolIdentifier, maxResponseBytes: responseBytes, maxResponseTokens: responseTokens,
                operationToken: operationToken, continuationToken: continuationToken, includeSummary: true);
        }

        Assert.True(complete, $"The impact continuation sequence did not finish after {requestCount} requests and {pageCount} body pages. Last response: {TextOf(result)}");
        Assert.True(pageCount > 1, "The impact response should be reconstructed from bounded response-window pages.");
        Assert.Equal(expectedBody, reconstructed.ToString());
        using var impactJson = JsonDocument.Parse(reconstructed.ToString());
        var callerMembers = impactJson.RootElement.GetProperty("references").EnumerateArray()
            .Select(site => site.GetProperty("enclosingSymbolName").GetString() ?? string.Empty).ToArray();
        Assert.Equal(30, callerMembers.Length);
        Assert.All(Enumerable.Range(0, 30), index =>
        {
            var caller = $"Caller{index:D3}";
            Assert.Single(callerMembers, member => member.Contains(caller, StringComparison.Ordinal));
        });
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }

    private static async Task AssertImpactBudgetRecoveryAsync(RelationshipTools tools, string targetPath, string symbolIdentifier)
    {
        await AssertBudgetScenarioAsync(tools, targetPath, symbolIdentifier, responseBytes: 512, responseTokens: 4096);
        await AssertBudgetScenarioAsync(tools, targetPath, symbolIdentifier, responseBytes: 65536, responseTokens: 80);
    }

    private static async Task AssertBudgetScenarioAsync(
        RelationshipTools tools,
        string targetPath,
        string symbolIdentifier,
        int responseBytes,
        int responseTokens)
    {
        var expectedPages = await ReadOuterResponsePagesAsync(
            (bytes, tokens, continuation) => tools.FindReferences(targetPath, symbolIdentifier,
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation, includeSummary: true),
            65536, 4096);
        var expectedBody = expectedPages.Text;
        var reconstructed = new System.Text.StringBuilder();
        var operationToken = (string?)null;
        var continuationToken = (string?)null;
        var minimumRetryObserved = false;
        var awaitingMinimumRetry = false;
        var awaitingControlMinimumRetry = false;
        var requestBytes = responseBytes;
        var requestTokens = responseTokens;
        var result = await tools.FindReferences(targetPath, symbolIdentifier,
            maxResponseBytes: responseBytes, maxResponseTokens: responseTokens, includeSummary: true);
        for (var request = 0; request < 200; request++)
        {
            var text = TextOf(result);
            if (result.IsError == true && text.Contains("RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
            {
                var controlBudget = text.Contains("nextAction: Repeat the unchanged fresh call", StringComparison.Ordinal)
                    || text.Contains("nextAction: Repeat the unchanged poll", StringComparison.Ordinal);
                if (controlBudget)
                {
                    Assert.False(awaitingControlMinimumRetry, "The advertised running-control minimum pair did not fit.");
                    awaitingControlMinimumRetry = true;
                }
                else
                {
                    Assert.False(awaitingMinimumRetry, "The advertised minimum pair did not deliver the current impact page.");
                    minimumRetryObserved = true;
                    awaitingMinimumRetry = true;
                    awaitingControlMinimumRetry = false;
                }
                var minimumBytes = ReadBudget(text, "minimumResponseBytes");
                var minimumTokens = ReadBudget(text, "minimumResponseTokens");
                Assert.True(minimumBytes > requestBytes || minimumTokens > requestTokens,
                    "The formatter should advertise a larger exact byte/token pair.");
                requestBytes = minimumBytes;
                requestTokens = minimumTokens;
                result = await tools.FindReferences(targetPath, symbolIdentifier,
                    maxResponseBytes: requestBytes, maxResponseTokens: requestTokens,
                    operationToken: operationToken, continuationToken: continuationToken, includeSummary: true);
                continue;
            }
            else if (TryReadToken(text, "operationToken", out var pendingOperation))
            {
                awaitingControlMinimumRetry = false;
                operationToken = pendingOperation;
                await Task.Delay(1000);
                result = await tools.FindReferences(targetPath, symbolIdentifier, maxResponseBytes: requestBytes,
                    maxResponseTokens: requestTokens, operationToken: operationToken, continuationToken: continuationToken, includeSummary: true);
                continue;
            }
            else if (text.Contains("operation=retry", StringComparison.Ordinal))
            {
                await Task.Delay(50);
                result = await tools.FindReferences(targetPath, symbolIdentifier, maxResponseBytes: requestBytes,
                    maxResponseTokens: requestTokens, operationToken: operationToken, continuationToken: continuationToken, includeSummary: true);
                continue;
            }
            else
            {
                Assert.False(result.IsError ?? false, text);
                reconstructed.Append(BodyOf(text));
            }

            operationToken = null;
            if (!TryReadToken(TextOf(result), "continuationToken", out continuationToken))
            {
                Assert.True(minimumRetryObserved, "The bounded impact response should reach an atomic unit requiring its advertised minimum budget.");
                break;
            }

            awaitingMinimumRetry = false;
            awaitingControlMinimumRetry = false;
            requestBytes = responseBytes;
            requestTokens = responseTokens;
            result = await tools.FindReferences(targetPath, symbolIdentifier, maxResponseBytes: responseBytes,
                maxResponseTokens: responseTokens, continuationToken: continuationToken, includeSummary: true);
        }

        Assert.True(minimumRetryObserved, "The bounded impact response should exercise exact minimum-budget recovery.");
        Assert.Equal(expectedBody, reconstructed.ToString());
        var longCallerNameInBody = "LongCaller" + new string('X', 700);
        using var impactDocument = ParseJsonWithDiagnostics(reconstructed.ToString(),
            $"FindReferences reconstructed long-caller page (callerLength={longCallerNameInBody.Length})");
        var longCallerSite = Assert.Single(impactDocument.RootElement.GetProperty("references").EnumerateArray()
            .Where(site => site.GetProperty("enclosingSymbolName").GetString()?.Contains(longCallerNameInBody, StringComparison.Ordinal) == true));
        var longCallerReference = longCallerSite.GetProperty("enclosingSymbolHandoffId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(longCallerReference), longCallerSite.ToString());
        Assert.Equal(1, CountOccurrences(longCallerReference!, longCallerNameInBody));
    }

    private static async Task<string> CreateSourceFixtureAsync(string root)
    {
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "ImpactContractProbe.csproj");
        var solutionPath = Path.Combine(root, "ImpactContractProbe.slnx");
        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"ImpactContractProbe.csproj\" /></Solution>");
        var nugetConfigPath = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(nugetConfigPath, "<configuration><packageSources><clear /></packageSources></configuration>");
        var callers = string.Join("\n", Enumerable.Range(0, 30).Select(index => $"public static int Caller{index:D3}() => CompactTarget.Read();"));
        await File.WriteAllTextAsync(Path.Combine(root, "ImpactContractProbe.cs"),
            "namespace ImpactContractProbe; public static class Target { public static int Read() => 1; } public static class LongCaller { public static int LongCaller"
            + new string('X', 700) + "() => Target.Read(); } public static class CompactTarget { public static int Read() => 2; } public static class Callers { " + callers + " }");
        await FixtureRestore.RunAsync(projectPath, root, nugetConfigPath, "Source fixture restore");
        return solutionPath;
    }
}
