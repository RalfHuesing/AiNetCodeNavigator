#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.FileStructure;
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
}
