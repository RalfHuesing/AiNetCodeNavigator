#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Dependencies;

[Trait("Category", "Unit")]
public sealed class DependencyGraphScannerTests
{
    [Fact]
    public async Task ScanSolutionAsync_FindsProjectAndNamespaceDependencies()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var graph = await DependencyGraphBuilder.BuildForSolutionAsync(fixture.Solution);

        // Project dependencies: Sample.App depends on Sample.Core
        Assert.NotEmpty(graph.ProjectDependencies);
        Assert.Contains(graph.ProjectDependencies, p => p.FromProject == "Sample.App" && p.ToProject == "Sample.Core");

        // File dependencies: Caller.cs references Greeter
        Assert.NotEmpty(graph.FileDependencies);
        Assert.Contains(graph.FileDependencies, f => f.CrossingTypes.Contains("Greeter"));
    }
}
