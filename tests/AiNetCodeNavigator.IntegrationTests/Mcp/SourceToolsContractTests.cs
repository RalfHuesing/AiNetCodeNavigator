using System.Text;
using System.Text.Json;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Tools;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class SourceToolsContractTests
{
    [Fact]
    public async Task StructureInventoriesReachEntriesBeyondThePreviousTwoHundredEntryCaps()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var fixture = TestTempDirectory.Create("ainet-structure-over-cap-");
        var solutionPath = fixture.CreateFile("Workspace.slnx", string.Empty);
        var sourcePath = fixture.CreateFile("ManyTypes.cs", string.Empty);
        var members = string.Join(Environment.NewLine, Enumerable.Range(0, 205).Select(index => $"    public void Member{index:D3}() {{ }}"));
        var types = string.Join(Environment.NewLine, Enumerable.Range(0, 205).Select(index => $"public sealed class Type{index:D3} {{ }}"));
        var source = $"namespace CapProbe;{Environment.NewLine}public sealed class ManyMembers{Environment.NewLine}{{{Environment.NewLine}{members}{Environment.NewLine}}}{Environment.NewLine}{types}";
        await File.WriteAllTextAsync(sourcePath, source);
        var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject("CapProbe", (sourcePath, source)).Build();
        await using var registry = new ProjectRegistry(new ProjectRegistryOptions(_ => ResidentSolutionCreation.Resident(new ResidentSolution(workspace.Solution)), TimeProvider.System));
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>(), projectRegistry: registry);
        var tools = new StructureTools(runtime);
        using var emitted = TestTempDirectory.Create("ainet-structure-over-cap-assembly-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(emitted, "CapProbe", source);

        var sourceMembers = await ReadClassMemberPagesAsync(tools, solutionPath, "CapProbe.ManyMembers");
        var assemblyMembers = await ReadClassMemberPagesAsync(tools, assemblyPath, "CapProbe.ManyMembers");
        Assert.Equal(2, sourceMembers.Pages);
        Assert.Equal(2, assemblyMembers.Pages);
        Assert.Equal(205, sourceMembers.Items.Count);
        Assert.Equal(205, assemblyMembers.Items.Count);
        Assert.Equal(205, sourceMembers.Items.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(205, assemblyMembers.Items.Distinct(StringComparer.Ordinal).Count());
        foreach (var (target, cursor) in new[] { (solutionPath, sourceMembers.FirstCursor), (assemblyPath, assemblyMembers.FirstCursor) })
        {
            AssertErrorWithinBudget(await tools.GetClassStructure(target, "CapProbe.ManyMembers", resultCursor: "malformed-cursor"), "RESULT_CURSOR_EXPIRED", 16384, 4096);
            AssertErrorWithinBudget(await tools.GetClassStructure(target, "CapProbe.ManyMembers", sortBy: "name", resultCursor: cursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);
        }

        var sourceInventory = await ReadNamespaceItemPagesAsync(tools, solutionPath);
        var assemblyInventory = await ReadNamespaceItemPagesAsync(tools, assemblyPath);
        Assert.Equal(2, sourceInventory.Pages);
        Assert.Equal(2, assemblyInventory.Pages);
        Assert.Equal(207, sourceInventory.Items.Count);
        Assert.Equal(207, assemblyInventory.Items.Count);
        Assert.Equal(207, sourceInventory.Items.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(207, assemblyInventory.Items.Distinct(StringComparer.Ordinal).Count());
        foreach (var (target, cursor) in new[] { (solutionPath, sourceInventory.FirstCursor), (assemblyPath, assemblyInventory.FirstCursor) })
        {
            AssertErrorWithinBudget(await tools.GetNamespaceTree(target, namespacePrefix: "CapProbe", resultCursor: "malformed-cursor"), "RESULT_CURSOR_EXPIRED", 16384, 4096);
            AssertErrorWithinBudget(await tools.GetNamespaceTree(target, namespacePrefix: "OtherNamespace", resultCursor: cursor), "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 4096);
        }
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
            Assert.Contains("\"name\": \"BeforeVersion\"", TextOf(result), StringComparison.Ordinal);
            Assert.DoesNotContain("\"name\": \"AfterVersion\"", TextOf(result), StringComparison.Ordinal);
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
    }

    [Fact]
    public async Task DisposedRuntimeHandoffsAreUnknownToFreshRuntimeConsumers()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var fixture = TestTempDirectory.Create("ainet-source-runtime-lifecycle-");
        var target = await CreateSourceSolutionAsync(fixture.DirectoryPath);
        var oldRuntime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var producer = new SymbolTools(oldRuntime);
        var found = await producer.FindSymbol(target, pattern: "Run", kind: "method", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(found, 16384, 1024);
        var oldHandoff = ReadHandoff(TextOf(found), "method Run in");

        await oldRuntime.DisposeAsync();

        await using var freshRuntime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var consumer = new SymbolTools(freshRuntime);
        var body = await consumer.GetSymbolBody(target, [oldHandoff], maxResponseBytes: 16384, maxResponseTokens: 1024);

        AssertErrorWithinBudget(body, "HANDOFF_UNKNOWN", 16384, 1024);

        var freshResult = await consumer.FindSymbol(target, pattern: "Run", kind: "method", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(freshResult, 16384, 1024);
        var freshHandoff = ReadHandoff(TextOf(freshResult), "method Run in");
        await oldRuntime.DisposeAsync();
        var freshBody = await consumer.GetSymbolBody(target, [freshHandoff], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(freshBody, 16384, 1024);
        Assert.Contains("Run", TextOf(freshBody), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceHandlersReturnNavigableResultsAndTypedDomainErrorsWithoutTransport()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var symbols = new SymbolTools(runtime);
        var structure = new StructureTools(runtime);
        var relationships = new RelationshipTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-source-tools-contract-");
        var target = await CreateSourceSolutionAsync(fixture.DirectoryPath);
        var appFile = Path.Combine(fixture.DirectoryPath, "src", "App", "Target.cs");

        var found = await symbols.FindSymbol(target, pattern: "Run", kind: "method", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(found, 16384, 1024);
        Assert.Contains("\"name\": \"Run\"", TextOf(found), StringComparison.Ordinal);
        Assert.Contains("snapshotId=source:", TextOf(found), StringComparison.Ordinal);
        Assert.Contains("analyzedScope=findSymbol(pattern=Run", TextOf(found), StringComparison.Ordinal);
        Assert.Contains("analysisCompleteness=complete", TextOf(found), StringComparison.Ordinal);
        Assert.Contains("resultContinuation=none", TextOf(found), StringComparison.Ordinal);
        var methodHandoff = ReadHandoff(TextOf(found), "method Run in");
        var generatedExcluded = await symbols.FindSymbol(target, pattern: "GeneratedProbe", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(generatedExcluded, 16384, 1024);
        Assert.DoesNotContain("\"name\": \"GeneratedProbe\"", TextOf(generatedExcluded), StringComparison.Ordinal);
        var generatedIncluded = await symbols.FindSymbol(target, pattern: "GeneratedProbe", includeGenerated: true,
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(generatedIncluded, 16384, 1024);
        Assert.Contains("\"name\": \"GeneratedProbe\"", TextOf(generatedIncluded), StringComparison.Ordinal);

        var pagedFind = await ReadAllFindSymbolPagesAsync(symbols, target, "PageEntry", 1024, 4096);
        Assert.True(pagedFind.Pages > 1);
        Assert.Equal(16, pagedFind.Text.Split("\"name\": \"PageEntry", StringSplitOptions.None).Length - 1);
        Assert.Contains("PageEntry15", pagedFind.Text, StringComparison.Ordinal);
        var tokenPagedFind = await ReadAllFindSymbolPagesAsync(symbols, target, "PageEntry", 65536, 512);
        Assert.Equal(pagedFind.Text, tokenPagedFind.Text);

        var body = await symbols.GetSymbolBody(target, [methodHandoff], maxBodyLines: 2, maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(body, 16384, 1024);
        Assert.Contains("Lines: 1-2 of", TextOf(body), StringComparison.Ordinal);
        Assert.Contains("startLine to 3", TextOf(body), StringComparison.Ordinal);
        var bodyRemainder = await symbols.GetSymbolBody(target, [methodHandoff], startLine: 3, maxBodyLines: 2,
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(bodyRemainder, 16384, 1024);
        Assert.Contains("Lines: 3-", TextOf(bodyRemainder), StringComparison.Ordinal);

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

        var mixedBody = await symbols.GetSymbolBody(target, [methodHandoff, "h:zzzz"], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(mixedBody, 16384, 1024);
        Assert.Contains("completeness=truncated", TextOf(mixedBody), StringComparison.Ordinal);
        Assert.Contains("HANDOFF_UNKNOWN", TextOf(mixedBody), StringComparison.Ordinal);
        Assert.Contains("Run", TextOf(mixedBody), StringComparison.Ordinal);
        var allInvalidBody = await symbols.GetSymbolBody(target, ["h:zzzz"], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(allInvalidBody, "HANDOFF_UNKNOWN", 16384, 1024);

        var skeleton = await structure.GetFileSkeleton(target, [appFile], maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(skeleton, 16384, 1024);
        var skeletonText = TextOf(skeleton);
        Assert.Contains("Target", skeletonText, StringComparison.Ordinal);
        Assert.Contains("First", skeletonText, StringComparison.Ordinal);
        Assert.Contains("Second", skeletonText, StringComparison.Ordinal);
        Assert.NotEqual(ReadHandoffOnLineContaining(skeletonText, "int First"), ReadHandoffOnLineContaining(skeletonText, "int Second"));
        var skeletonHandoffs = ReadHandoffs(skeletonText);
        Assert.True(skeletonHandoffs.Length >= 2, skeletonText);
        Assert.Equal(skeletonHandoffs.Length, skeletonHandoffs.Distinct(StringComparer.Ordinal).Count());
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

        var classStructure = await structure.GetClassStructure(target, "ScopeProbe.Target", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(classStructure, 16384, 1024);
        Assert.Contains("Run", TextOf(classStructure), StringComparison.Ordinal);
        var unknownType = await structure.GetClassStructure(target, "h:zzzz", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(unknownType, "HANDOFF_UNKNOWN", 16384, 1024);
        var declarationOrder = await structure.GetClassStructure(target, "ScopeProbe.OrderProbe", maxMembers: 1,
            sortBy: "lines", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(declarationOrder, 16384, 1024);
        Assert.Contains("Zebra", TextOf(declarationOrder), StringComparison.Ordinal);
        Assert.DoesNotContain("Alpha", TextOf(declarationOrder), StringComparison.Ordinal);
        var classStructureBytes = await structure.GetClassStructure(target, "ScopeProbe.OrderProbe", maxMembers: 1,
            sortBy: "lines", maxResponseBytes: 512, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(classStructureBytes, 512, 4096);
        Assert.True(TryReadToken(TextOf(classStructureBytes), "continuationToken", out _), TextOf(classStructureBytes));
        var classStructureTokens = await structure.GetClassStructure(target, "ScopeProbe.OrderProbe", maxMembers: 1,
            sortBy: "lines", maxResponseBytes: 65536, maxResponseTokens: 512);
        if (classStructureTokens.IsError == true)
        {
            AssertErrorWithinBudget(classStructureTokens, "RESPONSE_BUDGET_TOO_SMALL", 65536, 512);
            var minimumTokens = ReadBudget(TextOf(classStructureTokens), "minimumResponseTokens");
            var retry = await structure.GetClassStructure(target, "ScopeProbe.OrderProbe", maxMembers: 1,
                sortBy: "lines", maxResponseBytes: 65536, maxResponseTokens: minimumTokens);
            AssertSuccessWithinBudget(retry, 65536, minimumTokens);
        }
        else AssertSuccessWithinBudget(classStructureTokens, 65536, 512);
        var generatedMember = await structure.GetClassStructure(target, "ScopeProbe.OrderProbe", includeGenerated: true,
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(generatedMember, 16384, 1024);
        Assert.Contains("GeneratedMember", TextOf(generatedMember), StringComparison.Ordinal);

        var namespaceTree = await structure.GetNamespaceTree(target, project: Path.Combine(fixture.DirectoryPath, "src", "App", "ScopeProbe.App.csproj"), namespacePrefix: "ScopeProbe",
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(namespaceTree, 16384, 1024);
        using (var namespaceDocument = System.Text.Json.JsonDocument.Parse(JsonBody(TextOf(namespaceTree))))
        {
            var items = namespaceDocument.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Contains(items, item => item.GetProperty("kind").GetString() == "namespace"
                && item.GetProperty("fullName").GetString() == "ScopeProbe");
            Assert.Contains(items, item => item.GetProperty("kind").GetString() == "type"
                && item.GetProperty("name").GetString() == "Target");
        }
        var pagedNamespaceItems = new List<string>();
        string? namespaceCursor = null;
        var namespacePages = 0;
        do
        {
            var page = await structure.GetNamespaceTree(target, project: Path.Combine(fixture.DirectoryPath, "src", "App", "ScopeProbe.App.csproj"),
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
        Assert.Equal(5, pagedNamespaceItems.Count);
        Assert.Equal(5, pagedNamespaceItems.Distinct(StringComparer.Ordinal).Count());
        var namespaceBytes = await structure.GetNamespaceTree(target,
            project: Path.Combine(fixture.DirectoryPath, "src", "App", "ScopeProbe.App.csproj"),
            namespacePrefix: "ScopeProbe", depth: 1, includeTypes: false, maxResults: 1,
            maxResponseBytes: 512, maxResponseTokens: 4096);
        if (namespaceBytes.IsError == true)
        {
            AssertErrorWithinBudget(namespaceBytes, "RESPONSE_BUDGET_TOO_SMALL", 512, 4096);
            var minimumBytes = ReadBudget(TextOf(namespaceBytes), "minimumResponseBytes");
            var retry = await structure.GetNamespaceTree(target,
                project: Path.Combine(fixture.DirectoryPath, "src", "App", "ScopeProbe.App.csproj"),
                namespacePrefix: "ScopeProbe", depth: 1, includeTypes: false, maxResults: 1,
                maxResponseBytes: minimumBytes, maxResponseTokens: 4096);
            AssertSuccessWithinBudget(retry, minimumBytes, 4096);
        }
        else AssertSuccessWithinBudget(namespaceBytes, 512, 4096);
        var namespaceTokens = await structure.GetNamespaceTree(target,
            project: Path.Combine(fixture.DirectoryPath, "src", "App", "ScopeProbe.App.csproj"),
            namespacePrefix: "ScopeProbe", depth: 1, includeTypes: false, maxResults: 1,
            maxResponseBytes: 65536, maxResponseTokens: 512);
        if (namespaceTokens.IsError == true)
        {
            AssertErrorWithinBudget(namespaceTokens, "RESPONSE_BUDGET_TOO_SMALL", 65536, 512);
            var minimumTokens = ReadBudget(TextOf(namespaceTokens), "minimumResponseTokens");
            var retry = await structure.GetNamespaceTree(target,
                project: Path.Combine(fixture.DirectoryPath, "src", "App", "ScopeProbe.App.csproj"),
                namespacePrefix: "ScopeProbe", depth: 1, includeTypes: false, maxResults: 1,
                maxResponseBytes: 65536, maxResponseTokens: minimumTokens);
            AssertSuccessWithinBudget(retry, 65536, minimumTokens);
        }
        else AssertSuccessWithinBudget(namespaceTokens, 65536, 512);
        var generatedNamespaceExcluded = await structure.GetNamespaceTree(target, namespacePrefix: "ScopeProbe.GeneratedOnly",
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(generatedNamespaceExcluded, "INVALID_ARGUMENT", 16384, 1024);
        var generatedNamespaceIncluded = await structure.GetNamespaceTree(target, namespacePrefix: "ScopeProbe.GeneratedOnly",
            includeGenerated: true, maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(generatedNamespaceIncluded, 16384, 1024);
        Assert.Contains("GeneratedProbe", TextOf(generatedNamespaceIncluded), StringComparison.Ordinal);
        var invalidKind = await structure.GetNamespaceTree(target, kind: "unsupported", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(invalidKind, "INVALID_ARGUMENT", 16384, 1024);

        var indexScope = await structure.GetIndexScope(target, maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(indexScope, 16384, 1024);
        Assert.Contains("Roslyn documents", TextOf(indexScope), StringComparison.Ordinal);
        var assemblyAsSource = await structure.GetIndexScope(typeof(SourceToolsContractTests).Assembly.Location,
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(assemblyAsSource, "INVALID_ARGUMENT", 16384, 1024);

        var feature = await relationships.GetFeatureContext(target, methodHandoff, maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(feature, 16384, 1024);
        Assert.Contains("static-test-candidates-only", TextOf(feature), StringComparison.Ordinal);
        var featureBytes = await relationships.GetFeatureContext(target, methodHandoff, maxCallers: 1, maxTests: 1,
            maxResponseBytes: 512, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(featureBytes, 512, 4096);
        var featureTokens = await relationships.GetFeatureContext(target, methodHandoff, maxCallers: 1, maxTests: 1,
            maxResponseBytes: 65536, maxResponseTokens: 512);
        AssertSuccessWithinBudget(featureTokens, 65536, 512);
        var featureProduction = await relationships.GetFeatureContext(target, methodHandoff, scopeType: "production",
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(featureProduction, 16384, 1024);
        Assert.DoesNotContain("RunTest", TextOf(featureProduction), StringComparison.Ordinal);
        var featureTests = await relationships.GetFeatureContext(target, methodHandoff, scopeType: "tests",
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(featureTests, 16384, 1024);
        Assert.Contains("RunTest", TextOf(featureTests), StringComparison.Ordinal);
        var emptyFeature = await relationships.GetFeatureContext(target, " ", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(emptyFeature, "INVALID_ARGUMENT", 16384, 1024);

        var testContext = await relationships.GetTestContext(target, methodHandoff, maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(testContext, 16384, 1024);
        Assert.Contains("RunTest", TextOf(testContext), StringComparison.Ordinal);
        Assert.Contains("static-test-candidates-only", TextOf(testContext), StringComparison.Ordinal);
        var testContextBytes = await relationships.GetTestContext(target, methodHandoff, maxResults: 1,
            maxResponseBytes: 512, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(testContextBytes, 512, 4096);
        var testContextTokens = await relationships.GetTestContext(target, methodHandoff, maxResults: 1,
            maxResponseBytes: 65536, maxResponseTokens: 512);
        AssertSuccessWithinBudget(testContextTokens, 65536, 512);
        var testContextProduction = await relationships.GetTestContext(target, methodHandoff, scopeType: "production",
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(testContextProduction, 16384, 1024);
        Assert.DoesNotContain("RunTest", TextOf(testContextProduction), StringComparison.Ordinal);
        var emptyTestContext = await relationships.GetTestContext(target, " ", maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(emptyTestContext, "INVALID_ARGUMENT", 16384, 1024);

        var missingFindSelector = await symbols.FindSymbol(target, maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertErrorWithinBudget(missingFindSelector, "INVALID_ARGUMENT", 16384, 1024);
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
        var broadResult = await tools.GetFileSkeleton(target, [filePath], maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(broadResult, 65536, 4096);
        var expected = BodyOf(TextOf(broadResult));
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
            Assert.StartsWith("h:", handoff);
            return handoff!;
        }
        var declarationIndex = text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(declarationIndex >= 0, text);
        var marker = "[handoff: ";
        var start = text.IndexOf(marker, declarationIndex, StringComparison.Ordinal);
        Assert.True(start >= 0, text);
        start += marker.Length;
        var end = text.IndexOf(']', start);
        Assert.True(end > start, text);
        return text[start..end];
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
        var handoffs = new List<string>();
        foreach (var (marker, suffix) in new[] { ("[handoff: ", "]"), ("handoffId: `", "`") })
        {
            var position = 0;
            while ((position = text.IndexOf(marker, position, StringComparison.Ordinal)) >= 0)
            {
                position += marker.Length;
                var end = text.IndexOf(suffix, position, StringComparison.Ordinal);
                Assert.True(end > position, text);
                handoffs.Add(text[position..end]);
                position = end + suffix.Length;
            }
        }
        return handoffs.ToArray();
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
            }
            """);

        var nugetConfigPath = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(nugetConfigPath, "<configuration><packageSources><clear /></packageSources></configuration>");
        await FixtureRestore.RunAsync(solutionPath, root, nugetConfigPath, "Source-tool fixture restore");
        return solutionPath;
    }

    private static async Task<(List<string> Items, int Pages, string FirstCursor)> ReadClassMemberPagesAsync(StructureTools tools, string target, string typeName)
    {
        var items = new List<string>();
        string? cursor = null;
        string? firstCursor = null;
        var pageSize = 200;
        var pages = 0;
        do
        {
            var result = await tools.GetClassStructure(target, typeName, maxMembers: pageSize, resultCursor: cursor,
                maxResponseBytes: 65536, maxResponseTokens: 8192);
            var payload = await ReconstructOuterPagesAsync(result, async continuation =>
                await tools.GetClassStructure(target, typeName, maxMembers: pageSize, continuationToken: continuation,
                    maxResponseBytes: 65536, maxResponseTokens: 8192));
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            items.AddRange(root.GetProperty("members").EnumerateArray().Select(member => member.GetProperty("name").GetString()!));
            cursor = root.TryGetProperty("resultCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            firstCursor ??= cursor;
            if (cursor is not null) pageSize = 17;
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
        var pageSize = 200;
        var pages = 0;
        do
        {
            var result = await tools.GetNamespaceTree(target, namespacePrefix: "CapProbe", maxResults: pageSize, resultCursor: cursor,
                maxResponseBytes: 65536, maxResponseTokens: 8192);
            var payload = await ReconstructOuterPagesAsync(result, async continuation =>
                await tools.GetNamespaceTree(target, namespacePrefix: "CapProbe", maxResults: pageSize, continuationToken: continuation,
                    maxResponseBytes: 65536, maxResponseTokens: 8192));
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var name = item.TryGetProperty("fullName", out var fullName) ? fullName.GetString() : item.GetProperty("name").GetString();
                items.Add($"{item.GetProperty("kind").GetString()}:{name}");
            }
            cursor = root.TryGetProperty("resultCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            firstCursor ??= cursor;
            if (cursor is not null) pageSize = 17;
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
}
