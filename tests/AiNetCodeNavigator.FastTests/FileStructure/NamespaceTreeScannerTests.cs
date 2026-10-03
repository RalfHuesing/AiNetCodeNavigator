#nullable enable

using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.FileStructure;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace AiNetCodeNavigator.FastTests.FileStructure;

[Trait("Category", "Unit")]
public sealed class NamespaceTreeScannerTests
{
    [Fact]
    public async Task ScanSolutionAsync_BuildsHierarchicalNamespaceTree()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var payload = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution);

        Assert.NotEmpty(payload.RootNamespaces);
        Assert.True(payload.TotalNamespaces >= 3);
        Assert.True(payload.TotalTypes >= 5);

        // Root is SampleNamespace
        var root = payload.RootNamespaces.FirstOrDefault(n => n.Name == "SampleNamespace");
        Assert.NotNull(root);
        Assert.True(root.TypeCount > 0); // Greeter, ServiceCaller

        // Children: Hierarchy, Types, Extensions
        Assert.Contains(root.Children, c => c.Name == "Hierarchy");
        Assert.Contains(root.Children, c => c.Name == "Types");
        Assert.Contains(root.Children, c => c.Name == "Extensions");

        // Formatted output
        Assert.Contains("# Namespace Tree:", payload.FormattedText);
        Assert.Contains("- SampleNamespace", payload.FormattedText);
        Assert.Contains("Hierarchy", payload.FormattedText);
    }

    [Fact]
    public async Task ScanSolutionAsync_ReturnsBoundedProjectOverviewWhenRequested()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var payload = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution,
            options: new NamespaceTreeScanOptions(IncludeProjectOverview: true));

        Assert.Empty(payload.RootNamespaces);
        Assert.Equal(fixture.Solution.Projects.Count(project => project.Language == LanguageNames.CSharp), payload.TotalProjects);
        Assert.Equal(payload.TotalProjects, payload.Projects!.Count);
        Assert.Contains("Projects:", payload.FormattedText, StringComparison.Ordinal);
        Assert.Contains("namespaces", payload.FormattedText, StringComparison.Ordinal);
        Assert.All(payload.Projects, project => Assert.True(project.NamespaceCount > 0));
    }

    [Fact]
    public async Task ScanSolutionAsync_AppliesPrefixKindAndIncludeTypesOptions()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var filtered = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution, options: new NamespaceTreeScanOptions(
            MaxDepth: 4,
            MaxResults: 50,
            NamespacePrefix: "SampleNamespace.Hierarchy",
            Kind: "class"));
        var root = Assert.Single(filtered.RootNamespaces);
        Assert.Equal("SampleNamespace.Hierarchy", root.FullName);
        Assert.True(filtered.TotalTypes > 0);
        Assert.All(Descendants(filtered.RootNamespaces), node => Assert.StartsWith("SampleNamespace.Hierarchy", node.FullName, StringComparison.Ordinal));

        var hiddenTypes = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution, options: new NamespaceTreeScanOptions(
            MaxDepth: 4,
            MaxResults: 50,
            NamespacePrefix: "SampleNamespace.Hierarchy",
            Kind: "class",
            IncludeTypes: false));
        Assert.Equal(Assert.Single(filtered.RootNamespaces).TypeCount, Assert.Single(hiddenTypes.RootNamespaces).TypeCount);
        Assert.Equal(filtered.TotalTypes, hiddenTypes.TotalTypes);

        var missingPrefix = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution, options: new NamespaceTreeScanOptions(
            NamespacePrefix: "SampleNamespace.DoesNotExist"));
        Assert.Contains("Namespace prefix", missingPrefix.Error, StringComparison.Ordinal);

    }

    private static IEnumerable<NamespaceNode> Descendants(IEnumerable<NamespaceNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Descendants(node.Children)) yield return child;
        }
    }

    [Fact]
    public async Task ScanSolutionAsync_CountsDeclaredHierarchyAndCombinesPartialNamespaces()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\NamespaceSolution.slnx",
            new ProjectSpec("First", [
                ("Nested.cs", "namespace Company { namespace Product { public partial class Item {} } }"),
                ("FileScoped.cs", "namespace Company.Product; public partial class Item {}"),
            ]),
            new ProjectSpec("Second", [
                ("Other.cs", "namespace Company.Product.Other; public class OtherType {}"),
            ]));

        var payload = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution);

        var company = Assert.Single(payload.RootNamespaces);
        Assert.Equal("Company", company.FullName);
        var product = Assert.Single(company.Children);
        Assert.Equal("Company.Product", product.FullName);
        Assert.Equal(1, product.TypeCount); // The partial type is one declaration symbol.
        Assert.Single(product.Children);
        Assert.Equal("Company.Product.Other", product.Children[0].FullName);
        Assert.Equal(3, payload.TotalNamespaces); // Includes the synthesized parent namespaces.
        Assert.Equal(2, payload.TotalTypes);
        Assert.Contains("- Product (1 types)", payload.FormattedText);
        Assert.Contains(Descendants(payload.RootNamespaces).SelectMany(node => node.Types),
            entry => entry.Name == "Item");

        var parentNamespace = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution, "First", options: new NamespaceTreeScanOptions(
            MaxDepth: 1,
            NamespacePrefix: "Company"));
        Assert.Null(parentNamespace.Error);
        Assert.Equal("Company", Assert.Single(parentNamespace.RootNamespaces).FullName);
        Assert.Equal(1, parentNamespace.TotalTypes);
    }

    [Fact]
    public async Task ScanSolutionAsync_BoundsTypeEntriesAndRetainsTotalCounts()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\NamespaceTypeLimit.slnx",
            new ProjectSpec("One", [
                ("Types.cs", "namespace Limited; public sealed class Alpha {} public sealed class Beta {} public sealed class Gamma {}"),
            ]));

        var payload = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution, options: new NamespaceTreeScanOptions(
            MaxDepth: 1,
            MaxResults: 1,
            NamespacePrefix: "Limited"));

        var root = Assert.Single(payload.RootNamespaces);
        Assert.Equal(3, payload.TotalTypes);
        Assert.Equal(3, root.TypeCount);
        Assert.Single(root.Types);
        Assert.True(payload.Truncated);
        Assert.Contains("maxResults", payload.TruncatedBy!);

        var withoutTypeEntries = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution, options: new NamespaceTreeScanOptions(
            MaxDepth: 1,
            MaxResults: 1,
            NamespacePrefix: "Limited",
            IncludeTypes: false));
        Assert.Equal(3, withoutTypeEntries.TotalTypes);
        Assert.Equal(3, Assert.Single(withoutTypeEntries.RootNamespaces).TypeCount);
        Assert.Empty(Assert.Single(withoutTypeEntries.RootNamespaces).Types);
    }

    [Fact]
    public async Task ScanSolutionAsync_BoundsNamespaceTreeAndReportsTruncation()
    {
        var documents = new List<(string FileName, string Content)>();
        for (var index = 0; index < 205; index++)
        {
            documents.Add((
                $"Type{index}.cs",
                $"namespace Root.N{index:D3}; public class Type{index} {{}}"));
        }

        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\LargeNamespaceSolution.slnx",
            new ProjectSpec("Large", documents));

        var defaultPayload = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution);
        Assert.True(defaultPayload.Truncated);
        Assert.Equal(50, defaultPayload.ShownNamespaces);
        Assert.Contains("maxResults", defaultPayload.TruncatedBy!);
        Assert.Contains("Increase MaxResults", defaultPayload.NextAction);
        Assert.Contains("Truncated by: maxResults", defaultPayload.FormattedText);
        Assert.Contains("Next step:", defaultPayload.FormattedText);
        Assert.DoesNotContain("Gekürzt", defaultPayload.FormattedText);

        var payload = await NamespaceTreeScanner.ScanSolutionAsync(
            fixture.Solution,
            options: new NamespaceTreeScanOptions(MaxResults: NamespaceTreeScanner.MaxResultsCap));

        Assert.True(payload.TotalNamespaces == 206, payload.FormattedText);
        Assert.Equal(205, payload.TotalTypes);
        Assert.Equal(200, CountNodes(payload.RootNamespaces));
        Assert.Contains("206 namespaces", payload.FormattedText);
        Assert.Contains("200 shown", payload.FormattedText);
    }

    [Fact]
    public async Task ScanSolutionAsync_RejectsUnknownProjectWithRecoverableError()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var payload = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution, "Missing.Project");

        Assert.Contains("Missing.Project", payload.Error);
        Assert.Contains("Project 'Missing.Project' was not found", payload.FormattedText);
        Assert.Empty(payload.RootNamespaces);
    }

    [Fact]
    public async Task ScanSolutionAsync_UsesExactProjectPathToDisambiguateSameNamedProjects()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DuplicateProjectNames.slnx",
            new ProjectSpec("Shared.App", [("First.cs", "namespace FirstOwner; public class FirstType { }")], VirtualProjectDirectory: "src/first"));
        var first = fixture.Solution.Projects.Single();
        var secondId = ProjectId.CreateNewId("Shared.App");
        var withSecond = fixture.Solution.AddProject(ProjectInfo.Create(
                secondId,
                VersionStamp.Create(),
                "Shared.App",
                "Shared.App",
                LanguageNames.CSharp,
                filePath: @"C:\VirtualRepo\src\second\Shared.App.csproj",
                metadataReferences: TestWorkspaceBuilder.CoreReferences))
            .AddDocument(DocumentId.CreateNewId(secondId), "Second.cs",
                SourceText.From("namespace SecondOwner; public class SecondType { }"),
                filePath: @"C:\VirtualRepo\src\second\Second.cs");
        Assert.True(fixture.Workspace.TryApplyChanges(withSecond));

        var ambiguous = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Workspace.CurrentSolution, projectName: "Shared.App");
        Assert.Contains("matches multiple projects", ambiguous.Error, StringComparison.Ordinal);

        var selected = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Workspace.CurrentSolution, projectName: first.FilePath);
        Assert.Null(selected.Error);
        Assert.Contains(selected.RootNamespaces, node => node.Name == "FirstOwner");
        Assert.DoesNotContain(selected.RootNamespaces, node => node.Name == "SecondOwner");
        Assert.Equal(1, selected.TotalTypes);
    }

    [Fact]
    public async Task ScanSolutionAsync_ClampsDeepNamespaceTraversal()
    {
        var source = new StringBuilder();
        for (var index = 0; index < 40; index++)
        {
            source.Append("namespace N").Append(index).Append(".");
        }
        source.AppendLine("N40; public class DeepType {}");

        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\DeepNamespaceSolution.slnx",
            new ProjectSpec("Deep", [("Deep.cs", source.ToString())]));

        var payload = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution);

        Assert.True(MaxDepth(payload.RootNamespaces) == 32, payload.FormattedText);
        Assert.Contains("maxDepth", payload.FormattedText);
        Assert.Contains("namespacePrefix", payload.NextAction, StringComparison.Ordinal);
        Assert.Contains("depth", payload.NextAction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("select a single project", payload.NextAction, StringComparison.OrdinalIgnoreCase);

        var selectedProject = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution, projectName: "Deep",
            options: new NamespaceTreeScanOptions(MaxDepth: 1));
        Assert.Contains("namespacePrefix", selectedProject.NextAction, StringComparison.Ordinal);
        Assert.DoesNotContain("select a project", selectedProject.NextAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanSolutionAsync_CountsTypesBelowDepthLimitWithoutChangingDirectNodeCounts()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\DepthLimitedTypeSolution.slnx",
            new ProjectSpec("DepthLimited", [("DeepType.cs", "namespace A.B; public class T {}") ]));

        var payload = await NamespaceTreeScanner.ScanSolutionAsync(
            fixture.Solution,
            options: new NamespaceTreeScanOptions(MaxDepth: 1));

        var root = Assert.Single(payload.RootNamespaces);
        Assert.Equal("A", root.FullName);
        Assert.Equal(0, root.TypeCount); // T belongs to the omitted child namespace B.
        Assert.Equal(1, payload.TotalTypes); // The target-wide type total includes deeper namespaces.
        Assert.Contains("1 types", payload.FormattedText);
        Assert.Contains("maxDepth", payload.TruncatedBy!);
    }

    [Fact]
    public async Task ScanSolutionAsync_FormatsSuccessTruncationAndErrorsInEnglish()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\EnglishNamespaceSolution.slnx",
            new ProjectSpec("English", [("Type.cs", "namespace English; public class TypeOne {}") ]));

        var success = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution, "  English  ");
        var indexScope = await IndexScopeScanner.ScanAsync(
            fixture.Solution,
            options: new IndexScopeScanOptions(ProjectName: "  English  "));
        var error = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution, "Missing.Project");

        Assert.Equal("English", success.ProjectName);
        Assert.Equal(indexScope.ScopeProjectName, success.ProjectName);
        Assert.Contains("1 namespaces total, 1 shown | 1 types", success.FormattedText);
        Assert.Equal("Project 'Missing.Project' was not found.", error.Error);
        Assert.Contains("Project 'Missing.Project' was not found.", error.FormattedText);
        Assert.DoesNotContain("Typen", success.FormattedText);
        Assert.DoesNotContain("Gekürzt", success.FormattedText);
    }

    [Fact]
    public async Task ScanSolutionAsync_SkipsNonCSharpProjects()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\MixedLanguageNamespaceSolution.slnx",
            new ProjectSpec("CSharp", [("Type.cs", "namespace CSharpOnly; public class CSharpType {}") ]));
        var legacyProjectId = ProjectId.CreateNewId("Legacy");
        var legacyProject = fixture.Solution.AddProject(ProjectInfo.Create(
            legacyProjectId,
            VersionStamp.Create(),
            "Legacy",
            "Legacy",
            LanguageNames.VisualBasic));
        var solution = legacyProject.AddDocument(
            DocumentId.CreateNewId(legacyProjectId),
            "Legacy.vb",
            SourceText.From("Namespace VisualBasicOnly\n Public Class LegacyType\n End Class\nEnd Namespace"));

        var payload = await NamespaceTreeScanner.ScanSolutionAsync(solution);
        var indexScope = await IndexScopeScanner.ScanAsync(solution);
        var unsupported = await NamespaceTreeScanner.ScanSolutionAsync(solution, " Legacy ");

        Assert.Null(payload.Error);
        Assert.Equal(1, payload.TotalTypes);
        Assert.Equal("CSharpOnly", Assert.Single(payload.RootNamespaces).FullName);
        Assert.False(Assert.Single(indexScope.Projects, project => project.Name == "Legacy").IsCSharpProject);
        Assert.Equal("Project 'Legacy' is not a C# project.", unsupported.Error);
    }

    [Fact]
    public async Task ScanSolutionAsync_ExcludesGeneratedOnlyNamespacesByDefaultLikeFindSymbol()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\GeneratedNamespaceSolution.slnx",
            new ProjectSpec("App", [
                ("Handwritten.cs", "namespace Visible; public class HandwrittenType {}"),
                ("Generated.g.cs", "namespace GeneratedOnly; public class GeneratedType {}") ]));

        var defaultTree = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution);
        var includingGenerated = await NamespaceTreeScanner.ScanSolutionAsync(
            fixture.Solution,
            options: new NamespaceTreeScanOptions(IncludeGenerated: true));
        var indexScope = await IndexScopeScanner.ScanAsync(fixture.Solution);
        var defaultSymbol = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "GeneratedType"));
        var includingGeneratedSymbol = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "GeneratedType", IncludeGenerated: true));

        Assert.DoesNotContain(defaultTree.RootNamespaces, node => node.Name == "GeneratedOnly");
        Assert.Equal(1, defaultTree.TotalTypes);
        Assert.Equal(1, indexScope.GeneratedDocumentCount);
        Assert.False(defaultTree.IncludeGenerated);
        Assert.Contains("Generated source: excluded", defaultTree.FormattedText);
        Assert.Contains(includingGenerated.RootNamespaces, node => node.Name == "GeneratedOnly");
        Assert.Equal(2, includingGenerated.TotalTypes);
        Assert.True(includingGenerated.IncludeGenerated);
        Assert.Contains("Generated source: included", includingGenerated.FormattedText);
        Assert.Empty(defaultSymbol.Entries);
        Assert.Single(includingGeneratedSymbol.Entries);
    }

    [Fact]
    public async Task ScanSolutionAsync_ClampsRequestedBoundsAndKeepsCompleteSmallResults()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\SmallNamespaceSolution.slnx",
            new ProjectSpec("Small", [("One.cs", "namespace Small; public class One {}") ]));

        var payload = await NamespaceTreeScanner.ScanSolutionAsync(
            fixture.Solution,
            options: new NamespaceTreeScanOptions(MaxDepth: 40, MaxResults: 250));

        Assert.True(payload.BoundsWereClamped);
        Assert.Equal(32, payload.EffectiveMaxDepth);
        Assert.Equal(200, payload.EffectiveMaxResults);
        Assert.False(payload.Truncated);
        Assert.Empty(payload.TruncatedBy!);
        Assert.Equal(1, payload.ShownNamespaces);
    }

    [Fact]
    public async Task ScanSolutionAsync_HonorsCancellationAndLeavesDocumentTextUnchanged()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\ReadOnlyNamespaceSolution.slnx",
            new ProjectSpec("ReadOnly", [("Source.cs", "namespace ReadOnly; public class Source {}") ]));
        var document = fixture.Solution.Projects.Single().Documents.Single();
        var before = await document.GetTextAsync();

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution, ct: cancellation.Token));

        await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution);
        var after = await document.GetTextAsync();
        Assert.Equal(before.ToString(), after.ToString());
    }

    private static int CountNodes(IReadOnlyList<NamespaceNode> nodes) =>
        nodes.Sum(node => 1 + CountNodes(node.Children));

    private static int MaxDepth(IReadOnlyList<NamespaceNode> roots)
    {
        return roots.Count == 0 ? 0 : roots.Max(node => 1 + MaxDepth(node.Children));
    }
}
