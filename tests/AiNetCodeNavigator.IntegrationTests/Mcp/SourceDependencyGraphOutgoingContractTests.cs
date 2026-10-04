using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Builders;
using ModelContextProtocol.Protocol;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class SourceDependencyGraphOutgoingContractTests
{
    [Fact]
    public async Task DependencyGraph_ProjectReferencesResolveExactRootWithoutDocumentCollection()
    {
        using var fixture = TestTempDirectory.Create("ainet-project-dependencies-");
        var (target, app) = CreateSolution(fixture, new Dictionary<string, string>
        {
            ["Root.cs"] = "namespace ProjectProbe; public class Root { public void Run() { } }",
            ["Empty.cs"] = "// no declaration"
        });
        var libraryFile = fixture.CreateFile("src/Library/Library.cs", "namespace Library; public class Root { }");
        var leafFile = fixture.CreateFile("src/Leaf/Leaf.cs", "namespace Leaf; public class Root { }");
        var clientFile = fixture.CreateFile("src/Client/Client.cs", "namespace Client; public class Root { }");
        var scans = new ConcurrentQueue<string>();
        var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(DocumentCollected: document => scans.Enqueue(document.Name)));
        await using var testHost = InMemorySourceTestHost.Create(target,
            [app with { ProjectReferences = ["Library"] },
             new ProjectSpec("Library", [(libraryFile, await File.ReadAllTextAsync(libraryFile))], ProjectReferences: ["Leaf"], VirtualProjectDirectory: "src/Library"),
             new ProjectSpec("Leaf", [(leafFile, await File.ReadAllTextAsync(leafFile))], VirtualProjectDirectory: "src/Leaf"),
             new ProjectSpec("Client", [(clientFile, await File.ReadAllTextAsync(clientFile))], ProjectReferences: ["App"], VirtualProjectDirectory: "src/Client")], cache);
        var relationships = new RelationshipTools(testHost.Runtime);
        var outgoing = Payload(await relationships.DependencyGraph(target, filePath: "src/App/Empty.cs", level: "project", direction: "outgoing",
            maxResponseBytes: 65536, maxResponseTokens: 8192));
        Assert.Equal("project", outgoing.GetProperty("level").GetString());
        Assert.Equal("App", outgoing.GetProperty("root").GetProperty("name").GetString());
        Assert.False(outgoing.TryGetProperty("typeDependencies", out _));
        var direct = Assert.Single(outgoing.GetProperty("projectDependencies").EnumerateArray());
        Assert.Equal("ProjectReference", direct.GetProperty("origin").GetString());
        Assert.Equal(outgoing.GetProperty("root").GetProperty("projectId").GetString(), direct.GetProperty("fromProjectId").GetString());
        Assert.Equal(2, outgoing.GetProperty("projects").GetArrayLength());
        Assert.All(outgoing.GetProperty("projects").EnumerateArray(), owner =>
        {
            Assert.True(Path.IsPathFullyQualified(owner.GetProperty("projectPath").GetString()!));
            Assert.False(string.IsNullOrWhiteSpace(owner.GetProperty("ownerContextFingerprint").GetString()));
        });
        var member = Payload(await relationships.DependencyGraph(target, symbolIdentifier: "M:ProjectProbe.Root.Run", level: "project", direction: "outgoing",
            maxResponseBytes: 65536, maxResponseTokens: 8192));
        Assert.Equal(outgoing.GetProperty("root").GetRawText(), member.GetProperty("root").GetRawText());
        Assert.Equal(outgoing.GetProperty("projectDependencies").GetRawText(), member.GetProperty("projectDependencies").GetRawText());
        var deeper = Payload(await relationships.DependencyGraph(target, filePath: "src/App/Empty.cs", level: "project", direction: "outgoing", depth: 2,
            maxResponseBytes: 65536, maxResponseTokens: 8192));
        Assert.Equal(2, deeper.GetProperty("projectDependencies").GetArrayLength());
        var incoming = Payload(await relationships.DependencyGraph(target, filePath: "src/App/Empty.cs", level: "project", direction: "incoming",
            maxResponseBytes: 65536, maxResponseTokens: 8192));
        Assert.Single(incoming.GetProperty("projectDependencies").EnumerateArray());
        Assert.Contains("Client", incoming.GetRawText());
        var both = Payload(await relationships.DependencyGraph(target, filePath: "src/App/Empty.cs", level: "project", direction: "both", depth: 2, maxResults: 1,
            maxResponseBytes: 65536, maxResponseTokens: 8192));
        Assert.Equal(3, both.GetProperty("totalProjectDependencyCount").GetInt32());
        Assert.Single(both.GetProperty("projectDependencies").EnumerateArray());
        Assert.True(both.GetProperty("hasMore").GetBoolean());
        Assert.False(both.GetProperty("isComplete").GetBoolean());
        Assert.Contains("$.scopeType", TextOf(await relationships.DependencyGraph(target, filePath: "src/App/Empty.cs", level: "project", scopeType: "all")));
        Assert.Contains("$.includeGenerated", TextOf(await relationships.DependencyGraph(target, filePath: "src/App/Empty.cs", level: "project", includeGenerated: false)));
        Assert.Contains("$.level", TextOf(await relationships.DependencyGraph(typeof(RelationshipTools).Assembly.Location, symbolIdentifier: "T:Any", level: "project")));
        Assert.Contains("AMBIGUOUS_SYMBOL", TextOf(await relationships.DependencyGraph(target, symbolIdentifier: "Root", level: "project")));
        var isolated = Payload(await relationships.DependencyGraph(target, filePath: leafFile, level: "project", direction: "outgoing",
            maxResponseBytes: 65536, maxResponseTokens: 8192));
        Assert.Equal("Leaf", isolated.GetProperty("root").GetProperty("name").GetString());
        Assert.Empty(isolated.GetProperty("projectDependencies").EnumerateArray());
        Assert.True(isolated.GetProperty("isComplete").GetBoolean());
        Assert.Empty(scans);
    }

    [Fact]
    public async Task DependencyGraph_SelectedLevelsUseOwningTypeTraversalAndReuseEvidenceFacts()
    {
        using var fixture = TestTempDirectory.Create("ainet-selected-dependency-levels-");
        var (target, app) = CreateSolution(fixture, new Dictionary<string, string>
        {
            ["Root.cs"] = "namespace App; public class Root { public Library.First First; public Library.Second Second; public void Run() { } }",
        });
        var libraryFile = fixture.CreateFile("src/Library/Types.cs", "namespace Library; public class First { } public class Second { }");
        var library = new ProjectSpec("Library", [(libraryFile, await File.ReadAllTextAsync(libraryFile))], VirtualProjectDirectory: "src/Library");
        var scans = new ConcurrentQueue<string>();
        var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(DocumentCollected: document => scans.Enqueue(document.Name)));
        await using var testHost = InMemorySourceTestHost.Create(target, [app with { ProjectReferences = ["Library"] }, library], cache);
        var relationships = new RelationshipTools(testHost.Runtime);
        foreach (var level in new[] { "type", "file", "namespace" })
        {
            var response = await relationships.DependencyGraph(target, symbolIdentifier: "M:App.Root.Run", direction: "outgoing",
                level: level, maxResults: 1, maxResponseBytes: 65536, maxResponseTokens: 8192);
            var payload = Payload(response);
            Assert.Equal(level, payload.GetProperty("level").GetString());
            Assert.Contains("owning type", payload.GetProperty("rootSemantics").GetString());
            foreach (var unselected in new[] { "type", "file", "namespace", "project" }.Where(value => value != level))
            {
                Assert.False(payload.TryGetProperty(unselected + "Dependencies", out _));
                Assert.False(payload.TryGetProperty("total" + char.ToUpperInvariant(unselected[0]) + unselected[1..] + "DependencyCount", out _));
            }
            var edge = Assert.Single(payload.GetProperty(level + "Dependencies").EnumerateArray());
            var evidence = edge.GetProperty("evidence");
            Assert.Equal("src/App/Root.cs", evidence.GetProperty("filePath").GetString());
            Assert.Equal(1, evidence.GetProperty("line").GetInt32());
            Assert.True(evidence.GetProperty("column").GetInt32() > 1);
            Assert.Contains("App", evidence.GetProperty("fromProjectIdentity").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Library", evidence.GetProperty("toProjectIdentity").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(level != "type", payload.GetProperty("isComplete").GetBoolean());
        }
        Assert.Single(scans);
        AssertErrorWithinBudget(await relationships.DependencyGraph(target, symbolIdentifier: "T:App.Root", level: "invalid"),
            "INVALID_ARGUMENT", 24576, 8192);
        AssertErrorWithinBudget(await relationships.DependencyGraph(target, symbolIdentifier: "T:App.Root", depth: 0),
            "INVALID_ARGUMENT", 24576, 8192);
    }

    [Theory]
    [InlineData(1, "incoming")]
    [InlineData(2, "both")]
    public async Task DependencyGraph_ColdPartialRootsScanOnlyNeededFrontiersAndMatchBroadProjection(int depth, string broadDirection)
    {
        using var fixture = TestTempDirectory.Create("ainet-outgoing-frontiers-");
        var (target, project) = CreateSolution(fixture, new Dictionary<string, string>
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
        var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(
            DocumentCollected: document => scans.Enqueue(document.Name)));
        await using var testHost = InMemorySourceTestHost.Create(target, [project], cache);
        var relationships = new RelationshipTools(testHost.Runtime);

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
        foreach (var property in new[] { "typeDependencies", "totalTypeDependencyCount", "visitedTypeCount", "hiddenTypeDependencyCount" })
            Assert.Equal(coldPayload.GetProperty(property).GetRawText(), projectedPayload.GetProperty(property).GetRawText());

        var changedRoot = await relationships.DependencyGraph(target, symbolIdentifier: "T:OutgoingProbe.Noise", direction: "outgoing", depth: 2,
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertEdge(Payload(changedRoot).GetProperty("typeDependencies").EnumerateArray().ToArray(), "Noise", "Tail", 1);
        Assert.Equal(8, scans.Count);
        var endpoint = partialEdge.GetProperty("toHandoffId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(endpoint));
        var body = await new SymbolTools(testHost.Runtime).GetSymbolBody(target, [endpoint!], maxResponseBytes: 32768, maxResponseTokens: 4096);
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
    public async Task DependencyGraph_FileSeedsEveryNamedTypeAndEmptyFileHasNoTypeDependencies()
    {
        using var fixture = TestTempDirectory.Create("ainet-outgoing-file-seeds-");
        var (target, app) = CreateSolution(fixture, new Dictionary<string, string>
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
        });
        var libraryFile = fixture.CreateFile("src/Library/Library.cs", "namespace LibraryProbe; public class LibraryType { }");
        var library = new ProjectSpec("Library", [(libraryFile, await File.ReadAllTextAsync(libraryFile))], VirtualProjectDirectory: "src/Library");
        var scans = new ConcurrentQueue<string>();
        var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(DocumentCollected: document => scans.Enqueue(document.Name)));
        app = app with { ProjectReferences = ["Library"] };
        await using var testHost = InMemorySourceTestHost.Create(target, [app, library], cache);
        var relationships = new RelationshipTools(testHost.Runtime);

        var emptyProjects = Payload(await relationships.DependencyGraph(target, filePath: fixture.GetPath("src/App/Empty.cs"), level: "project", direction: "outgoing",
            maxResponseBytes: 65536, maxResponseTokens: 8192));
        Assert.Equal("App", emptyProjects.GetProperty("root").GetProperty("name").GetString());
        Assert.Single(emptyProjects.GetProperty("projectDependencies").EnumerateArray());
        var selectedProjects = Payload(await relationships.DependencyGraph(target, filePath: fixture.GetPath("src/App/Selected.cs"), level: "project", direction: "outgoing",
            maxResponseBytes: 65536, maxResponseTokens: 8192));
        Assert.Equal(emptyProjects.GetProperty("projectDependencies").GetRawText(), selectedProjects.GetProperty("projectDependencies").GetRawText());
        Assert.Empty(scans);

        var empty = await relationships.DependencyGraph(target, filePath: fixture.GetPath("src/App/Empty.cs"), direction: "outgoing",
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        var emptyPayload = Payload(empty);
        AssertCoverage(emptyPayload, 5, 0);
        Assert.Equal(0, emptyPayload.GetProperty("visitedTypeCount").GetInt32());
        Assert.Empty(emptyPayload.GetProperty("typeDependencies").EnumerateArray());
        Assert.False(emptyPayload.TryGetProperty("projectDependencies", out _));
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
        Assert.False(payload.TryGetProperty("projectDependencies", out _));

        var broadEmpty = await relationships.DependencyGraph(target, filePath: fixture.GetPath("src/App/Empty.cs"), direction: "both",
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        var broadEmptyPayload = Payload(broadEmpty);
        AssertCoverage(broadEmptyPayload, 5, 5);
        Assert.Equal(0, broadEmptyPayload.GetProperty("visitedTypeCount").GetInt32());
        Assert.Empty(broadEmptyPayload.GetProperty("typeDependencies").EnumerateArray());
        Assert.False(broadEmptyPayload.TryGetProperty("projectDependencies", out _));
        Assert.Equal(5, scans.Count);
    }

    [Fact]
    public async Task DependencyGraph_FileValidationPrecedesCollectionAndExcludedRootRemainsAnchor()
    {
        using var fixture = TestTempDirectory.Create("ainet-outgoing-validation-");
        var (target, project) = CreateSolution(fixture, new Dictionary<string, string>
        {
            ["Anchor.Tests.cs"] = "namespace FilterProbe; public class Anchor { public Caller Value = new(); }",
            ["Caller.cs"] = "namespace FilterProbe; public class Caller { public Anchor Value = new(); }"
        });
        var foreign = fixture.CreateFile("foreign/Caller.cs", "namespace Foreign; public class Caller { }");
        var scans = new ConcurrentQueue<string>();
        var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(DocumentCollected: document => scans.Enqueue(document.Name)));
        await using var testHost = InMemorySourceTestHost.Create(target, [project], cache);
        var relationships = new RelationshipTools(testHost.Runtime);
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
        var target = fixture.CreateFile("Linked.slnx", string.Empty);
        var linked = fixture.CreateFile("src/Shared/Linked.g.cs", "// <auto-generated/>\nnamespace LinkedProbe; public class Root { }");
        var source = await File.ReadAllTextAsync(linked);
        var projects = new[]
        {
            new ProjectSpec("Zebra", [(linked, source)], VirtualProjectDirectory: "src/Zebra"),
            new ProjectSpec("Alpha", [(linked, source)], VirtualProjectDirectory: "src/Alpha")
        };
        var scans = new ConcurrentQueue<string>();
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(DocumentCollected: _ => scans.Enqueue("collected")));
        await using var testHost = InMemorySourceTestHost.Create(target, projects, cache);
        var relationships = new RelationshipTools(testHost.Runtime);
        var projectResult = await relationships.DependencyGraph(target, filePath: linked, level: "project",
            maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertErrorWithinBudget(projectResult, "INVALID_ARGUMENT", 65536, 8192);
        Assert.Contains("multiple loaded owners", TextOf(projectResult), StringComparison.Ordinal);
        foreach (var direction in new[] { "outgoing", "incoming", "both" })
        {
            var result = await relationships.DependencyGraph(target, filePath: linked, direction: direction, scopeType: "tests", includeGenerated: false,
                maxResponseBytes: 65536, maxResponseTokens: 8192);
            AssertErrorWithinBudget(result, "INVALID_ARGUMENT", 65536, 8192);
            var text = TextOf(result);
            var candidates = new[] { "Alpha", "Zebra" }.Select(name =>
                (OperatingSystem.IsWindows() ? Path.Combine(fixture.DirectoryPath, "src", name, name + ".csproj").ToUpperInvariant()
                    : Path.Combine(fixture.DirectoryPath, "src", name, name + ".csproj")).Replace('\\', '/')).ToArray();
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
        using var payload = JsonDocument.Parse(BodyOf(TextOf(result)));
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

    private static (string Target, ProjectSpec Project) CreateSolution(TestTempDirectory fixture, Dictionary<string, string> documents,
        bool includeReferencedProject = false)
    {
        var target = fixture.CreateFile("Outgoing.slnx", string.Empty);
        var sourceDocuments = documents.Select(pair =>
        {
            var path = fixture.CreateFile("src/App/" + pair.Key, pair.Value);
            return (path, pair.Value);
        }).ToArray();
        var project = new ProjectSpec("App", sourceDocuments,
            ProjectReferences: includeReferencedProject ? ["Library"] : null,
            VirtualProjectDirectory: "src/App");
        return (target, project);
    }
}
