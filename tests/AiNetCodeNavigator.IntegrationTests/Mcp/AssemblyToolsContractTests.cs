using System.Text;
using System.Reflection;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Tools;
using AiNetCodeNavigator.Mcp.Tools.Assemblies;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class AssemblyToolsContractTests
{
    [Fact]
    public async Task FindSymbolResultCursor_PagesAssemblyBatchAndPreservesReferenceOwners()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var symbols = new SymbolTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-assembly-symbol-pages-");
        var dependencySource = "namespace SymbolPageDependency; public sealed class Dependency { "
            + string.Join(" ", Enumerable.Range(0, 16).Select(index => $"public int PageEntry{index:D2}() => {index};"))
            + " }";
        var dependencyPath = AssemblyTestHelper.EmitAssembly(fixture, "SymbolPageDependency", dependencySource);
        var rootPath = AssemblyTestHelper.EmitAssembly(fixture, "SymbolPageRoot.Tests",
            "namespace SymbolPageRoot; public sealed class Root { private readonly SymbolPageDependency.Dependency _dependency = new(); "
            + "public int PageEntryRoot() => _dependency.PageEntry00(); public int Run() => PageEntryRoot(); }", dependencyPath);

        var dependencyScopeResult = await AssemblyNavigationSessionScope.OpenAsync(dependencyPath, default);
        Assert.True(dependencyScopeResult.IsSuccess, dependencyScopeResult.Error?.ToString());
        await using (var dependencyScope = dependencyScopeResult.Value!)
        {
            var project = Assert.Single(dependencyScope.Solution.Projects);
            Assert.False(TestDetector.IsTestProject(project, classificationPath: dependencyPath),
                $"The assembly owner should be classified by its path: {project.Name}; {dependencyPath}");
        }

        var matches = new List<(string Pattern, string Name, string Owner, string Handoff)>();
        var pageBodies = new List<string>();
        string? cursor = null;
        string? firstCursor = null;
        var pages = 0;
        do
        {
            var result = await symbols.FindSymbol(rootPath, namePatterns: ["PageEntry", "PageEntryRoot"], kind: "method",
                includeReferences: true, maxResults: 5, resultCursor: cursor,
                maxResponseBytes: 65536, maxResponseTokens: 8192);
            AssertSuccessWithinBudget(result, 65536, 8192);
            var jsonBody = BodyOf(TextOf(result));
            var jsonStart = jsonBody.IndexOf('{');
            Assert.True(jsonStart >= 0, TextOf(result));
            pageBodies.Add(jsonBody[jsonStart..]);
            using var document = System.Text.Json.JsonDocument.Parse(pageBodies[^1]);
            var response = document.RootElement;
            foreach (var patternResult in response.GetProperty("results").EnumerateArray())
            foreach (var entry in patternResult.GetProperty("entries").EnumerateArray())
            {
                var pattern = patternResult.GetProperty("pattern").GetString()!;
                var handoff = entry.GetProperty("handoffId").GetString()!;
                var owner = entry.GetProperty("ownerTargetPath").GetString()!;
                matches.Add((pattern, entry.GetProperty("name").GetString()!, owner, handoff));
            }
            cursor = response.TryGetProperty("resultCursor", out var value)
                && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
            firstCursor ??= cursor;
            pages++;
            Assert.True(pages <= 10, $"Assembly find_symbol paging exceeded the finite request cap. cursor={cursor}; page={pageBodies[^1]}");
        } while (cursor is not null);

        Assert.Equal(4, pages);
        var expectedMatches = Enumerable.Range(0, 16)
            .Select(index => (Pattern: "PageEntry", Name: $"PageEntry{index:D2}", Owner: Path.GetFullPath(dependencyPath)))
            .Append(("PageEntry", "PageEntryRoot", Path.GetFullPath(rootPath)))
            .Append(("PageEntryRoot", "PageEntryRoot", Path.GetFullPath(rootPath)))
            .ToArray();
        Assert.Equal(expectedMatches, matches.Select(match => (match.Pattern, match.Name, Path.GetFullPath(match.Owner))).ToArray());
        Assert.Equal(expectedMatches.Length, matches.Select(match => (match.Pattern, match.Name, match.Owner)).Distinct().Count());
        Assert.NotNull(firstCursor);

        var dependencyProductionScope = await symbols.FindSymbol(dependencyPath, pattern: "PageEntry", kind: "method", scopeType: "production",
            maxResults: 5, maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertSuccessWithinBudget(dependencyProductionScope, 65536, 8192);
        using (var scopeDocument = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(dependencyProductionScope))))
        {
            var patternResult = Assert.Single(scopeDocument.RootElement.GetProperty("results").EnumerateArray());
            Assert.Equal(16, patternResult.GetProperty("totalMatches").GetInt32());
        }
        var productionScope = await symbols.FindSymbol(rootPath, pattern: "PageEntry", kind: "method", scopeType: "production",
            includeReferences: true, maxResults: 5, maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertSuccessWithinBudget(productionScope, 65536, 8192);
        using (var scopeDocument = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(productionScope))))
        {
            var patternResult = Assert.Single(scopeDocument.RootElement.GetProperty("results").EnumerateArray());
            Assert.True(patternResult.GetProperty("totalMatches").GetInt32() == 16, TextOf(productionScope));
            Assert.Equal(5, patternResult.GetProperty("returnedMatches").GetInt32());
            Assert.Equal("maxResults", Assert.Single(patternResult.GetProperty("truncatedBy").EnumerateArray()).GetString());
            Assert.True(scopeDocument.RootElement.GetProperty("resultCursor").ValueKind == System.Text.Json.JsonValueKind.String);
        }
        var testScope = await symbols.FindSymbol(rootPath, pattern: "PageEntry", kind: "method", scopeType: "tests",
            includeReferences: true, maxResults: 5, maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertSuccessWithinBudget(testScope, 65536, 8192);
        using (var scopeDocument = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(testScope))))
        {
            var patternResult = Assert.Single(scopeDocument.RootElement.GetProperty("results").EnumerateArray());
            Assert.Equal(1, patternResult.GetProperty("totalMatches").GetInt32());
            Assert.Equal("PageEntryRoot", Assert.Single(patternResult.GetProperty("entries").EnumerateArray()).GetProperty("name").GetString());
            Assert.False(scopeDocument.RootElement.TryGetProperty("resultCursor", out var scopeCursor)
                && scopeCursor.ValueKind == System.Text.Json.JsonValueKind.String);
        }

        var replay = await ReadOuterPagesAsync((bytes, tokens, continuation) => symbols.FindSymbol(rootPath,
            namePatterns: ["PageEntry", "PageEntryRoot"], kind: "method", includeReferences: true, maxResults: 5,
            resultCursor: continuation is null ? firstCursor : null, continuationToken: continuation,
            maxResponseBytes: bytes, maxResponseTokens: tokens), 1024, 4096);
        Assert.True(replay.Pages > 1);
        Assert.Equal(pageBodies[1], replay.Text);

        AssertErrorWithinBudget(await symbols.FindSymbol(rootPath, pattern: "PageEntry", kind: "method",
            includeReferences: true, maxResults: 5, resultCursor: firstCursor,
            maxResponseBytes: 65536, maxResponseTokens: 8192), "RESULT_CURSOR_ARGUMENT_MISMATCH", 65536, 8192);
        AssertErrorWithinBudget(await symbols.FindSymbol(rootPath, namePatterns: ["PageEntry", "PageEntryRoot"], kind: "method",
            includeReferences: true, maxResults: 5, resultCursor: "malformed-cursor",
            maxResponseBytes: 16384, maxResponseTokens: 2048), "RESULT_CURSOR_EXPIRED", 16384, 2048);

        var identicalTarget = fixture.GetPath("SymbolPageRoot-copy.dll");
        File.Copy(rootPath, identicalTarget);
        AssertErrorWithinBudget(await symbols.FindSymbol(identicalTarget, namePatterns: ["PageEntry", "PageEntryRoot"], kind: "method",
            includeReferences: true, maxResults: 5, resultCursor: firstCursor,
            maxResponseBytes: 65536, maxResponseTokens: 8192), "RESULT_CURSOR_ARGUMENT_MISMATCH", 65536, 8192);

        var changedAssembly = AssemblyTestHelper.EmitAssembly(fixture, "SymbolPageRootChanged",
            "namespace SymbolPageRoot; public sealed class Root { public int Different() => 1; }");
        File.Copy(changedAssembly, rootPath, overwrite: true);
        AssertErrorWithinBudget(await symbols.FindSymbol(rootPath, namePatterns: ["PageEntry", "PageEntryRoot"], kind: "method",
            includeReferences: true, maxResults: 5, resultCursor: firstCursor,
            maxResponseBytes: 65536, maxResponseTokens: 8192), "STALE_SNAPSHOT", 65536, 8192);

        var broad = await symbols.FindSymbol(identicalTarget, pattern: "PageEntry", kind: "method", includeReferences: true,
            maxResults: 100, maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertSuccessWithinBudget(broad, 65536, 8192);
        var outerPages = await ReadOuterPagesAsync((bytes, tokens, continuation) => symbols.FindSymbol(
            identicalTarget, pattern: "PageEntry", kind: "method", includeReferences: true,
            maxResults: 100, continuationToken: continuation, maxResponseBytes: bytes, maxResponseTokens: tokens), 1024, 4096);
        Assert.True(outerPages.Pages > 1);
        Assert.Equal(BodyOf(TextOf(broad)), outerPages.Text);

        var dependencyMatch = matches.First(match => string.Equals(match.Owner, dependencyPath, StringComparison.OrdinalIgnoreCase));
        var body = await symbols.GetSymbolBody(dependencyPath, [dependencyMatch.Handoff], maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(body, 16384, 2048);
        Assert.Contains(dependencyMatch.Name, TextOf(body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssemblyImpact_ResultCursorReconstructsAllCallSitesAcrossPages()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        var symbols = new SymbolTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-assembly-impact-pages-");
        var callers = string.Join("\n", Enumerable.Range(0, 8).Select(index =>
            $"public sealed class Caller{index:D2} {{ public int Invoke(Target target) => target.Read(); }}"));
        var subtypes = string.Join("\n", Enumerable.Range(0, 8).Select(index =>
            $"public sealed class Subtype{index:D2} : Target {{ }}"));
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "AssemblyImpactPages", $$"""
            namespace AssemblyImpactPages;
            public class Target { public int Read() => 1; }
            {{callers}}
            {{subtypes}}
            """);
        var broadImpact = await relationships.GetImpact(assemblyPath, "M:AssemblyImpactPages.Target.Read",
            maxResults: 100, maxResponseBytes: 32768, maxResponseTokens: 4096);
        Assert.False(broadImpact.IsError ?? false, TextOf(broadImpact));
        using var broadImpactDocument = System.Text.Json.JsonDocument.Parse(IntegrationMcpAssertions.BodyOf(TextOf(broadImpact)));
        var expectedImpact = broadImpactDocument.RootElement.GetProperty("callSites").EnumerateArray()
            .Select(item => $"{item.GetProperty("filePath").GetString()}:{item.GetProperty("line").GetInt32()}:{item.GetProperty("callingMember").GetString()}")
            .ToArray();

        var seen = new List<string>();
        string? cursor = null;
        string? firstImpactCursor = null;
        var pages = 0;
        int? total = null;
        do
        {
            var response = await relationships.GetImpact(assemblyPath, "M:AssemblyImpactPages.Target.Read",
                maxResults: 2, resultCursor: cursor, maxResponseBytes: 32768, maxResponseTokens: 4096);
            Assert.False(response.IsError ?? false, TextOf(response));
            await FollowAssemblyHandoffAsync(symbols, assemblyPath, response);
            using var document = System.Text.Json.JsonDocument.Parse(IntegrationMcpAssertions.BodyOf(TextOf(response)));
            var root = document.RootElement;
            total ??= root.GetProperty("transitiveImpactCount").GetInt32();
            foreach (var item in root.GetProperty("callSites").EnumerateArray())
                seen.Add($"{item.GetProperty("filePath").GetString()}:{item.GetProperty("line").GetInt32()}:{item.GetProperty("callingMember").GetString()}");
            cursor = root.TryGetProperty("resultCursor", out var cursorValue)
                && cursorValue.ValueKind == System.Text.Json.JsonValueKind.String ? cursorValue.GetString() : null;
            firstImpactCursor ??= cursor;
            pages++;
            Assert.InRange(pages, 1, 10);
        } while (cursor is not null);

        Assert.Equal(4, pages);
        Assert.Equal(8, total);
        Assert.Equal(8, seen.Count);
        Assert.Equal(8, seen.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(expectedImpact, seen);
        Assert.NotNull(firstImpactCursor);
        AssertErrorWithinBudget(await relationships.GetImpact(assemblyPath, "M:AssemblyImpactPages.Target.Read",
            maxResults: 3, resultCursor: firstImpactCursor, maxResponseBytes: 32768, maxResponseTokens: 4096),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 32768, 4096);

        seen.Clear();
        cursor = null;
        string? firstHierarchyCursor = null;
        pages = 0;
        string? firstReferenceCursor = null;
        var broadReferences = await relationships.FindReferences(assemblyPath, "M:AssemblyImpactPages.Target.Read",
            maxResults: 100, maxResponseBytes: 32768, maxResponseTokens: 4096);
        Assert.False(broadReferences.IsError ?? false, TextOf(broadReferences));
        using var broadReferencesDocument = System.Text.Json.JsonDocument.Parse(IntegrationMcpAssertions.BodyOf(TextOf(broadReferences)));
        var expectedReferences = broadReferencesDocument.RootElement.GetProperty("references").EnumerateArray()
            .Select(item => $"{item.GetProperty("filePath").GetString()}:{item.GetProperty("line").GetInt32()}:{item.GetProperty("enclosingSymbolName").GetString()}")
            .ToArray();
        do
        {
            var response = await relationships.FindReferences(assemblyPath, "M:AssemblyImpactPages.Target.Read",
                maxResults: 2, resultCursor: cursor, maxResponseBytes: cursor is null ? 32768 : 65536,
                maxResponseTokens: cursor is null ? 4096 : 8192);
            Assert.False(response.IsError ?? false, TextOf(response));
            using var document = System.Text.Json.JsonDocument.Parse(IntegrationMcpAssertions.BodyOf(TextOf(response)));
            var root = document.RootElement;
            foreach (var item in root.GetProperty("references").EnumerateArray())
                seen.Add($"{item.GetProperty("filePath").GetString()}:{item.GetProperty("line").GetInt32()}:{item.GetProperty("enclosingSymbolName").GetString()}");
            cursor = root.TryGetProperty("resultCursor", out var cursorValue)
                && cursorValue.ValueKind == System.Text.Json.JsonValueKind.String ? cursorValue.GetString() : null;
            firstReferenceCursor ??= cursor;
            pages++;
            Assert.InRange(pages, 1, 10);
        } while (cursor is not null);
        Assert.Equal(4, pages);
        Assert.Equal(8, seen.Count);
        Assert.Equal(8, seen.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(expectedReferences, seen);
        Assert.NotNull(firstReferenceCursor);
        AssertErrorWithinBudget(await relationships.FindReferences(assemblyPath, "M:AssemblyImpactPages.Target.Read",
            maxResults: 3, resultCursor: firstReferenceCursor, maxResponseBytes: 16384, maxResponseTokens: 2048),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 2048);
        AssertErrorWithinBudget(await relationships.FindReferences(assemblyPath, "M:AssemblyImpactPages.Caller00.Invoke",
            maxResults: 2, resultCursor: firstReferenceCursor, maxResponseBytes: 16384, maxResponseTokens: 2048),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 2048);
        AssertErrorWithinBudget(await relationships.FindReferences(assemblyPath, "M:AssemblyImpactPages.Target.Read",
            scopeType: "tests", maxResults: 2, resultCursor: firstReferenceCursor, maxResponseBytes: 16384, maxResponseTokens: 2048),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 2048);
        AssertErrorWithinBudget(await relationships.FindReferences(assemblyPath, "M:AssemblyImpactPages.Target.Read",
            maxResults: 2, resultCursor: "malformed-cursor", maxResponseBytes: 16384, maxResponseTokens: 2048),
            "RESULT_CURSOR_EXPIRED", 16384, 2048);
        var referenceCopyPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, "AssemblyImpactPages-copy.dll");
        File.Copy(assemblyPath, referenceCopyPath);
        AssertErrorWithinBudget(await relationships.FindReferences(referenceCopyPath, "M:AssemblyImpactPages.Target.Read",
            maxResults: 2, resultCursor: firstReferenceCursor, maxResponseBytes: 16384, maxResponseTokens: 2048),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 2048);

        seen.Clear();
        cursor = null;
        pages = 0;
        var broadHierarchy = await relationships.GetTypeHierarchy(assemblyPath, "T:AssemblyImpactPages.Target",
            maxResults: 100, maxResponseBytes: 32768, maxResponseTokens: 4096);
        Assert.False(broadHierarchy.IsError ?? false, TextOf(broadHierarchy));
        using var broadHierarchyDocument = System.Text.Json.JsonDocument.Parse(IntegrationMcpAssertions.BodyOf(TextOf(broadHierarchy)));
        var expectedHierarchy = broadHierarchyDocument.RootElement.GetProperty("subtypes").EnumerateArray()
            .Select(item => $"{item.GetProperty("filePath").GetString()}:{item.GetProperty("line").GetInt32()}:{item.GetProperty("name").GetString()}")
            .ToArray();
        do
        {
            var response = await relationships.GetTypeHierarchy(assemblyPath, "T:AssemblyImpactPages.Target",
                maxResults: 2, resultCursor: cursor, maxResponseBytes: 32768, maxResponseTokens: 4096);
            Assert.False(response.IsError ?? false, TextOf(response));
            await FollowAssemblyHandoffAsync(symbols, assemblyPath, response);
            using var document = System.Text.Json.JsonDocument.Parse(IntegrationMcpAssertions.BodyOf(TextOf(response)));
            var root = document.RootElement;
            foreach (var item in root.GetProperty("subtypes").EnumerateArray())
                seen.Add($"{item.GetProperty("filePath").GetString()}:{item.GetProperty("line").GetInt32()}:{item.GetProperty("name").GetString()}");
            cursor = root.TryGetProperty("resultCursor", out var cursorValue)
                && cursorValue.ValueKind == System.Text.Json.JsonValueKind.String ? cursorValue.GetString() : null;
            firstHierarchyCursor ??= cursor;
            pages++;
            Assert.InRange(pages, 1, 10);
        } while (cursor is not null);
        Assert.Equal(4, pages);
        Assert.Equal(8, seen.Count);
        Assert.Equal(8, seen.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(8, broadHierarchyDocument.RootElement.GetProperty("totalSubtypes").GetInt32());
        Assert.Equal(expectedHierarchy, seen);
        Assert.NotNull(firstHierarchyCursor);
        AssertErrorWithinBudget(await relationships.GetTypeHierarchy(assemblyPath, "T:AssemblyImpactPages.Target",
            maxResults: 3, resultCursor: firstHierarchyCursor, maxResponseBytes: 32768, maxResponseTokens: 4096),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 32768, 4096);
        AssertErrorWithinBudget(await relationships.GetImpact(assemblyPath, "M:AssemblyImpactPages.Target.Read",
            maxResults: 2, resultCursor: "malformed-cursor", maxResponseBytes: 32768, maxResponseTokens: 4096),
            "RESULT_CURSOR_EXPIRED", 32768, 4096);
        AssertErrorWithinBudget(await relationships.GetTypeHierarchy(assemblyPath, "T:AssemblyImpactPages.Target",
            maxResults: 2, resultCursor: "malformed-cursor", maxResponseBytes: 32768, maxResponseTokens: 4096),
            "RESULT_CURSOR_EXPIRED", 32768, 4096);
        AssertErrorWithinBudget(await relationships.GetImpact(referenceCopyPath, "M:AssemblyImpactPages.Target.Read",
            maxResults: 2, resultCursor: firstImpactCursor, maxResponseBytes: 32768, maxResponseTokens: 4096),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 32768, 4096);
        AssertErrorWithinBudget(await relationships.GetTypeHierarchy(referenceCopyPath, "T:AssemblyImpactPages.Target",
            maxResults: 2, resultCursor: firstHierarchyCursor, maxResponseBytes: 32768, maxResponseTokens: 4096),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 32768, 4096);
        var changedAssemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "AssemblyImpactPagesChanged", """
            namespace AssemblyImpactPages;
            public class Target { public int Read() => 2; }
            public sealed class ReplacementCaller { public int Invoke(Target target) => target.Read(); }
            public sealed class ReplacementSubtype : Target { }
            """);
        File.Copy(changedAssemblyPath, assemblyPath, overwrite: true);
        AssertErrorWithinBudget(await relationships.GetImpact(assemblyPath, "M:AssemblyImpactPages.Target.Read",
            maxResults: 2, resultCursor: firstImpactCursor, maxResponseBytes: 32768, maxResponseTokens: 4096),
            "STALE_SNAPSHOT", 32768, 4096);
        AssertErrorWithinBudget(await relationships.GetTypeHierarchy(assemblyPath, "T:AssemblyImpactPages.Target",
            maxResults: 2, resultCursor: firstHierarchyCursor, maxResponseBytes: 32768, maxResponseTokens: 4096),
            "STALE_SNAPSHOT", 32768, 4096);
    }

    [Fact]
    public async Task AssemblyFindImplementations_ResultCursorReconstructsAllMatchesAcrossPages()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        var symbols = new SymbolTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-assembly-implementation-pages-");
        var readers = string.Join("\n", Enumerable.Range(0, 5).Select(index =>
            $"public sealed class Reader{index:D2} : IReadable {{ public int Read() => {index}; public string Label => \"reader\"; }}"));
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "AssemblyImplementationPages", $$"""
            namespace AssemblyImplementationPages;
            public interface IReadable { int Read(); string Label { get; } }
            public sealed class Probe : IReadable { public int Read() => 1; public string Label => "probe"; }
            {{readers}}
            """);

        var seen = new List<string>();
        string? cursor = null;
        string? firstImplementationCursor = null;
        var pages = 0;
        var broadImplementations = await relationships.FindImplementations(assemblyPath, "T:AssemblyImplementationPages.IReadable",
            maxResults: 100, maxResponseBytes: 32768, maxResponseTokens: 4096);
        Assert.False(broadImplementations.IsError ?? false, TextOf(broadImplementations));
        using var broadImplementationsDocument = System.Text.Json.JsonDocument.Parse(IntegrationMcpAssertions.BodyOf(TextOf(broadImplementations)));
        var expectedImplementations = broadImplementationsDocument.RootElement.GetProperty("implementations").EnumerateArray()
            .Select(item => $"{item.GetProperty("filePath").GetString()}:{item.GetProperty("line").GetInt32()}:{item.GetProperty("symbolName").GetString()}")
            .ToArray();
        do
        {
            var response = await relationships.FindImplementations(assemblyPath, "T:AssemblyImplementationPages.IReadable",
                maxResults: 2, resultCursor: cursor, maxResponseBytes: cursor is null ? 32768 : 65536,
                maxResponseTokens: cursor is null ? 4096 : 8192);
            Assert.False(response.IsError ?? false, TextOf(response));
            await FollowAssemblyHandoffAsync(symbols, assemblyPath, response);
            using var document = System.Text.Json.JsonDocument.Parse(IntegrationMcpAssertions.BodyOf(TextOf(response)));
            var root = document.RootElement;
            foreach (var item in root.GetProperty("implementations").EnumerateArray())
                seen.Add($"{item.GetProperty("filePath").GetString()}:{item.GetProperty("line").GetInt32()}:{item.GetProperty("symbolName").GetString()}");
            cursor = root.TryGetProperty("resultCursor", out var cursorValue)
                && cursorValue.ValueKind == System.Text.Json.JsonValueKind.String ? cursorValue.GetString() : null;
            firstImplementationCursor ??= cursor;
            pages++;
            Assert.InRange(pages, 1, 10);
        } while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal(6, seen.Count);
        Assert.Equal(6, seen.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(expectedImplementations, seen);
        Assert.NotNull(firstImplementationCursor);
        AssertErrorWithinBudget(await relationships.FindImplementations(assemblyPath, "T:AssemblyImplementationPages.IReadable",
            maxResults: 3, resultCursor: firstImplementationCursor, maxResponseBytes: 16384, maxResponseTokens: 2048),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 2048);
        AssertErrorWithinBudget(await relationships.FindImplementations(assemblyPath, "M:AssemblyImplementationPages.IReadable.Read",
            maxResults: 2, resultCursor: firstImplementationCursor, maxResponseBytes: 16384, maxResponseTokens: 2048),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 2048);
        AssertErrorWithinBudget(await relationships.FindImplementations(assemblyPath, "T:AssemblyImplementationPages.IReadable",
            scopeType: "tests", maxResults: 2, resultCursor: firstImplementationCursor, maxResponseBytes: 16384, maxResponseTokens: 2048),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 2048);
        AssertErrorWithinBudget(await relationships.FindImplementations(assemblyPath, "T:AssemblyImplementationPages.IReadable",
            maxResults: 2, resultCursor: "malformed-cursor", maxResponseBytes: 16384, maxResponseTokens: 2048),
            "RESULT_CURSOR_EXPIRED", 16384, 2048);
        var implementationCopyPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, "AssemblyImplementationPages-copy.dll");
        File.Copy(assemblyPath, implementationCopyPath);
        AssertErrorWithinBudget(await relationships.FindImplementations(implementationCopyPath, "T:AssemblyImplementationPages.IReadable",
            maxResults: 2, resultCursor: firstImplementationCursor, maxResponseBytes: 16384, maxResponseTokens: 2048),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 16384, 2048);
        var changedAssemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "AssemblyImplementationPagesChanged", """
            namespace AssemblyImplementationPages;
            public interface IReadable { int Read(); string Label { get; } }
            public sealed class Replacement : IReadable { public int Read() => 9; public string Label => "replacement"; }
            """);
        File.Copy(changedAssemblyPath, assemblyPath, overwrite: true);
        AssertErrorWithinBudget(await relationships.FindImplementations(assemblyPath, "T:AssemblyImplementationPages.IReadable",
            maxResults: 2, resultCursor: firstImplementationCursor, maxResponseBytes: 16384, maxResponseTokens: 2048),
            "STALE_SNAPSHOT", 16384, 2048);
    }

    [Fact]
    public async Task AssemblyCallTree_PreservesRecursiveAndSameLineCallSitesWithEvidence()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var symbols = new SymbolTools(runtime);
        var relationships = new RelationshipTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-assembly-recursive-calltree-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "AssemblyRecursiveProbe", """
            namespace AssemblyRecursiveProbe;
            public sealed class Probe
            {
                public int Read() => 1;
                public int Twice() => Read() + Read();
                public int Self(int remaining) => remaining <= 0 ? 0 : Self(remaining - 1);
                public int First(int remaining) => remaining <= 0 ? 0 : Second(remaining - 1);
                public int Second(int remaining) => remaining <= 0 ? 0 : First(remaining - 1);
            }
            """);

        var twice = await symbols.FindSymbol(assemblyPath, pattern: "Probe.Twice", kind: "method", maxResponseBytes: 32768);
        var self = await symbols.FindSymbol(assemblyPath, pattern: "Probe.Self", kind: "method", maxResponseBytes: 32768);
        var first = await symbols.FindSymbol(assemblyPath, pattern: "Probe.First", kind: "method", maxResponseBytes: 32768);
        AssertOwnerResult(twice, "Twice");
        AssertOwnerResult(self, "Self");
        AssertOwnerResult(first, "First");

        var twiceTree = await relationships.GetCallTree(assemblyPath, ReadAnyHandoff(TextOf(twice)),
            direction: "outgoing", depth: 3, maxResponseBytes: 32768);
        var selfTree = await relationships.GetCallTree(assemblyPath, ReadAnyHandoff(TextOf(self)),
            direction: "outgoing", depth: 3, maxResponseBytes: 32768);
        var mutualTree = await relationships.GetCallTree(assemblyPath, ReadAnyHandoff(TextOf(first)),
            direction: "outgoing", depth: 3, maxResponseBytes: 32768);
        var selfReferences = await relationships.FindReferences(assemblyPath, ReadAnyHandoff(TextOf(self)),
            maxResponseBytes: 32768);
        var selfImpact = await relationships.GetImpact(assemblyPath, ReadAnyHandoff(TextOf(self)),
            maxResponseBytes: 32768);

        var twiceText = TextOf(twiceTree);
        var selfText = TextOf(selfTree);
        var mutualText = TextOf(mutualTree);
        Assert.Contains(":", twiceText, StringComparison.Ordinal);
        Assert.True(twiceText.Split("[call]", StringSplitOptions.None).Length - 1 >= 2, twiceText);
        Assert.Contains("Probe.Self ->", selfText, StringComparison.Ordinal);
        Assert.Contains("[call]", selfText, StringComparison.Ordinal);
        Assert.Contains("Probe.Second", mutualText, StringComparison.Ordinal);
        Assert.Contains("Probe.First", mutualText, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics count: 1", mutualText, StringComparison.Ordinal);
        Assert.Contains("\"evidenceKind\": \"call\"", TextOf(selfReferences), StringComparison.Ordinal);
        Assert.Contains("\"column\":", TextOf(selfReferences), StringComparison.Ordinal);
        Assert.Contains("Probe.Self", TextOf(selfReferences), StringComparison.Ordinal);
        Assert.Contains("\"evidenceKind\": \"call\"", TextOf(selfImpact), StringComparison.Ordinal);
        Assert.Contains("\"column\":", TextOf(selfImpact), StringComparison.Ordinal);
        Assert.Contains("Probe.Self", TextOf(selfImpact), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssemblyCallTree_MetadataUsesTheGraphGenerationWhenTargetChangesAfterGraphBuild()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("ainet-calltree-generation-boundary-");
        using var replacementFixture = TestTempDirectory.Create("ainet-calltree-generation-replacement-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "CallTreeGenerationProbe", "namespace Probe; public sealed class Caller { public int Target() => 1; public int Run() => Target(); }");
        var replacementPath = AssemblyTestHelper.EmitAssembly(replacementFixture, "CallTreeGenerationProbe", "namespace Probe; public sealed class Caller { public int Target() => 2; public int Run() => Target(); public int Added() => 3; }");
        AssertSameAssemblyIdentity(assemblyPath, replacementPath);
        await AssertDifferentBytesAsync(assemblyPath, replacementPath);
        var symbols = new SymbolTools(runtime);
        var produced = await symbols.FindSymbol(assemblyPath, pattern: "Caller.Run", kind: "method", maxResponseBytes: 32768);
        AssertOwnerResult(produced, "Run");
        var graphSnapshot = ReadHeader(TextOf(produced), "snapshotId");
        var replaced = false;
        var relationships = new RelationshipTools(runtime, _ =>
        {
            File.Copy(replacementPath, assemblyPath, overwrite: true);
            replaced = true;
            return Task.CompletedTask;
        }, null);

        var result = await relationships.GetCallTree(assemblyPath, ReadAnyHandoff(TextOf(produced)),
            direction: "outgoing", includeDiagnostics: true, maxResponseBytes: 32768);

        Assert.True(replaced);
        AssertOwnerResult(result, "Caller.Target");
        Assert.Equal(graphSnapshot, ReadHeader(TextOf(result), "snapshotId"));
    }

    [Fact]
    public async Task AssemblyCallTree_RejectsChangedTransitiveOwnerAfterHandoffAcquisition()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("ainet-closure-owner-generation-");
        using var replacementFixture = TestTempDirectory.Create("ainet-closure-owner-replacement-");
        var leaf = AssemblyTestHelper.EmitAssembly(fixture, "ClosureLeafProbe",
            "namespace ClosureLeaf; public sealed class Probe { public int Run() => new ClosureTransitive.Value().Number; }",
            AssemblyTestHelper.EmitAssembly(fixture, "ClosureTransitiveProbe", "namespace ClosureTransitive; public sealed class Value { public int Number => 1; }"));
        var transitivePath = Path.Combine(Path.GetDirectoryName(leaf)!, "ClosureTransitiveProbe.dll");
        var replacementTransitive = AssemblyTestHelper.EmitAssembly(replacementFixture, "ClosureTransitiveProbe",
            "namespace ClosureTransitive; public sealed class Value { public int Number => 2; public int Added => 3; }");
        AssertSameAssemblyIdentity(transitivePath, replacementTransitive);
        await AssertDifferentBytesAsync(transitivePath, replacementTransitive);
        var root = AssemblyTestHelper.EmitAssembly(fixture, "ClosureRootProbe",
            "namespace ClosureRoot; public sealed class Root { public int Run() => new ClosureLeaf.Probe().Run(); }", leaf);
        var symbols = new SymbolTools(runtime);
        var produced = await symbols.FindSymbol(root, pattern: "ClosureRoot.Root.Run", kind: "method", maxResponseBytes: 32768);
        AssertOwnerResult(produced, "Run");
        var replaced = false;
        var relationships = new RelationshipTools(runtime, null, null, (_, _) =>
        {
            File.Copy(replacementTransitive, transitivePath, overwrite: true);
            replaced = true;
            return Task.CompletedTask;
        });

        var result = await relationships.GetCallTree(root, ReadAnyHandoff(TextOf(produced)),
            direction: "outgoing", includeReferences: true, maxResponseBytes: 32768);

        Assert.True(replaced);
        Assert.True(result.IsError ?? false, TextOf(result));
        Assert.Contains("STALE_SNAPSHOT", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssemblyCallTree_UsesPinnedRootWhenTargetChangesBetweenRootScopeAndRootHandoffAcquisitions()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("ainet-closure-root-generation-");
        using var replacementFixture = TestTempDirectory.Create("ainet-closure-root-replacement-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "ClosureRootGenerationProbe", "namespace Probe; public sealed class Root { public int Run() => 1; }");
        var replacementPath = AssemblyTestHelper.EmitAssembly(replacementFixture, "ClosureRootGenerationProbe", "namespace Probe; public sealed class Root { public int Run() => 2; public int Added() => 3; }");
        var symbols = new SymbolTools(runtime);
        var produced = await symbols.FindSymbol(assemblyPath, pattern: "Root.Run", kind: "method", maxResponseBytes: 32768);
        AssertOwnerResult(produced, "Run");
        var graphSnapshot = ReadHeader(TextOf(produced), "snapshotId");
        var replaced = false;
        var relationships = new RelationshipTools(runtime, null, null, (_, _) =>
        {
            File.Copy(replacementPath, assemblyPath, overwrite: true);
            replaced = true;
            return Task.CompletedTask;
        });

        var result = await relationships.GetCallTree(assemblyPath, ReadAnyHandoff(TextOf(produced)),
            direction: "outgoing", includeReferences: true, maxResponseBytes: 32768);

        Assert.True(replaced);
        AssertOwnerResult(result, "Root.Run");
        Assert.Equal(graphSnapshot, ReadHeader(TextOf(result), "snapshotId"));
    }

    [Fact]
    public async Task AssemblyCallTree_RawClosureResolutionRejectsChangesBeforeHandoffReopen()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("ainet-closure-raw-handoff-");
        using var replacementFixture = TestTempDirectory.Create("ainet-closure-raw-replacement-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "ClosureRawProbe", "namespace Probe; public sealed class Root { public int Run() => 1; }");
        var replacementPath = AssemblyTestHelper.EmitAssembly(replacementFixture, "ClosureRawProbe", "namespace Probe; public sealed class Root { public int Run() => 2; public int Added() => 3; }");
        var replaced = false;
        var relationships = new RelationshipTools(runtime, null, null, null, _ =>
        {
            File.Copy(replacementPath, assemblyPath, overwrite: true);
            replaced = true;
            return Task.CompletedTask;
        });

        var result = await relationships.GetCallTree(assemblyPath, "Root.Run",
            direction: "outgoing", includeReferences: true, maxResponseBytes: 32768);

        Assert.True(replaced);
        Assert.True(result.IsError ?? false, TextOf(result));
        Assert.Contains("STALE_SNAPSHOT", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssemblyBodyBatch_UsesOneSnapshotAcrossItems()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("ainet-assembly-body-batch-");
        const string original = "namespace AssemblyBodyBatchProbe; public sealed class Probe { public int First() => 101; public int Second() => 101; }";
        const string replacement = "namespace AssemblyBodyBatchProbe; public sealed class Probe { public int First() => 202; public int Second() => 202; }";
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "AssemblyBodyBatchProbe", original);
        var symbols = new SymbolTools(runtime)
        {
            BeforeAssemblyBodyBatchItemForTesting = index =>
            {
                if (index == 1) AssemblyTestHelper.EmitAssembly(fixture, "AssemblyBodyBatchProbe", replacement);
            },
        };

        var result = await symbols.GetSymbolBody(assemblyPath,
            ["AssemblyBodyBatchProbe.Probe.First", "AssemblyBodyBatchProbe.Probe.Second"],
            maxResponseBytes: 32768, maxResponseTokens: 4096);

        AssertSuccessWithinBudget(result, 32768, 4096);
        var text = TextOf(result);
        Assert.Equal(2, text.Split("return 101;", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("return 202;", text, StringComparison.Ordinal);

        AssemblyTestHelper.EmitAssembly(fixture, "AssemblyBodyBatchProbe", original);
        var first = await symbols.FindSymbol(assemblyPath, pattern: "Probe.First", kind: "method", maxResponseBytes: 16384);
        var second = await symbols.FindSymbol(assemblyPath, pattern: "Probe.Second", kind: "method", maxResponseBytes: 16384);
        var handoffBatch = await symbols.GetSymbolBody(assemblyPath,
            [ReadAnyHandoff(TextOf(first)), ReadAnyHandoff(TextOf(second))],
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(handoffBatch, 32768, 4096);
        var handoffText = TextOf(handoffBatch);
        Assert.Equal(2, handoffText.Split("return 101;", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("return 202;", handoffText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssemblySkeletonBatch_UsesOneSnapshotAcrossHandoffs()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("ainet-assembly-skeleton-batch-");
        const string original = "namespace AssemblySkeletonBatchProbe; public sealed class Alpha { public int First() => 101; } public sealed class Beta { public int Second() => 101; }";
        const string replacement = "namespace AssemblySkeletonBatchProbe; public sealed class Alpha { public int First() => 202; } public sealed class Beta { public int Second() => 202; }";
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "AssemblySkeletonBatchProbe", original);
        var symbols = new SymbolTools(runtime);
        var structure = new StructureTools(runtime)
        {
            BeforeAssemblySkeletonItemForTesting = index =>
            {
                if (index == 1) AssemblyTestHelper.EmitAssembly(fixture, "AssemblySkeletonBatchProbe", replacement);
            },
        };
        var alpha = await symbols.FindSymbol(assemblyPath, pattern: "Alpha", kind: "class", maxResponseBytes: 16384);
        var beta = await symbols.FindSymbol(assemblyPath, pattern: "Beta", kind: "class", maxResponseBytes: 16384);
        var alphaHandle = ReadHandoff(TextOf(alpha), "class Alpha");
        var betaHandle = ReadHandoff(TextOf(beta), "class Beta");

        var result = await structure.GetFileSkeleton(assemblyPath, [alphaHandle, betaHandle], maxResponseBytes: 32768, maxResponseTokens: 4096);

        AssertSuccessWithinBudget(result, 32768, 4096);
        var text = TextOf(result);
        Assert.Contains("completeness=complete", text, StringComparison.Ordinal);
        Assert.Contains("### Alpha", text, StringComparison.Ordinal);
        Assert.Contains("### Beta", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposedRuntimeAssemblyHandoffsAreUnknownAndRepeatedDisposeKeepsFreshHandles()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var fixture = TestTempDirectory.Create("ainet-assembly-runtime-lifecycle-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "AssemblyRuntimeLifecycleProbe", """
            namespace AssemblyRuntimeLifecycleProbe;
            public sealed class Probe { public int Read() => 42; }
            """);

        var oldRuntime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var oldTools = new SymbolTools(oldRuntime);
        var oldResult = await oldTools.FindSymbol(assemblyPath, pattern: "Probe.Read", kind: "method", maxResponseBytes: 16384);
        AssertOwnerResult(oldResult, "Read");
        var oldHandoff = ReadAnyHandoff(TextOf(oldResult));
        await oldRuntime.DisposeAsync();

        await using var freshRuntime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var freshTools = new SymbolTools(freshRuntime);
        var staleResult = await freshTools.GetSymbolBody(assemblyPath, [oldHandoff], maxResponseBytes: 16384);
        AssertError(staleResult, "HANDOFF_UNKNOWN");

        var freshResult = await freshTools.FindSymbol(assemblyPath, pattern: "Probe.Read", kind: "method", maxResponseBytes: 16384);
        AssertOwnerResult(freshResult, "Read");
        var freshHandoff = ReadAnyHandoff(TextOf(freshResult));
        await oldRuntime.DisposeAsync();
        var freshBody = await freshTools.GetSymbolBody(assemblyPath, [freshHandoff], maxResponseBytes: 16384);
        AssertOwnerResult(freshBody, "Read");
    }

    [Fact]
    public async Task AssemblyClassStructureUsesDeclarationOrderBeforeCapAndContextAndMemberHandlesStayNavigable()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var structureTools = new StructureTools(runtime);
        var assemblyTools = new AssemblyTools(runtime);
        var symbolTools = new SymbolTools(runtime);
        var relationshipTools = new RelationshipTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-structure-contract-");
        const string source = """
            namespace StructureOrderProbe;
            public sealed class OrderProbe
            {
                public int Zulu()
                {
                    var first = 1;
                    var second = first + 1;
                    var third = second + 1;
                    var fourth = third + 1;
                    var fifth = fourth + 1;
                    var sixth = fifth + 1;
                    var seventh = sixth + 1;
                    var eighth = seventh + 1;
                    return eighth;
                }

                public int Alpha() => 42;
            }
            """;
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "StructureOrderProbe", source);

        var limited = await structureTools.GetClassStructure(assemblyPath, "StructureOrderProbe.OrderProbe", maxMembers: 1);
        AssertSuccessWithinBudget(limited, 16 * 1024, 4096);
        Assert.Contains("Zulu", TextOf(limited), StringComparison.Ordinal);
        Assert.DoesNotContain("Alpha", TextOf(limited), StringComparison.Ordinal);
        var zuluHandoff = ReadHandoff(TextOf(limited), "Zulu");
        var zuluBody = await symbolTools.GetSymbolBody(assemblyPath, [zuluHandoff]);
        AssertSuccessWithinBudget(zuluBody, 16 * 1024, 4096);
        Assert.Contains("Zulu", TextOf(zuluBody), StringComparison.Ordinal);

        var reconstructedNames = new List<string>();
        string? memberCursor = null;
        do
        {
            var page = await structureTools.GetClassStructure(assemblyPath, "StructureOrderProbe.OrderProbe", maxMembers: 1,
                resultCursor: memberCursor);
            AssertSuccessWithinBudget(page, 16 * 1024, 4096);
            using var pageDocument = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(page)));
            var root = pageDocument.RootElement;
            reconstructedNames.AddRange(root.GetProperty("members").EnumerateArray().Select(member => member.GetProperty("name").GetString()!));
            memberCursor = root.TryGetProperty("resultCursor", out var cursor) && cursor.ValueKind == System.Text.Json.JsonValueKind.String
                ? cursor.GetString() : null;
        } while (memberCursor is not null);
        Assert.Equal(new[] { "Zulu", "Alpha" }, reconstructedNames);

        var complete = await structureTools.GetClassStructure(assemblyPath, "StructureOrderProbe.OrderProbe", maxMembers: 50);
        AssertSuccessWithinBudget(complete, 16 * 1024, 4096);
        AssertDeclarationOrder(TextOf(complete));

        var sourceRoot = Path.Combine(fixture.DirectoryPath, "source");
        Directory.CreateDirectory(sourceRoot);
        var sourceSolution = Path.Combine(sourceRoot, "OrderProbe.slnx");
        var sourceProject = Path.Combine(sourceRoot, "OrderProbe.csproj");
        await File.WriteAllTextAsync(sourceSolution, "<Solution><Project Path=\"OrderProbe.csproj\" /></Solution>");
        await File.WriteAllTextAsync(sourceProject,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "OrderProbe.cs"), source);
        var sourceStructure = await structureTools.GetClassStructure(sourceSolution, "StructureOrderProbe.OrderProbe", maxMembers: 50,
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(sourceStructure, 32768, 4096);
        Assert.Contains("snapshotId=source:", TextOf(sourceStructure), StringComparison.Ordinal);
        Assert.Contains("analyzedScope=classStructure(symbol=StructureOrderProbe.OrderProbe, scope=all", TextOf(sourceStructure), StringComparison.Ordinal);
        AssertDeclarationOrder(TextOf(sourceStructure));
        Assert.Equal(ReadMemberNames(TextOf(sourceStructure)), ReadMemberNames(TextOf(complete)));
        var sourceNamespaceTree = await structureTools.GetNamespaceTree(sourceSolution,
            namespacePrefix: "StructureOrderProbe", maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(sourceNamespaceTree, 32768, 4096);
        var sourceNamespaceHandle = ReadAnyHandoff(TextOf(sourceNamespaceTree));
        var sourceNamespaceBody = await symbolTools.GetSymbolBody(sourceSolution, [sourceNamespaceHandle], maxResponseBytes: 32768,
            maxResponseTokens: 4096);
        AssertSuccessWithinBudget(sourceNamespaceBody, 32768, 4096);
        Assert.Contains("OrderProbe", TextOf(sourceNamespaceBody), StringComparison.Ordinal);

        var context = await relationshipTools.GetContext(assemblyPath, "StructureOrderProbe.OrderProbe", ["members"],
            maxResults: 50, maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(context, 65536, 4096);
        var contextText = TextOf(context);
        Assert.Contains("Zulu", contextText, StringComparison.Ordinal);
        Assert.Contains("Alpha", contextText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetContextAggregatesLaterAssemblyCallerErrorsForOwnerAndReferenceScopes()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-assembly-context-section-error-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "AssemblyContextErrorProbe", """
            namespace AssemblyContextErrorProbe;
            public sealed class Probe { public int Read() => 1; }
            """);
        relationships.BeforeContextSectionForTesting = section =>
        {
            if (section == "callers") throw new InvalidOperationException("forced assembly caller failure");
        };

        foreach (var includeReferences in new[] { false, true })
        {
            var result = await relationships.GetContext(assemblyPath, "AssemblyContextErrorProbe.Probe.Read", ["body", "callers"],
                includeReferences: includeReferences, maxResponseBytes: 32768, maxResponseTokens: 4096);
            Assert.True(result.IsError == true, TextOf(result));
            using var document = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(result)));
            var root = document.RootElement;
            Assert.Equal("error", root.GetProperty("status").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("target").GetProperty("snapshotId").GetString()));
            var sections = root.GetProperty("sections").EnumerateArray().ToArray();
            Assert.Equal(new[] { "body", "callers" }, sections.Select(section => section.GetProperty("name").GetString()));
            Assert.Equal("partial", sections[0].GetProperty("status").GetString());
            Assert.Equal("error", sections[1].GetProperty("status").GetString());
            Assert.False(sections[1].GetProperty("analysisComplete").GetBoolean());
            Assert.Equal("CONTEXT_SECTION_FAILED", sections[1].GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task GetContextAssemblyMemberCursorRetainsOriginalReferenceClosureSelection()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-assembly-context-closure-cursor-");
        var memberDeclarations = string.Join(Environment.NewLine, Enumerable.Range(0, 6)
            .Select(index => $"    public int Member{index:D2}() => {index};"));
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "AssemblyClosureCursorProbe", $$"""
            namespace AssemblyClosureCursorProbe;
            public sealed class Probe
            {
            {{memberDeclarations}}
            }
            """);
        var invoked = new List<string>();
        var openedOwners = new List<string>();
        var activeAccessCounts = new List<int>();
        relationships.BeforeContextSectionForTesting = section =>
        {
            invoked.Add(section);
            activeAccessCounts.Add(runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath));
        };
        relationships.BeforeAssemblyContextOwnerOpenForTesting = openedOwners.Add;

        var first = await relationships.GetContext(assemblyPath, "AssemblyClosureCursorProbe.Probe", ["members", "callers"],
            includeReferences: true, maxResults: 1, maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertSuccessWithinBudget(first, 65536, 8192);
        using var firstDocument = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(first)));
        var firstRoot = firstDocument.RootElement;
        Assert.Equal("partial", firstRoot.GetProperty("status").GetString());
        var firstSections = firstRoot.GetProperty("sections").EnumerateArray().ToArray();
        Assert.Equal(new[] { "members", "callers" }, firstSections.Select(section => section.GetProperty("name").GetString()));
        Assert.Equal("partial", firstSections[0].GetProperty("status").GetString());
        var cursor = firstSections[0].GetProperty("resultCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));
        Assert.Equal(new[] { "members", "callers" }, invoked);
        Assert.Equal(new[] { 1, 1 }, activeAccessCounts);
        Assert.Equal(0, runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath));
        Assert.Equal(1, openedOwners.Count(path => string.Equals(path, Path.GetFullPath(assemblyPath), StringComparison.OrdinalIgnoreCase)));

        invoked.Clear();
        openedOwners.Clear();
        activeAccessCounts.Clear();
        var continuation = await relationships.GetContext(assemblyPath, "AssemblyClosureCursorProbe.Probe", ["members", "callers"],
            includeReferences: true, maxResults: 1, resultCursor: cursor, maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertSuccessWithinBudget(continuation, 65536, 8192);
        using var continuationDocument = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(continuation)));
        var continuationRoot = continuationDocument.RootElement;
        Assert.Equal("members", continuationRoot.GetProperty("continuationSection").GetString());
        Assert.Equal(new[] { "members" }, continuationRoot.GetProperty("sections").EnumerateArray()
            .Select(section => section.GetProperty("name").GetString()));
        Assert.Equal(new[] { "members" }, invoked);
        Assert.Equal(new[] { 1 }, activeAccessCounts);
        Assert.Equal(0, runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath));
        Assert.Equal(1, openedOwners.Count(path => string.Equals(path, Path.GetFullPath(assemblyPath), StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GetContextClosureHookFailureReleasesResidentHandoffLease()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("ainet-context-closure-hook-lease-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "ContextClosureHookLeaseProbe", "namespace Probe; public sealed class Target { public int Run() => 1; }");
        var symbols = new SymbolTools(runtime);
        var discovered = await symbols.FindSymbol(assemblyPath, pattern: "Target.Run", kind: "method", maxResponseBytes: 32768);
        AssertOwnerResult(discovered, "Target.Run");
        Assert.Equal(0, runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath));

        var hookCalled = false;
        var relationships = new RelationshipTools(runtime, null, null, (_, _) =>
        {
            hookCalled = true;
            return Task.FromException(new InvalidOperationException("forced closure handoff callback failure"));
        });
        var result = await relationships.GetContext(assemblyPath,
            ReadAnyHandoff(TextOf(discovered)), ["body", "callers"], includeReferences: true,
            maxResponseBytes: 32768, maxResponseTokens: 4096);

        Assert.True(hookCalled, TextOf(result));
        Assert.True(result.IsError == true, TextOf(result));
        Assert.Equal(0, runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath));
    }

    [Fact]
    public async Task AssemblyNamespaceDepthRecoveryUsesAssemblyPrefixWithoutRequestingSourceProject()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("assembly-namespace-recovery-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "NamespaceRecoveryProbe", "namespace NamespaceRecoveryProbe.One.Two.Three; public sealed class Target { }");
        var structure = new StructureTools(runtime);

        var result = await structure.GetNamespaceTree(assemblyPath, namespacePrefix: "NamespaceRecoveryProbe", depth: 1,
            maxResponseBytes: 16384, maxResponseTokens: 2048);

        AssertSuccessWithinBudget(result, 16384, 2048);
        var text = TextOf(result);
        Assert.Contains("completeness=truncated", text, StringComparison.Ordinal);
        Assert.Contains("nextAction:", text, StringComparison.Ordinal);
        Assert.Contains("namespacePrefix", text, StringComparison.Ordinal);
        Assert.Contains("increase depth", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("select a project", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AssemblyBodyRecoveryDistinguishesWrongOwnerStaleAndEvictedSessions()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("assembly-body-recovery-");
        const string original = "namespace BodyRecoveryProbe; public sealed class Target { public int Read() { var value = 1; return value; } }";
        const string replacement = "namespace BodyRecoveryProbe; public sealed class Target { public int Read() { var value = 2; return value; } }";
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "BodyRecoveryProbe", original);
        var otherAssemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "OtherBodyRecoveryProbe", "namespace OtherBodyRecoveryProbe; public sealed class Other { public int Read() => 0; }");
        var symbols = new SymbolTools(runtime);
        var relationships = new RelationshipTools(runtime);
        var discovery = await symbols.FindSymbol(assemblyPath, pattern: "Target.Read", kind: "method", maxResponseBytes: 16384);
        AssertOwnerResult(discovery, "Read");
        var originalHandle = ReadAnyHandoff(TextOf(discovery));

        var firstWindow = await symbols.GetSymbolBody(assemblyPath, [originalHandle], startLine: 1, endLine: 1,
            maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(firstWindow, 16384, 2048);
        Assert.Contains("Next body window: startLine=2, maxBodyLines=1; omit endLine", TextOf(firstWindow), StringComparison.Ordinal);
        var secondWindow = await symbols.GetSymbolBody(assemblyPath, [originalHandle], startLine: 2, maxBodyLines: 1,
            maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(secondWindow, 16384, 2048);
        Assert.Contains("Resolution status: resolved", TextOf(secondWindow), StringComparison.Ordinal);

        var wrongOwner = await symbols.GetSymbolBody(otherAssemblyPath, [originalHandle], maxResponseBytes: 16384);
        AssertErrorWithinBudget(wrongOwner, "TARGET_MISMATCH", 16384, 4096);
        Assert.Contains("Resolution status: failed (TARGET_MISMATCH)", TextOf(wrongOwner), StringComparison.Ordinal);
        Assert.Contains("ownerTargetPath", TextOf(wrongOwner), StringComparison.Ordinal);

        AssemblyTestHelper.EmitAssembly(fixture, "BodyRecoveryProbe", replacement);
        var stale = await symbols.GetSymbolBody(assemblyPath, [originalHandle], maxResponseBytes: 16384);
        AssertErrorWithinBudget(stale, "STALE_SNAPSHOT", 16384, 4096);
        Assert.Contains("find_symbol", TextOf(stale), StringComparison.Ordinal);
        var mixedFailures = await symbols.GetSymbolBody(assemblyPath, [originalHandle, "h:aaaa"], maxResponseBytes: 16384, maxResponseTokens: 4096);
        AssertErrorWithinBudget(mixedFailures, "STALE_SNAPSHOT", 16384, 4096);
        Assert.Contains("Resolution status: failed (STALE_SNAPSHOT)", TextOf(mixedFailures), StringComparison.Ordinal);
        Assert.Contains("Resolution status: failed (HANDOFF_UNKNOWN)", TextOf(mixedFailures), StringComparison.Ordinal);
        Assert.Contains("Repeat find_symbol against the current assembly snapshot", TextOf(mixedFailures), StringComparison.Ordinal);
        Assert.Contains("Find the symbol again using find_symbol", TextOf(mixedFailures), StringComparison.Ordinal);

        var currentDiscovery = await symbols.FindSymbol(assemblyPath, pattern: "Target.Read", kind: "method", maxResponseBytes: 16384);
        AssertOwnerResult(currentDiscovery, "Read");
        var currentHandle = ReadAnyHandoff(TextOf(currentDiscovery));
        var otherDiscovery = await symbols.FindSymbol(otherAssemblyPath, pattern: "Other.Read", kind: "method", maxResponseBytes: 16384);
        AssertOwnerResult(otherDiscovery, "Read");
        var otherHandle = ReadAnyHandoff(TextOf(otherDiscovery));
        await AssemblyAnalysisSessionRegistry.Default.ExpireIdleSessionsAsync(DateTime.UtcNow.AddMinutes(11), assemblyPath);

        var mixedEviction = await symbols.GetSymbolBody(assemblyPath, [currentHandle, "BodyRecoveryProbe.Target.Read"],
            maxResponseBytes: 16384, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(mixedEviction, 16384, 4096);
        Assert.Contains("Resolution status: failed (HANDOFF_OWNER_UNRESIDENT)", TextOf(mixedEviction), StringComparison.Ordinal);
        Assert.Contains("Resolution status: resolved", TextOf(mixedEviction), StringComparison.Ordinal);
        Assert.Contains("return 2;", TextOf(mixedEviction), StringComparison.Ordinal);
        var otherOwnerStillResident = await symbols.GetSymbolBody(otherAssemblyPath, [otherHandle], maxResponseBytes: 16384);
        AssertOwnerResult(otherOwnerStillResident, "Read");

        await AssemblyAnalysisSessionRegistry.Default.ExpireIdleSessionsAsync(DateTime.UtcNow.AddMinutes(11), assemblyPath);
        var unresidentSourceContext = await relationships.GetContext(assemblyPath, currentHandle, ["body"],
            maxResponseBytes: 16384, maxResponseTokens: 4096);
        AssertErrorWithinBudget(unresidentSourceContext, NavigationErrorCodes.HandoffOwnerUnresident, 16384, 4096);
        var unresidentClosureContext = await relationships.GetContext(assemblyPath, currentHandle, ["body", "callers"],
            includeReferences: true, maxResponseBytes: 16384, maxResponseTokens: 4096);
        AssertErrorWithinBudget(unresidentClosureContext, NavigationErrorCodes.HandoffOwnerUnresident, 16384, 4096);
        Assert.DoesNotContain("STALE_SNAPSHOT", TextOf(unresidentSourceContext), StringComparison.Ordinal);
        Assert.DoesNotContain("STALE_SNAPSHOT", TextOf(unresidentClosureContext), StringComparison.Ordinal);
        var evicted = await symbols.GetSymbolBody(assemblyPath, [currentHandle], maxResponseBytes: 16384, maxResponseTokens: 4096);
        AssertErrorWithinBudget(evicted, NavigationErrorCodes.HandoffOwnerUnresident, 16384, 4096);
        Assert.Contains("Repeat the original discovery query on the owner target", TextOf(evicted), StringComparison.Ordinal);
        Assert.DoesNotContain("STALE_SNAPSHOT", TextOf(evicted), StringComparison.Ordinal);

        var rediscovered = await symbols.FindSymbol(assemblyPath, pattern: "Target.Read", kind: "method", maxResponseBytes: 16384);
        AssertOwnerResult(rediscovered, "Read");
        var rediscoveredHandle = ReadAnyHandoff(TextOf(rediscovered));
        var recovered = await symbols.GetSymbolBody(assemblyPath, [rediscoveredHandle], maxResponseBytes: 16384);
        AssertOwnerResult(recovered, "Read");
        Assert.Contains("return 2;", TextOf(recovered), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssemblyNavigationHandlersReturnOwnerResultsAcrossAllSeventeenRoutes()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var symbols = new SymbolTools(runtime);
        var structure = new StructureTools(runtime);
        var relationships = new RelationshipTools(runtime);
        var assemblies = new AssemblyTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-routes-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "AssemblyRouteProbe", """
            namespace AssemblyRouteProbe;
            public interface IReadable { int Read(); string Label { get; } }
            public class BaseProbe { public virtual int Value() => 1; public virtual string Label => "base"; }
            public sealed class Probe : BaseProbe, IReadable
            {
                public override int Value() => 2;
                public override string Label => "probe";
                public int Read() => Value();
                public int Entry() => Read();
            }
            public static class ProbeExtensions
            {
                public static int Twice(this Probe value) => value.Read() * 2;
                public static int Thrice(this Probe value) => value.Read() * 3;
            }
            """);

        var foundEntry = await symbols.FindSymbol(assemblyPath, pattern: "Entry", kind: "method", maxResponseBytes: 32768);
        AssertOwnerResult(foundEntry, "Entry");
        var entryHandle = ReadAnyHandoff(TextOf(foundEntry));
        var foundType = await symbols.FindSymbol(assemblyPath, pattern: "AssemblyRouteProbe.Probe", kind: "class", maxResponseBytes: 32768);
        AssertOwnerResult(foundType, "Probe");
        var typeHandle = ReadHandoff(TextOf(foundType), "class Probe");
        var foundInterface = await symbols.FindSymbol(assemblyPath, pattern: "IReadable", kind: "interface", maxResponseBytes: 32768);
        AssertOwnerResult(foundInterface, "IReadable");
        var interfaceHandle = ReadAnyHandoff(TextOf(foundInterface));
        var foundRead = await symbols.FindSymbol(assemblyPath, pattern: "Probe.Read", kind: "method", maxResponseBytes: 32768);
        AssertOwnerResult(foundRead, "Probe.Read");
        var readHandle = ReadHandoffByDocumentationId(TextOf(foundRead),
            "M:AssemblyRouteProbe.Probe.Read", assemblyPath);

        AssertOwnerResult(await symbols.GetSymbolBody(assemblyPath, [entryHandle], maxResponseBytes: 32768), "Entry");
        var inspectedProbe = await assemblies.InspectAssembly(assemblyPath, typeName: "AssemblyRouteProbe.Probe",
            exactTypeName: true, maxResponseBytes: 32768);
        AssertOwnerResult(inspectedProbe, "AssemblyRouteProbe.Probe");
        var probeHandoff = ReadAnyHandoff(TextOf(inspectedProbe));
        var skeleton = await structure.GetFileSkeleton(assemblyPath, [probeHandoff], maxResponseBytes: 32768);
        var skeletonText = TextOf(skeleton);
        AssertOwnerResult(skeleton, "Probe");
        Assert.Contains("## AssemblyRouteProbe", skeletonText, StringComparison.Ordinal);
        Assert.Contains("### Probe ", skeletonText, StringComparison.Ordinal);
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, skeleton);
        var readSkeletonLine = skeletonText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.Contains("Read()", StringComparison.Ordinal));
        Assert.True(readSkeletonLine is not null, $"The selected Probe skeleton did not contain Read(). Skeleton:\n{skeletonText}");
        Assert.Contains("handoffId: `", readSkeletonLine!, StringComparison.Ordinal);
        var readFromSkeleton = ReadAnyHandoff(readSkeletonLine!);
        AssertOwnerResult(await symbols.GetSymbolBody(assemblyPath, [readFromSkeleton], maxResponseBytes: 32768), "Read");
        AssertOwnerResult(await structure.GetClassStructure(assemblyPath, typeHandle, maxResponseBytes: 32768), "Entry");
        var namespaceTree = await structure.GetNamespaceTree(assemblyPath, namespacePrefix: "AssemblyRouteProbe", maxResponseBytes: 32768);
        AssertOwnerResult(namespaceTree, "Probe");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, namespaceTree);
        var assemblyInventory = new List<string>();
        string? assemblyInventoryCursor = null;
        var assemblyInventoryPages = 0;
        do
        {
            var page = await structure.GetNamespaceTree(assemblyPath, namespacePrefix: "AssemblyRouteProbe", maxResults: 2,
                resultCursor: assemblyInventoryCursor, maxResponseBytes: 16384, maxResponseTokens: 2048);
            AssertSuccessWithinBudget(page, 16384, 2048);
            using var document = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(page)));
            var root = document.RootElement;
            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var value = item.TryGetProperty("fullName", out var fullName) ? fullName.GetString() : item.GetProperty("name").GetString();
                assemblyInventory.Add($"{item.GetProperty("kind").GetString()}:{value}");
            }
            assemblyInventoryCursor = root.TryGetProperty("resultCursor", out var resultCursor)
                && resultCursor.ValueKind == System.Text.Json.JsonValueKind.String ? resultCursor.GetString() : null;
            assemblyInventoryPages++;
        } while (assemblyInventoryCursor is not null);
        Assert.Equal(3, assemblyInventoryPages);
        Assert.Equal(5, assemblyInventory.Count);
        Assert.Equal(5, assemblyInventory.Distinct(StringComparer.Ordinal).Count());

        var callTree = await relationships.GetCallTree(assemblyPath, entryHandle, direction: "outgoing", maxResponseBytes: 32768);
        AssertOwnerResult(callTree, "Entry");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, callTree);
        var mermaidCallTree = await relationships.GetCallTree(assemblyPath, entryHandle, direction: "outgoing",
            format: "mermaid", maxResponseBytes: 32768);
        AssertOwnerResult(mermaidCallTree, "flowchart TD");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, mermaidCallTree);
        var probeReferences = await relationships.FindReferences(assemblyPath, readHandle, maxResponseBytes: 32768);
        AssertOwnerResult(probeReferences, "Probe.Read");
        using (var probeReferencesDocument = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(probeReferences))))
        {
            var references = probeReferencesDocument.RootElement.GetProperty("references").EnumerateArray().ToArray();
            Assert.NotEmpty(references);
            Assert.All(references, reference => Assert.Equal("Probe.Read",
                reference.GetProperty("reachedFromSymbolName").GetString()));
            Assert.Contains(references, reference => reference.GetProperty("enclosingSymbolName").GetString() == "Probe.Entry");
        }
        var hierarchy = await relationships.GetTypeHierarchy(assemblyPath, typeHandle, maxResponseBytes: 32768);
        AssertOwnerResult(hierarchy, "Probe");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, hierarchy);
        var implementations = await relationships.FindImplementations(assemblyPath, interfaceHandle, maxResponseBytes: 32768);
        AssertOwnerResult(implementations, "Probe");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, implementations);
        var methodOverrides = await relationships.FindImplementations(assemblyPath, "M:AssemblyRouteProbe.BaseProbe.Value", maxResponseBytes: 32768);
        AssertOwnerResult(methodOverrides, "Value");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, methodOverrides);
        var propertyOverrides = await relationships.FindImplementations(assemblyPath, "P:AssemblyRouteProbe.BaseProbe.Label", maxResponseBytes: 32768);
        AssertOwnerResult(propertyOverrides, "Label");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, propertyOverrides);
        var impact = await relationships.GetImpact(assemblyPath, "M:AssemblyRouteProbe.Probe.Read", maxResponseBytes: 32768);
        AssertOwnerResult(impact, "Entry");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, impact);
        var impactOpaque = await relationships.GetImpact(assemblyPath, readHandle, maxResponseBytes: 32768);
        var impactQualified = await relationships.GetImpact(assemblyPath, "AssemblyRouteProbe.Probe.Read", includeReferences: false, maxResponseBytes: 32768);
        var impactPosition = await relationships.GetImpact(assemblyPath, "Probe.cs:" + ReadPosition(TextOf(foundRead)), maxResponseBytes: 32768);
        Assert.Equal(TextOf(impact), TextOf(impactOpaque));
        Assert.Equal(TextOf(impact), TextOf(impactQualified));
        Assert.Equal(TextOf(impact), TextOf(impactPosition));
        var dependency = await relationships.DependencyGraph(assemblyPath, symbolIdentifier: typeHandle, maxResponseBytes: 32768);
        AssertOwnerResult(dependency, "Probe");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, dependency);
        AssertOwnerResult(await relationships.ResolveTypeOrigin(assemblyPath, typeName: "AssemblyRouteProbe.Probe", maxResponseBytes: 32768), "AssemblyRouteProbe");

        var selectedContext = await relationships.GetContext(assemblyPath, "AssemblyRouteProbe.Probe", ["body", "members", "callers"],
            maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(selectedContext, 65536, 4096);
        Assert.Contains("AssemblyRouteProbe.Probe", TextOf(selectedContext), StringComparison.Ordinal);
        var inspectDefault = await assemblies.InspectAssembly(assemblyPath, typeName: "Probe", maxResponseBytes: 32768);
        var inspectZero = await assemblies.InspectAssembly(assemblyPath, typeName: "Probe", maxResults: 0, maxResponseBytes: 32768);
        AssertOwnerResult(inspectDefault, "Probe");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, inspectDefault);
        Assert.Equal(TextOf(inspectDefault), TextOf(inspectZero));
        var searchDefault = await assemblies.SearchAssembly(assemblyPath, pattern: "Entry", declarationOnly: true,
            kind: "method", maxResponseBytes: 32768);
        var searchZero = await assemblies.SearchAssembly(assemblyPath, pattern: "Entry", declarationOnly: true,
            kind: "method", maxResults: 0, maxFiles: 0, maxResponseBytes: 32768);
        AssertOwnerResult(searchDefault, "Entry");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, searchDefault);
        Assert.Equal(TextOf(searchDefault), TextOf(searchZero));
        var extensionDefault = await assemblies.FindAssemblyExtensions(assemblyPath, receiverType: "", maxResponseBytes: 32768);
        var extensionZero = await assemblies.FindAssemblyExtensions(assemblyPath, receiverType: "", maxResults: 0, maxResponseBytes: 32768);
        AssertOwnerResult(extensionDefault, "Twice");
        Assert.Contains("Thrice", TextOf(extensionDefault), StringComparison.Ordinal);
        Assert.Equal(TextOf(extensionDefault), TextOf(extensionZero));
        var filteredExtensions = await assemblies.FindAssemblyExtensions(assemblyPath, receiverType: "AssemblyRouteProbe.Probe",
            extensionName: "Twice", @namespace: "AssemblyRouteProbe", maxResults: 1, maxResponseBytes: 32768);
        AssertOwnerResult(filteredExtensions, "Twice");
        Assert.DoesNotContain("Thrice", TextOf(filteredExtensions), StringComparison.Ordinal);
        var cappedExtensions = await assemblies.FindAssemblyExtensions(assemblyPath, receiverType: "AssemblyRouteProbe.Probe",
            maxResults: 1, maxResponseBytes: 32768);
        AssertOwnerPage(TextOf(cappedExtensions));
        Assert.Contains("truncated", TextOf(cappedExtensions), StringComparison.Ordinal);
        AssertError(await relationships.GetContext(assemblyPath, "AssemblyRouteProbe.Probe", ["tests"], maxResponseBytes: 16384), "INVALID_ARGUMENT");
        AssertError(await relationships.GetContext(assemblyPath, "AssemblyRouteProbe.Probe", ["body"], includeReferences: false,
            maxResponseBytes: 16384), "INVALID_ARGUMENT");
        AssertError(await symbols.GetSymbolBody(assemblyPath, ["h:zzzz"], maxResponseBytes: 16384), "HANDOFF_UNKNOWN");
        AssertError(await structure.GetClassStructure(assemblyPath, "h:zzzz", maxResponseBytes: 16384), "HANDOFF_UNKNOWN");
        AssertError(await relationships.GetTypeHierarchy(assemblyPath, "h:zzzz", maxResponseBytes: 16384), "HANDOFF_UNKNOWN");
        AssertError(await relationships.FindImplementations(assemblyPath, "h:zzzz", maxResponseBytes: 16384), "HANDOFF_UNKNOWN");
        AssertError(await assemblies.SearchAssembly(assemblyPath, pattern: "(", isRegex: true, maxResponseBytes: 16384), "INVALID_ARGUMENT");
        AssertError(await assemblies.SearchAssembly(assemblyPath, pattern: null, maxResponseBytes: 16384), "INVALID_ARGUMENT");

        Func<int, int?, Task<CallToolResult>>[] budgetProjections =
        [
            (bytes, tokens) => symbols.FindSymbol(assemblyPath, pattern: "Entry", kind: "method", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => symbols.GetSymbolBody(assemblyPath, [entryHandle], maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => structure.GetFileSkeleton(assemblyPath, [probeHandoff], maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => structure.GetClassStructure(assemblyPath, typeHandle, maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => structure.GetNamespaceTree(assemblyPath, namespacePrefix: "AssemblyRouteProbe", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.GetCallTree(assemblyPath, entryHandle, direction: "outgoing", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.FindReferences(assemblyPath, "M:AssemblyRouteProbe.Probe.Read", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.GetTypeHierarchy(assemblyPath, typeHandle, maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.FindImplementations(assemblyPath, interfaceHandle, maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.GetImpact(assemblyPath, "M:AssemblyRouteProbe.Probe.Read", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.DependencyGraph(assemblyPath, symbolIdentifier: typeHandle, maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.ResolveTypeOrigin(assemblyPath, typeName: "AssemblyRouteProbe.Probe", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.GetContext(assemblyPath, "AssemblyRouteProbe.Probe", ["body"], maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => assemblies.InspectAssembly(assemblyPath, maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => assemblies.SearchAssembly(assemblyPath, pattern: "Probe", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => assemblies.FindAssemblyExtensions(assemblyPath, receiverType: "", maxResponseBytes: bytes, maxResponseTokens: tokens),
        ];
        foreach (var projection in budgetProjections)
        {
            await AssertRecoverableProjectionAsync(projection, 512, 4096);
            await AssertRecoverableProjectionAsync(projection, 65536, 512);
        }
    }

    [Fact]
    public async Task AssemblyLockedTargetReturnsTypedErrorAndRecoversAfterUnlock()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var assemblies = new AssemblyTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-lock-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "LockedAssemblyProbe",
            "namespace LockedAssemblyProbe; public sealed class Probe { public int Read() => 1; }");
        CallToolResult locked;
        await using (new FileStream(assemblyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            locked = await assemblies.InspectAssembly(assemblyPath, typeName: "Probe", maxResponseBytes: 24576);
        }

        AssertError(locked, "TARGET_UNREADABLE");
        var recovered = await assemblies.InspectAssembly(assemblyPath, typeName: "Probe", maxResponseBytes: 24576);
        AssertOwnerResult(recovered, "Probe");
    }

    [Fact]
    public async Task AssemblyZeroByteDefaultsMatchPublishedBudgetsForLargeResults()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var assemblies = new AssemblyTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-zero-bytes-");
        var suffix = new string('T', 120);
        var source = new StringBuilder("namespace ZeroByteProbe;\n");
        for (var index = 0; index < 400; index++)
        {
            source.Append("public sealed class Type").Append(index.ToString("D3")).Append('_').Append(suffix).AppendLine(" {");
            source.Append("public string NeedleBudget").Append(index.ToString("D3"))
                .Append("() => \"").Append('x', 600).AppendLine("\";");
            source.AppendLine("}");
        }
        source.AppendLine("public static class Extensions {");
        for (var index = 0; index < 100; index++)
        {
            source.Append("public static int ExtensionBudget").Append(index.ToString("D3")).Append('_').Append(suffix)
                .Append("(this Type000_").Append(suffix).Append(" receiver) => ").Append(index).AppendLine(";");
        }
        source.AppendLine("}");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "ZeroByteProbe", source.ToString());

        var inspectDefault = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.InspectAssembly(
            assemblyPath, maxResults: 42, maxResponseTokens: tokens, continuationToken: continuation), 24576, 16000);
        var inspectZero = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.InspectAssembly(
            assemblyPath, maxResults: 42, maxResponseBytes: 0, maxResponseTokens: tokens, continuationToken: continuation), 24576, 16000);
        Assert.Contains("Type000", inspectDefault.Text, StringComparison.Ordinal);
        Assert.Equal(BodyOf(inspectDefault.FirstPage), BodyOf(inspectZero.FirstPage));
        Assert.Equal(inspectDefault.Text, inspectZero.Text);
        Assert.InRange(Encoding.UTF8.GetByteCount(inspectDefault.FirstPage), 16 * 1024 + 1, 24 * 1024);

        var searchDefault = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.SearchAssembly(
            assemblyPath, pattern: "NeedleBudget", declarationOnly: true, kind: "method", maxResults: 30,
            maxResponseTokens: tokens, continuationToken: continuation), 24576, 16000);
        var searchZero = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.SearchAssembly(
            assemblyPath, pattern: "NeedleBudget", declarationOnly: true, kind: "method", maxResults: 30,
            maxResponseBytes: 0, maxResponseTokens: tokens, continuationToken: continuation), 24576, 16000);
        Assert.Contains("NeedleBudget", searchDefault.Text, StringComparison.Ordinal);
        Assert.Equal(BodyOf(searchDefault.FirstPage), BodyOf(searchZero.FirstPage));
        Assert.Equal(searchDefault.Text, searchZero.Text);
        Assert.InRange(Encoding.UTF8.GetByteCount(searchDefault.FirstPage), 16 * 1024 + 1, 24 * 1024);

        var extensionsDefault = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.FindAssemblyExtensions(
            assemblyPath, receiverType: "", maxResults: 50, maxResponseTokens: tokens, continuationToken: continuation), 16384, 16000);
        var extensionsZero = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.FindAssemblyExtensions(
            assemblyPath, receiverType: "", maxResults: 50, maxResponseBytes: 0, maxResponseTokens: tokens,
            continuationToken: continuation), 16384, 16000);
        Assert.Contains("ExtensionBudget000", extensionsDefault.Text, StringComparison.Ordinal);
        Assert.Equal(BodyOf(extensionsDefault.FirstPage), BodyOf(extensionsZero.FirstPage));
        Assert.Equal(extensionsDefault.Text, extensionsZero.Text);
        Assert.InRange(Encoding.UTF8.GetByteCount(extensionsDefault.FirstPage), 8 * 1024 + 1, 16 * 1024);

    }

    [Fact]
    public async Task AssemblyInspectAndSearchHandlersKeepDomainCursorsSeparateFromOuterPages()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var assemblies = new AssemblyTools(runtime);
        var symbols = new SymbolTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-domain-pages-");
        var source = "namespace DomainPageProbe;\n" + string.Join("\n", Enumerable.Range(0, 8).Select(index =>
            $"public sealed class Probe{index} {{ {string.Join(" ", Enumerable.Range(0, 24).Select(member => $"public int Needle{index}_{member}() => {index + member};"))} }}"));
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "DomainPageProbe", source);
        var foreignPath = AssemblyTestHelper.EmitAssembly(fixture, "ForeignDomainPageProbe", "public sealed class ForeignProbe { }");

        var inspectFirst = await assemblies.InspectAssembly(assemblyPath, maxResults: 1,
            includeReferences: false, maxResponseBytes: 65536, maxResponseTokens: 16000);
        AssertOwnerResultWithinBudget(inspectFirst, "Probe0", 65536, 16000);
        using (var inspectJson = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(inspectFirst))))
        {
            Assert.True(inspectJson.RootElement.TryGetProperty("resultCursor", out _),
                "Domain continuation belongs in resultCursor, separate from outer continuationToken pages.");
            Assert.False(inspectJson.RootElement.TryGetProperty("continuationToken", out _));
        }
        var inspectCursor = ReadDomainCursor(TextOf(inspectFirst));
        var snapshotId = ReadHeader(TextOf(inspectFirst), "snapshotId");
        Assert.StartsWith("types(namespace=*", ReadHeader(TextOf(inspectFirst), "analyzedScope"), StringComparison.Ordinal);
        Assert.Contains("analysisCompleteness=complete", TextOf(inspectFirst), StringComparison.Ordinal);
        Assert.Contains("resultContinuation=available", TextOf(inspectFirst), StringComparison.Ordinal);
        Assert.Contains("omissions=none", TextOf(inspectFirst), StringComparison.Ordinal);
        var inspectNext = await assemblies.InspectAssembly(assemblyPath, maxResults: 1,
            includeReferences: false, resultCursor: inspectCursor, maxResponseBytes: 65536, maxResponseTokens: 16000);
        AssertOwnerResultWithinBudget(inspectNext, "Probe1", 65536, 16000);
        Assert.Equal(snapshotId, ReadHeader(TextOf(inspectNext), "snapshotId"));
        var inspectReplay = await assemblies.InspectAssembly(assemblyPath, maxResults: 1,
            includeReferences: false, resultCursor: inspectCursor, maxResponseBytes: 65536, maxResponseTokens: 16000);
        Assert.Equal(TextOf(inspectNext), TextOf(inspectReplay));
        AssertError(await assemblies.InspectAssembly(assemblyPath, typeName: "Changed", maxResults: 1,
            includeReferences: false, resultCursor: inspectCursor, maxResponseBytes: 16384), "RESULT_CURSOR_ARGUMENT_MISMATCH");
        AssertError(await assemblies.InspectAssembly(foreignPath, maxResults: 1,
            includeReferences: false, resultCursor: inspectCursor, maxResponseBytes: 16384), "RESULT_CURSOR_ARGUMENT_MISMATCH");
        var inspectNames = new List<string>();
        string? fullInspectCursor = null;
        var inspectDomains = 0;
        do
        {
            var inspectDomainCursor = fullInspectCursor;
            var broadPage = await ReadOuterPagesAsync((bytes, tokens, continuation) => assemblies.InspectAssembly(
                assemblyPath, maxResults: 1, includeReferences: false,
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation, resultCursor: continuation is null ? inspectDomainCursor : null), 65536, 16000,
                initialContinuation: null);
            var smallPage = await ReadOuterPagesAsync((bytes, tokens, continuation) => assemblies.InspectAssembly(
                assemblyPath, maxResults: 1, includeReferences: false,
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation, resultCursor: continuation is null ? inspectDomainCursor : null), 4096, 1600,
                initialContinuation: null);
            Assert.Equal(broadPage.Text, smallPage.Text);
            Assert.True(smallPage.Pages > 1, "This inspect domain page must itself require outer response pages.");
            using var pageJson = System.Text.Json.JsonDocument.Parse(broadPage.Text);
            inspectNames.Add(pageJson.RootElement.GetProperty("types")[0].GetProperty("name").GetString()!);
            fullInspectCursor = ReadOptionalDomainCursor(broadPage.Text);
            inspectDomains++;
        } while (fullInspectCursor is not null && inspectDomains < 20);
        Assert.Equal(8, inspectDomains);
        Assert.Equal(Enumerable.Range(0, 8).Select(index => $"Probe{index}"), inspectNames);

        var searchFirst = await assemblies.SearchAssembly(assemblyPath, pattern: "Needle", declarationOnly: true,
            kind: "method", maxResults: 100, maxFiles: 0, maxResponseBytes: 65536, maxResponseTokens: 50000);
        AssertOwnerResultWithinBudget(searchFirst, "Needle0", 65536, 50000);
        var searchCursor = ReadDomainCursor(TextOf(searchFirst));
        var searchNext = await assemblies.SearchAssembly(assemblyPath, pattern: "Needle", declarationOnly: true,
            kind: "method", maxResults: 100, maxFiles: 0, resultCursor: searchCursor,
            maxResponseBytes: 65536, maxResponseTokens: 50000);
        AssertOwnerPage(TextOf(searchNext));
        Assert.Contains("Probe", TextOf(searchNext), StringComparison.Ordinal);
        Assert.Equal(TextOf(searchNext), TextOf(await assemblies.SearchAssembly(assemblyPath, pattern: "Needle",
            declarationOnly: true, kind: "method", maxResults: 100, maxFiles: 0, resultCursor: searchCursor,
            maxResponseBytes: 65536, maxResponseTokens: 50000)));
        AssertError(await assemblies.SearchAssembly(assemblyPath, pattern: "Needle", declarationOnly: true,
            kind: "type", maxResults: 1, maxFiles: 0, resultCursor: searchCursor, maxResponseBytes: 16384), "RESULT_CURSOR_ARGUMENT_MISMATCH");
        AssertError(await assemblies.SearchAssembly(foreignPath, pattern: "Needle", declarationOnly: true,
            kind: "method", maxResults: 1, maxFiles: 0, resultCursor: searchCursor, maxResponseBytes: 16384), "RESULT_CURSOR_ARGUMENT_MISMATCH");
        var searchNames = new List<string>();
        string? fullSearchCursor = null;
        var searchDomains = 0;
        do
        {
            var searchDomainCursor = fullSearchCursor;
            var broadPage = await ReadOuterPagesAsync((bytes, tokens, continuation) => assemblies.SearchAssembly(
                assemblyPath, pattern: "Needle", declarationOnly: true, kind: "method", maxResults: 100, maxFiles: 0,
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation, resultCursor: continuation is null ? searchDomainCursor : null), 65536, 16000,
                initialContinuation: null);
            var smallPage = await ReadOuterPagesAsync((bytes, tokens, continuation) => assemblies.SearchAssembly(
                assemblyPath, pattern: "Needle", declarationOnly: true, kind: "method", maxResults: 100, maxFiles: 0,
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation, resultCursor: continuation is null ? searchDomainCursor : null), 4096, 1600,
                initialContinuation: null);
            Assert.Equal(broadPage.Text, smallPage.Text);
            Assert.True(smallPage.Pages > 1, "This search domain page must itself require outer response pages.");
            using var pageJson = System.Text.Json.JsonDocument.Parse(broadPage.Text);
            foreach (var hit in pageJson.RootElement.GetProperty("results").EnumerateArray())
                searchNames.Add(hit.GetProperty("symbol").GetString()!);
            fullSearchCursor = ReadOptionalDomainCursor(broadPage.Text);
            searchDomains++;
        } while (fullSearchCursor is not null && searchDomains < 20);
        Assert.Equal(2, searchDomains);
        Assert.Equal(192, searchNames.Count);
        Assert.Equal(192, searchNames.Distinct(StringComparer.Ordinal).Count());

        var filesLimited = await assemblies.SearchAssembly(assemblyPath, pattern: "Needle", declarationOnly: true,
            kind: "method", maxResults: 100, maxFiles: 1, maxResponseBytes: 65536, maxResponseTokens: 4096);
        var filesLimitedText = TextOf(filesLimited);
        AssertOwnerPage(filesLimitedText);
        Assert.Contains("maxFiles", filesLimitedText, StringComparison.Ordinal);
        Assert.Contains("analysisCompleteness=partial", filesLimitedText, StringComparison.Ordinal);
        Assert.Contains("omissions=maxFiles", filesLimitedText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"continuationToken\": \"v1.", filesLimitedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Needle7", filesLimitedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OuterContinuationReadsItsImmutablePageAfterTargetFileIsDeleted()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var assemblies = new AssemblyTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-assembly-outer-snapshot-");
        var source = string.Join("\n", Enumerable.Range(0, 24).Select(index =>
            $"public sealed class SnapshotProbe{index} {{ public int Read() => {index}; }}"));
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "OuterSnapshotProbe", source);

        var first = await assemblies.InspectAssembly(assemblyPath, maxResults: 24,
            includeReferences: false, maxResponseBytes: 1024, maxResponseTokens: 300);
        var outerToken = ReadOuterContinuation(TextOf(first));
        Assert.False(first.IsError ?? false, TextOf(first));
        Assert.NotNull(outerToken);

        File.Delete(assemblyPath);
        var next = await assemblies.InspectAssembly(assemblyPath, maxResults: 24,
            includeReferences: false, continuationToken: outerToken, maxResponseBytes: 1024, maxResponseTokens: 300);

        Assert.False(next.IsError ?? false, TextOf(next));
        Assert.Contains("Status: operation=ok, completeness=truncated", TextOf(next), StringComparison.Ordinal);
        Assert.DoesNotContain("Assembly file not found", TextOf(next), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyAssemblySearchAndExtensionProjectionsFitExact256TokenBudget()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var assemblies = new AssemblyTools(runtime);
        using var fixture = TestTempDirectory.Create("x-");
        var dependency = AssemblyTestHelper.EmitAssembly(fixture, "MissingOwner", "namespace MissingOwner; public sealed class Dependency { }");
        var builtTarget = AssemblyTestHelper.EmitAssembly(fixture, "EmptyProbe", "public sealed class Probe { public MissingOwner.Dependency? Dependency; public int Read() => 1; }", dependency);
        File.Delete(dependency);
        var shortTargetDirectory = Directory.CreateTempSubdirectory("x-");
        var target = Path.Combine(shortTargetDirectory.FullName, "p.dll");
        File.Copy(builtTarget, target);

        try
        {
        var search = await assemblies.SearchAssembly(target, pattern: "absent");
        AssertSuccessWithinBudget(search, 16_384, 16_384);
        var searchText = TextOf(search);
        Assert.True(TokenCount(searchText) <= 256, $"Search response uses {TokenCount(searchText)} cl100k_base tokens:\n{searchText}");
        Assert.Contains("analysisCompleteness=partial", searchText, StringComparison.Ordinal);
        Assert.Contains("incompleteRelationships", searchText, StringComparison.Ordinal);
        Assert.Contains("unresolvedReference: ", searchText, StringComparison.Ordinal);
        Assert.DoesNotContain("continuationToken=", searchText, StringComparison.Ordinal);
        Assert.DoesNotContain("resultContinuation=available", searchText, StringComparison.Ordinal);
        using (var document = System.Text.Json.JsonDocument.Parse(BodyOf(searchText)))
            Assert.Empty(document.RootElement.GetProperty("results").EnumerateArray());

        var extensions = await assemblies.FindAssemblyExtensions(target, receiverType: "NoType");
        AssertSuccessWithinBudget(extensions, 16_384, 16_384);
        var extensionText = TextOf(extensions);
        Assert.InRange(TokenCount(extensionText), 0, 256);
        Assert.Contains("analysisCompleteness=partial", extensionText, StringComparison.Ordinal);
        Assert.Contains("incompleteRelationships", extensionText, StringComparison.Ordinal);
        Assert.Contains("unresolvedReference: ", extensionText, StringComparison.Ordinal);
        Assert.DoesNotContain("continuationToken=", extensionText, StringComparison.Ordinal);
        Assert.DoesNotContain("resultContinuation=available", extensionText, StringComparison.Ordinal);
        using (var document = System.Text.Json.JsonDocument.Parse(BodyOf(extensionText)))
            Assert.Empty(document.RootElement.GetProperty("extensions").EnumerateArray());
        }
        finally
        {
            Directory.Delete(shortTargetDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task AssemblySearchAndExtensionsKeepOwnResultsAndCompactMissingReferenceDiagnostics()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var assemblies = new AssemblyTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-assembly-missing-reference-projection-");
        var dependency = AssemblyTestHelper.EmitAssembly(fixture, "MissingOwner", "namespace MissingOwner; public sealed class Dependency { }");
        var target = AssemblyTestHelper.EmitAssembly(fixture, "UsableOwner", """
            namespace UsableOwner;
            public sealed class Root { public MissingOwner.Dependency? Dependency; public int Read() => 1; }
            public static class Extensions { public static int Twice(this int value) => value * 2; }
            """, dependency);
        File.Delete(dependency);

        var compactSearch = await assemblies.SearchAssembly(target, pattern: "Read", declarationOnly: true, kind: "method");
        var detailedSearch = await assemblies.SearchAssembly(target, pattern: "Read", declarationOnly: true, kind: "method", includeDiagnostics: true);
        Assert.False(compactSearch.IsError ?? false, TextOf(compactSearch));
        Assert.False(detailedSearch.IsError ?? false, TextOf(detailedSearch));
        using var compactSearchJson = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(compactSearch)));
        using var detailedSearchJson = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(detailedSearch)));
        Assert.Equal(compactSearchJson.RootElement.GetProperty("results").GetRawText(), detailedSearchJson.RootElement.GetProperty("results").GetRawText());
        Assert.Equal(compactSearchJson.RootElement.GetProperty("totalCount").GetInt32(), detailedSearchJson.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("Read", Assert.Single(compactSearchJson.RootElement.GetProperty("results").EnumerateArray()).GetProperty("symbol").GetString());
        Assert.Contains("incompleteRelationships", compactSearchJson.RootElement.GetProperty("analysis").GetProperty("omissionReasons").GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFileName(dependency), TextOf(compactSearch), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MissingOwner", TextOf(detailedSearch), StringComparison.OrdinalIgnoreCase);

        var compactExtensions = await assemblies.FindAssemblyExtensions(target, receiverType: "System.Int32");
        var detailedExtensions = await assemblies.FindAssemblyExtensions(target, receiverType: "System.Int32", includeDiagnostics: true);
        Assert.False(compactExtensions.IsError ?? false, TextOf(compactExtensions));
        Assert.False(detailedExtensions.IsError ?? false, TextOf(detailedExtensions));
        using var compactExtensionJson = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(compactExtensions)));
        using var detailedExtensionJson = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(detailedExtensions)));
        Assert.Equal(compactExtensionJson.RootElement.GetProperty("extensions").GetRawText(), detailedExtensionJson.RootElement.GetProperty("extensions").GetRawText());
        Assert.Equal(compactExtensionJson.RootElement.GetProperty("totalCount").GetInt32(), detailedExtensionJson.RootElement.GetProperty("totalCount").GetInt32());
        var ownExtension = Assert.Single(compactExtensionJson.RootElement.GetProperty("extensions").EnumerateArray());
        Assert.Equal("Twice", ownExtension.GetProperty("name").GetString());
        Assert.StartsWith("h:", ownExtension.GetProperty("handoffId").GetString(), StringComparison.Ordinal);
        Assert.Equal(target, ownExtension.GetProperty("ownerTargetPath").GetString(), ignoreCase: true);
        Assert.Contains("incompleteRelationships", compactExtensionJson.RootElement.GetProperty("analysis").GetProperty("omissionReasons").GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFileName(dependency), TextOf(compactExtensions), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MissingOwner", TextOf(detailedExtensions), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicAssemblyExtensionCursorRejectsChangedReferencedOwnerSnapshot()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var assemblies = new AssemblyTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-extension-owner-cursor-");
        var owner = AssemblyTestHelper.EmitAssembly(fixture, "ExtensionCursorOwner", "namespace ExtensionCursor; public static class NumberExtensions { public static int Twice(this int value) => value * 2; public static int Thrice(this int value) => value * 3; }");
        var root = AssemblyTestHelper.EmitAssembly(fixture, "ExtensionCursorRoot", "using ExtensionCursor; namespace ExtensionCursorRoot; public sealed class Root { public int Call() => 1.Twice(); }", owner);

        var first = await PollAssemblyOwnerAsync(operation => assemblies.FindAssemblyExtensions(root,
            receiverType: "System.Int32", includeReferences: true, maxResults: 1,
            maxResponseBytes: 65536, maxResponseTokens: 4096, operationToken: operation));
        AssertOwnerPage(TextOf(first));
        var cursor = ReadDomainCursor(TextOf(first));
        Assert.False(string.IsNullOrWhiteSpace(cursor));

        using var replacementFixture = TestTempDirectory.Create("ainet-extension-owner-cursor-replacement-");
        var replacement = AssemblyTestHelper.EmitAssembly(replacementFixture, "ExtensionCursorOwner", "namespace ExtensionCursor; public static class NumberExtensions { public static int Twice(this int value) => value * 2; public static int Thrice(this int value) => value * 3; public static int FourTimes(this int value) => value * 4; }");
        File.Copy(replacement, owner, overwrite: true);

        var stale = await assemblies.FindAssemblyExtensions(root, receiverType: "System.Int32", includeReferences: true,
            maxResults: 1, resultCursor: cursor, maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertErrorWithinBudget(stale, "STALE_SNAPSHOT", 65536, 4096);
    }

    [Fact]
    public async Task AssemblyReferenceClosureHandsOffAcrossRootBridgeAndLeafOwners()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var symbols = new SymbolTools(runtime);
        var relationships = new RelationshipTools(runtime);
        var assemblies = new AssemblyTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-root-bridge-leaf-");
        var longLiteral = new string('x', 1200);
        var leafBodySource = $"namespace ClosureLeaf; public sealed class Leaf {{ public int Read() {{ var longText = \"{longLiteral}\"; var value = 0; "
            + string.Join(" ", Enumerable.Range(0, 80).Select(index => $"value += {index};"))
            + " return value + longText.Length; } } public static class LeafExtensions { public static int Twice(this int value) => value * 2; public static int Thrice(this int value) => value * 3; }";
        var leaf = AssemblyTestHelper.EmitAssembly(fixture, "ClosureLeaf", leafBodySource);
        var bridge = AssemblyTestHelper.EmitAssembly(fixture, "ClosureBridge",
            "namespace ClosureBridge; public sealed class Bridge { public int Forward() => new ClosureLeaf.Leaf().Read(); }", leaf);
        var root = AssemblyTestHelper.EmitAssembly(fixture, "ClosureRoot",
            "namespace ClosureRoot; public sealed class Entry { public int Run() => new ClosureBridge.Bridge().Forward(); }", bridge);

        var leafFromRoot = await PollAssemblyOwnerAsync(operation => symbols.FindSymbol(root, pattern: "ClosureLeaf.Leaf.Read", kind: "method", includeReferences: true,
            maxResults: 10, maxResponseBytes: 65536, maxResponseTokens: 4096, operationToken: operation));
        AssertOwnerResult(leafFromRoot, "Read");
        var leafHandle = ReadHandoff(TextOf(leafFromRoot), "Read");
        var leafBody = await symbols.GetSymbolBody(leaf, [leafHandle], maxResponseBytes: 32768, maxResponseTokens: 4096);
        Assert.False(leafBody.IsError ?? false, TextOf(leafBody) + "\nProducer:\n" + TextOf(leafFromRoot) + "\nHandle: " + leafHandle);
        Assert.Contains("Read", TextOf(leafBody), StringComparison.Ordinal);

        var extensionMatches = new List<(string Name, string Owner, string Handoff)>();
        string? extensionCursor = null;
        string? firstExtensionCursor = null;
        var extensionPages = 0;
        do
        {
            var page = await PollAssemblyOwnerAsync(operation => assemblies.FindAssemblyExtensions(root,
                receiverType: "System.Int32", includeReferences: true, maxResults: 1,
                resultCursor: extensionCursor, maxResponseBytes: 65536, maxResponseTokens: 4096, operationToken: operation));
            using var document = System.Text.Json.JsonDocument.Parse(BodyOf(TextOf(page)));
            var pageRoot = document.RootElement;
            foreach (var item in pageRoot.GetProperty("extensions").EnumerateArray())
            {
                var owner = item.GetProperty("ownerTargetPath").GetString()!;
                var handoff = item.GetProperty("handoffId").GetString()!;
                extensionMatches.Add((item.GetProperty("name").GetString()!, owner, handoff));
            }
            extensionCursor = pageRoot.TryGetProperty("resultCursor", out var cursorValue)
                && cursorValue.ValueKind == System.Text.Json.JsonValueKind.String ? cursorValue.GetString() : null;
            firstExtensionCursor ??= extensionCursor;
            extensionPages++;
            Assert.InRange(extensionPages, 1, 10);
        } while (extensionCursor is not null);
        Assert.Equal(2, extensionPages);
        Assert.Equal(new[] { "Thrice", "Twice" }, extensionMatches.Select(match => match.Name));
        Assert.All(extensionMatches, match => Assert.Equal(leaf, match.Owner, ignoreCase: true));
        foreach (var extension in extensionMatches)
        {
            var body = await symbols.GetSymbolBody(extension.Owner, [extension.Handoff], maxResponseBytes: 32768, maxResponseTokens: 4096);
            Assert.False(body.IsError ?? false, TextOf(body));
            Assert.Contains(extension.Name, TextOf(body), StringComparison.Ordinal);
        }
        Assert.NotNull(firstExtensionCursor);
        AssertErrorWithinBudget(await assemblies.FindAssemblyExtensions(root, receiverType: "System.Int32",
            includeReferences: true, maxResults: 2, resultCursor: firstExtensionCursor,
            maxResponseBytes: 65536, maxResponseTokens: 4096),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 65536, 4096);

        var incoming = await PollAssemblyOwnerAsync(operation => relationships.GetCallTree(root, leafHandle, direction: "incoming", depth: 3,
            includeReferences: true, maxResponseBytes: 65536, maxResponseTokens: 4096, operationToken: operation));
        AssertOwnerPage(TextOf(incoming));
        var incomingText = TextOf(incoming);
        Assert.Contains("ClosureBridge", incomingText, StringComparison.Ordinal);
        Assert.Contains("ClosureRoot", incomingText, StringComparison.Ordinal);
        Assert.Contains(leaf, incomingText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(bridge, incomingText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(root, incomingText, StringComparison.OrdinalIgnoreCase);
        var bridgeHandle = ReadCallTreeHandoff(incomingText, "Forward", bridge);
        var bridgeBody = await symbols.GetSymbolBody(bridge, [bridgeHandle], maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertOwnerResult(bridgeBody, "Forward");
        var rootHandle = ReadCallTreeHandoff(incomingText, "Run", root);
        var rootBody = await symbols.GetSymbolBody(root, [rootHandle], maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertOwnerResult(rootBody, "Run");

        var references = await PollAssemblyOwnerAsync(operation => relationships.FindReferences(root, leafHandle, includeReferences: true, depth: 3,
            maxResponseBytes: 65536, maxResponseTokens: 4096, operationToken: operation));
        AssertOwnerPage(TextOf(references));
        var referencesText = TextOf(references);
        Assert.Contains("ClosureBridge", referencesText, StringComparison.Ordinal);
        Assert.Contains("ClosureRoot", referencesText, StringComparison.Ordinal);

        var pagedReferences = new List<string>();
        string? referenceCursor = null;
        var referencePages = 0;
        do
        {
            var page = await PollAssemblyOwnerAsync(operation => relationships.FindReferences(root, leafHandle,
                includeReferences: true, depth: 3, maxResults: 1, resultCursor: referenceCursor,
                maxResponseBytes: 65536, maxResponseTokens: 4096, operationToken: operation));
            using var document = System.Text.Json.JsonDocument.Parse(IntegrationMcpAssertions.BodyOf(TextOf(page)));
            var pageRoot = document.RootElement;
            foreach (var item in pageRoot.GetProperty("references").EnumerateArray())
                pagedReferences.Add($"{item.GetProperty("ownerTargetPath").GetString()}:{item.GetProperty("filePath").GetString()}:{item.GetProperty("line").GetInt32()}");
            referenceCursor = pageRoot.TryGetProperty("resultCursor", out var cursorValue)
                && cursorValue.ValueKind == System.Text.Json.JsonValueKind.String ? cursorValue.GetString() : null;
            referencePages++;
            Assert.InRange(referencePages, 1, 10);
        } while (referenceCursor is not null);
        Assert.Equal(2, referencePages);
        Assert.Equal(2, pagedReferences.Count);
        Assert.Equal(2, pagedReferences.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var pagedImpact = new List<string>();
        string? impactCursor = null;
        string? firstImpactCursor = null;
        var impactPages = 0;
        do
        {
            var page = await PollAssemblyOwnerAsync(operation => relationships.GetImpact(root, leafHandle,
                includeReferences: true, depth: 3, maxResults: 1, resultCursor: impactCursor,
                maxResponseBytes: 65536, maxResponseTokens: 4096, operationToken: operation));
            using var document = System.Text.Json.JsonDocument.Parse(IntegrationMcpAssertions.BodyOf(TextOf(page)));
            var pageRoot = document.RootElement;
            foreach (var item in pageRoot.GetProperty("callSites").EnumerateArray())
                pagedImpact.Add($"{item.GetProperty("ownerTargetPath").GetString()}:{item.GetProperty("filePath").GetString()}:{item.GetProperty("line").GetInt32()}");
            impactCursor = pageRoot.TryGetProperty("resultCursor", out var cursorValue)
                && cursorValue.ValueKind == System.Text.Json.JsonValueKind.String ? cursorValue.GetString() : null;
            firstImpactCursor ??= impactCursor;
            impactPages++;
            Assert.InRange(impactPages, 1, 10);
        } while (impactCursor is not null);
        Assert.Equal(2, impactPages);
        Assert.Equal(2, pagedImpact.Count);
        Assert.Equal(2, pagedImpact.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.NotNull(firstImpactCursor);
        AssertErrorWithinBudget(await relationships.GetImpact(root, leafHandle, includeReferences: true, depth: 3,
            maxResults: 2, resultCursor: firstImpactCursor, maxResponseBytes: 65536, maxResponseTokens: 4096),
            "RESULT_CURSOR_ARGUMENT_MISMATCH", 65536, 4096);
        Assert.Contains("Forward", referencesText, StringComparison.Ordinal);

        var context = await PollAssemblyOwnerAsync(operation => relationships.GetContext(root, leafHandle, ["body", "callers"],
            includeReferences: true, maxResults: 20, maxResponseBytes: 65536, maxResponseTokens: 12000, operationToken: operation));
        AssertSuccessWithinBudget(context, 65536, 12000);
        var contextText = TextOf(context);
        Assert.Contains("callers", contextText, StringComparison.Ordinal);
        Assert.Contains("ClosureBridge", contextText, StringComparison.Ordinal);
    }

    private static void AssertDeclarationOrder(string text)
    {
        var body = BodyOf(text);
        if (body.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var names = document.RootElement.GetProperty("members").EnumerateArray()
                .Select(member => member.GetProperty("name").GetString()).ToArray();
            Assert.True(Array.IndexOf(names, "Zulu") >= 0 && Array.IndexOf(names, "Alpha") > Array.IndexOf(names, "Zulu"), text);
            return;
        }
        var zulu = text.IndexOf("Zulu()", StringComparison.Ordinal);
        var alpha = text.IndexOf("Alpha()", StringComparison.Ordinal);
        Assert.True(zulu >= 0 && alpha > zulu, text);
    }

    private static string[] ReadMemberNames(string text)
    {
        var body = BodyOf(text);
        if (body.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            return document.RootElement.GetProperty("members").EnumerateArray()
                .Select(member => member.GetProperty("name").GetString()!).ToArray();
        }
        return text.Split('\n')
        .Where(line => line.StartsWith("- Method ", StringComparison.Ordinal))
        .Select(line => line[(line.IndexOf("int ", StringComparison.Ordinal) + 4)..line.IndexOf('(', StringComparison.Ordinal)])
        .ToArray();
    }

    private static string ReadHandoff(string text, string memberName)
    {
        var body = IntegrationMcpAssertions.BodyOf(text);
        if (body.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var name = memberName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last().Split('.')[^1];
            var entries = document.RootElement.TryGetProperty("members", out var members)
                ? members.EnumerateArray().ToArray()
                : document.RootElement.GetProperty("results").EnumerateArray()
                    .SelectMany(result => result.GetProperty("entries").EnumerateArray()).ToArray();
            var selected = entries.Where(entry => string.Equals(entry.GetProperty("name").GetString(), name, StringComparison.Ordinal))
                .FirstOrDefault();
            var handoff = selected.ValueKind == System.Text.Json.JsonValueKind.Object
                ? selected.GetProperty("handoffId").GetString()
                : null;
            Assert.True(handoff?.StartsWith("h:", StringComparison.Ordinal) == true, body);
            return handoff!;
        }
        var line = text.Split('\n').First(line => line.Contains(memberName, StringComparison.Ordinal)
            && line.Contains("[handoff: ", StringComparison.Ordinal));
        var start = line.IndexOf("[handoff: ", StringComparison.Ordinal);
        var end = line.IndexOf(']', start + 10);
        Assert.True(start >= 0 && end > start, line);
        return line[(start + 10)..end];
    }

    private static string ReadHandoffByDocumentationId(string text, string documentationId, string ownerTargetPath)
    {
        using var document = System.Text.Json.JsonDocument.Parse(BodyOf(text));
        var entries = document.RootElement.GetProperty("results").EnumerateArray()
            .SelectMany(result => result.GetProperty("entries").EnumerateArray());
        var selected = Assert.Single(entries.Where(entry =>
            string.Equals(entry.GetProperty("docCommentId").GetString(), documentationId, StringComparison.Ordinal)
            && string.Equals(Path.GetFullPath(entry.GetProperty("ownerTargetPath").GetString()!),
                Path.GetFullPath(ownerTargetPath), StringComparison.OrdinalIgnoreCase)));
        var handoff = selected.GetProperty("handoffId").GetString();
        Assert.True(handoff?.StartsWith("h:", StringComparison.Ordinal) == true,
            $"The selected owner result for {documentationId} did not include a handoff.");
        return handoff!;
    }

    private static string ReadCallTreeHandoff(string text, string name, string expectedOwnerPath)
    {
        var line = text.Split('\n').FirstOrDefault(value => value.Contains(name, StringComparison.Ordinal)
            && value.Contains("h:", StringComparison.Ordinal));
        Assert.True(line is not null, text);
        Assert.Contains("targetPath: " + expectedOwnerPath, line, StringComparison.OrdinalIgnoreCase);
        var marker = "`h:";
        var start = line.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, line);
        start += 1;
        var end = line.IndexOf('`', start + 2);
        Assert.True(end > start, line);
        return line[start..end];
    }

    private static string ReadAnyHandoff(string text)
    {
        var body = IntegrationMcpAssertions.BodyOf(text);
        if (body.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var handoff = FindFirstHandoff(document.RootElement);
            if (handoff?.StartsWith("h:", StringComparison.Ordinal) == true) return handoff;
        }
        for (var start = text.IndexOf("h:", StringComparison.Ordinal); start >= 0;
             start = text.IndexOf("h:", start + 2, StringComparison.Ordinal))
        {
            var end = start + 2;
            while (end < text.Length && char.IsAsciiLetterOrDigit(text[end])) end++;
            if (end > start + 2) return text[start..end];
        }
        Assert.Fail("The handler did not emit an opaque assembly handoff.\n" + text);
        return string.Empty;
    }

    private static string? FindFirstHandoff(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("handoffId") && property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
                var nested = FindFirstHandoff(property.Value);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindFirstHandoff(item);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return null;
    }

    private static int ReadPosition(string text)
    {
        try
        {
            var jsonStart = text.IndexOf('{');
            using var document = System.Text.Json.JsonDocument.Parse(jsonStart >= 0 ? text[jsonStart..] : text);
            var jsonLine = FindLine(document.RootElement);
            if (jsonLine is not null) return jsonLine.Value;
        }
        catch (System.Text.Json.JsonException) { }
        var start = text.IndexOf("Probe.cs:", StringComparison.Ordinal);
        Assert.True(start >= 0, text);
        start += "Probe.cs:".Length;
        var end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
        Assert.True(int.TryParse(text[start..end], out var line), text);
        return line;
    }

    private static int? FindLine(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (element.TryGetProperty("filePath", out var path) && path.GetString() == "Probe.cs"
                && element.TryGetProperty("line", out var line) && line.TryGetInt32(out var value)) return value;
            foreach (var property in element.EnumerateObject())
            {
                var nested = FindLine(property.Value);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindLine(item);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

    private static async Task FollowAssemblyHandoffAsync(SymbolTools symbols, string targetPath, CallToolResult producer)
    {
        var handle = ReadAnyHandoff(TextOf(producer));
        var body = await symbols.GetSymbolBody(targetPath, [handle], maxResponseBytes: 32768, maxResponseTokens: 4096);
        var text = TextOf(body);
        Assert.False(body.IsError ?? false, text);
        Assert.DoesNotContain("HANDOFF_UNKNOWN", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TARGET_MISMATCH", text, StringComparison.Ordinal);
        Assert.Contains("Status: operation=ok", text, StringComparison.Ordinal);
    }

    private static void AssertOwnerResult(CallToolResult result, string expectedText)
        => AssertOwnerResultWithinBudget(result, expectedText, 65536, 4096);

    private static void AssertOwnerResultWithinBudget(CallToolResult result, string expectedText, int bytes, int tokens)
    {
        var text = TextOf(result);
        Assert.False(result.IsError ?? false, text);
        Assert.DoesNotContain("operation=running", text, StringComparison.Ordinal);
        Assert.DoesNotContain("operation=retry", text, StringComparison.Ordinal);
        if (text.Contains("\"results\": [", StringComparison.Ordinal) && expectedText.StartsWith("class ", StringComparison.Ordinal))
        {
            Assert.Contains("\"kind\": \"class\"", text, StringComparison.Ordinal);
            Assert.Contains($"\"name\": \"{expectedText[6..]}\"", text, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(expectedText, text, StringComparison.Ordinal);
        }
        Assert.InRange(Encoding.UTF8.GetByteCount(text), 0, bytes);
        Assert.InRange(TokenCount(text), 0, tokens);
    }

    private static void AssertSameAssemblyIdentity(string originalPath, string replacementPath)
    {
        var original = AssemblyName.GetAssemblyName(originalPath);
        var replacement = AssemblyName.GetAssemblyName(replacementPath);
        Assert.Equal(original.Name, replacement.Name);
        Assert.Equal(original.Version, replacement.Version);
        Assert.Equal(original.CultureName, replacement.CultureName);
        Assert.Equal(original.GetPublicKeyToken(), replacement.GetPublicKeyToken());
    }

    private static async Task AssertDifferentBytesAsync(string originalPath, string replacementPath)
    {
        var original = await File.ReadAllBytesAsync(originalPath);
        var replacement = await File.ReadAllBytesAsync(replacementPath);
        Assert.False(original.SequenceEqual(replacement));
    }

    private static void AssertError(CallToolResult result, string code)
    {
        var text = TextOf(result);
        Assert.True(result.IsError ?? false, text);
        Assert.Contains(code, text, StringComparison.Ordinal);
    }

    private static async Task AssertRecoverableProjectionAsync(
        Func<int, int?, Task<CallToolResult>> invoke,
        int requestedBytes,
        int requestedTokens)
    {
        var first = await invoke(requestedBytes, requestedTokens);
        var firstText = TextOf(first);
        Assert.InRange(Encoding.UTF8.GetByteCount(firstText), 0, requestedBytes);
        Assert.InRange(TokenCount(firstText), 0, requestedTokens);
        if (!firstText.Contains("RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
        {
            Assert.False(first.IsError ?? false, firstText);
            AssertOwnerPage(firstText);
            return;
        }

        var minBytes = ReadBudget(firstText, "minimumResponseBytes");
        var minTokens = ReadBudget(firstText, "minimumResponseTokens");
        var recovered = await invoke(minBytes, minTokens);
        var recoveredText = TextOf(recovered);
        Assert.False(recovered.IsError ?? false, recoveredText);
        Assert.DoesNotContain("RESPONSE_BUDGET_TOO_SMALL", recoveredText, StringComparison.Ordinal);
        Assert.InRange(Encoding.UTF8.GetByteCount(recoveredText), 0, minBytes);
        Assert.InRange(TokenCount(recoveredText), 0, minTokens);
        AssertOwnerPage(recoveredText);

        var repeated = await invoke(minBytes, minTokens);
        var repeatedText = TextOf(repeated);
        Assert.Equal(recoveredText, repeatedText);
        Assert.InRange(Encoding.UTF8.GetByteCount(repeatedText), 0, minBytes);
        Assert.InRange(TokenCount(repeatedText), 0, minTokens);
    }

    private static void AssertOwnerPage(string text)
    {
        Assert.Contains("Status: operation=ok", text, StringComparison.Ordinal);
        Assert.DoesNotContain("operation=running", text, StringComparison.Ordinal);
        Assert.DoesNotContain("operation=retry", text, StringComparison.Ordinal);
        Assert.DoesNotContain("operation=loading", text, StringComparison.Ordinal);
    }

    private static string ReadDomainCursor(string text)
    {
        return ReadOptionalDomainCursor(text) ?? throw new Xunit.Sdk.XunitException("The domain page omitted its resultCursor.");
    }

    private static string? ReadOptionalDomainCursor(string text)
    {
        var body = BodyOf(text);
        var jsonStart = body.IndexOf('{');
        Assert.True(jsonStart >= 0, body);
        using var document = System.Text.Json.JsonDocument.Parse(body[jsonStart..]);
        return document.RootElement.TryGetProperty("resultCursor", out var cursor) ? cursor.GetString() : null;
    }

    private static async Task<(string Text, int Pages, string FirstPage)> ReadOuterPagesAsync(
        Func<int, int?, string?, Task<CallToolResult>> invoke,
        int bytes,
        int tokens,
        string? initialContinuation = null)
    {
        var accumulated = new StringBuilder();
        string? continuation = initialContinuation;
        string? firstPage = null;
        for (var pageNumber = 0; pageNumber < 100; pageNumber++)
        {
            var result = await invoke(bytes, tokens, continuation);
            var text = TextOf(result);
            firstPage ??= text;
            Assert.False(result.IsError ?? false, $"Outer page {pageNumber} failed: {text}");
            Assert.InRange(Encoding.UTF8.GetByteCount(text), 0, bytes);
            Assert.InRange(TokenCount(text), 0, tokens);
            Assert.DoesNotContain("operation=running", text, StringComparison.Ordinal);
            Assert.DoesNotContain("operation=retry", text, StringComparison.Ordinal);
            accumulated.Append(BodyOf(text));
            continuation = ReadOuterContinuation(text);
            if (continuation is null) return (accumulated.ToString(), pageNumber + 1, firstPage);
            Assert.NotEmpty(continuation);
            Assert.All(continuation, character => Assert.True(char.IsAsciiDigit(character)));
        }
        throw new Xunit.Sdk.XunitException("The outer response did not reach its final page.");
    }

    private static async Task<(string Text, int Pages, bool SawExactBudgetRecovery)> ReadOuterPagesWithBudgetRecoveryAsync(
        Func<int, int?, string?, Task<CallToolResult>> invoke)
    {
        var text = new StringBuilder();
        var bytes = 512;
        int? tokens = 89;
        string? continuation = null;
        var sawExactBudgetRecovery = false;
        for (var pageNumber = 0; pageNumber < 100; pageNumber++)
        {
            var result = await invoke(bytes, tokens, continuation);
            var page = TextOf(result);
            Assert.InRange(Encoding.UTF8.GetByteCount(page), 0, bytes);
            Assert.InRange(TokenCount(page), 0, tokens.Value);
            if (page.Contains("RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
            {
                Assert.True(result.IsError ?? false, page);
                var minBytes = ReadBudget(page, "minimumResponseBytes");
                var minTokens = ReadBudget(page, "minimumResponseTokens");
                Assert.InRange(minBytes, 512, 65536);
                Assert.True(minTokens > 0);
                var recovered = await invoke(minBytes, minTokens, continuation);
                var recoveredText = TextOf(recovered);
                Assert.False(recovered.IsError ?? false, recoveredText);
                Assert.DoesNotContain("RESPONSE_BUDGET_TOO_SMALL", recoveredText, StringComparison.Ordinal);
                Assert.InRange(Encoding.UTF8.GetByteCount(recoveredText), 0, minBytes);
                Assert.InRange(TokenCount(recoveredText), 0, minTokens);
                Assert.Equal(recoveredText, TextOf(await invoke(minBytes, minTokens, continuation)));
                result = recovered;
                page = recoveredText;
                bytes = minBytes;
                tokens = minTokens;
                sawExactBudgetRecovery = true;
            }
            else
            {
                Assert.False(result.IsError ?? false, page);
            }

            Assert.Contains("Status: operation=ok", page, StringComparison.Ordinal);
            Assert.DoesNotContain("operation=running", page, StringComparison.Ordinal);
            Assert.DoesNotContain("operation=retry", page, StringComparison.Ordinal);
            text.Append(BodyOf(page));
            continuation = ReadOuterContinuation(page);
            if (continuation is null) return (text.ToString(), pageNumber + 1, sawExactBudgetRecovery);
            Assert.NotEmpty(continuation);
            Assert.All(continuation, character => Assert.True(char.IsAsciiDigit(character)));
        }
        throw new Xunit.Sdk.XunitException("The recovered mixed context did not reach its final outer page.");
    }

    private static string BodyOf(string text)
    {
        var lines = text.Split('\n');
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

    private static string? ReadOuterContinuation(string text)
    {
        var line = text.Split('\n').FirstOrDefault(value => value.StartsWith("continuationToken=", StringComparison.Ordinal));
        return line?[("continuationToken=".Length)..];
    }

    private static string ReadHeader(string text, string name)
    {
        var prefix = name + "=";
        var line = text.Split('\n').FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return line is null ? throw new Xunit.Sdk.XunitException($"Missing {name} metadata.") : line[prefix.Length..];
    }

    private static async Task<CallToolResult> PollAssemblyOwnerAsync(Func<string?, Task<CallToolResult>> invoke)
    {
        string? operation = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var result = await invoke(operation);
            var text = TextOf(result);
            if (!text.Contains("operation=running", StringComparison.Ordinal)) return result;
            operation = text.Split('\n').FirstOrDefault(line => line.StartsWith("operationToken=", StringComparison.Ordinal))?
                ["operationToken=".Length..];
            Assert.False(string.IsNullOrWhiteSpace(operation), text);
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException("The direct assembly owner did not complete after polling its operation token.");
    }

    private static string TextOf(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
}
