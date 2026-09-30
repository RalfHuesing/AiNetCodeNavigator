#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.CallTree;
using AiNetCodeNavigator.TestKit.Builders;
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
    public async Task BuildGraphAsync_OutgoingCalls_IncludesObjectCreationAndMemberAccess()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution("""
            namespace Calls;
            public sealed class Callee { public static Callee Instance { get; } = new(); }
            public sealed class Caller
            {
                public void Invoke()
                {
                    _ = new Callee();
                    var value = Callee.Instance;
                }
            }
            """);
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var caller = compilation.GetTypeByMetadataName("Calls.Caller");
        Assert.NotNull(caller);
        var invoke = caller.GetMembers("Invoke").OfType<IMethodSymbol>().Single();

        var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, invoke, Direction: CallTreeDirection.Outgoing));

        Assert.Contains(graph.Nodes, node => node.Name == "Callee");
        Assert.Contains(graph.Nodes, node => node.Name == "Callee.Instance");
    }

    [Fact]
    public async Task BuildGraphAsync_EnforcesNodeCapAndReportsTruncation()
    {
        var callers = string.Join("\n", Enumerable.Range(0, CallTreeBuilder.MaxCallTreeNodes + 5)
            .Select(index => $"public sealed class Caller{index} {{ public void Invoke(Target target) => target.Run(); }}"));
        using var fixture = TestWorkspaceBuilder.CreateSolution($$"""
            namespace Calls;
            public sealed class Target { public void Run() { } }
            {{callers}}
            """);
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Calls.Target");
        Assert.NotNull(target);
        var run = target.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, run, TopN: CallTreeBuilder.MaxCallTreeNodes + 5, Direction: CallTreeDirection.Incoming));

        Assert.True(graph.Truncated);
        Assert.InRange(graph.Nodes.Count, 1, CallTreeBuilder.MaxCallTreeNodes);
        Assert.True(graph.HiddenEdgeCount > 0);
    }

    [Fact]
    public async Task BuildGraphAsync_ClampsNonPositiveTopNToOne()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);
        var greeter = coreCompilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeter);
        var greet = greeter.GetMembers("Greet").OfType<IMethodSymbol>().Single();

        var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, greet, TopN: 0, Direction: CallTreeDirection.Incoming));

        Assert.Contains(graph.Edges, edge => edge.FromNodeId != graph.RootNodeId);
        Assert.True(graph.Truncated);
        Assert.True(graph.HiddenEdgeCount > 0);
    }

    [Fact]
    public async Task BuildGraphAsync_IncomingCalls_ExcludesOverrideDeclarations()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution("""
            namespace Calls;
            public class Base { public virtual void Run() { } }
            public sealed class Derived : Base { public override void Run() { } }
            public sealed class Caller { public void Invoke() { new Derived().Run(); } }
            """);
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var baseType = compilation.GetTypeByMetadataName("Calls.Base");
        Assert.NotNull(baseType);
        var run = baseType.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, run, Direction: CallTreeDirection.Incoming));

        Assert.Contains(graph.Nodes, node => node.Name == "Caller.Invoke");
        Assert.DoesNotContain(graph.Nodes, node => node.Name == "Derived.Run");
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
        Assert.All(graph.Nodes.Where(n => n.HandoffId is not null), node => Assert.StartsWith("h:", node.HandoffId));
        Assert.StartsWith("h:", graph.Nodes.Single(n => n.NodeId == graph.RootNodeId).HandoffId);

        var mermaid = CallTreeMermaidRenderer.RenderMermaid(graph);
        Assert.Contains("flowchart TD", mermaid);
        Assert.Contains("Greeter.Greet", mermaid);
        Assert.Contains("-->", mermaid);
        Assert.Contains("handoffId:", mermaid);
    }

    [Fact]
    public void RenderMermaid_EscapesQuotesAndLineBreaksInNodeLabels()
    {
        var graph = new CallGraphPayload(
            "n1",
            [new CallGraphNode("n1", "symbol", "Name\"]\n    evil[\"node", "", "method")],
            []);

        var mermaid = CallTreeMermaidRenderer.RenderMermaid(graph);

        Assert.DoesNotContain("\n    evil", mermaid);
        Assert.DoesNotContain("\"node\"", mermaid);
        Assert.Contains("Name'", mermaid);
    }

    [Fact]
    public async Task BuildGraphAsync_RejectsNullRequestAndRenderersRejectNullPayload()
    {
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => CallTreeBuilder.BuildGraphAsync(null!));
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => CallTreeBuilder.BuildGraphAsync(
            new CallTreeBuildRequest(null!, null!)));
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => CallTreeBuilder.BuildGraphAsync(
            new CallTreeBuildRequest(fixture.Solution, null!)));
        Assert.Throws<System.ArgumentNullException>(() => CallGraphTextRenderer.RenderAscii(null!));
        Assert.Throws<System.ArgumentNullException>(() => CallTreeMermaidRenderer.RenderMermaid(null!));
    }
}
