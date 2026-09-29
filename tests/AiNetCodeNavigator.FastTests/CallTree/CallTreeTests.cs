#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.CallTree;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Xunit;

namespace AiNetCodeNavigator.FastTests.CallTree;

[Trait("Category", "Unit")]
public sealed class CallTreeTests
{
    [Fact]
    public async Task BuildGraphAsync_IncomingCalls_TraversesCallers()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);

        var greeterType = coreCompilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);

        var greetMethod = greeterType.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var request = new CallTreeBuildRequest(
            Solution: fixture.Solution,
            SeedSymbol: greetMethod,
            RequestedDepth: 2,
            Direction: CallTreeDirection.Incoming);

        var graph = await CallTreeBuilder.BuildGraphAsync(request);

        Assert.NotEmpty(graph.Nodes);
        Assert.NotEmpty(graph.Edges);

        // Root is Greeter.Greet
        var root = graph.Nodes.First(n => n.NodeId == graph.RootNodeId);
        Assert.Equal("Greeter.Greet", root.Name);

        // Callers from ServiceCaller
        Assert.Contains(graph.Nodes, n => n.Name.Contains("ServiceCaller.ExecuteSingle"));
        Assert.Contains(graph.Nodes, n => n.Name.Contains("ServiceCaller.ExecuteMultiple"));
    }

    [Fact]
    public async Task BuildGraphAsync_OutgoingCalls_TraversesCallees()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var appCompilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.App").GetCompilationAsync();
        Assert.NotNull(appCompilation);

        var callerType = appCompilation.GetTypeByMetadataName("SampleNamespace.ServiceCaller");
        Assert.NotNull(callerType);

        var executeMultiple = callerType.GetMembers("ExecuteMultiple").OfType<IMethodSymbol>().First();

        var request = new CallTreeBuildRequest(
            Solution: fixture.Solution,
            SeedSymbol: executeMultiple,
            RequestedDepth: 2,
            Direction: CallTreeDirection.Outgoing);

        var graph = await CallTreeBuilder.BuildGraphAsync(request);

        Assert.NotEmpty(graph.Nodes);
        Assert.NotEmpty(graph.Edges);

        // Outgoing callees: Greet, GreetLoud
        Assert.Contains(graph.Nodes, n => n.Name.Contains("Greeter.Greet"));
        Assert.Contains(graph.Nodes, n => n.Name.Contains("Greeter.GreetLoud"));
    }

    [Fact]
    public async Task RenderAsciiAndMermaid_FormatsExpectedOutput()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);

        var greeterType = coreCompilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);

        var greetMethod = greeterType.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var request = new CallTreeBuildRequest(
            Solution: fixture.Solution,
            SeedSymbol: greetMethod,
            Direction: CallTreeDirection.Incoming);

        var graph = await CallTreeBuilder.BuildGraphAsync(request);

        var ascii = CallGraphTextRenderer.RenderAscii(graph);
        Assert.Contains("[n1] Greeter.Greet", ascii);
        Assert.Contains("ServiceCaller.ExecuteSingle", ascii);

        var mermaid = CallTreeMermaidRenderer.RenderMermaid(graph);
        Assert.Contains("flowchart TD", mermaid);
        Assert.Contains("Greeter.Greet", mermaid);
        Assert.Contains("-->", mermaid);
    }
}
