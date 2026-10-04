using System.IO;
using System.Linq;
using System.Text.Json;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Builders;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class SourceDependencyGraphCollectionContractTests
{
    [Fact]
    public async Task DependencyGraph_BroadIncomingAndBothCoverLateDocuments()
    {
        using var fixture = TestTempDirectory.Create("ainet-source-dependency-graph-broad-window-");
        var (target, project) = CreateSolution(fixture, fillerCount: 1000, includeLateRoot: true);
        await using var testHost = InMemorySourceTestHost.Create(target, [project]);
        var relationships = new RelationshipTools(testHost.Runtime);

        foreach (var direction in new[] { "incoming", "both" })
        {
            var response = await relationships.DependencyGraph(target, symbolIdentifier: "T:RelationshipProbe.LateRoot",
                direction: direction, depth: 1, maxResponseBytes: 65536, maxResponseTokens: 4096);
            AssertSuccessWithinBudget(response, 65536, 4096);
            using var document = JsonDocument.Parse(JsonBody(TextOf(response)));
            var root = document.RootElement;
            var edges = root.GetProperty("typeDependencies").EnumerateArray().ToArray();
            Assert.Contains(edges, edge => edge.GetProperty("fromTypeName").GetString() == "LateCaller"
                && edge.GetProperty("toTypeName").GetString() == "LateRoot");
            if (direction == "both")
                Assert.Contains(edges, edge => edge.GetProperty("fromTypeName").GetString() == "LateRoot"
                    && edge.GetProperty("toTypeName").GetString() == "LaterDependency");
            Assert.True(root.GetProperty("totalDocumentCount").GetInt32() > 1000);
            Assert.Equal(root.GetProperty("totalDocumentCount").GetInt32(), root.GetProperty("scannedDocumentCount").GetInt32());
            Assert.Equal(0, root.GetProperty("documentOffset").GetInt32());
            Assert.False(root.TryGetProperty("nextDocumentOffset", out var nextDocumentOffset)
                && nextDocumentOffset.ValueKind != JsonValueKind.Null);
            Assert.False(root.GetProperty("documentLimitReached").GetBoolean());
            Assert.False(root.GetProperty("continuationInputIncomplete").GetBoolean());
            Assert.True(root.GetProperty("isComplete").GetBoolean(), TextOf(response));
        }
    }

    [Fact]
    public async Task DependencyGraph_FileSelectorRejectsLinkedPhysicalFileWithSortedOwnerCandidates()
    {
        using var fixture = TestTempDirectory.Create("ainet-source-dependency-graph-linked-owner-");
        var (target, linkedPath, firstProject, secondProject, projects) = CreateLinkedFileSolution(fixture);
        await using var testHost = InMemorySourceTestHost.Create(target, projects);
        var relationships = new RelationshipTools(testHost.Runtime);

        var response = await relationships.DependencyGraph(target, filePath: linkedPath,
            maxResponseBytes: 32768, maxResponseTokens: 4096);

        AssertErrorWithinBudget(response, "INVALID_ARGUMENT", 32768, 4096);
        var errorText = TextOf(response);
        var firstCandidate = OperatingSystem.IsWindows() ? firstProject.ToUpperInvariant() : firstProject;
        var secondCandidate = OperatingSystem.IsWindows() ? secondProject.ToUpperInvariant() : secondProject;
        Assert.Contains("$.filePath", errorText, StringComparison.Ordinal);
        Assert.Contains(firstCandidate, errorText, StringComparison.Ordinal);
        Assert.Contains(secondCandidate, errorText, StringComparison.Ordinal);
        Assert.Contains("unique owner-bound symbol reference", errorText, StringComparison.OrdinalIgnoreCase);
        Assert.True(errorText.IndexOf(firstCandidate, StringComparison.Ordinal) < errorText.IndexOf(secondCandidate, StringComparison.Ordinal), errorText);
        Assert.DoesNotContain("SharedDependency", errorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DependencyGraph_FileSelectorRejectsForeignAbsolutePathWithLoadedBasename()
    {
        using var fixture = TestTempDirectory.Create("ainet-source-dependency-graph-foreign-file-");
        var (target, project) = CreateSolution(fixture);
        await using var testHost = InMemorySourceTestHost.Create(target, [project]);
        var relationships = new RelationshipTools(testHost.Runtime);
        var foreignAbsolutePath = Path.Combine(Path.DirectorySeparatorChar.ToString(), "Relationships.cs");
        Assert.True(Path.IsPathRooted(foreignAbsolutePath));
        Assert.False(File.Exists(foreignAbsolutePath), $"Expected an unloaded foreign path: {foreignAbsolutePath}");

        var response = await relationships.DependencyGraph(target, filePath: foreignAbsolutePath,
            maxResponseBytes: 32768, maxResponseTokens: 4096);

        AssertErrorWithinBudget(response, "INVALID_ARGUMENT", 32768, 4096);
        Assert.Contains("$.filePath", TextOf(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DependencyGraph_FileSelectorSeedsNestedAndEdgeFreeTypesAndIncludesPartialDeclarations()
    {
        using var fixture = TestTempDirectory.Create("ainet-source-dependency-graph-file-seeds-");
        var (target, selectedFile, partialFile, project) = CreateNamedTypeFileSolution(fixture);
        await using var testHost = InMemorySourceTestHost.Create(target, [project]);
        var relationships = new RelationshipTools(testHost.Runtime);

        var response = await relationships.DependencyGraph(target, filePath: selectedFile,
            direction: "outgoing", depth: 1, maxResponseBytes: 65536, maxResponseTokens: 4096);

        AssertSuccessWithinBudget(response, 65536, 4096);
        using var document = JsonDocument.Parse(JsonBody(TextOf(response)));
        var root = document.RootElement;
        Assert.Equal(8, root.GetProperty("visitedTypeCount").GetInt32());
        var edges = root.GetProperty("typeDependencies").EnumerateArray().ToArray();
        Assert.Contains(edges, edge => edge.GetProperty("fromTypeName").GetString() == "NestedClass"
            && edge.GetProperty("toTypeName").GetString() == "Dependency");
        Assert.Contains(edges, edge => edge.GetProperty("fromTypeName").GetString() == "NestedRecord"
            && edge.GetProperty("toTypeName").GetString() == "Dependency");
        var partialOuterEdge = Assert.Single(edges.Where(edge => edge.GetProperty("fromTypeName").GetString() == "Outer"
            && edge.GetProperty("toTypeName").GetString() == "Dependency"));
        Assert.Equal(Path.GetRelativePath(Path.GetDirectoryName(target)!, partialFile).Replace('\\', '/'),
            partialOuterEdge.GetProperty("fromFile").GetString());
    }

    [Fact]
    public async Task DependencyGraph_AppliesScopeAndGeneratedFiltersToCollectionDocuments()
    {
        using var fixture = TestTempDirectory.Create("ainet-source-dependency-graph-filters-");
        var (target, project) = CreateSolution(fixture, additionalDocuments:
        [
            ("RelationshipProbe.Tests.cs", "namespace RelationshipProbe; public sealed class ScopedTestCaller { public Target Value { get; set; } = new(); }"),
            ("Generated.g.cs", "// <auto-generated/>\nnamespace RelationshipProbe; public sealed class ScopedGeneratedCaller { public Target Value { get; set; } = new(); }")
        ]);
        await using var testHost = InMemorySourceTestHost.Create(target, [project]);
        var relationships = new RelationshipTools(testHost.Runtime);

        var all = await relationships.DependencyGraph(target, symbolIdentifier: "T:RelationshipProbe.Target",
            direction: "incoming", maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(all, 65536, 4096);
        using var allDocument = JsonDocument.Parse(JsonBody(TextOf(all)));
        var allEdges = allDocument.RootElement.GetProperty("typeDependencies").EnumerateArray().ToArray();
        Assert.Contains(allEdges, edge => edge.GetProperty("fromTypeName").GetString() == "ScopedTestCaller");
        Assert.DoesNotContain(allEdges, edge => edge.GetProperty("fromTypeName").GetString() == "ScopedGeneratedCaller");

        var tests = await relationships.DependencyGraph(target, symbolIdentifier: "T:RelationshipProbe.Target",
            direction: "incoming", scopeType: "tests", maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(tests, 65536, 4096);
        using var testsDocument = JsonDocument.Parse(JsonBody(TextOf(tests)));
        var testEdges = testsDocument.RootElement.GetProperty("typeDependencies").EnumerateArray().ToArray();
        Assert.Contains(testEdges, edge => edge.GetProperty("fromTypeName").GetString() == "ScopedTestCaller");
        Assert.DoesNotContain(testEdges, edge => edge.GetProperty("fromTypeName").GetString() == "ProductionCaller");

        var generated = await relationships.DependencyGraph(target, symbolIdentifier: "T:RelationshipProbe.Target",
            direction: "incoming", includeGenerated: true, maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(generated, 65536, 4096);
        using var generatedDocument = JsonDocument.Parse(JsonBody(TextOf(generated)));
        var generatedEdges = generatedDocument.RootElement.GetProperty("typeDependencies").EnumerateArray().ToArray();
        Assert.Contains(generatedEdges, edge => edge.GetProperty("fromTypeName").GetString() == "ScopedGeneratedCaller");
        Assert.Contains(generatedEdges, edge => edge.GetProperty("fromTypeName").GetString() == "ScopedTestCaller");
    }

    private static string JsonBody(string text)
    {
        var start = text.IndexOf('{');
        Assert.True(start >= 0, text);
        return text[start..];
    }

    private static (string Target, ProjectSpec Project) CreateSolution(
        TestTempDirectory fixture,
        int fillerCount = 0,
        bool includeLateRoot = false,
        IReadOnlyList<(string FileName, string Content)>? additionalDocuments = null)
    {
        var target = fixture.CreateFile("DependencyGraph.slnx", string.Empty);
        var sourceDocuments = new List<(string FileName, string Content)>();
        const string rootSource = "namespace RelationshipProbe; public sealed class Target { } public sealed class ProductionCaller { public Target Value { get; set; } = new(); }";
        sourceDocuments.Add((fixture.CreateFile("src/App/Relationships.cs", rootSource), rootSource));
        foreach (var index in Enumerable.Range(0, fillerCount))
        {
            var source = $"namespace Filler{index:D4}; public sealed class Filler{index:D4} {{ }}";
            sourceDocuments.Add((fixture.CreateFile($"src/App/A{index:D4}.cs", source), source));
        }
        if (includeLateRoot)
        {
            const string source = "namespace RelationshipProbe; public sealed class LaterDependency { } public sealed class LateRoot { public LaterDependency Value { get; set; } = new(); } public sealed class LateCaller { public LateRoot Value { get; set; } = new(); }";
            sourceDocuments.Add((fixture.CreateFile("src/App/ZLater.cs", source), source));
        }
        foreach (var (fileName, source) in additionalDocuments ?? [])
            sourceDocuments.Add((fixture.CreateFile("src/App/" + fileName, source), source));
        var project = new ProjectSpec("DependencyGraph", sourceDocuments, VirtualProjectDirectory: "src/App");
        return (target, project);
    }

    private static (string Target, string LinkedFile, string FirstProject, string SecondProject, ProjectSpec[] Projects) CreateLinkedFileSolution(TestTempDirectory fixture)
    {
        var target = fixture.CreateFile("LinkedOwners.slnx", string.Empty);
        var linkedFile = fixture.CreateFile("src/Shared/Linked.cs",
            "namespace LinkedProbe; public sealed class SharedRoot { public SharedDependency Value { get; set; } = new(); } public sealed class SharedDependency { }");
        var firstProject = Path.Combine(fixture.DirectoryPath, "src", "First", "First.csproj").Replace('\\', '/');
        var secondProject = Path.Combine(fixture.DirectoryPath, "src", "Second", "Second.csproj").Replace('\\', '/');
        const string source = "namespace LinkedProbe; public sealed class SharedRoot { public SharedDependency Value { get; set; } = new(); } public sealed class SharedDependency { }";
        var projects = new[]
        {
            new ProjectSpec("First", [(linkedFile, source)], VirtualProjectDirectory: "src/First"),
            new ProjectSpec("Second", [(linkedFile, source)], VirtualProjectDirectory: "src/Second")
        };
        return (target, linkedFile, firstProject, secondProject, projects);
    }

    private static (string Target, string SelectedFile, string PartialFile, ProjectSpec Project) CreateNamedTypeFileSolution(TestTempDirectory fixture)
    {
        var target = fixture.CreateFile("NamedTypes.slnx", string.Empty);
        var selectedFile = fixture.CreateFile("src/App/Root.cs", """
            namespace RootProbe;
            public sealed partial class Outer
            {
                public sealed class NestedClass { public Dependency Value { get; set; } = new(); }
                public sealed record NestedRecord(Dependency Value);
                public struct NestedStruct { public int Value; }
                public interface NestedInterface { }
                public enum NestedEnum { Value }
                public delegate void NestedDelegate();
            }
            public sealed class Dependency { }
            """);
        var partialFile = fixture.CreateFile("src/App/Outer.Partial.cs",
            "namespace RootProbe; public sealed partial class Outer { public Dependency PartialValue { get; set; } = new(); }");
        var project = new ProjectSpec("NamedTypes",
            [(selectedFile, """
                namespace RootProbe;
                public sealed partial class Outer
                {
                    public sealed class NestedClass { public Dependency Value { get; set; } = new(); }
                    public sealed record NestedRecord(Dependency Value);
                    public struct NestedStruct { public int Value; }
                    public interface NestedInterface { }
                    public enum NestedEnum { Value }
                    public delegate void NestedDelegate();
                }
                public sealed class Dependency { }
                """), (partialFile, "namespace RootProbe; public sealed partial class Outer { public Dependency PartialValue { get; set; } = new(); }")],
            VirtualProjectDirectory: "src/App");
        return (target, selectedFile, partialFile, project);
    }
}
