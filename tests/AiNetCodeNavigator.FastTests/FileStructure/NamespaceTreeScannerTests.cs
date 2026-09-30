#nullable enable

using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.FileStructure;
using AiNetCodeNavigator.TestKit.Builders;
using AiNetCodeNavigator.TestKit.Fixtures;
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
        Assert.Equal("Select a single project to narrow the namespace tree.", payload.NextAction);
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

        var success = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution);
        var error = await NamespaceTreeScanner.ScanSolutionAsync(fixture.Solution, "Missing.Project");

        Assert.Contains("1 namespaces total, 1 shown | 1 types", success.FormattedText);
        Assert.Equal("Project 'Missing.Project' was not found.", error.Error);
        Assert.Contains("Project 'Missing.Project' was not found.", error.FormattedText);
        Assert.DoesNotContain("Typen", success.FormattedText);
        Assert.DoesNotContain("Gekürzt", success.FormattedText);
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
