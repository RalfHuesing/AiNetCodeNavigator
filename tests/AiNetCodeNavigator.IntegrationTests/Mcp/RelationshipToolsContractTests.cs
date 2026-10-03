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
    public async Task GetImpact_OriginalSdkContractRequiresSymbolAndRoutesSourceAndAssemblySymbols()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var tools = new RelationshipTools(runtime);
        Func<string, string, int, int, bool, int, int?, string?, string?, string?, CancellationToken, Task<ModelContextProtocol.Protocol.CallToolResult>> handler = tools.GetImpact;
        var sdkTool = McpServerTool.Create(handler, new McpServerToolCreateOptions { Name = "get_impact" });
        var schema = sdkTool.ProtocolTool.InputSchema;
        var properties = schema.GetProperty("properties");
        var required = schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();

        Assert.Contains("symbolIdentifier", required);
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
        Assert.True((await tools.GetImpact(sourcePath, null!, maxResponseBytes: 32768)).IsError == true);
        Assert.True((await tools.GetImpact(sourcePath, " ", maxResponseBytes: 32768)).IsError == true);

        var sourceResult = await tools.GetImpact(sourcePath,
            "M:ImpactContractProbe.CompactTarget.Read", maxResponseBytes: 32768);
        Assert.False(sourceResult.IsError ?? false, TextOf(sourceResult));
        Assert.Contains("Caller029", TextOf(sourceResult), StringComparison.Ordinal);
        await AssertCallerHandoffBodyAsync(new SymbolTools(runtime), sourcePath, sourceResult, "Caller029",
            (bytes, tokens, continuation) => tools.GetImpact(sourcePath, "M:ImpactContractProbe.CompactTarget.Read",
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation));
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
        var assemblyResult = await tools.GetImpact(assemblyPath, "M:ImpactContractProbe.CompactTarget.Read", maxResponseBytes: 32768);
        Assert.False(assemblyResult.IsError ?? false, TextOf(assemblyResult));
        Assert.Contains("Caller029", TextOf(assemblyResult), StringComparison.Ordinal);
        await AssertCallerHandoffBodyAsync(new SymbolTools(runtime), assemblyPath, assemblyResult, "Caller029",
            (bytes, tokens, continuation) => tools.GetImpact(assemblyPath, "M:ImpactContractProbe.CompactTarget.Read",
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation));
        var omittedReferences = TextOf(assemblyResult);
        var explicitFalse = await tools.GetImpact(assemblyPath, "M:ImpactContractProbe.CompactTarget.Read", includeReferences: false, maxResponseBytes: 32768);
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
        using var impactJson = ParseJsonWithDiagnostics(BodyOf(pages.Text),
            $"GetImpact caller extraction for {expectedCaller} (outerPages={pages.Pages}, bytes={responseBytes})");
        var selectedCaller = impactJson.RootElement.GetProperty("callSites").EnumerateArray()
            .Select(site => new
            {
                CallingMember = site.GetProperty("callingMember").GetString(),
                Handoff = site.GetProperty("callingMemberHandoffId").GetString(),
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
            (bytes, tokens, continuation) => tools.GetImpact(targetPath, symbolIdentifier,
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation),
            65536, 4096);
        var expectedBody = expectedPages.Text;
        var reconstructed = new System.Text.StringBuilder();
        var pageCount = 0;
        var complete = false;
        var seenContinuationTokens = new HashSet<string>(StringComparer.Ordinal);
        var requestCount = 0;
        var operationToken = (string?)null;
        var continuationToken = (string?)null;
        var result = await tools.GetImpact(targetPath, symbolIdentifier, maxResponseBytes: responseBytes, maxResponseTokens: responseTokens);
        for (; requestCount < 1_024; requestCount++)
        {
            var text = TextOf(result);
            if (result.IsError == true && text.Contains("RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
            {
                var minBytes = ReadBudget(text, "minimumResponseBytes");
                var minTokens = ReadBudget(text, "minimumResponseTokens");
                result = await tools.GetImpact(targetPath, symbolIdentifier, maxResponseBytes: minBytes, maxResponseTokens: minTokens,
                    operationToken: operationToken, continuationToken: continuationToken);
                continue;
            }

            if (TryReadToken(text, "operationToken", out var pendingOperation))
            {
                operationToken = pendingOperation;
                await Task.Delay(50);
                result = await tools.GetImpact(targetPath, symbolIdentifier, maxResponseBytes: responseBytes, maxResponseTokens: responseTokens,
                    operationToken: operationToken, continuationToken: continuationToken);
                continue;
            }

            if (text.Contains("operation=retry", StringComparison.Ordinal))
            {
                await Task.Delay(50);
                result = await tools.GetImpact(targetPath, symbolIdentifier, maxResponseBytes: responseBytes, maxResponseTokens: responseTokens,
                    operationToken: operationToken, continuationToken: continuationToken);
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
            result = await tools.GetImpact(targetPath, symbolIdentifier, maxResponseBytes: responseBytes, maxResponseTokens: responseTokens,
                operationToken: operationToken, continuationToken: continuationToken);
        }

        Assert.True(complete, $"The impact continuation sequence did not finish after {requestCount} requests and {pageCount} body pages. Last response: {TextOf(result)}");
        Assert.True(pageCount > 1, "The impact response should be reconstructed from bounded response-window pages.");
        Assert.Equal(expectedBody, reconstructed.ToString());
        using var impactJson = JsonDocument.Parse(reconstructed.ToString());
        var callerMembers = impactJson.RootElement.GetProperty("callSites").EnumerateArray()
            .Select(site => site.GetProperty("callingMember").GetString() ?? string.Empty).ToArray();
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
            (bytes, tokens, continuation) => tools.GetImpact(targetPath, symbolIdentifier,
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation),
            65536, 4096);
        var expectedBody = expectedPages.Text;
        var reconstructed = new System.Text.StringBuilder();
        var operationToken = (string?)null;
        var continuationToken = (string?)null;
        var minimumRetryObserved = false;
        var awaitingMinimumRetry = false;
        var result = await tools.GetImpact(targetPath, symbolIdentifier,
            maxResponseBytes: responseBytes, maxResponseTokens: responseTokens);
        for (var request = 0; request < 200; request++)
        {
            var text = TextOf(result);
            if (result.IsError == true && text.Contains("RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
            {
                Assert.False(awaitingMinimumRetry, "The advertised minimum pair did not deliver the current impact page.");
                minimumRetryObserved = true;
                awaitingMinimumRetry = true;
                var minimumBytes = ReadBudget(text, "minimumResponseBytes");
                var minimumTokens = ReadBudget(text, "minimumResponseTokens");
                Assert.True(minimumBytes > responseBytes || minimumTokens > responseTokens,
                    "The formatter should advertise a larger exact byte/token pair.");
                result = await tools.GetImpact(targetPath, symbolIdentifier,
                    maxResponseBytes: minimumBytes, maxResponseTokens: minimumTokens,
                    operationToken: operationToken, continuationToken: continuationToken);
                var retryText = TextOf(result);
                Assert.False(result.IsError ?? false, retryText);
                reconstructed.Append(BodyOf(retryText));
            }
            else if (TryReadToken(text, "operationToken", out var pendingOperation))
            {
                operationToken = pendingOperation;
                await Task.Delay(50);
                result = await tools.GetImpact(targetPath, symbolIdentifier, maxResponseBytes: responseBytes,
                    maxResponseTokens: responseTokens, operationToken: operationToken, continuationToken: continuationToken);
                continue;
            }
            else if (text.Contains("operation=retry", StringComparison.Ordinal))
            {
                await Task.Delay(50);
                result = await tools.GetImpact(targetPath, symbolIdentifier, maxResponseBytes: responseBytes,
                    maxResponseTokens: responseTokens, operationToken: operationToken, continuationToken: continuationToken);
                continue;
            }
            else
            {
                reconstructed.Append(BodyOf(text));
            }

            operationToken = null;
            if (!TryReadToken(TextOf(result), "continuationToken", out continuationToken))
            {
                Assert.True(minimumRetryObserved, "The bounded impact response should reach an atomic unit requiring its advertised minimum budget.");
                break;
            }

            awaitingMinimumRetry = false;
            result = await tools.GetImpact(targetPath, symbolIdentifier, maxResponseBytes: responseBytes,
                maxResponseTokens: responseTokens, continuationToken: continuationToken);
        }

        Assert.True(minimumRetryObserved, "The bounded impact response should exercise exact minimum-budget recovery.");
        Assert.Equal(expectedBody, reconstructed.ToString());
        var longCallerNameInBody = "LongCaller" + new string('X', 700);
        using var impactDocument = ParseJsonWithDiagnostics(reconstructed.ToString(),
            $"GetImpact reconstructed long-caller page (callerLength={longCallerNameInBody.Length})");
        var longCallerSite = Assert.Single(impactDocument.RootElement.GetProperty("callSites").EnumerateArray()
            .Where(site => site.GetProperty("callingMember").GetString()?.Contains(longCallerNameInBody, StringComparison.Ordinal) == true));
        var longCallerReference = longCallerSite.GetProperty("callingMemberHandoffId").GetString();
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
