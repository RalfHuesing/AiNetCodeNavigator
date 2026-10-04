using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Tools;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Protocol;
using Xunit;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class SourceToolsContractTests
{
    private readonly Xunit.ITestOutputHelper _output;

    public SourceToolsContractTests(Xunit.ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task FindSymbolFiltersSelectExactProjectsAndDeclaredExtensionsBeforePaging()
    {
        using var fixture = TestTempDirectory.Create("discovery-filter-");
        var target = Path.Combine(fixture.DirectoryPath, "Filter.slnx");
        await File.WriteAllTextAsync(target, "<Solution />");
        var firstPath = Path.Combine(fixture.DirectoryPath, "first", "Extensions.cs");
        var secondPath = Path.Combine(fixture.DirectoryPath, "second", "Extensions.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondPath)!);
        const string firstSource = "namespace Discovery.First; public static class Extensions { public static int Mark(this string value) => 1; public static int Size(this string value) => 2; public static int Plain(string value) => 3; }";
        const string secondSource = "namespace Discovery.Second; public static class Extensions { public static int Mark(this int value) => 4; }";
        await File.WriteAllTextAsync(firstPath, firstSource);
        await File.WriteAllTextAsync(secondPath, secondSource);
        await using var host = InMemorySourceTestHost.Create(target, [
            new ProjectSpec("Owner", [(firstPath, firstSource)], VirtualProjectDirectory: "first"),
            new ProjectSpec("OWNER", [(secondPath, secondSource)], VirtualProjectDirectory: "second"),
        ]);
        var tools = new SymbolTools(host.Runtime);
        var sdk = ModelContextProtocol.Server.McpServerTool.Create(typeof(SymbolTools).GetMethod(nameof(SymbolTools.FindSymbol))!, tools,
            new ModelContextProtocol.Server.McpServerToolCreateOptions { Name = "find_symbol" });
        var properties = sdk.ProtocolTool.InputSchema.GetProperty("properties");
        foreach (var option in new[] { "project", "namespaceFilter", "signatureFilter", "extensionOnly", "receiverType", "includeDiagnostics" })
            Assert.True(properties.TryGetProperty(option, out _));
        Assert.False(properties.GetProperty("extensionOnly").GetProperty("default").GetBoolean());
        Assert.Contains("$.namePatterns", TextOf(await tools.FindSymbol(target, extensionOnly: true, pattern: " ")), StringComparison.Ordinal);
        Assert.Contains("$.namePatterns", TextOf(await tools.FindSymbol(target, extensionOnly: true, pattern: "*", namePatterns: ["*"])), StringComparison.Ordinal);
        foreach (var invalid in new[] {
            await tools.FindSymbol(target, pattern: "*", project: ""),
            await tools.FindSymbol(target, pattern: "*", project: "missing"),
            await tools.FindSymbol(typeof(SourceToolsContractTests).Assembly.Location, pattern: "*", project: "Owner"),
        }) Assert.Contains("$.project", TextOf(invalid), StringComparison.Ordinal);
        AssertErrorWithinBudget(await tools.FindSymbol(target, pattern: "*", project: "owner"), "AMBIGUOUS_SYMBOL", 16384, 4096);
        Assert.Contains("$.receiverType", TextOf(await tools.FindSymbol(target, pattern: "*", receiverType: "string")), StringComparison.Ordinal);
        Assert.Contains("$.kind", TextOf(await tools.FindSymbol(target, extensionOnly: true, kind: "class")), StringComparison.Ordinal);
        Assert.Contains("$.namePatterns", TextOf(await tools.FindSymbol(target)), StringComparison.Ordinal);
        var projectPath = Path.Combine(fixture.DirectoryPath, "first", "Owner.csproj").Replace('\\', '/');
        var first = await tools.FindSymbol(target, extensionOnly: true, project: projectPath, namespaceFilter: "FIRST", receiverType: "global::System.String", maxResults: 1);
        AssertSuccessWithinBudget(first, 16384, 4096);
        using var firstJson = JsonDocument.Parse(JsonBody(TextOf(first)));
        var result = firstJson.RootElement.GetProperty("results")[0];
        Assert.Equal(2, result.GetProperty("totalMatches").GetInt32());
        var entry = Assert.Single(result.GetProperty("entries").EnumerateArray());
        Assert.True(entry.GetProperty("isExtension").GetBoolean());
        Assert.Equal("Mark", entry.GetProperty("name").GetString());
        var reference = entry.GetProperty("handoffId").GetString()!;
        var cursor = firstJson.RootElement.GetProperty("resultCursor").GetString();
        var second = await tools.FindSymbol(target, extensionOnly: true, project: projectPath, namespaceFilter: "FIRST", receiverType: "global::System.String", maxResults: 1, resultCursor: cursor);
        AssertSuccessWithinBudget(second, 16384, 4096);
        Assert.Contains("Size", TextOf(second), StringComparison.Ordinal);
        AssertErrorWithinBudget(await tools.FindSymbol(target, extensionOnly: true, project: projectPath, namespaceFilter: "SECOND", receiverType: "global::System.String", maxResults: 1, resultCursor: cursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);
        foreach (var (signature, expected) in new[] { ("Mark", 1), ("mark", 0), ("Mark ", 0), (" ", 2) })
        {
            var filtered = await tools.FindSymbol(target, extensionOnly: true, project: projectPath, receiverType: "string", signatureFilter: signature);
            AssertSuccessWithinBudget(filtered, 16384, 4096);
            using var json = JsonDocument.Parse(JsonBody(TextOf(filtered)));
            Assert.Equal(expected, json.RootElement.GetProperty("results")[0].GetProperty("totalMatches").GetInt32());
            Assert.Empty(json.RootElement.GetProperty("results")[0].GetProperty("kindAlternatives").EnumerateArray());
        }
        var body = await tools.GetSymbolBody(target, [reference]);
        AssertSuccessWithinBudget(body, 32768, 4096);
        Assert.Contains("=> 1", TextOf(body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindSymbolProjectionMeasuresFiftyGenericSingleLocationMatches()
    {
        using var fixture = TestTempDirectory.Create("find-symbol-token-projection-");
        var target = fixture.CreateFile("TokenFixture.slnx", "<Solution />");
        var declarations = string.Join(Environment.NewLine, Enumerable.Range(0, 50)
            .Select(index => $"    public void TokenEntry{index:D2}() {{ }}"));
        var source = $"namespace TokenFixture; public sealed class EntrySet {{{Environment.NewLine}{declarations}{Environment.NewLine}}}";
        var sourcePath = fixture.CreateFile("src/EntrySet.cs", source);
        var project = new ProjectSpec("TokenFixture", [(sourcePath, source)], VirtualProjectDirectory: "src");
        await using var testHost = InMemorySourceTestHost.Create(target, [project]);

        var result = await new SymbolTools(testHost.Runtime).FindSymbol(target, pattern: "TokenEntry", kind: "method",
            maxResults: 50, maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertSuccessWithinBudget(result, 65536, 8192);

        var visibleText = TextOf(result);
        var projectedJson = BodyOf(visibleText);
        using var projectedDocument = JsonDocument.Parse(projectedJson);
        var entries = projectedDocument.RootElement.GetProperty("results").EnumerateArray()
            .SelectMany(patternResult => patternResult.GetProperty("entries").EnumerateArray()).ToArray();
        Assert.True(entries.Length == 50,
            $"Expected 50 generic fixture matches; actual={entries.Length}. Visible response: {visibleText}");
        Assert.All(entries, entry =>
        {
            Assert.False(entry.TryGetProperty("locations", out _));
            var handoff = Assert.IsType<string>(entry.GetProperty("handoffId").GetString());
            var docCommentId = Assert.IsType<string>(entry.GetProperty("docCommentId").GetString());
            Assert.StartsWith("src:", handoff);
            Assert.StartsWith("M:", docCommentId, StringComparison.Ordinal);
        });

        var baseline = JsonNode.Parse(projectedJson)!.AsObject();
        foreach (var entry in baseline["results"]!.AsArray()
            .SelectMany(patternResult => patternResult!["entries"]!.AsArray()))
        {
            entry!["locations"] = new JsonArray(new JsonObject
            {
                ["filePath"] = entry["filePath"]!.DeepClone(),
                ["line"] = entry["line"]!.DeepClone(),
                ["endLine"] = entry["endLine"]!.DeepClone(),
                ["projectName"] = entry["projectName"]!.DeepClone(),
            });
        }

        var baselineJson = baseline.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var payloadOffset = visibleText.IndexOf(projectedJson, StringComparison.Ordinal);
        Assert.True(payloadOffset >= 0, "The visible response should contain the JSON payload after its shared header.");
        var baselineHeader = visibleText[..payloadOffset];
        if (!baselineHeader.Contains("resultContinuation=", StringComparison.Ordinal))
            baselineHeader += "resultContinuation=none\n";
        if (!baselineHeader.Contains("omissions=", StringComparison.Ordinal))
            baselineHeader += "omissions=none\n";
        var baselineText = baselineHeader + baselineJson;
        var baselineTokens = TokenCount(baselineText);
        var projectedTokens = TokenCount(visibleText);
        var tokensSaved = baselineTokens - projectedTokens;
        var baselineBytes = Encoding.UTF8.GetByteCount(baselineText);
        var projectedBytes = Encoding.UTF8.GetByteCount(visibleText);

        _output.WriteLine($"50 generic one-location find_symbol results: reconstructed prior pretty-JSON/negative-header baseline={baselineTokens} cl100k_base tokens/{baselineBytes} UTF-8 bytes; projected compact response={projectedTokens} tokens/{projectedBytes} bytes; saved={tokensSaved} tokens ({tokensSaved * 100.0 / baselineTokens:F1}%).");
        Assert.Contains("\n\"results\":[\n", projectedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"results\": [", projectedJson, StringComparison.Ordinal);
        Assert.True(tokensSaved > 0, "Omitting duplicated singleton locations should reduce visible token count.");
    }

    [Fact]
    public async Task StructureInventoriesReachEntriesBeyondThePreviousTwoHundredEntryCaps()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var fixture = TestTempDirectory.Create("structure-");
        var solutionPath = fixture.CreateFile("Workspace.slnx", string.Empty);
        var sourcePath = fixture.CreateFile("ManyTypes.cs", string.Empty);
        var members = string.Join(Environment.NewLine, Enumerable.Range(0, 205).Select(index => $"    public void Member{index:D3}() {{ }}"));
        var types = string.Join(Environment.NewLine, Enumerable.Range(0, 205).Select(index => $"public sealed class Type{index:D3} {{ }}"));
        var source = $"namespace CapProbe;{Environment.NewLine}public sealed class ManyMembers{Environment.NewLine}{{{Environment.NewLine}{members}{Environment.NewLine}}}{Environment.NewLine}{types}";
        await File.WriteAllTextAsync(sourcePath, source);
        var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath).WithCapturedCoreReferences()
            .WithProject("CapProbe", (sourcePath, source)).Build();
        await using var registry = new ProjectRegistry(new ProjectRegistryOptions(_ => ResidentSolutionCreation.Resident(new ResidentSolution(workspace.Solution)), TimeProvider.System));
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>(), projectRegistry: registry);
        var tools = new StructureTools(runtime);
        using var emitted = TestTempDirectory.Create("structure-assembly-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(emitted, "CapProbe", source);

        var sourceMembers = await ReadClassMemberPagesAsync(new RelationshipTools(runtime), solutionPath, "CapProbe.ManyMembers");
        var assemblyMembers = await ReadClassMemberPagesAsync(new RelationshipTools(runtime), assemblyPath, "CapProbe.ManyMembers");
        Assert.Equal(3, sourceMembers.Pages);
        Assert.Equal(3, assemblyMembers.Pages);
        Assert.Equal(205, sourceMembers.Items.Count);
        Assert.Equal(205, assemblyMembers.Items.Count);
        Assert.Equal(205, sourceMembers.Items.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(205, assemblyMembers.Items.Distinct(StringComparer.Ordinal).Count());
        var expectedMembers = Enumerable.Range(0, 205).Select(index => $"Member{index:D3}");
        Assert.Equal(expectedMembers, sourceMembers.Items);
        Assert.Equal(expectedMembers, assemblyMembers.Items);
        foreach (var (target, cursor) in new[] { (solutionPath, sourceMembers.FirstCursor), (assemblyPath, assemblyMembers.FirstCursor) })
        {
            AssertErrorWithinBudget(await new RelationshipTools(runtime).GetContext(target, "CapProbe.ManyMembers", ["members"], resultCursor: "malformed-cursor"), "RESULT_CURSOR_EXPIRED", 16384, 4096);
            AssertErrorWithinBudget(await new RelationshipTools(runtime).GetContext(target, "CapProbe.ManyMembers", ["members"], memberSortBy: "name", maxResults: 100, resultCursor: cursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);
            AssertErrorWithinBudget(await new RelationshipTools(runtime).GetContext(target, "CapProbe.ManyMembers", ["members"], memberScope: "tests", maxResults: 100, resultCursor: cursor), target == assemblyPath ? "INVALID_ARGUMENT" : "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);
            AssertErrorWithinBudget(await new RelationshipTools(runtime).GetContext(target, "CapProbe.Type000", ["members"], maxResults: 100, resultCursor: cursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);
            AssertErrorWithinBudget(await new RelationshipTools(runtime).GetContext(target, "CapProbe.ManyMembers", ["members"], maxResults: 99, resultCursor: cursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);
        }
        AssertErrorWithinBudget(await new RelationshipTools(runtime).GetContext(assemblyPath, "CapProbe.ManyMembers", ["members"], maxResults: 100,
            resultCursor: sourceMembers.FirstCursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);
        foreach (var target in new[] { solutionPath, assemblyPath })
        {
            var filtered = await new RelationshipTools(runtime).GetContext(target, "CapProbe.ManyMembers", ["members"], memberNameFilter: "Member204", maxResults: 100);
            AssertSuccessWithinBudget(filtered, 16 * 1024, 4096);
            using var filteredDocument = JsonDocument.Parse(BodyOf(TextOf(filtered)));
            Assert.Equal(1, filteredDocument.RootElement.GetProperty("sections")[0].GetProperty("totalCount").GetInt32());
            Assert.Equal("Member204", Assert.Single(filteredDocument.RootElement.GetProperty("sections")[0].GetProperty("items").EnumerateArray())
                .GetProperty("name").GetString());
        }

        AssertErrorWithinBudget(await new RelationshipTools(runtime).GetContext(solutionPath, "CapProbe.ManyMembers", ["members"],
            memberSortBy: "lines", maxResults: 100, resultCursor: sourceMembers.FirstCursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);
        AssertErrorWithinBudget(await new RelationshipTools(runtime).GetContext(solutionPath, "CapProbe.ManyMembers", ["members"],
            memberScope: "all", maxResults: 100, resultCursor: sourceMembers.FirstCursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);

        var sourceInventory = await ReadNamespaceItemPagesAsync(tools, solutionPath);
        var assemblyInventory = await ReadNamespaceItemPagesAsync(tools, assemblyPath);
        Assert.Equal(2, sourceInventory.Pages);
        Assert.Equal(2, assemblyInventory.Pages);
        Assert.Equal(207, sourceInventory.Items.Count);
        Assert.Equal(207, assemblyInventory.Items.Count);
        Assert.Equal(207, sourceInventory.Items.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(207, assemblyInventory.Items.Distinct(StringComparer.Ordinal).Count());
        var expectedInventory = new[] { "namespace:CapProbe", "type:ManyMembers" }
            .Concat(Enumerable.Range(0, 205).Select(index => $"type:Type{index:D3}"));
        Assert.Equal(expectedInventory, sourceInventory.Items);
        Assert.Equal(expectedInventory, assemblyInventory.Items);
        foreach (var (target, cursor) in new[] { (solutionPath, sourceInventory.FirstCursor), (assemblyPath, assemblyInventory.FirstCursor) })
        {
            AssertErrorWithinBudget(await tools.BrowseTarget(target, "namespaces", namespacePrefix: "CapProbe", resultCursor: "malformed-cursor"), "RESULT_CURSOR_EXPIRED", 16384, 4096);
            AssertErrorWithinBudget(await tools.BrowseTarget(target, "namespaces", namespacePrefix: "OtherNamespace", maxResults: 200, resultCursor: cursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);
            AssertErrorWithinBudget(await tools.BrowseTarget(target, "namespaces", namespacePrefix: "CapProbe", project: "OtherProject", maxResults: 200, resultCursor: cursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);
            AssertErrorWithinBudget(await tools.BrowseTarget(target, "namespaces", namespacePrefix: "CapProbe", maxResults: 199, resultCursor: cursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);
        }
        AssertErrorWithinBudget(await tools.BrowseTarget(assemblyPath, "namespaces", namespacePrefix: "CapProbe", maxResults: 200,
            resultCursor: sourceInventory.FirstCursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);

        AssertErrorWithinBudget(await tools.BrowseTarget(solutionPath, "namespaces", namespacePrefix: "CapProbe", depth: 1,
            maxResults: 200, resultCursor: sourceInventory.FirstCursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);
        AssertErrorWithinBudget(await tools.BrowseTarget(solutionPath, "scope", maxResults: 100,
            resultCursor: sourceInventory.FirstCursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);

        await File.WriteAllTextAsync(sourcePath, source + Environment.NewLine + "public sealed class SnapshotAdded { }");
        AssertErrorWithinBudget(await new RelationshipTools(runtime).GetContext(solutionPath, "CapProbe.ManyMembers", ["members"], maxResults: 100,
            resultCursor: sourceMembers.FirstCursor), "STALE_SNAPSHOT", 16384, 4096);
        AssertErrorWithinBudget(await tools.BrowseTarget(solutionPath, "namespaces", namespacePrefix: "CapProbe", maxResults: 200,
            resultCursor: sourceInventory.FirstCursor), "STALE_SNAPSHOT", 16384, 4096);

        AssemblyTestHelper.EmitAssembly(emitted, "CapProbe", source + Environment.NewLine + "public sealed class SnapshotAdded { }");
        AssertErrorWithinBudget(await new RelationshipTools(runtime).GetContext(assemblyPath, "CapProbe.ManyMembers", ["members"], maxResults: 100,
            resultCursor: assemblyMembers.FirstCursor), "STALE_SNAPSHOT", 16384, 4096);
        AssertErrorWithinBudget(await tools.BrowseTarget(assemblyPath, "namespaces", namespacePrefix: "CapProbe", maxResults: 200,
            resultCursor: assemblyInventory.FirstCursor), "STALE_SNAPSHOT", 16384, 4096);
    }

    [Fact]
    public async Task FindSymbolPatternBatch_UsesOneSnapshotAcrossPatternParts()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var fixture = TestTempDirectory.Create("ainet-source-pattern-batch-snapshot-");
        var solutionPath = fixture.CreateFile("Workspace.slnx", string.Empty);
        var sourcePath = fixture.CreateFile("Target.cs", "public sealed class BeforeVersion { }");
        var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject("App", (sourcePath, "public sealed class BeforeVersion { }"))
            .Build();
        using (workspace)
        {
            var editCount = 0;
            await using var registry = new ProjectRegistry(new ProjectRegistryOptions(
                _ => ResidentSolutionCreation.Resident(new ResidentSolution(workspace.Solution)),
                TimeProvider.System)
            {
                BeforeLeaseRelease = () =>
                {
                    if (Interlocked.Exchange(ref editCount, 1) == 0)
                        File.WriteAllText(sourcePath, "public sealed class AfterVersion { }");
                },
            });
            await using var runtime = new NavigatorHostRuntime(
                host.Services.GetRequiredService<IHostApplicationLifetime>(),
                projectRegistry: registry);
            var tools = new SymbolTools(runtime);

            var result = await tools.FindSymbol(solutionPath,
                namePatterns: ["BeforeVersion", "AfterVersion"], kind: "class", maxResponseBytes: 16384);

            AssertSuccessWithinBudget(result, 16384, 1024);
            Assert.Contains("\"name\":\"BeforeVersion\"", TextOf(result), StringComparison.Ordinal);
            Assert.DoesNotContain("\"name\":\"AfterVersion\"", TextOf(result), StringComparison.Ordinal);
            Assert.Equal(1, editCount);
        }
    }

    [Fact]
    public async Task FindSymbolResultCursor_ReconstructsEveryKnownMatchExactlyOnce()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var tools = new SymbolTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-source-result-cursor-");
        var target = await CreateSourceSolutionAsync(fixture.DirectoryPath);
        var names = new List<(string Pattern, string Name)>();
        string? cursor = null;
        string? firstCursor = null;

        for (var request = 0; request < 20; request++)
        {
            var result = await tools.FindSymbol(target, namePatterns: ["PageEntry", "Run"], kind: "method", maxResults: 3,
                resultCursor: cursor, maxResponseBytes: 65536, maxResponseTokens: 8192);
            AssertSuccessWithinBudget(result, 65536, 8192);
            using var document = JsonDocument.Parse(BodyOf(TextOf(result)));
            var response = document.RootElement;
            foreach (var patternResult in response.GetProperty("results").EnumerateArray())
            foreach (var entry in patternResult.GetProperty("entries").EnumerateArray())
                names.Add((patternResult.GetProperty("pattern").GetString()!, entry.GetProperty("name").GetString()!));
            cursor = response.TryGetProperty("resultCursor", out var cursorProperty)
                && cursorProperty.ValueKind != JsonValueKind.Null ? cursorProperty.GetString() : null;
            firstCursor ??= cursor;
            if (cursor is null) break;
        }

        Assert.Equal(18, names.Count);
        Assert.Equal(18, names.Distinct().Count());
        Assert.Contains(names, item => item == ("PageEntry", "PageEntry15"));
        Assert.Contains(names, item => item == ("Run", "RunTest"));
        Assert.NotNull(firstCursor);
        AssertErrorWithinBudget(await tools.FindSymbol(target, namePatterns: ["PageEntry", "Run"], kind: "method",
            scopeType: "tests", maxResults: 3, resultCursor: firstCursor,
            maxResponseBytes: 65536, maxResponseTokens: 8192), "RESULT_CURSOR_ARGUMENT_MISMATCH", 65536, 8192);

        var productionScope = await tools.FindSymbol(target, pattern: "Run", kind: "method", scopeType: "production",
            maxResults: 1, maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertSuccessWithinBudget(productionScope, 65536, 8192);
        using (var document = JsonDocument.Parse(BodyOf(TextOf(productionScope))))
        {
            var patternResult = Assert.Single(document.RootElement.GetProperty("results").EnumerateArray());
            Assert.Equal(1, patternResult.GetProperty("totalMatches").GetInt32());
            Assert.Equal("Run", Assert.Single(patternResult.GetProperty("entries").EnumerateArray()).GetProperty("name").GetString());
        }

        var testsScope = await tools.FindSymbol(target, pattern: "Run", kind: "method", scopeType: "tests",
            maxResults: 1, maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertSuccessWithinBudget(testsScope, 65536, 8192);
        using (var document = JsonDocument.Parse(BodyOf(TextOf(testsScope))))
        {
            var patternResult = Assert.Single(document.RootElement.GetProperty("results").EnumerateArray());
            Assert.Equal(1, patternResult.GetProperty("totalMatches").GetInt32());
            Assert.Equal("RunTest", Assert.Single(patternResult.GetProperty("entries").EnumerateArray()).GetProperty("name").GetString());
        }
    }

    [Fact]
    public async Task RawLocalFunctionLocationKeepsBodyWithoutStableReference()
    {
        using var workspace = TestWorkspaceBuilder.Create()
            .WithVirtualSolutionPath(@"C:\VirtualRepo\RawLocalFunction.slnx")
            .WithProject("App", ("Target.cs", "namespace RawProbe; public sealed class Target { public int Run() { int Local() => 41; return Local(); } }"))
            .Build();
        var source = workspace.Solution.Projects.Single().Documents.Single();
        var text = await source.GetTextAsync();
        var localNameColumn = text.ToString().IndexOf("Local", StringComparison.Ordinal) + 1;
        Assert.True(localNameColumn > 0);

        var body = await SourceSymbolBodyResolver.ResolveAsync(workspace.Solution,
            $"Target.cs:1:{localNameColumn}", maxBodyLines: 20);

        Assert.Null(body.Error);
        Assert.NotNull(body.Body);
        Assert.Contains("int Local() => 41", body.Body!.Body, StringComparison.Ordinal);
        Assert.Equal("available", body.Body.Availability);
        Assert.Null(body.Body.HandoffId);
        var candidate = Assert.Single(body.ResolutionCandidates);
        Assert.Equal("Local", candidate.Name);
        Assert.Null(candidate.HandoffId);
        Assert.Equal(1, candidate.Line);
    }

    [Fact]
    public async Task StableSourceReferenceSurvivesBodySourceProjectOptionAndRuntimeChanges()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var fixture = TestTempDirectory.Create("ainet-source-runtime-lifecycle-");
        var target = await CreateSourceSolutionAsync(fixture.DirectoryPath);
        var appDirectory = Path.Combine(Path.GetDirectoryName(target)!, "src", "App");
        var testsDirectory = Path.Combine(Path.GetDirectoryName(target)!, "tests", "ScopeProbe.Tests");
        await using var oldRuntime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var producer = new SymbolTools(oldRuntime);
        var found = await producer.FindSymbol(target, pattern: "Run", kind: "method", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(found, 16384, 1024);
        var reference = ReadHandoff(TextOf(found), "method Run in");
        Assert.StartsWith("src:", reference, StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(appDirectory, "Target.cs"), "namespace ScopeProbe; public class Target { public void Run() { var updated = 42; _ = updated; } }");
        var afterBodyEdit = await producer.GetSymbolBody(target, [reference], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(afterBodyEdit, 16384, 1024);
        Assert.Contains("updated", TextOf(afterBodyEdit), StringComparison.Ordinal);
        Assert.Contains("42", TextOf(afterBodyEdit), StringComparison.Ordinal);

        await File.AppendAllTextAsync(Path.Combine(testsDirectory, "TargetTests.cs"), Environment.NewLine + "// unrelated loaded source edit");
        var afterOtherSourceEdit = await producer.GetSymbolBody(target, [reference], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(afterOtherSourceEdit, 16384, 1024);
        Assert.Contains("42", TextOf(afterOtherSourceEdit), StringComparison.Ordinal);

        var projectFile = Path.Combine(appDirectory, "ScopeProbe.App.csproj");
        var projectText = await File.ReadAllTextAsync(projectFile);
        Assert.Contains("</Project>", projectText, StringComparison.Ordinal);
        await File.WriteAllTextAsync(projectFile, projectText.Replace("</Project>",
            "<PropertyGroup><DefineConstants>$(DefineConstants);R02_STABLE_REFERENCE_OPTION</DefineConstants></PropertyGroup></Project>",
            StringComparison.Ordinal));
        var afterProjectOptionChange = await producer.GetSymbolBody(target, [reference], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(afterProjectOptionChange, 16384, 1024);
        Assert.Contains("42", TextOf(afterProjectOptionChange), StringComparison.Ordinal);

        await oldRuntime.DisposeAsync();

        await using var freshRuntime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var consumer = new SymbolTools(freshRuntime);
        var body = await consumer.GetSymbolBody(target, [reference], maxResponseBytes: 16384, maxResponseTokens: 1024);

        AssertSuccessWithinBudget(body, 16384, 1024);
        Assert.Contains("updated", TextOf(body), StringComparison.Ordinal);
        Assert.Contains("42", TextOf(body), StringComparison.Ordinal);

        var freshResult = await consumer.FindSymbol(target, pattern: "Run", kind: "method", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(freshResult, 16384, 1024);
        var freshReference = ReadHandoff(TextOf(freshResult), "method Run in");
        Assert.Equal(reference, freshReference);
        await oldRuntime.DisposeAsync();
        var freshBody = await consumer.GetSymbolBody(target, [freshReference], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(freshBody, 16384, 1024);
        Assert.Contains("Run", TextOf(freshBody), StringComparison.Ordinal);

        await File.WriteAllTextAsync(Path.Combine(appDirectory, "Target.cs"),
            "namespace ScopeProbe; public class Target { public void Run(int changedSignature) { var changed = changedSignature; _ = changed; } }");
        var changedDeclaration = await consumer.GetSymbolBody(target, [reference], maxResponseBytes: 16384, maxResponseTokens: 1024);
        Assert.Contains("SYMBOL_NOT_FOUND", TextOf(changedDeclaration), StringComparison.Ordinal);
        AssertActionableRediscovery(TextOf(changedDeclaration));
        Assert.DoesNotContain("changedSignature", TextOf(changedDeclaration), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceBodyBatchPreservesValidItemAndEveryTypedReferenceFailure()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("ainet-source-body-batch-reference-preflight-");
        var target = await CreateSourceSolutionAsync(fixture.DirectoryPath);
        var symbols = new SymbolTools(runtime);
        var foundSource = await symbols.FindSymbol(target, pattern: "Run", kind: "method", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(foundSource, 16384, 1024);
        var sourceReference = ReadHandoff(TextOf(foundSource), "method Run in");
        Assert.StartsWith("src:", sourceReference, StringComparison.Ordinal);

        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "ForeignBodyBatchProbe", """
            namespace ForeignBodyBatchProbe;
            public sealed class Target { public int Read() { var first = 1; var second = 2; return first + second; } }
            """);
        var foundAssembly = await symbols.FindSymbol(assemblyPath, pattern: "Target.Read", kind: "method", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(foundAssembly, 16384, 1024);
        var assemblyReference = Assert.Single(ReadHandoffs(TextOf(foundAssembly)));
        Assert.StartsWith("asm:", assemblyReference, StringComparison.Ordinal);

        var invalidReferences = new[] { "src:malformed", "asm:malformed", assemblyReference, "h:zzzz", "i:zzzz" };
        var mixed = await symbols.GetSymbolBody(target, [sourceReference, .. invalidReferences], maxBodyLines: 1,
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(mixed, 32768, 4096);
        var mixedText = TextOf(mixed);
        Assert.Contains("Resolution status: resolved", mixedText, StringComparison.Ordinal);
        Assert.Contains("Resolution status: failed", mixedText, StringComparison.Ordinal);
        Assert.Contains("completeness=truncated", mixedText, StringComparison.Ordinal);
        Assert.Contains("Next body window: startLine=2", mixedText, StringComparison.Ordinal);
        foreach (var reference in invalidReferences)
        {
            Assert.Contains($"Symbol: {reference}", mixedText, StringComparison.Ordinal);
        }

        var allFailed = await symbols.GetSymbolBody(target, invalidReferences, maxResponseBytes: 32768, maxResponseTokens: 4096);
        Assert.True(allFailed.IsError ?? false, TextOf(allFailed));
        AssertBudget(TextOf(allFailed), 32768, 4096);
        var allFailedText = TextOf(allFailed);
        foreach (var reference in invalidReferences)
        {
            Assert.Contains($"Symbol: {reference}", allFailedText, StringComparison.Ordinal);
        }
        Assert.Equal(invalidReferences.Length, allFailedText.Split("Resolution status: failed", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task SourceSkeletonReferenceBatchPreservesMixedAndAllFailedItemsAndMetadata()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("ainet-source-skeleton-reference-batch-");
        var target = await CreateSourceSolutionAsync(fixture.DirectoryPath);
        var symbols = new SymbolTools(runtime);
        var structure = new StructureTools(runtime);

        var sourceDiscovery = await symbols.FindSymbol(target, pattern: "ScopeProbe.Target", kind: "class", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(sourceDiscovery, 16384, 1024);
        var sourceReference = ReadHandoff(TextOf(sourceDiscovery), "class Target");
        Assert.StartsWith("src:", sourceReference, StringComparison.Ordinal);
        var sourceOnly = await structure.GetFileSkeleton(target, [sourceReference], maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(sourceOnly, 32768, 4096);
        Assert.Contains("snapshotId=source:", TextOf(sourceOnly), StringComparison.Ordinal);
        Assert.Equal($"fileSkeleton(paths={sourceReference})", ReadHeader(TextOf(sourceOnly), "analyzedScope"));
        Assert.Contains("### Target", TextOf(sourceOnly), StringComparison.Ordinal);
        Assert.Contains("Run", TextOf(sourceOnly), StringComparison.Ordinal);

        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "ForeignSkeletonBatchProbe",
            "namespace ForeignSkeletonBatchProbe; public sealed class Foreign { public int Read() => 7; }");
        var assemblyDiscovery = await symbols.FindSymbol(assemblyPath, pattern: "ForeignSkeletonBatchProbe.Foreign", kind: "class", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(assemblyDiscovery, 16384, 1024);
        var assemblyReference = Assert.Single(ReadHandoffs(TextOf(assemblyDiscovery)));
        Assert.StartsWith("asm:", assemblyReference, StringComparison.Ordinal);

        var invalidReferences = new[] { "src:malformed", "asm:malformed", assemblyReference, "h:zzzz", "i:zzzz" };
        var mixed = await structure.GetFileSkeleton(target, [sourceReference, .. invalidReferences], maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(mixed, 32768, 4096);
        var mixedText = TextOf(mixed);
        Assert.Contains("snapshotId=source:", mixedText, StringComparison.Ordinal);
        Assert.Equal($"fileSkeleton(paths={string.Join('|', new[] { sourceReference }.Concat(invalidReferences))})",
            ReadHeader(mixedText, "analyzedScope"));
        Assert.Contains("### Target", mixedText, StringComparison.Ordinal);
        Assert.Contains("Run", mixedText, StringComparison.Ordinal);
        foreach (var reference in invalidReferences)
        {
            var item = IntegrationMcpAssertions.ReadItemSection(mixedText, reference);
            var code = reference == assemblyReference ? "TARGET_MISMATCH" : "INVALID_SYMBOL_REFERENCE";
            Assert.Contains($"Resolution status: failed ({code})", item, StringComparison.Ordinal);
            if (reference == assemblyReference)
            {
                Assert.Contains("Next action: Use the src:", item, StringComparison.Ordinal);
                Assert.Contains("source solution", item, StringComparison.Ordinal);
                Assert.Contains("owner targetPath", item, StringComparison.Ordinal);
            }
            else
            {
                AssertActionableRediscovery(item);
            }
        }

        var allFailed = await structure.GetFileSkeleton(target, invalidReferences, maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertErrorWithinBudget(allFailed, "INVALID_SYMBOL_REFERENCE", 32768, 4096);
        var allFailedText = TextOf(allFailed);
        foreach (var reference in invalidReferences)
        {
            var item = IntegrationMcpAssertions.ReadItemSection(allFailedText, reference);
            var code = reference == assemblyReference ? "TARGET_MISMATCH" : "INVALID_SYMBOL_REFERENCE";
            Assert.Contains($"Resolution status: failed ({code})", item, StringComparison.Ordinal);
            if (reference == assemblyReference)
            {
                Assert.Contains("Next action: Use the src:", item, StringComparison.Ordinal);
                Assert.Contains("source solution", item, StringComparison.Ordinal);
                Assert.Contains("owner targetPath", item, StringComparison.Ordinal);
            }
            else
            {
                AssertActionableRediscovery(item);
            }
        }
        Assert.Contains("Use each item's next action", allFailedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceHandlersReturnNavigableResultsAndTypedDomainErrorsWithoutTransport()
    {
        using var fixture = TestTempDirectory.Create("ainet-source-tools-contract-");
        var (target, appFile, testHost) = CreateSourceToolsHost(fixture.DirectoryPath);
        await using var ownedTestHost = testHost;
        var symbols = new SymbolTools(testHost.Runtime);
        var structure = new StructureTools(testHost.Runtime);
        var relationships = new RelationshipTools(testHost.Runtime);

        var found = await symbols.FindSymbol(target, pattern: "Run", kind: "method", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(found, 16384, 1024);
        Assert.Contains("\"name\":\"Run\"", TextOf(found), StringComparison.Ordinal);
        string runDocCommentId;
        using (var findDocument = JsonDocument.Parse(BodyOf(TextOf(found))))
        {
            var runEntry = Assert.Single(findDocument.RootElement.GetProperty("results").EnumerateArray()
                .SelectMany(result => result.GetProperty("entries").EnumerateArray())
                .Where(entry => entry.GetProperty("name").GetString() == "Run"));
            runDocCommentId = runEntry.GetProperty("docCommentId").GetString()!;
            Assert.False(runEntry.TryGetProperty("locations", out _),
                "A one-location result should use its top-level primary location without repeating it in locations.");
        }
        Assert.Contains("snapshotId=source:", TextOf(found), StringComparison.Ordinal);
        Assert.Contains("analyzedScope=findSymbol(pattern=Run", TextOf(found), StringComparison.Ordinal);
        Assert.Contains("analysisCompleteness=complete", TextOf(found), StringComparison.Ordinal);
        Assert.DoesNotContain("resultContinuation=none", TextOf(found), StringComparison.Ordinal);
        Assert.DoesNotContain("omissions=none", TextOf(found), StringComparison.Ordinal);
        var methodHandoff = ReadHandoff(TextOf(found), "method Run in");
        var generatedExcluded = await symbols.FindSymbol(target, pattern: "GeneratedProbe", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(generatedExcluded, 16384, 1024);
        Assert.DoesNotContain("\"name\":\"GeneratedProbe\"", TextOf(generatedExcluded), StringComparison.Ordinal);
        var generatedIncluded = await symbols.FindSymbol(target, pattern: "GeneratedProbe", includeGenerated: true,
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(generatedIncluded, 16384, 1024);
        Assert.Contains("\"name\":\"GeneratedProbe\"", TextOf(generatedIncluded), StringComparison.Ordinal);

        var pagedFind = await ReadAllFindSymbolPagesAsync(symbols, target, "PageEntry", 1024, 4096);
        Assert.True(pagedFind.Pages > 1);
        Assert.Equal(16, pagedFind.Text.Split("\"name\":\"PageEntry", StringSplitOptions.None).Length - 1);
        Assert.Contains("PageEntry15", pagedFind.Text, StringComparison.Ordinal);
        var tokenPagedFind = await ReadAllFindSymbolPagesAsync(symbols, target, "PageEntry", 65536, 512);
        Assert.Equal(pagedFind.Text, tokenPagedFind.Text);

        var body = await symbols.GetSymbolBody(target, [methodHandoff], maxBodyLines: 2, maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(body, 16384, 1024);
        Assert.Contains("Lines: 1-2 of", TextOf(body), StringComparison.Ordinal);
        Assert.Contains("Resolution status: resolved", TextOf(body), StringComparison.Ordinal);
        Assert.DoesNotContain($"Symbol: {methodHandoff}", TextOf(body), StringComparison.Ordinal);
        Assert.Contains($"Handoff: {methodHandoff}", TextOf(body), StringComparison.Ordinal);
        Assert.Contains("Next body window: startLine=3", TextOf(body), StringComparison.Ordinal);
        var rawSelectorBody = await symbols.GetSymbolBody(target, [runDocCommentId], maxBodyLines: 2,
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(rawSelectorBody, 16384, 1024);
        Assert.Contains($"Symbol: {runDocCommentId}", TextOf(rawSelectorBody), StringComparison.Ordinal);
        Assert.Contains($"Handoff: {methodHandoff}", TextOf(rawSelectorBody), StringComparison.Ordinal);
        var bodyRanges = new List<(int Start, int End, int Total)> { ReadBodyRange(TextOf(body)) };
        var nextStartLine = 3;
        while (bodyRanges[^1].End < bodyRanges[^1].Total)
        {
            var bodyWindow = await symbols.GetSymbolBody(target, [methodHandoff], startLine: nextStartLine, maxBodyLines: 2,
                maxResponseBytes: 16384, maxResponseTokens: 1024);
            AssertSuccessWithinBudget(bodyWindow, 16384, 1024);
            var windowText = TextOf(bodyWindow);
            Assert.Contains("Resolution status: resolved", windowText, StringComparison.Ordinal);
            var range = ReadBodyRange(windowText);
            Assert.Equal(nextStartLine, range.Start);
            Assert.Equal(bodyRanges[^1].End + 1, range.Start);
            Assert.Equal(bodyRanges[0].Total, range.Total);
            bodyRanges.Add(range);
            if (range.End < range.Total)
            {
                Assert.Contains($"Next body window: startLine={range.End + 1}, maxBodyLines=2; omit endLine", windowText, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("Next action: none; this declaration window is complete.", windowText, StringComparison.Ordinal);
            }

            nextStartLine = range.End + 1;
        }

        Assert.Equal(bodyRanges[0].Total, bodyRanges[^1].End);

        var unavailableSearch = await symbols.FindSymbol(target, pattern: "Execute", kind: "method",
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        var unavailableHandoff = ReadHandoff(TextOf(unavailableSearch), "Execute");
        var unavailableBody = await symbols.GetSymbolBody(target, [unavailableHandoff], maxResponseBytes: 16384,
            maxResponseTokens: 1024);
        AssertSuccessWithinBudget(unavailableBody, 16384, 1024);
        Assert.Contains("availability: unavailable", TextOf(unavailableBody), StringComparison.Ordinal);
        Assert.Contains("Hint: Interfaces do not provide an executable body for this symbol.", TextOf(unavailableBody), StringComparison.Ordinal);

        var longBodySymbol = await symbols.FindSymbol(target, pattern: "LargeBody", kind: "method",
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        var longBodyHandoff = ReadHandoff(TextOf(longBodySymbol), "method LargeBody in");
        var pagedBody = await ReadAllBodyPagesAsync(symbols, target, longBodyHandoff, 1024, 4096);
        Assert.True(pagedBody.Pages > 1);
        Assert.Contains("body-line-00", pagedBody.Text, StringComparison.Ordinal);
        Assert.Contains("body-line-19", pagedBody.Text, StringComparison.Ordinal);
        Assert.Contains("body-end", pagedBody.Text, StringComparison.Ordinal);
        Assert.Equal(20, pagedBody.Text.Split("body-line-", StringSplitOptions.None).Length - 1);
        Assert.Equal(20, pagedBody.Text.Split("😀", StringSplitOptions.None).Length - 1);
        var tokenPagedBody = await ReadAllBodyPagesAsync(symbols, target, longBodyHandoff, 65536, 512);
        Assert.Equal(pagedBody.Text, tokenPagedBody.Text);

        var mixedBody = await symbols.GetSymbolBody(target, [methodHandoff, "h:zzzz"], maxBodyLines: 1,
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(mixedBody, 16384, 1024);
        Assert.Contains("completeness=truncated", TextOf(mixedBody), StringComparison.Ordinal);
        Assert.Contains("INVALID_SYMBOL_REFERENCE", TextOf(mixedBody), StringComparison.Ordinal);
        Assert.Contains("Resolution status: failed (INVALID_SYMBOL_REFERENCE)", TextOf(mixedBody), StringComparison.Ordinal);
        AssertActionableRediscovery(TextOf(mixedBody));
        Assert.Contains("Next body window: startLine=2", TextOf(mixedBody), StringComparison.Ordinal);
        Assert.Contains("Run", TextOf(mixedBody), StringComparison.Ordinal);
        var allInvalidBody = await symbols.GetSymbolBody(target, ["h:zzzz", "h:aaaa"], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(allInvalidBody, "INVALID_SYMBOL_REFERENCE", 16384, 1024);
        Assert.Contains("Symbol: h:zzzz", TextOf(allInvalidBody), StringComparison.Ordinal);
        Assert.Contains("Symbol: h:aaaa", TextOf(allInvalidBody), StringComparison.Ordinal);

        var skeleton = await structure.GetFileSkeleton(target, [appFile], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(skeleton, 16384, 1024);
        var skeletonText = TextOf(skeleton);
        Assert.Contains("Target", skeletonText, StringComparison.Ordinal);
        Assert.Contains("First", skeletonText, StringComparison.Ordinal);
        Assert.Contains("Second", skeletonText, StringComparison.Ordinal);
        Assert.NotEqual(ReadHandoffOnLineContaining(skeletonText, "int First"), ReadHandoffOnLineContaining(skeletonText, "int Second"));
        var completeSkeleton = await ReadAllSkeletonPagesAsync(structure, target, appFile, 16384, 1024);
        Assert.True(completeSkeleton.Pages > 1, skeletonText);
        var skeletonHandoffs = ReadHandoffs(completeSkeleton.Text);
        Assert.True(skeletonHandoffs.Length >= 2, skeletonText);
        Assert.Equal(skeletonHandoffs.Length, skeletonHandoffs.Distinct(StringComparer.Ordinal).Count());

        var partialTypeSearch = await symbols.FindSymbol(target, pattern: "OrderProbe", kind: "class", includeGenerated: true,
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(partialTypeSearch, 16384, 1024);
        using (var partialFindDocument = JsonDocument.Parse(BodyOf(TextOf(partialTypeSearch))))
        {
            var partialEntry = Assert.Single(partialFindDocument.RootElement.GetProperty("results").EnumerateArray()
                .SelectMany(result => result.GetProperty("entries").EnumerateArray())
                .Where(entry => entry.GetProperty("name").GetString() == "OrderProbe"));
            var primary = partialEntry.GetProperty("locations")[0];
            Assert.True(partialEntry.GetProperty("locations").GetArrayLength() > 1,
                "A partial declaration must retain every visible declaring location.");
            Assert.Equal(primary.GetProperty("filePath").GetString(), partialEntry.GetProperty("filePath").GetString());
            Assert.Equal(primary.GetProperty("line").GetInt32(), partialEntry.GetProperty("line").GetInt32());
            Assert.Equal(primary.GetProperty("endLine").GetInt32(), partialEntry.GetProperty("endLine").GetInt32());
            Assert.Equal(primary.GetProperty("projectName").GetString(), partialEntry.GetProperty("projectName").GetString());
        }
        var partialTypeReference = ReadHandoff(TextOf(partialTypeSearch), "OrderProbe");
        var partialSkeleton = await structure.GetFileSkeleton(target, [partialTypeReference],
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(partialSkeleton, 16384, 1024);
        var partialSkeletonText = await ReconstructOuterPagesAsync(partialSkeleton, continuation =>
            structure.GetFileSkeleton(target, [partialTypeReference], continuationToken: continuation,
                maxResponseBytes: 16384, maxResponseTokens: 1024));
        Assert.Contains("Zebra", partialSkeletonText, StringComparison.Ordinal);
        Assert.Contains("GeneratedMember", partialSkeletonText, StringComparison.Ordinal);
        var filteredPartialStructure = await relationships.GetContext(target, partialTypeReference, ["members"],
            includeGenerated: false, maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(filteredPartialStructure, 16384, 1024);
        var filteredPartialStructureText = await ReconstructOuterPagesAsync(filteredPartialStructure, continuation =>
            relationships.GetContext(target, partialTypeReference, ["members"], includeGenerated: false,
                continuationToken: continuation, maxResponseBytes: 16384, maxResponseTokens: 1024));
        Assert.Contains("Zebra", filteredPartialStructureText, StringComparison.Ordinal);
        Assert.DoesNotContain("GeneratedMember", filteredPartialStructureText, StringComparison.Ordinal);
        var generatedPartialStructure = await relationships.GetContext(target, partialTypeReference, ["members"],
            includeGenerated: true, maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(generatedPartialStructure, 16384, 1024);
        var generatedPartialStructureText = await ReconstructOuterPagesAsync(generatedPartialStructure, continuation =>
            relationships.GetContext(target, partialTypeReference, ["members"], includeGenerated: true,
                continuationToken: continuation, maxResponseBytes: 16384, maxResponseTokens: 1024));
        Assert.Contains("Zebra", generatedPartialStructureText, StringComparison.Ordinal);
        Assert.Contains("GeneratedMember", generatedPartialStructureText, StringComparison.Ordinal);

        var pagedSkeleton = await ReadAllSkeletonPagesAsync(structure, target, appFile, 1024, 4096);
        Assert.True(pagedSkeleton.Pages > 1);
        Assert.Contains("PageEntry15", pagedSkeleton.Text, StringComparison.Ordinal);
        var tokenPagedSkeleton = await ReadAllSkeletonPagesAsync(structure, target, appFile, 65536, 512);
        Assert.Equal(pagedSkeleton.Text, tokenPagedSkeleton.Text);
        var missingSkeleton = await structure.GetFileSkeleton(target, ["Missing.cs"], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(missingSkeleton, "INVALID_ARGUMENT", 16384, 1024);
        using var outsideFileRoot = TestTempDirectory.Create("ainet-outside-skeleton-");
        var outsideFile = Path.Combine(outsideFileRoot.DirectoryPath, "Outside.cs");
        await File.WriteAllTextAsync(outsideFile, "public sealed class Outside { }");
        var outsideSkeleton = await structure.GetFileSkeleton(target, [outsideFile], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(outsideSkeleton, "INVALID_ARGUMENT", 16384, 1024);

        var classStructure = await relationships.GetContext(target, "ScopeProbe.Target", ["members"], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(classStructure, 16384, 1024);
        Assert.Contains("Run", TextOf(classStructure), StringComparison.Ordinal);
        var unknownType = await relationships.GetContext(target, "h:zzzz", ["members"], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(unknownType, "INVALID_SYMBOL_REFERENCE", 16384, 1024);
        var declarationOrder = await relationships.GetContext(target, "ScopeProbe.OrderProbe", ["members"], maxResults: 1,
            memberSortBy: "lines", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(declarationOrder, 16384, 1024);
        Assert.Contains("Zebra", TextOf(declarationOrder), StringComparison.Ordinal);
        Assert.DoesNotContain("Alpha", TextOf(declarationOrder), StringComparison.Ordinal);
        var classStructureBytes = await relationships.GetContext(target, "ScopeProbe.OrderProbe", ["members"], maxResults: 1,
            memberSortBy: "lines", maxResponseBytes: 512, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(classStructureBytes, 512, 4096);
        Assert.True(TryReadToken(TextOf(classStructureBytes), "continuationToken", out _), TextOf(classStructureBytes));
        var classStructureTokens = await relationships.GetContext(target, "ScopeProbe.OrderProbe", ["members"], maxResults: 1,
            memberSortBy: "lines", maxResponseBytes: 65536, maxResponseTokens: 512);
        if (classStructureTokens.IsError == true)
        {
            AssertErrorWithinBudget(classStructureTokens, "RESPONSE_BUDGET_TOO_SMALL", 65536, 512);
            var minimumTokens = ReadBudget(TextOf(classStructureTokens), "minimumResponseTokens");
            var retry = await relationships.GetContext(target, "ScopeProbe.OrderProbe", ["members"], maxResults: 1,
                memberSortBy: "lines", maxResponseBytes: 65536, maxResponseTokens: minimumTokens);
            AssertSuccessWithinBudget(retry, 65536, minimumTokens);
        }
        else AssertSuccessWithinBudget(classStructureTokens, 65536, 512);
        var generatedMember = await relationships.GetContext(target, "ScopeProbe.OrderProbe", ["members"], includeGenerated: true,
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(generatedMember, 16384, 1024);
        Assert.Contains("GeneratedMember", TextOf(generatedMember), StringComparison.Ordinal);

        var browseScans = new List<string>();
        structure.BeforeBrowseScanForTesting = browseScans.Add;
        var namespaceTree = await structure.BrowseTarget(target, "namespaces", project: Path.Combine(fixture.DirectoryPath, "src", "App", "ScopeProbe.App.csproj"), namespacePrefix: "ScopeProbe",
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(namespaceTree, 16384, 1024);
        using (var namespaceDocument = System.Text.Json.JsonDocument.Parse(JsonBody(TextOf(namespaceTree))))
        {
            var items = namespaceDocument.RootElement.GetProperty("items").EnumerateArray().ToArray();
            var totals = namespaceDocument.RootElement.GetProperty("totalsScope");
            Assert.Equal("selectedPrefix", totals.GetProperty("namespaces").GetString());
            Assert.Equal("selectedProject", totals.GetProperty("types").GetString());
            Assert.Contains(items, item => item.GetProperty("kind").GetString() == "namespace"
                && item.GetProperty("fullName").GetString() == "ScopeProbe");
            Assert.Contains(items, item => item.GetProperty("kind").GetString() == "type"
                && item.GetProperty("name").GetString() == "Target");
        }
        Assert.Equal(["namespaces"], browseScans);
        browseScans.Clear();
        var selectedProjectDepthRecovery = await structure.BrowseTarget(target, "namespaces",
            project: Path.Combine(fixture.DirectoryPath, "src", "App", "ScopeProbe.App.csproj"),
            namespacePrefix: "ScopeProbe", depth: 1, maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(selectedProjectDepthRecovery, 16384, 1024);
        var selectedProjectRecoveryText = TextOf(selectedProjectDepthRecovery);
        Assert.Contains("completeness=truncated", selectedProjectRecoveryText, StringComparison.Ordinal);
        Assert.Contains("increase depth to 2 for the selected namespacePrefix 'ScopeProbe'", selectedProjectRecoveryText, StringComparison.Ordinal);
        Assert.DoesNotContain("select a project", selectedProjectRecoveryText, StringComparison.OrdinalIgnoreCase);
        var pagedNamespaceItems = new List<string>();
        string? namespaceCursor = null;
        var namespacePages = 0;
        do
        {
            var page = await structure.BrowseTarget(target, "namespaces", project: Path.Combine(fixture.DirectoryPath, "src", "App", "ScopeProbe.App.csproj"),
                namespacePrefix: "ScopeProbe", depth: 1, maxResults: 2, resultCursor: namespaceCursor,
                maxResponseBytes: 16384, maxResponseTokens: 1024);
            AssertSuccessWithinBudget(page, 16384, 1024);
            using var document = System.Text.Json.JsonDocument.Parse(JsonBody(TextOf(page)));
            var root = document.RootElement;
            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var name = item.TryGetProperty("fullName", out var fullName) ? fullName.GetString() : item.GetProperty("name").GetString();
                pagedNamespaceItems.Add($"{item.GetProperty("kind").GetString()}:{name}");
            }
            namespaceCursor = root.TryGetProperty("resultCursor", out var cursorValue)
                && cursorValue.ValueKind == System.Text.Json.JsonValueKind.String ? cursorValue.GetString() : null;
            namespacePages++;
            Assert.InRange(namespacePages, 1, 10);
        } while (namespaceCursor is not null);
        Assert.Equal(3, namespacePages);
        Assert.Equal(6, pagedNamespaceItems.Count);
        Assert.Equal(6, pagedNamespaceItems.Distinct(StringComparer.Ordinal).Count());
        var namespaceBytes = await structure.BrowseTarget(target, "namespaces",
            project: Path.Combine(fixture.DirectoryPath, "src", "App", "ScopeProbe.App.csproj"),
            namespacePrefix: "ScopeProbe", depth: 1, includeTypes: false, maxResults: 1,
            maxResponseBytes: 512, maxResponseTokens: 4096);
        if (namespaceBytes.IsError == true)
        {
            AssertErrorWithinBudget(namespaceBytes, "RESPONSE_BUDGET_TOO_SMALL", 512, 4096);
            var minimumBytes = ReadBudget(TextOf(namespaceBytes), "minimumResponseBytes");
            var retry = await structure.BrowseTarget(target, "namespaces",
                project: Path.Combine(fixture.DirectoryPath, "src", "App", "ScopeProbe.App.csproj"),
                namespacePrefix: "ScopeProbe", depth: 1, includeTypes: false, maxResults: 1,
                maxResponseBytes: minimumBytes, maxResponseTokens: 4096);
            AssertSuccessWithinBudget(retry, minimumBytes, 4096);
        }
        else AssertSuccessWithinBudget(namespaceBytes, 512, 4096);
        var namespaceTokens = await structure.BrowseTarget(target, "namespaces",
            project: Path.Combine(fixture.DirectoryPath, "src", "App", "ScopeProbe.App.csproj"),
            namespacePrefix: "ScopeProbe", depth: 1, includeTypes: false, maxResults: 1,
            maxResponseBytes: 65536, maxResponseTokens: 512);
        if (namespaceTokens.IsError == true)
        {
            AssertErrorWithinBudget(namespaceTokens, "RESPONSE_BUDGET_TOO_SMALL", 65536, 512);
            var minimumTokens = ReadBudget(TextOf(namespaceTokens), "minimumResponseTokens");
            var retry = await structure.BrowseTarget(target, "namespaces",
                project: Path.Combine(fixture.DirectoryPath, "src", "App", "ScopeProbe.App.csproj"),
                namespacePrefix: "ScopeProbe", depth: 1, includeTypes: false, maxResults: 1,
                maxResponseBytes: 65536, maxResponseTokens: minimumTokens);
            AssertSuccessWithinBudget(retry, 65536, minimumTokens);
        }
        else AssertSuccessWithinBudget(namespaceTokens, 65536, 512);
        var generatedNamespaceExcluded = await structure.BrowseTarget(target, "namespaces", namespacePrefix: "ScopeProbe.GeneratedOnly",
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(generatedNamespaceExcluded, "INVALID_ARGUMENT", 16384, 1024);
        var generatedNamespaceIncluded = await structure.BrowseTarget(target, "namespaces", namespacePrefix: "ScopeProbe.GeneratedOnly",
            includeGenerated: true, maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(generatedNamespaceIncluded, 16384, 1024);
        Assert.Contains("GeneratedProbe", TextOf(generatedNamespaceIncluded), StringComparison.Ordinal);
        var invalidKind = await structure.BrowseTarget(target, "namespaces", kind: "unsupported", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(invalidKind, "INVALID_ARGUMENT", 16384, 1024);

        browseScans.Clear();
        var indexScope = await structure.BrowseTarget(target, "scope", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(indexScope, 16384, 1024);
        Assert.Contains("Roslyn documents", TextOf(indexScope), StringComparison.Ordinal);
        Assert.Equal(["scope"], browseScans);
        structure.BeforeBrowseScanForTesting = null;
        var assemblyAsSource = await structure.BrowseTarget(typeof(SourceToolsContractTests).Assembly.Location, "scope",
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(assemblyAsSource, "INVALID_ARGUMENT", 16384, 1024);

        var missingFindSelector = await symbols.FindSymbol(target, maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(missingFindSelector, "INVALID_ARGUMENT", 16384, 1024);
    }

    [Fact]
    public async Task TestCandidates_FreshCapturedProjectReferencesMapBoundMethodsToExactSourceDefinitions()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("ainet-test-candidate-binding-");
        var target = await CreateSourceSolutionAsync(fixture.DirectoryPath);
        var acquired = runtime.ProjectRegistry.Lease(target);
        Assert.True(acquired.Succeeded);
        using var lease = acquired.Lease!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var snapshot = await lease.ResidentSolution.GetCurrentSnapshotAsync(timeout.Token);
        Assert.True(snapshot.Succeeded);
        var solution = snapshot.Solution!;
        var app = solution.Projects.Single(project => project.Name == "ScopeProbe.App");
        var compilation = await app.GetCompilationAsync(timeout.Token);
        Assert.NotNull(compilation);
        var sourceMethod = Assert.Single(compilation.GetTypeByMetadataName("ScopeProbe.Target")!
            .GetMembers("Run").OfType<IMethodSymbol>());
        var tests = solution.Projects.Single(project => project.Name == "ScopeProbe.Tests");
        var document = tests.Documents.Single(item => item.Name == "TargetTests.cs");
        var model = await document.GetSemanticModelAsync(timeout.Token);
        var syntax = await document.GetSyntaxRootAsync(timeout.Token);
        Assert.NotNull(model);
        Assert.NotNull(syntax);
        var references = syntax.DescendantNodes().OfType<SimpleNameSyntax>()
            .Where(name => name.Identifier.ValueText == "Run").ToArray();
        Assert.Equal(2, references.Length);
        foreach (var reference in references)
        {
            var bound = Assert.IsAssignableFrom<IMethodSymbol>(model.GetSymbolInfo(reference, timeout.Token).Symbol);
            Assert.False(SymbolEqualityComparer.Default.Equals(sourceMethod, bound));
            var mapped = await SymbolFinder.FindSourceDefinitionAsync(bound, solution, timeout.Token);
            Assert.NotNull(mapped);
            Assert.True(SymbolEqualityComparer.Default.Equals(sourceMethod, mapped));
        }
        var recommendations = await TestRecommendationBuilder.BuildAsync(sourceMethod, solution, timeout.Token);
        Assert.Equal(new[] { "TargetTests", "OtherBehavior" }.Order(StringComparer.Ordinal),
            recommendations.TestFixtures.Select(item => item.ClassName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetContextSelectsSectionsSharesIdentityAndContinuesOnlyTheCursorSection()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-get-context-contract-");
        var target = await CreateSourceSolutionAsync(fixture.DirectoryPath);

        var invoked = new List<string>();
        relationships.BeforeContextSectionForTesting = invoked.Add;
        var bodyOnly = await relationships.GetContext(target, "ScopeProbe.Target.Run", ["body"],
            maxBodyLines: 2, maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(bodyOnly, 16384, 2048);
        using (var bodyDocument = JsonDocument.Parse(JsonBody(TextOf(bodyOnly))))
        {
            var root = bodyDocument.RootElement;
            Assert.Equal("partial", root.GetProperty("status").GetString());
            Assert.Equal("void Target.Run()", root.GetProperty("target").GetProperty("signature").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("target").GetProperty("snapshotId").GetString()));
            Assert.Equal(new[] { "body" }, root.GetProperty("sections").EnumerateArray()
                .Select(item => item.GetProperty("name").GetString()).ToArray());
            Assert.True(root.GetProperty("sections")[0].GetProperty("nextStartLine").GetInt32() > 0);
            Assert.False(root.GetProperty("sections")[0].GetProperty("items").TryGetProperty("handoffId", out _));
        }
        Assert.Equal(["body"], invoked);

        invoked.Clear();
        var callersAndTests = await relationships.GetContext(target, "ScopeProbe.Target.Run", ["uses", "tests"],
            usageScope: "production", maxResults: 10, maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(callersAndTests, 16384, 2048);
        using (var bothDocument = JsonDocument.Parse(JsonBody(TextOf(callersAndTests))))
        {
            var sections = bothDocument.RootElement.GetProperty("sections").EnumerateArray().ToArray();
            Assert.Equal(new[] { "uses", "tests" }, sections.Select(item => item.GetProperty("name").GetString()).ToArray());
            Assert.Contains("TargetTests", sections[1].GetProperty("items").ToString(), StringComparison.Ordinal);
            Assert.Contains("OtherBehavior", sections[1].GetProperty("items").ToString(), StringComparison.Ordinal);
        }
        Assert.Equal(["uses", "tests"], invoked);

        // Member scope and filters affect only that section, preserving the common declaration and all other sections.
        var fullContext = await relationships.GetContext(target, "ScopeProbe.Target", ["body", "members", "uses", "tests"],
            maxResults: 10, maxResponseBytes: 65536, maxResponseTokens: 16000);
        var testMembersContext = await relationships.GetContext(target, "ScopeProbe.Target", ["body", "members", "uses", "tests"],
            memberScope: "tests", maxResults: 10, maxResponseBytes: 65536, maxResponseTokens: 16000);
        AssertSuccessWithinBudget(fullContext, 65536, 16000);
        AssertSuccessWithinBudget(testMembersContext, 65536, 16000);
        using (var full = JsonDocument.Parse(JsonBody(TextOf(fullContext))))
        using (var scoped = JsonDocument.Parse(JsonBody(TextOf(testMembersContext))))
        {
            Assert.Equal(full.RootElement.GetProperty("target").GetRawText(), scoped.RootElement.GetProperty("target").GetRawText());
            var fullSections = full.RootElement.GetProperty("sections");
            var scopedSections = scoped.RootElement.GetProperty("sections");
            Assert.NotEmpty(fullSections[1].GetProperty("items").EnumerateArray());
            Assert.Empty(scopedSections[1].GetProperty("items").EnumerateArray());
            Assert.Equal("complete", scopedSections[1].GetProperty("status").GetString());
            foreach (var index in new[] { 0, 2, 3 })
                Assert.Equal(fullSections[index].GetRawText(), scopedSections[index].GetRawText());
            var member = fullSections[1].GetProperty("items")[0];
            foreach (var field in new[] { "visibility", "signature", "startLine", "endLine", "lineCount", "filePath", "kind" })
                Assert.True(member.TryGetProperty(field, out _));
            Assert.True(fullSections[1].GetProperty("structure").GetProperty("totalLines").GetInt32() > 0);
        }
        var emptyMembers = await relationships.GetContext(target, "ScopeProbe.Target", ["members"],
            memberNameFilter: "no-such-member", memberKindFilter: "Method", memberSortBy: "name", maxResponseBytes: 16384);
        AssertSuccessWithinBudget(emptyMembers, 16384, 4096);
        using (var empty = JsonDocument.Parse(JsonBody(TextOf(emptyMembers))))
        {
            var section = empty.RootElement.GetProperty("sections")[0];
            Assert.Equal("complete", section.GetProperty("status").GetString());
            Assert.Empty(section.GetProperty("items").EnumerateArray());
        }
        Assert.Contains("INVALID_ARGUMENT", TextOf(await relationships.GetContext(target, "ScopeProbe.Target.Run", ["members"])), StringComparison.Ordinal);

        var generatedMemberExcluded = await relationships.GetContext(target, "ScopeProbe.OrderProbe", ["members"],
            maxResponseBytes: 16384, maxResponseTokens: 2048);
        Assert.False(generatedMemberExcluded.IsError ?? false, TextOf(generatedMemberExcluded));
        Assert.DoesNotContain("GeneratedMember", TextOf(generatedMemberExcluded), StringComparison.Ordinal);
        var generatedMemberIncluded = await relationships.GetContext(target, "ScopeProbe.OrderProbe", ["members"],
            includeGenerated: true, maxResponseBytes: 16384, maxResponseTokens: 2048);
        Assert.False(generatedMemberIncluded.IsError ?? false, TextOf(generatedMemberIncluded));
        Assert.Contains("GeneratedMember", TextOf(generatedMemberIncluded), StringComparison.Ordinal);
        var generatedTargetExcluded = await relationships.GetContext(target, "ScopeProbe.GeneratedOnly.GeneratedProbe", ["body"],
            maxResponseBytes: 16384, maxResponseTokens: 2048);
        Assert.True(generatedTargetExcluded.IsError == true, TextOf(generatedTargetExcluded));
        Assert.Contains("INVALID_ARGUMENT", TextOf(generatedTargetExcluded), StringComparison.Ordinal);
        Assert.Contains("generated source", TextOf(generatedTargetExcluded), StringComparison.Ordinal);
        var generatedTargetIncluded = await relationships.GetContext(target, "ScopeProbe.GeneratedOnly.GeneratedProbe", ["body"],
            includeGenerated: true, maxResponseBytes: 16384, maxResponseTokens: 2048);
        Assert.False(generatedTargetIncluded.IsError ?? false, TextOf(generatedTargetIncluded));

        var callerFirst = await relationships.GetContext(target, "ScopeProbe.Target.Run", ["uses"],
            maxResults: 1, maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(callerFirst, 16384, 2048);
        using (var callerDocument = JsonDocument.Parse(JsonBody(TextOf(callerFirst))))
        {
            var root = callerDocument.RootElement;
            Assert.Equal("partial", root.GetProperty("status").GetString());
            var section = Assert.Single(root.GetProperty("sections").EnumerateArray());
            Assert.Equal("uses", section.GetProperty("name").GetString());
            Assert.Equal("partial", section.GetProperty("status").GetString());
            Assert.False(string.IsNullOrWhiteSpace(section.GetProperty("resultCursor").GetString()));
        }

        invoked.Clear();
        var first = await relationships.GetContext(target, "ScopeProbe.Paged", ["body", "members"],
            maxBodyLines: 2, maxResults: 1, maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(first, 16384, 2048);
        using var firstDocument = JsonDocument.Parse(JsonBody(TextOf(first)));
        var firstRoot = firstDocument.RootElement;
        var cursor = firstRoot.GetProperty("sections").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "members").GetProperty("resultCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));
        invoked.Clear();
        var continuation = await relationships.GetContext(target, "ScopeProbe.Paged", ["body", "members"],
            maxBodyLines: 2, maxResults: 1, resultCursor: cursor, maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(continuation, 16384, 2048);
        using (var continuationDocument = JsonDocument.Parse(JsonBody(TextOf(continuation))))
        {
            var root = continuationDocument.RootElement;
            Assert.Equal("members", root.GetProperty("continuationSection").GetString());
            Assert.Equal(new[] { "members" }, root.GetProperty("sections").EnumerateArray()
                .Select(item => item.GetProperty("name").GetString()).ToArray());
        }
        Assert.Equal(["members"], invoked);
        var wrongPageSize = await relationships.GetContext(target, "ScopeProbe.Paged", ["body", "members"],
            maxBodyLines: 2, maxResults: 2, resultCursor: cursor, maxResponseBytes: 16384, maxResponseTokens: 2048);
        Assert.Contains("RESULT_CURSOR_ARGUMENT_MISMATCH", TextOf(wrongPageSize), StringComparison.Ordinal);

        var invalidAssemblyTests = await relationships.GetContext(Path.Combine(fixture.DirectoryPath, "missing.dll"),
            "Type.Member", ["tests"], maxResponseBytes: 16384);
        Assert.Contains("INVALID_ARGUMENT", TextOf(invalidAssemblyTests), StringComparison.Ordinal);
        var invalidMemberTarget = await relationships.GetContext(target, "ScopeProbe.Target.Run", ["members"], maxResponseBytes: 16384);
        Assert.Contains("INVALID_ARGUMENT", TextOf(invalidMemberTarget), StringComparison.Ordinal);
        var ineffective = await relationships.GetContext(target, "ScopeProbe.Target.Run", ["tests"], usageScope: "production", maxResponseBytes: 16384);
        Assert.Contains("INVALID_ARGUMENT", TextOf(ineffective), StringComparison.Ordinal);

        relationships.BeforeContextSectionForTesting = section =>
        {
            if (section == "tests") throw new InvalidOperationException("forced section test failure");
        };
        var partial = await relationships.GetContext(target, "ScopeProbe.Target.Run", ["body", "tests", "uses"],
            maxResponseBytes: 16384, maxResponseTokens: 2048);
        Assert.True(partial.IsError == true, TextOf(partial));
        using var partialDocument = JsonDocument.Parse(JsonBody(TextOf(partial)));
        var partialRoot = partialDocument.RootElement;
        Assert.Equal("error", partialRoot.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(partialRoot.GetProperty("target").GetProperty("snapshotId").GetString()));
        var partialSections = partialRoot.GetProperty("sections").EnumerateArray().ToArray();
        Assert.Equal("partial", partialSections[0].GetProperty("status").GetString());
        Assert.Equal("error", partialSections[1].GetProperty("status").GetString());
        Assert.Equal("CONTEXT_SECTION_FAILED", partialSections[1].GetProperty("error").GetProperty("code").GetString());
        Assert.False(partialSections[1].GetProperty("analysisComplete").GetBoolean());
        Assert.Equal("uses", partialSections[2].GetProperty("name").GetString());
        Assert.Equal("notAnalyzed", partialSections[2].GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetContextTestsPagesAllCandidatesAndKeepsExpansionLimitPartialOnFinalPage()
    {
        using var fixture = TestTempDirectory.Create("context-limit-");
        using var otherFixture = TestTempDirectory.Create("context-other-");
        var (target, targetProjects) = CreateTestCandidateProjects(fixture.DirectoryPath,
            TestRecommendationBuilder.MaxCandidateFixtures + 3);
        var (otherTarget, otherProjects) = CreateTestCandidateProjects(otherFixture.DirectoryPath, 1);
        await using var testHost = InMemorySourceTestHost.CreateForSolutions(
        [
            (target, targetProjects),
            (otherTarget, otherProjects),
        ]);
        var relationships = new RelationshipTools(testHost.Runtime);

        var names = new List<string>();
        string? cursor = null;
        string? firstCursor = null;
        var pages = 0;
        do
        {
            var page = await relationships.GetContext(target, "ScopeProbe.Target.Run", ["tests"], maxResults: 100,
                resultCursor: cursor, maxResponseBytes: 65536, maxResponseTokens: 8192);
            AssertSuccessWithinBudget(page, 65536, 8192);
            var pageText = await ReconstructOuterPagesAsync(page, async continuation =>
                await relationships.GetContext(target, "ScopeProbe.Target.Run", ["tests"], maxResults: 100,
                    continuationToken: continuation, maxResponseBytes: 65536, maxResponseTokens: 8192));
            using var document = JsonDocument.Parse(JsonBody(pageText));
            var root = document.RootElement;
            var section = Assert.Single(root.GetProperty("sections").EnumerateArray());
            Assert.Equal("tests", section.GetProperty("name").GetString());
            Assert.Equal(257, section.GetProperty("totalCount").GetInt32());
            var analysis = section.GetProperty("analysis");
            Assert.Equal("static-test-candidates-only", analysis.GetProperty("evidenceMode").GetString());
            Assert.True(analysis.GetProperty("candidateExpansionLimitReached").GetBoolean());
            Assert.InRange(section.GetProperty("items").GetArrayLength(), 1, 100);
            Assert.False(section.GetProperty("analysisComplete").GetBoolean());
            Assert.Contains("narrower symbol", section.GetProperty("nextAction").GetString(), StringComparison.Ordinal);
            names.AddRange(section.GetProperty("items").EnumerateArray().Select(item =>
                $"{item.GetProperty("projectIdentity").GetString()}|{item.GetProperty("className").GetString()}|{item.GetProperty("filePath").GetString()}"));
            cursor = section.TryGetProperty("resultCursor", out var cursorValue)
                && cursorValue.ValueKind == JsonValueKind.String ? cursorValue.GetString() : null;
            firstCursor ??= cursor;
            pages++;
            Assert.InRange(pages, 1, 5);
            Assert.Equal("partial", section.GetProperty("status").GetString());
        } while (cursor is not null);

        var expected = Enumerable.Range(0, 257).Select(index =>
            $"{Path.GetFullPath(Path.Combine(fixture.DirectoryPath, "tests", "ScopeProbe.Tests", "ScopeProbe.Tests.csproj")).Replace('\\', '/')}::ScopeProbe.Tests|DirectTest{index:D3}|tests/ScopeProbe.Tests/TargetTests.cs").ToArray();
        Assert.Equal(3, pages);
        Assert.Equal(expected, names);
        Assert.Equal(expected.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.NotNull(firstCursor);

        var replayA = await relationships.GetContext(target, "ScopeProbe.Target.Run", ["tests"], maxResults: 100,
            resultCursor: firstCursor, maxResponseBytes: 65536, maxResponseTokens: 8192);
        var replayB = await relationships.GetContext(target, "ScopeProbe.Target.Run", ["tests"], maxResults: 100,
            resultCursor: firstCursor, maxResponseBytes: 65536, maxResponseTokens: 8192);
        var replayAText = await ReconstructOuterPagesAsync(replayA, async continuation =>
            await relationships.GetContext(target, "ScopeProbe.Target.Run", ["tests"], maxResults: 100,
                continuationToken: continuation, maxResponseBytes: 65536, maxResponseTokens: 8192));
        var replayBText = await ReconstructOuterPagesAsync(replayB, async continuation =>
            await relationships.GetContext(target, "ScopeProbe.Target.Run", ["tests"], maxResults: 100,
                continuationToken: continuation, maxResponseBytes: 65536, maxResponseTokens: 8192));
        using var replayADocument = JsonDocument.Parse(JsonBody(replayAText));
        using var replayBDocument = JsonDocument.Parse(JsonBody(replayBText));
        var replayASection = replayADocument.RootElement.GetProperty("sections")[0];
        var replayBSection = replayBDocument.RootElement.GetProperty("sections")[0];
        Assert.Equal(replayASection.GetProperty("items").ToString(), replayBSection.GetProperty("items").ToString());
        Assert.Equal(replayASection.GetProperty("resultCursor").GetString(), replayBSection.GetProperty("resultCursor").GetString());
        Assert.Contains("RESULT_CURSOR_ARGUMENT_MISMATCH", TextOf(await relationships.GetContext(target,
            "ScopeProbe.Target.Run", ["tests"], maxResults: 50, resultCursor: firstCursor,
            maxResponseBytes: 65536, maxResponseTokens: 8192)), StringComparison.Ordinal);
        Assert.Contains("RESULT_CURSOR_ARGUMENT_MISMATCH", TextOf(await relationships.GetContext(target,
            "ScopeProbe.Target.LargeBody", ["tests"], maxResults: 100, resultCursor: firstCursor,
            maxResponseBytes: 65536, maxResponseTokens: 8192)), StringComparison.Ordinal);
        Assert.Contains("RESULT_CURSOR_ARGUMENT_MISMATCH", TextOf(await relationships.GetContext(target,
            "ScopeProbe.Target.Run", ["tests"], includeGenerated: true, maxResults: 100, resultCursor: firstCursor,
            maxResponseBytes: 65536, maxResponseTokens: 8192)), StringComparison.Ordinal);

        Assert.Contains("RESULT_CURSOR_ARGUMENT_MISMATCH", TextOf(await relationships.GetContext(otherTarget,
            "ScopeProbe.Target.Run", ["tests"], maxResults: 100, resultCursor: firstCursor,
            maxResponseBytes: 65536, maxResponseTokens: 8192)), StringComparison.Ordinal);

        var requestedBytes = 512;
        var requestedTokens = 4096;
        var offeredMinimumBytes = 0;
        var offeredMinimumTokens = 0;
        var tiny = await GetTinyBudgetPageAsync(null);
        var tinyText = new StringBuilder();
        var tinyPagesCompleted = false;
        var tinyPageCount = 0;
        for (var outerPage = 0; outerPage < 100; outerPage++)
        {
            AssertSuccessWithinBudget(tiny, requestedBytes, requestedTokens);
            tinyPageCount++;
            tinyText.Append(BodyOf(TextOf(tiny)));
            if (!TryReadToken(TextOf(tiny), "continuationToken", out var outerCursor))
            {
                tinyPagesCompleted = true;
                break;
            }
            tiny = await GetTinyBudgetPageAsync(outerCursor);
            Assert.InRange(outerPage, 0, 99);
        }
        Assert.True(tinyPagesCompleted, "The tiny-budget response did not finish within 100 outer pages.");
        Assert.InRange(tinyPageCount, 1, 100);
        Assert.True(offeredMinimumBytes >= 512);
        Assert.True(offeredMinimumTokens > 0);
        using (var tinyDocument = JsonDocument.Parse(JsonBody(tinyText.ToString())))
        {
            var tinySection = Assert.Single(tinyDocument.RootElement.GetProperty("sections").EnumerateArray());
            Assert.Equal("tests", tinySection.GetProperty("name").GetString());
            Assert.Equal(1, tinySection.GetProperty("items").GetArrayLength());
        }

        async Task<CallToolResult> GetTinyBudgetPageAsync(string? outerCursor)
        {
            while (true)
            {
                var result = await relationships.GetContext(target, "ScopeProbe.Target.Run", ["tests"], maxResults: 1,
                    continuationToken: outerCursor, maxResponseBytes: requestedBytes, maxResponseTokens: requestedTokens);
                if (result.IsError != true || !TextOf(result).Contains("RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
                    return result;
                AssertErrorWithinBudget(result, "RESPONSE_BUDGET_TOO_SMALL", requestedBytes, requestedTokens);
                requestedBytes = ReadBudget(TextOf(result), "minimumResponseBytes");
                requestedTokens = ReadBudget(TextOf(result), "minimumResponseTokens");
                offeredMinimumBytes = requestedBytes;
                offeredMinimumTokens = requestedTokens;
            }
        }

        await File.AppendAllTextAsync(Path.Combine(fixture.DirectoryPath, "src", "App", "Target.cs"), "\n// snapshot change");
        Assert.Contains("STALE_SNAPSHOT", TextOf(await relationships.GetContext(target, "ScopeProbe.Target.Run", ["tests"], maxResults: 100,
            resultCursor: firstCursor, maxResponseBytes: 65536, maxResponseTokens: 8192)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetContextSectionsShareOneSourceSnapshotAndOldSectionCursorBecomesStaleAfterChange()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-get-context-shared-snapshot-");
        var target = await CreateSourceSolutionAsync(fixture.DirectoryPath);
        var sourcePath = Path.Combine(fixture.DirectoryPath, "src", "App", "Target.cs");
        var namesBefore = Enumerable.Range(0, 4).Select(index => $"SnapshotMember{index}").ToArray();
        var namesAfter = Enumerable.Range(0, 4).Select(index => $"ChangedMember{index}").ToArray();
        var initialSource = await File.ReadAllTextAsync(sourcePath);
        var changedSource = initialSource.Replace("public const int Version = 1;", "public const int Version = 2;", StringComparison.Ordinal)
            .Replace("var marker = 1;", "var marker = 2;", StringComparison.Ordinal)
            .Replace("SnapshotMember", "ChangedMember", StringComparison.Ordinal);
        initialSource = initialSource.Replace("    public void Run()", string.Join(Environment.NewLine, namesBefore.Select(name => $"    public void {name}() {{ }}")) + Environment.NewLine + "    public void Run()", StringComparison.Ordinal);
        changedSource = changedSource.Replace("    public void Run()", string.Join(Environment.NewLine, namesAfter.Select(name => $"    public void {name}() {{ }}")) + Environment.NewLine + "    public void Run()", StringComparison.Ordinal);
        await File.WriteAllTextAsync(sourcePath, initialSource);

        var changedBetweenSections = false;
        relationships.BeforeContextSectionForTesting = section =>
        {
            if (section == "members" && !changedBetweenSections)
            {
                changedBetweenSections = true;
                File.WriteAllText(sourcePath, changedSource);
            }
        };
        var first = await relationships.GetContext(target, "ScopeProbe.Target", ["body", "members"], maxResults: 100,
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(first, 32768, 4096);
        using var firstDocument = JsonDocument.Parse(JsonBody(TextOf(first)));
        var firstRoot = firstDocument.RootElement;
        var firstSnapshot = firstRoot.GetProperty("target").GetProperty("snapshotId").GetString();
        var firstMembers = Assert.Single(firstRoot.GetProperty("sections").EnumerateArray()
            .Where(section => section.GetProperty("name").GetString() == "members"));
        var firstBody = Assert.Single(firstRoot.GetProperty("sections").EnumerateArray()
            .Where(section => section.GetProperty("name").GetString() == "body"));
        Assert.Contains("marker = 1", firstBody.GetProperty("items").GetProperty("body").GetString(), StringComparison.Ordinal);
        Assert.Contains(namesBefore[0], firstMembers.GetProperty("items").ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(namesAfter[0], firstMembers.GetProperty("items").ToString(), StringComparison.Ordinal);

        relationships.BeforeContextSectionForTesting = null;
        var next = await relationships.GetContext(target, "ScopeProbe.Target", ["body", "members"], maxResults: 1,
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(next, 32768, 4096);
        using var nextDocument = JsonDocument.Parse(JsonBody(TextOf(next)));
        var nextRoot = nextDocument.RootElement;
        Assert.NotEqual(firstSnapshot, nextRoot.GetProperty("target").GetProperty("snapshotId").GetString());
        Assert.Contains("marker = 2", nextRoot.GetProperty("sections").ToString(), StringComparison.Ordinal);
        Assert.Contains(namesAfter[0], nextRoot.GetProperty("sections").ToString(), StringComparison.Ordinal);

        var cursorResult = await relationships.GetContext(target, "ScopeProbe.Target", ["body", "members"], maxResults: 1,
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        using var cursorDocument = JsonDocument.Parse(JsonBody(TextOf(cursorResult)));
        var staleCursor = cursorDocument.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("name").GetString() == "members").GetProperty("resultCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(staleCursor));
        await File.WriteAllTextAsync(sourcePath, changedSource.Replace("public const int Version = 2;", "public const int Version = 3;", StringComparison.Ordinal));
        AssertErrorWithinBudget(await relationships.GetContext(target, "ScopeProbe.Target", ["body", "members"], maxResults: 1,
            resultCursor: staleCursor, maxResponseBytes: 32768, maxResponseTokens: 4096), "STALE_SNAPSHOT", 32768, 4096);
    }

    [Fact]
    public async Task SourceChangeGetsANewSnapshotWhileStoredOuterPagesKeepTheOldSnapshot()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var symbols = new SymbolTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-source-immutable-pages-");
        var target = await CreateSourceSolutionAsync(fixture.DirectoryPath);
        var sourcePath = Path.Combine(fixture.DirectoryPath, "src", "App", "Target.cs");

        var first = await symbols.FindSymbol(target, pattern: "PageEntry", kind: "method", maxResults: 100,
            maxResponseBytes: 1024, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(first, 1024, 4096);
        var oldSnapshotId = ReadHeader(TextOf(first), "snapshotId");
        Assert.True(TryReadToken(TextOf(first), "continuationToken", out var outerToken));

        var source = await File.ReadAllTextAsync(sourcePath);
        var marker = "    public void PageEntry15() { }";
        Assert.Contains(marker, source, StringComparison.Ordinal);
        await File.WriteAllTextAsync(sourcePath, source.Replace(marker, marker + "\n    public void PageEntry16() { }", StringComparison.Ordinal));

        var oldPage = await symbols.FindSymbol(target, pattern: "PageEntry", kind: "method", maxResults: 100,
            continuationToken: outerToken, maxResponseBytes: 1024, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(oldPage, 1024, 4096);
        Assert.Equal(oldSnapshotId, ReadHeader(TextOf(oldPage), "snapshotId"));

        var latest = await symbols.FindSymbol(target, pattern: "PageEntry", kind: "method", maxResults: 100,
            maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(latest, 65536, 4096);
        Assert.NotEqual(oldSnapshotId, ReadHeader(TextOf(latest), "snapshotId"));
        Assert.Contains("PageEntry16", TextOf(latest), StringComparison.Ordinal);
    }

    private static async Task<(string Text, int Pages)> ReadAllFindSymbolPagesAsync(
        SymbolTools tools, string target, string pattern, int bytes, int tokens)
    {
        var text = new StringBuilder();
        var broadResult = await tools.FindSymbol(target, pattern: pattern, maxResults: 100,
            maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(broadResult, 65536, 4096);
        var expected = BodyOf(TextOf(broadResult));
        var result = await tools.FindSymbol(target, pattern: pattern, maxResults: 100, maxResponseBytes: bytes, maxResponseTokens: tokens);
        var pages = 0;
        for (var request = 0; request < 100; request++)
        {
            var page = TextOf(result);
            Assert.False(result.IsError ?? false, page);
            AssertBudget(page, bytes, tokens);
            text.Append(BodyOf(page));
            pages++;
            if (!TryReadToken(page, "continuationToken", out var continuation))
            {
                Assert.Equal(expected, text.ToString());
                return (text.ToString(), pages);
            }
            result = await tools.FindSymbol(target, pattern: pattern, maxResults: 100, maxResponseBytes: bytes,
                maxResponseTokens: tokens, continuationToken: continuation);
        }
        throw new Xunit.Sdk.XunitException("The find_symbol response did not reach its final outer page.");
    }

    private static async Task<(string Text, int Pages)> ReadAllSkeletonPagesAsync(
        StructureTools tools, string target, string filePath, int bytes, int tokens)
    {
        var text = new StringBuilder();
        var broadPages = await ReadOuterResponsePagesAsync(
            (responseBytes, responseTokens, continuation) => tools.GetFileSkeleton(target, [filePath],
                maxResponseBytes: responseBytes, maxResponseTokens: responseTokens, continuationToken: continuation),
            65536, 4096);
        var expected = broadPages.Text;
        var result = await tools.GetFileSkeleton(target, [filePath], maxResponseBytes: bytes, maxResponseTokens: tokens);
        var pages = 0;
        for (var request = 0; request < 100; request++)
        {
            var page = TextOf(result);
            Assert.False(result.IsError ?? false, page);
            AssertBudget(page, bytes, tokens);
            text.Append(BodyOf(page));
            pages++;
            if (!TryReadToken(page, "continuationToken", out var continuation))
            {
                Assert.Equal(expected, text.ToString());
                return (text.ToString(), pages);
            }
            result = await tools.GetFileSkeleton(target, [filePath], maxResponseBytes: bytes,
                maxResponseTokens: tokens, continuationToken: continuation);
        }
        throw new Xunit.Sdk.XunitException("The get_file_skeleton response did not reach its final outer page.");
    }

    private static async Task<(string Text, int Pages)> ReadAllBodyPagesAsync(
        SymbolTools tools, string target, string symbolIdentifier, int bytes, int tokens)
    {
        var text = new StringBuilder();
        var broadResult = await tools.GetSymbolBody(target, [symbolIdentifier], maxBodyLines: 80,
            maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(broadResult, 65536, 4096);
        var expected = BodyOf(TextOf(broadResult));
        var result = await tools.GetSymbolBody(target, [symbolIdentifier], maxBodyLines: 80,
            maxResponseBytes: bytes, maxResponseTokens: tokens);
        var pages = 0;
        for (var request = 0; request < 100; request++)
        {
            var page = TextOf(result);
            Assert.False(result.IsError ?? false, page);
            AssertBudget(page, bytes, tokens);
            text.Append(BodyOf(page));
            pages++;
            if (!TryReadToken(page, "continuationToken", out var continuation))
            {
                Assert.Equal(expected, text.ToString());
                return (text.ToString(), pages);
            }
            result = await tools.GetSymbolBody(target, [symbolIdentifier], maxBodyLines: 80,
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation);
        }
        throw new Xunit.Sdk.XunitException("The get_symbol_body response did not reach its final outer page.");
    }

    private static string ReadHandoff(string text, string declaration)
    {
        var body = BodyOf(text);
        if (body.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            using var document = JsonDocument.Parse(body);
            var parts = declaration.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var namePart = parts.Length > 2 && parts[^1] == "in" ? parts[1] : parts[^1];
            var name = namePart.Split('.')[^1];
            var handoff = document.RootElement.GetProperty("results").EnumerateArray()
                .SelectMany(result => result.GetProperty("entries").EnumerateArray())
                .FirstOrDefault(entry => string.Equals(entry.GetProperty("name").GetString(), name, StringComparison.Ordinal))
                .GetProperty("handoffId").GetString();
            Assert.StartsWith("src:", handoff);
            return handoff!;
        }
        var declarationIndex = text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(declarationIndex >= 0, text);
        var lineStart = text.LastIndexOf('\n', declarationIndex);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', declarationIndex);
        lineEnd = lineEnd < 0 ? text.Length : lineEnd;
        return Assert.Single(IntegrationMcpAssertions.ReadStableReferences(text[lineStart..lineEnd]));
    }

    private static string[] ReadHandoffs(string text)
    {
        var body = BodyOf(text);
        if (body.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("results").EnumerateArray()
                .SelectMany(result => result.GetProperty("entries").EnumerateArray())
                .Select(entry => entry.TryGetProperty("handoffId", out var value) ? value.GetString() : null)
                .Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value!).ToArray();
        }
        return IntegrationMcpAssertions.ReadStableReferences(text);
    }

    private static string ReadHandoffOnLineContaining(string text, string fragment)
    {
        var line = text.Split('\n').FirstOrDefault(value => value.Contains(fragment, StringComparison.Ordinal));
        Assert.True(line is not null, text);
        const string marker = "handoffId: `";
        var start = line!.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, line);
        start += marker.Length;
        var end = line.IndexOf('`', start);
        Assert.True(end > start, line);
        return line[start..end];
    }

    private static (string Target, string AppFile, InMemorySourceTestHost Host) CreateSourceToolsHost(string root)
    {
        var solutionPath = Path.Combine(root, "SourceTools.slnx");
        var appDirectory = Path.Combine(root, "src", "App");
        var testsDirectory = Path.Combine(root, "tests", "ScopeProbe.Tests");
        Directory.CreateDirectory(appDirectory);
        Directory.CreateDirectory(testsDirectory);
        var appFile = Path.Combine(appDirectory, "Target.cs");
        var generatedFile = Path.Combine(appDirectory, "Generated.g.cs");
        var recoveryFile = Path.Combine(appDirectory, "NamespaceRecovery.cs");
        var testsFile = Path.Combine(testsDirectory, "TargetTests.cs");

        var pageEntries = string.Join(Environment.NewLine, Enumerable.Range(0, 16)
            .Select(index => $"    public void PageEntry{index:D2}() {{ }}"));
        var bodyLines = string.Join(Environment.NewLine, Enumerable.Range(0, 20)
            .Select(index => $"        // body-line-{index:D2} 😀 {new string('x', 90)}"));
        var source = $$"""
            namespace ScopeProbe;
            public class Target
            {
                public const int Version = 1;
                public void Run()
                {
                    var marker = 1;
                    marker++;
                    _ = marker;
                }
                public void LargeBody()
                {
            {{bodyLines}}
                    // body-end
                }
            }
            public interface IUnavailableTarget { void Execute(); }
            public partial class OrderProbe
            {
                public void Zebra() { }
                public void Alpha() { }
            }
            public static class Fields { public static int First = 1, Second = 2; }
            public static class Paged
            {
            {{pageEntries}}
            }
            """;
        var generated = "namespace ScopeProbe.GeneratedOnly { public class GeneratedProbe { } } namespace ScopeProbe { public partial class OrderProbe { public void GeneratedMember() { } } }";
        var recovery = "namespace ScopeProbe.Recovery.Deep { public sealed class NestedType { } }";
        var testSource = """
            using System;
            namespace Xunit { public sealed class FactAttribute : Attribute { } }
            namespace ScopeProbe.Tests
            {
                public sealed class TargetTests
                {
                    [Xunit.Fact]
                    public void RunTest() => new Target().Run();
                }
                public sealed class OtherBehavior
                {
                    [Xunit.Fact]
                    public void UsesTarget() => new Target().Run();
                }
            }
            """;

        File.WriteAllText(solutionPath, "<Solution><Project Path=\"src/App/ScopeProbe.App.csproj\" /><Project Path=\"tests/ScopeProbe.Tests/ScopeProbe.Tests.csproj\" /></Solution>");
        File.WriteAllText(Path.Combine(appDirectory, "ScopeProbe.App.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(testsDirectory, "ScopeProbe.Tests.csproj"), "<Project />");
        File.WriteAllText(appFile, source);
        File.WriteAllText(generatedFile, generated);
        File.WriteAllText(recoveryFile, recovery);
        File.WriteAllText(testsFile, testSource);

        var host = InMemorySourceTestHost.Create(solutionPath,
        [
            new ProjectSpec("ScopeProbe.App",
                [(appFile, source), (generatedFile, generated), (recoveryFile, recovery)],
                VirtualProjectDirectory: "src/App"),
            new ProjectSpec("ScopeProbe.Tests", [(testsFile, testSource)], ProjectReferences: ["ScopeProbe.App"],
                VirtualProjectDirectory: "tests/ScopeProbe.Tests"),
        ]);
        return (solutionPath, appFile, host);
    }

    private static (string Target, IReadOnlyList<ProjectSpec> Projects) CreateTestCandidateProjects(string root, int testCount)
    {
        var solutionPath = Path.Combine(root, "SourceTools.slnx");
        var appDirectory = Path.Combine(root, "src", "App");
        var testsDirectory = Path.Combine(root, "tests", "ScopeProbe.Tests");
        Directory.CreateDirectory(appDirectory);
        Directory.CreateDirectory(testsDirectory);

        var targetFilePath = Path.Combine(appDirectory, "Target.cs");
        var testFilePath = Path.Combine(testsDirectory, "TargetTests.cs");
        var targetSource = "namespace ScopeProbe; public sealed class Target { public void Run() { } public void LargeBody() { } }";
        var methods = string.Join(Environment.NewLine, Enumerable.Range(0, testCount)
            .Select(index => $"public sealed class DirectTest{index:D3} {{ [Xunit.Fact] public void UsesTarget() => new ScopeProbe.Target().Run(); }}"));
        var testSource = $"using System; namespace Xunit {{ public sealed class FactAttribute : Attribute {{ }} }} namespace ScopeProbe.Tests {{ {methods} }}";

        File.WriteAllText(solutionPath, "<Solution><Project Path=\"src/App/ScopeProbe.App.csproj\" /><Project Path=\"tests/ScopeProbe.Tests/ScopeProbe.Tests.csproj\" /></Solution>");
        File.WriteAllText(Path.Combine(appDirectory, "ScopeProbe.App.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(testsDirectory, "ScopeProbe.Tests.csproj"), "<Project />");
        File.WriteAllText(targetFilePath, targetSource);
        File.WriteAllText(testFilePath, testSource);

        IReadOnlyList<ProjectSpec> projects =
        [
            new ProjectSpec("ScopeProbe.App", [(targetFilePath, targetSource)], VirtualProjectDirectory: "src/App"),
            new ProjectSpec("ScopeProbe.Tests", [(testFilePath, testSource)], ProjectReferences: ["ScopeProbe.App"],
                VirtualProjectDirectory: "tests/ScopeProbe.Tests"),
        ];
        return (solutionPath, projects);
    }

    private static async Task<string> CreateSourceSolutionAsync(string root)
    {
        var solutionPath = Path.Combine(root, "SourceTools.slnx");
        await File.WriteAllTextAsync(solutionPath,
            "<Solution><Project Path=\"src/App/ScopeProbe.App.csproj\" /><Project Path=\"tests/ScopeProbe.Tests/ScopeProbe.Tests.csproj\" /></Solution>");
        var appDirectory = Path.Combine(root, "src", "App");
        var testsDirectory = Path.Combine(root, "tests", "ScopeProbe.Tests");
        Directory.CreateDirectory(appDirectory);
        Directory.CreateDirectory(testsDirectory);
        await File.WriteAllTextAsync(Path.Combine(appDirectory, "ScopeProbe.App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        var pageEntries = string.Join("\n", Enumerable.Range(0, 16).Select(index => $"    public void PageEntry{index:D2}() {{ }}"));
        var bodyLines = string.Join("\n", Enumerable.Range(0, 20).Select(index => $"        // body-line-{index:D2} 😀 {new string('x', 90)}"));
        await File.WriteAllTextAsync(Path.Combine(appDirectory, "Target.cs"), $$"""
            namespace ScopeProbe;
            public class Target
            {
                public const int Version = 1;
                public void Run()
                {
                    var marker = 1;
                    marker++;
                    _ = marker;
                }
                public void LargeBody()
                {
            {{bodyLines}}
                    // body-end
                }
            }
            public partial class OrderProbe
            {
                public void Zebra() { }
                public void Alpha() { }
            }
            public static class Fields { public static int First = 1, Second = 2; }
            public static class Paged
            {
            {{pageEntries}}
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(appDirectory, "Generated.g.cs"),
            "namespace ScopeProbe.GeneratedOnly { public class GeneratedProbe { } } namespace ScopeProbe { public partial class OrderProbe { public void GeneratedMember() { } } }");
        await File.WriteAllTextAsync(Path.Combine(testsDirectory, "ScopeProbe.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include=\"../../src/App/ScopeProbe.App.csproj\" /></ItemGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(testsDirectory, "TargetTests.cs"), """
            using System;
            namespace Xunit { public sealed class FactAttribute : Attribute { } }
            namespace ScopeProbe.Tests
            {
                public sealed class TargetTests
                {
                    [Xunit.Fact]
                    public void RunTest() => new Target().Run();
                }
                public sealed class OtherBehavior
                {
                    [Xunit.Fact]
                    public void UsesTarget() => new Target().Run();
                }
            }
            """);

        var nugetConfigPath = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(nugetConfigPath, "<configuration><packageSources><clear /></packageSources></configuration>");
        await FixtureRestore.RunAsync(solutionPath, root, nugetConfigPath, "Source-tool fixture restore");
        return solutionPath;
    }

    private static async Task<(List<string> Items, int Pages, string FirstCursor)> ReadClassMemberPagesAsync(RelationshipTools tools, string target, string typeName)
    {
        var items = new List<string>();
        string? cursor = null;
        string? firstCursor = null;
        const int pageSize = 100;
        var pages = 0;
        do
        {
            var bytes = cursor is null ? 65536 : 16384;
            var tokens = cursor is null ? 8192 : 2048;
            var result = await tools.GetContext(target, typeName, ["members"], maxResults: pageSize, resultCursor: cursor,
                maxResponseBytes: bytes, maxResponseTokens: tokens);
            var payload = await ReconstructOuterPagesAsync(result, async continuation =>
                await tools.GetContext(target, typeName, ["members"], maxResults: pageSize, continuationToken: continuation,
                    maxResponseBytes: bytes, maxResponseTokens: tokens));
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement.GetProperty("sections")[0];
            Assert.Equal(205, root.GetProperty("totalCount").GetInt32());
            items.AddRange(root.GetProperty("items").EnumerateArray().Select(member => member.GetProperty("name").GetString()!));
            cursor = root.TryGetProperty("resultCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            firstCursor ??= cursor;
            pages++;
            Assert.InRange(pages, 1, 4);
        } while (cursor is not null);
        return (items, pages, firstCursor ?? throw new Xunit.Sdk.XunitException("The member inventory did not expose a continuation cursor."));
    }

    private static async Task<(List<string> Items, int Pages, string FirstCursor)> ReadNamespaceItemPagesAsync(StructureTools tools, string target)
    {
        var items = new List<string>();
        string? cursor = null;
        string? firstCursor = null;
        const int pageSize = 200;
        var pages = 0;
        do
        {
            var bytes = cursor is null ? 65536 : 16384;
            var tokens = cursor is null ? 8192 : 2048;
            var result = await tools.BrowseTarget(target, "namespaces", namespacePrefix: "CapProbe", maxResults: pageSize, resultCursor: cursor,
                maxResponseBytes: bytes, maxResponseTokens: tokens);
            var payload = await ReconstructOuterPagesAsync(result, async continuation =>
                await tools.BrowseTarget(target, "namespaces", namespacePrefix: "CapProbe", maxResults: pageSize, continuationToken: continuation,
                    maxResponseBytes: bytes, maxResponseTokens: tokens));
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            Assert.Equal(206, root.GetProperty("totalTypes").GetInt32());
            Assert.Equal(207, root.GetProperty("totalItems").GetInt32());
            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var name = item.TryGetProperty("fullName", out var fullName) ? fullName.GetString() : item.GetProperty("name").GetString();
                items.Add($"{item.GetProperty("kind").GetString()}:{name}");
            }
            cursor = root.TryGetProperty("resultCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            firstCursor ??= cursor;
            pages++;
            Assert.InRange(pages, 1, 4);
        } while (cursor is not null);
        return (items, pages, firstCursor ?? throw new Xunit.Sdk.XunitException("The namespace inventory did not expose a continuation cursor."));
    }

    private static async Task<string> ReconstructOuterPagesAsync(CallToolResult first,
        Func<string, Task<CallToolResult>> continuePage)
    {
        var output = new StringBuilder();
        var result = first;
        for (var index = 0; index < 100; index++)
        {
            var text = TextOf(result);
            AssertSuccessWithinBudget(result, 65536, 8192);
            output.Append(BodyOf(text));
            if (!TryReadToken(text, "continuationToken", out var continuation)) return output.ToString();
            result = await continuePage(continuation);
        }
        throw new Xunit.Sdk.XunitException("The outer response did not finish within 100 pages.");
    }

    private static string JsonBody(string text)
    {
        var start = text.IndexOf('{');
        Assert.True(start >= 0, text);
        return text[start..];
    }

    private static (int Start, int End, int Total) ReadBodyRange(string text)
    {
        var line = text.Split('\n').First(value => value.StartsWith("Lines: ", StringComparison.Ordinal));
        var parts = line[7..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var range = parts[0].Split('-');
        return (int.Parse(range[0]), int.Parse(range[1]), int.Parse(parts[2].TrimEnd(',')));
    }
}
