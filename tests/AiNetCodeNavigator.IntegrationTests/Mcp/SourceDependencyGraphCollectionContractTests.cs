using System.IO;
using System.Linq;
using System.Text.Json;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class SourceDependencyGraphCollectionContractTests
{
    [Fact]
    public async Task DependencyGraph_BroadIncomingAndBothCoverLateDocuments()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-source-dependency-graph-broad-window-");
        var target = await CreateSolutionAsync(fixture.DirectoryPath, fillerCount: 1000, includeLateRoot: true);

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
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-source-dependency-graph-linked-owner-");
        var (target, linkedPath, firstProject, secondProject) = await CreateLinkedFileSolutionAsync(fixture.DirectoryPath);

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
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-source-dependency-graph-foreign-file-");
        var target = await CreateSolutionAsync(fixture.DirectoryPath);
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
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-source-dependency-graph-file-seeds-");
        var (target, selectedFile, partialFile) = await CreateNamedTypeFileSolutionAsync(fixture.DirectoryPath);

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
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-source-dependency-graph-filters-");
        var target = await CreateSolutionAsync(fixture.DirectoryPath);
        var appDirectory = Path.Combine(fixture.DirectoryPath, "src", "App");
        await File.WriteAllTextAsync(Path.Combine(appDirectory, "RelationshipProbe.Tests.cs"),
            "namespace RelationshipProbe; public sealed class ScopedTestCaller { public Target Value { get; set; } = new(); }");
        await File.WriteAllTextAsync(Path.Combine(appDirectory, "Generated.g.cs"),
            "// <auto-generated/>\nnamespace RelationshipProbe; public sealed class ScopedGeneratedCaller { public Target Value { get; set; } = new(); }");

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

    private static async Task<string> CreateSolutionAsync(string root, int fillerCount = 0, bool includeLateRoot = false)
    {
        var solution = Path.Combine(root, "DependencyGraph.slnx");
        var projectDirectory = Path.Combine(root, "src", "App");
        Directory.CreateDirectory(projectDirectory);
        await File.WriteAllTextAsync(solution, "<Solution><Project Path=\"src/App/DependencyGraph.csproj\" /></Solution>");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "DependencyGraph.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Relationships.cs"),
            "namespace RelationshipProbe; public sealed class Target { } public sealed class ProductionCaller { public Target Value { get; set; } = new(); }");
        foreach (var index in Enumerable.Range(0, fillerCount))
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, $"A{index:D4}.cs"), $"namespace Filler{index:D4}; public sealed class Filler{index:D4} {{ }}");
        if (includeLateRoot)
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "ZLater.cs"),
                "namespace RelationshipProbe; public sealed class LaterDependency { } public sealed class LateRoot { public LaterDependency Value { get; set; } = new(); } public sealed class LateCaller { public LateRoot Value { get; set; } = new(); }");
        var nugetConfig = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(nugetConfig, "<configuration><packageSources><clear /></packageSources></configuration>");
        await FixtureRestore.RunAsync(solution, root, nugetConfig, "Dependency-graph fixture restore");
        return solution;
    }

    private static async Task<(string Solution, string LinkedFile, string FirstProject, string SecondProject)> CreateLinkedFileSolutionAsync(string root)
    {
        var solution = Path.Combine(root, "LinkedOwners.slnx");
        var firstProject = Path.Combine(root, "src", "First", "First.csproj");
        var secondProject = Path.Combine(root, "src", "Second", "Second.csproj");
        var linkedFile = Path.Combine(root, "src", "Shared", "Linked.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(firstProject)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondProject)!);
        Directory.CreateDirectory(Path.GetDirectoryName(linkedFile)!);
        await File.WriteAllTextAsync(solution,
            "<Solution><Project Path=\"src/First/First.csproj\" /><Project Path=\"src/Second/Second.csproj\" /></Solution>");
        const string projectText = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><Compile Include=\"../Shared/Linked.cs\" Link=\"Linked.cs\" /></ItemGroup></Project>";
        await File.WriteAllTextAsync(firstProject, projectText);
        await File.WriteAllTextAsync(secondProject, projectText);
        await File.WriteAllTextAsync(linkedFile,
            "namespace LinkedProbe; public sealed class SharedRoot { public SharedDependency Value { get; set; } = new(); } public sealed class SharedDependency { }");
        var nugetConfig = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(nugetConfig, "<configuration><packageSources><clear /></packageSources></configuration>");
        await FixtureRestore.RunAsync(solution, root, nugetConfig, "Linked-file owner fixture restore");
        return (solution, linkedFile, Path.GetFullPath(firstProject).Replace('\\', '/'), Path.GetFullPath(secondProject).Replace('\\', '/'));
    }

    private static async Task<(string Solution, string SelectedFile, string PartialFile)> CreateNamedTypeFileSolutionAsync(string root)
    {
        var solution = Path.Combine(root, "NamedTypes.slnx");
        var projectDirectory = Path.Combine(root, "src", "App");
        var selectedFile = Path.Combine(projectDirectory, "Root.cs");
        var partialFile = Path.Combine(projectDirectory, "Outer.Partial.cs");
        Directory.CreateDirectory(projectDirectory);
        await File.WriteAllTextAsync(solution, "<Solution><Project Path=\"src/App/NamedTypes.csproj\" /></Solution>");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "NamedTypes.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(selectedFile, """
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
        await File.WriteAllTextAsync(partialFile,
            "namespace RootProbe; public sealed partial class Outer { public Dependency PartialValue { get; set; } = new(); }");
        var nugetConfig = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(nugetConfig, "<configuration><packageSources><clear /></packageSources></configuration>");
        await FixtureRestore.RunAsync(solution, root, nugetConfig, "Named-type file-root fixture restore");
        return (solution, selectedFile, partialFile);
    }
}
