using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class SourceDependencyGraphOutgoingContractTests
{
    [Theory]
    [InlineData(1, "incoming")]
    [InlineData(2, "both")]
    public async Task DependencyGraph_ColdPartialRootsScanOnlyNeededFrontiersAndMatchBroadProjection(int depth, string broadDirection)
    {
        using var fixture = TestTempDirectory.Create("ainet-outgoing-frontiers-");
        var target = await CreateSolutionAsync(fixture, new Dictionary<string, string>
        {
            ["Root.cs"] = "namespace OutgoingProbe; public partial class Root { public Left Value = new(); } public class Unrelated { public Noise Value = new(); }",
            ["Root.Partial.cs"] = "namespace OutgoingProbe; public partial class Root { public Right Other = new(); }",
            ["Neighbor.cs"] = "namespace OutgoingProbe; public class Left { public Terminal Value = new(); public Root Cycle = new(); } public class Right { public Terminal Value = new(); }",
            ["Terminal.cs"] = "namespace OutgoingProbe; public class Terminal { public Tail Value = new(); }",
            ["Tail.cs"] = "namespace OutgoingProbe; public class Tail { }",
            ["Noise.cs"] = "namespace OutgoingProbe; public class Noise { public Tail Value = new(); }",
            ["Empty.cs"] = "namespace OutgoingProbe; // no declaration",
            ["Excluded.Tests.cs"] = "namespace OutgoingProbe; public class TestCaller { public Root Value = new(); }"
        });
        var scans = new ConcurrentQueue<string>();
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(
            DocumentCollected: document => scans.Enqueue(document.Name)));
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>(), dependencyGraphCache: cache);
        var relationships = new RelationshipTools(runtime);

        var cold = await relationships.DependencyGraph(target, symbolIdentifier: "T:OutgoingProbe.Root", direction: "outgoing", depth: depth,
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        var coldPayload = Payload(cold);
        var expectedDocuments = depth == 1 ? new[] { "Root.Partial.cs", "Root.cs" } : new[] { "Neighbor.cs", "Root.Partial.cs", "Root.cs" };
        Assert.Equal(expectedDocuments, scans.Order(StringComparer.Ordinal).ToArray());
        AssertCoverage(coldPayload, 8, expectedDocuments.Length);
        var edges = coldPayload.GetProperty("typeDependencies").EnumerateArray().ToArray();
        AssertEdge(edges, "Root", "Left", 1);
        var partialEdge = AssertEdge(edges, "Root", "Right", 1);
        Assert.Equal("src/App/Root.Partial.cs", partialEdge.GetProperty("fromFile").GetString());
        Assert.DoesNotContain(edges, edge => edge.GetProperty("fromTypeName").GetString() is "Unrelated" or "Noise" or "Terminal");
        Assert.Equal(depth == 1 ? 2 : 5, edges.Length);
        Assert.Equal(depth == 1 ? 3 : 4, coldPayload.GetProperty("visitedTypeCount").GetInt32());
        if (depth == 2)
        {
            AssertEdge(edges, "Left", "Terminal", 2);
            AssertEdge(edges, "Right", "Terminal", 2);
            AssertEdge(edges, "Left", "Root", 2);
        }

        var broad = await relationships.DependencyGraph(target, symbolIdentifier: "T:OutgoingProbe.Root", direction: broadDirection,
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertCoverage(Payload(broad), 8, 8);
        Assert.Equal(8, scans.Count);
        Assert.Equal(8, scans.Distinct(StringComparer.Ordinal).Count());
        var projected = await relationships.DependencyGraph(target, symbolIdentifier: "T:OutgoingProbe.Root", direction: "outgoing", depth: depth,
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        var projectedPayload = Payload(projected);
        AssertCoverage(projectedPayload, 8, 8);
        foreach (var property in new[] { "typeDependencies", "projectDependencies", "namespaceDependencies", "fileDependencies", "totalTypeDependencyCount", "totalProjectDependencyCount", "totalNamespaceDependencyCount", "totalFileDependencyCount", "visitedTypeCount", "hiddenTypeDependencyCount" })
            Assert.Equal(coldPayload.GetProperty(property).GetRawText(), projectedPayload.GetProperty(property).GetRawText());

        var changedRoot = await relationships.DependencyGraph(target, symbolIdentifier: "T:OutgoingProbe.Noise", direction: "outgoing", depth: 2,
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertEdge(Payload(changedRoot).GetProperty("typeDependencies").EnumerateArray().ToArray(), "Noise", "Tail", 1);
        Assert.Equal(8, scans.Count);
        var endpoint = partialEdge.GetProperty("toHandoffId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(endpoint));
        var body = await new SymbolTools(runtime).GetSymbolBody(target, [endpoint!], maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(body, 32768, 4096);
        Assert.Contains("Resolution status: resolved", TextOf(body), StringComparison.Ordinal);
        Assert.Contains("class Right", TextOf(body), StringComparison.Ordinal);

        var rootFile = fixture.GetPath("src/App/Root.cs");
        var timestamp = File.GetLastWriteTimeUtc(rootFile);
        await File.AppendAllTextAsync(rootFile, "\n// changed snapshot with preserved timestamp");
        File.SetLastWriteTimeUtc(rootFile, timestamp);
        var fresh = await relationships.DependencyGraph(target, symbolIdentifier: "T:OutgoingProbe.Root", direction: "outgoing", depth: 1,
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertCoverage(Payload(fresh), 8, 2);
        Assert.Equal(10, scans.Count);
        Assert.NotEqual(ReadHeader(TextOf(cold), "snapshotId"), ReadHeader(TextOf(fresh), "snapshotId"));
    }

    [Fact]
    public async Task DependencyGraph_FileSeedsEveryNamedTypeAndEmptyFilePreservesProjectSummary()
    {
        using var fixture = TestTempDirectory.Create("ainet-outgoing-file-seeds-");
        var target = await CreateSolutionAsync(fixture, new Dictionary<string, string>
        {
            ["Selected.cs"] = """
                namespace FileProbe;
                public partial class Outer
                {
                    public class NestedClass { public Dependency Value = new(); }
                    public record NestedRecord(Dependency Value);
                    public record struct NestedRecordStruct(Dependency Value);
                    public struct NestedStruct { public Dependency Value; }
                    public interface NestedInterface { Dependency Value { get; } }
                    public enum NestedEnum { Value }
                    public delegate Dependency NestedDelegate();
                }
                """,
            ["Partial.cs"] = "namespace FileProbe; public partial class Outer { public Dependency Value = new(); }",
            ["Dependency.cs"] = "namespace FileProbe; public class Dependency { }",
            ["Empty.cs"] = "namespace FileProbe; // no named type"
        }, includeReferencedProject: true);
        var scans = new ConcurrentQueue<string>();
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(DocumentCollected: document => scans.Enqueue(document.Name)));
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>(), dependencyGraphCache: cache);
        var relationships = new RelationshipTools(runtime);

        var empty = await relationships.DependencyGraph(target, filePath: fixture.GetPath("src/App/Empty.cs"), direction: "outgoing",
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        var emptyPayload = Payload(empty);
        AssertCoverage(emptyPayload, 5, 0);
        Assert.Equal(0, emptyPayload.GetProperty("visitedTypeCount").GetInt32());
        Assert.Empty(emptyPayload.GetProperty("typeDependencies").EnumerateArray());
        Assert.Single(emptyPayload.GetProperty("projectDependencies").EnumerateArray());
        Assert.Empty(scans);

        var selected = await relationships.DependencyGraph(target, filePath: fixture.GetPath("src/App/Selected.cs"), direction: "outgoing",
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        var payload = Payload(selected);
        AssertCoverage(payload, 5, 2);
        Assert.Equal(new[] { "Partial.cs", "Selected.cs" }, scans.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(9, payload.GetProperty("visitedTypeCount").GetInt32());
        var edges = payload.GetProperty("typeDependencies").EnumerateArray().ToArray();
        foreach (var name in new[] { "Outer", "NestedClass", "NestedRecord", "NestedRecordStruct", "NestedStruct", "NestedInterface", "NestedDelegate" })
            AssertEdge(edges, name, "Dependency", 1);
        Assert.Equal(7, edges.Length);
        Assert.Equal("src/App/Partial.cs", AssertEdge(edges, "Outer", "Dependency", 1).GetProperty("fromFile").GetString());
        Assert.Equal(emptyPayload.GetProperty("projectDependencies").GetRawText(), payload.GetProperty("projectDependencies").GetRawText());

        var broadEmpty = await relationships.DependencyGraph(target, filePath: fixture.GetPath("src/App/Empty.cs"), direction: "both",
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        var broadEmptyPayload = Payload(broadEmpty);
        AssertCoverage(broadEmptyPayload, 5, 5);
        Assert.Equal(0, broadEmptyPayload.GetProperty("visitedTypeCount").GetInt32());
        Assert.Empty(broadEmptyPayload.GetProperty("typeDependencies").EnumerateArray());
        Assert.Equal(emptyPayload.GetProperty("projectDependencies").GetRawText(), broadEmptyPayload.GetProperty("projectDependencies").GetRawText());
        Assert.Equal(5, scans.Count);
    }

    [Fact]
    public async Task DependencyGraph_FileValidationPrecedesCollectionAndExcludedRootRemainsAnchor()
    {
        using var fixture = TestTempDirectory.Create("ainet-outgoing-validation-");
        var target = await CreateSolutionAsync(fixture, new Dictionary<string, string>
        {
            ["Anchor.Tests.cs"] = "namespace FilterProbe; public class Anchor { public Caller Value = new(); }",
            ["Caller.cs"] = "namespace FilterProbe; public class Caller { public Anchor Value = new(); }"
        });
        var foreign = fixture.CreateFile("foreign/Caller.cs", "namespace Foreign; public class Caller { }");
        var scans = new ConcurrentQueue<string>();
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(DocumentCollected: document => scans.Enqueue(document.Name)));
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>(), dependencyGraphCache: cache);
        var relationships = new RelationshipTools(runtime);
        foreach (var direction in new[] { "outgoing", "incoming", "both" })
        {
            var invalid = await relationships.DependencyGraph(target, filePath: foreign, direction: direction, scopeType: "production",
                maxResponseBytes: 65536, maxResponseTokens: 8192);
            AssertErrorWithinBudget(invalid, "INVALID_ARGUMENT", 65536, 8192);
            Assert.Contains("$.filePath", TextOf(invalid), StringComparison.Ordinal);
            Assert.Contains("find_symbol", TextOf(invalid), StringComparison.Ordinal);
            Assert.Empty(scans);
        }
        var anchor = fixture.GetPath("src/App/Anchor.Tests.cs");
        var outgoing = await relationships.DependencyGraph(target, filePath: anchor, direction: "outgoing", scopeType: "production",
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        var outgoingPayload = Payload(outgoing);
        AssertCoverage(outgoingPayload, 1, 0);
        Assert.Equal(1, outgoingPayload.GetProperty("visitedTypeCount").GetInt32());
        Assert.Empty(outgoingPayload.GetProperty("typeDependencies").EnumerateArray());
        Assert.Empty(scans);
        var incoming = await relationships.DependencyGraph(target, filePath: anchor, direction: "incoming", scopeType: "production",
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertCoverage(Payload(incoming), 1, 1);
        AssertEdge(Payload(incoming).GetProperty("typeDependencies").EnumerateArray().ToArray(), "Caller", "Anchor", 1);
        Assert.Equal(new[] { "Caller.cs" }, scans.ToArray());
    }

    [Fact]
    public async Task DependencyGraph_LinkedGeneratedFileIsAmbiguousBeforeFiltersOrCollection()
    {
        using var fixture = TestTempDirectory.Create("ainet-outgoing-linked-");
        var target = fixture.CreateFile("Linked.slnx", "<Solution><Project Path=\"src/Zebra/Zebra.csproj\" /><Project Path=\"src/Alpha/Alpha.csproj\" /></Solution>");
        var linked = fixture.CreateFile("src/Shared/Linked.g.cs", "// <auto-generated/>\nnamespace LinkedProbe; public class Root { }");
        var owners = new[] { "Alpha", "Zebra" }.Select(name => fixture.CreateFile($"src/{name}/{name}.csproj",
            ProjectXml("<ItemGroup><Compile Include=\"../Shared/Linked.g.cs\" Link=\"Linked.g.cs\" /></ItemGroup>"))).ToArray();
        await RestoreAsync(fixture, target);
        var scans = new ConcurrentQueue<string>();
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(DocumentCollected: document => scans.Enqueue(document.Name)));
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>(), dependencyGraphCache: cache);
        var relationships = new RelationshipTools(runtime);
        foreach (var direction in new[] { "outgoing", "incoming", "both" })
        {
            var result = await relationships.DependencyGraph(target, filePath: linked, direction: direction, scopeType: "tests", includeGenerated: false,
                maxResponseBytes: 65536, maxResponseTokens: 8192);
            AssertErrorWithinBudget(result, "INVALID_ARGUMENT", 65536, 8192);
            var text = TextOf(result);
            var candidates = owners.Select(path => OperatingSystem.IsWindows() ? path.Replace('\\', '/').ToUpperInvariant() : path.Replace('\\', '/')).ToArray();
            Assert.Contains("$.filePath", text, StringComparison.Ordinal);
            Assert.Contains("unique owner-bound symbol reference", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(candidates[0], text, StringComparison.Ordinal);
            Assert.Contains(candidates[1], text, StringComparison.Ordinal);
            Assert.True(text.IndexOf(candidates[0], StringComparison.Ordinal) < text.IndexOf(candidates[1], StringComparison.Ordinal), text);
            Assert.Empty(scans);
        }
    }

    private static JsonElement Payload(CallToolResult result)
    {
        AssertSuccessWithinBudget(result, 65536, 8192);
        var text = TextOf(result);
        var start = text.IndexOf('{');
        Assert.True(start >= 0, text);
        using var payload = JsonDocument.Parse(text[start..]);
        return payload.RootElement.Clone();
    }

    private static void AssertCoverage(JsonElement payload, int eligible, int covered)
    {
        Assert.Equal(eligible, payload.GetProperty("totalDocumentCount").GetInt32());
        Assert.Equal(covered, payload.GetProperty("scannedDocumentCount").GetInt32());
        Assert.Equal(0, payload.GetProperty("documentOffset").GetInt32());
        Assert.False(payload.TryGetProperty("nextDocumentOffset", out var next) && next.ValueKind != JsonValueKind.Null);
        Assert.False(payload.GetProperty("documentLimitReached").GetBoolean());
        Assert.False(payload.GetProperty("continuationInputIncomplete").GetBoolean());
        Assert.False(payload.GetProperty("nodeLimitReached").GetBoolean());
        Assert.True(payload.GetProperty("isComplete").GetBoolean(), payload.GetRawText());
    }

    private static JsonElement AssertEdge(JsonElement[] edges, string from, string to, int depth)
    {
        var edge = Assert.Single(edges.Where(edge => edge.GetProperty("fromTypeName").GetString() == from && edge.GetProperty("toTypeName").GetString() == to));
        Assert.Equal(depth, edge.GetProperty("depth").GetInt32());
        return edge;
    }

    private static async Task<string> CreateSolutionAsync(TestTempDirectory fixture, Dictionary<string, string> documents, bool includeReferencedProject = false)
    {
        var library = includeReferencedProject ? "<Project Path=\"src/Library/Library.csproj\" />" : string.Empty;
        var target = fixture.CreateFile("Outgoing.slnx", $"<Solution><Project Path=\"src/App/App.csproj\" />{library}</Solution>");
        fixture.CreateFile("src/App/App.csproj", ProjectXml(includeReferencedProject
            ? "<ItemGroup><ProjectReference Include=\"../Library/Library.csproj\" /></ItemGroup>" : string.Empty));
        foreach (var (name, source) in documents) fixture.CreateFile($"src/App/{name}", source);
        if (includeReferencedProject)
        {
            fixture.CreateFile("src/Library/Library.csproj", ProjectXml(string.Empty));
            fixture.CreateFile("src/Library/Library.cs", "namespace LibraryProbe; public class LibraryType { }");
        }
        await RestoreAsync(fixture, target);
        return target;
    }

    private static string ProjectXml(string items) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>disable</ImplicitUsings><Nullable>enable</Nullable><GenerateAssemblyInfo>false</GenerateAssemblyInfo><GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute></PropertyGroup>" + items + "</Project>";

    private static Task RestoreAsync(TestTempDirectory fixture, string target)
    {
        var configuration = fixture.CreateFile("NuGet.Config", "<configuration><packageSources><clear /></packageSources></configuration>");
        return FixtureRestore.RunAsync(target, fixture.DirectoryPath, configuration, "Outgoing dependency-graph fixture restore");
    }
}
