#nullable enable

using System.Linq;
using System.IO;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.CallTree;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    public async Task BuildGraphAsync_IncomingCalls_PreservesDirectAndMutualRecursiveCallSites()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution("""
            namespace Calls;
            public sealed class Caller
            {
                public void First() { First(); First(); Second(); }
                public void Second() { First(); First(); }
            }
            """);
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var caller = compilation.GetTypeByMetadataName("Calls.Caller")!;
        var first = caller.GetMembers("First").OfType<IMethodSymbol>().Single();

        var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, first, RequestedDepth: 3, Direction: CallTreeDirection.Incoming));

        var recursive = Assert.Single(graph.Edges.Where(edge =>
            edge.FromNodeId == graph.RootNodeId && edge.ToNodeId == graph.RootNodeId));
        Assert.Equal(2, recursive.CallSites.Count);
        Assert.Equal(2, recursive.CallSites.Select(site => site.Column).Distinct().Count());
        var mutual = Assert.Single(graph.Edges.Where(edge =>
            edge.ToNodeId == graph.RootNodeId && edge.FromNodeId != graph.RootNodeId));
        Assert.Equal(2, mutual.CallSites.Count);
        Assert.False(graph.Truncated);
    }

    [Fact]
    public async Task BuildGraphAsync_OutgoingCalls_DistinguishesSameLineCallSites()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution("""
            namespace Calls;
            public sealed class Caller { public void Invoke(Target target) { target.Run(); target.Run(); } }
            public sealed class Target { public void Run() { } }
            """);
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var caller = compilation.GetTypeByMetadataName("Calls.Caller")!;
        var invoke = caller.GetMembers("Invoke").OfType<IMethodSymbol>().Single();

        var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, invoke, Direction: CallTreeDirection.Outgoing));

        var edge = Assert.Single(graph.Edges);
        Assert.Equal(2, edge.CallSites.Count);
        Assert.Equal(2, edge.CallSites.Distinct().Count());
        Assert.Equal(2, edge.CallSites.Select(site => site.Column).Distinct().Count());
    }

    [Fact]
    public async Task BuildGraphAsync_OutgoingCalls_DoesNotGuessAmongAmbiguousOverloads()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution("""
            namespace Calls;
            public sealed class Caller { public void Invoke() { Target.Run(null); } }
            public static class Target
            {
                public static void Run(string? value) { }
                public static void Run(System.Uri? value) { }
            }
            """);
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var caller = compilation.GetTypeByMetadataName("Calls.Caller")!;
        var invoke = caller.GetMembers("Invoke").OfType<IMethodSymbol>().Single();

        var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, invoke, Direction: CallTreeDirection.Outgoing));

        Assert.DoesNotContain(graph.Nodes, node => node.Name == "Target.Run");
        var ambiguousSite = Assert.Single(graph.UnresolvedCallSites!);
        Assert.Equal(RelationshipEvidence.PossibleTarget, ambiguousSite.EvidenceKind);
        Assert.Equal(2, ambiguousSite.CandidateTargets.Count);
        var stringOverload = compilation.GetTypeByMetadataName("Calls.Target")!.GetMembers("Run").OfType<IMethodSymbol>()
            .Single(member => member.Parameters[0].Type.SpecialType == SpecialType.System_String);
        var ambiguousReferences = await FindReferencesResolver.FindReferencesAsync(stringOverload, fixture.Solution, 50, 1);
        var reference = Assert.Single(ambiguousReferences.References);
        Assert.Equal(RelationshipEvidence.PossibleTarget, reference.EvidenceKind);
        Assert.Equal(ambiguousSite.CandidateTargets, reference.CandidateTargets);

        Assert.Contains(ambiguousSite.CandidateTargets, target => target.Contains("Target.Run(string?", System.StringComparison.Ordinal));
        Assert.Contains(ambiguousSite.CandidateTargets, target => target.Contains("Target.Run(Uri?", System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task RelationshipSites_DistinguishInvocationsFromVirtualMethodGroupsAndPropertyUses()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution("""
            namespace Calls;
            public interface IWorker { void Work(); int Value { get; } }
            public class Worker : IWorker { public virtual void Work() { } public virtual int Value => 1; }
            public sealed class Caller
            {
                public void Execute(IWorker contract, Worker worker)
                {
                    contract.Work();
                    worker.Work();
                    System.Action contractGroup = contract.Work;
                    System.Action virtualGroup = worker.Work;
                    _ = contract.Value;
                    _ = worker.Value;
                    _ = new Worker();
                }
            }
            """);
        var project = fixture.Solution.Projects.Single();
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var method = compilation.GetTypeByMetadataName("Calls.Caller")!.GetMembers("Execute").OfType<IMethodSymbol>().Single();
        var contractMethod = compilation.GetTypeByMetadataName("Calls.IWorker")!.GetMembers("Work").OfType<IMethodSymbol>().Single();
        var references = await FindReferencesResolver.FindReferencesAsync(contractMethod, fixture.Solution, 50, 1);
        Assert.Contains(references.References, site => site.Snippet.Contains("contract.Work();", System.StringComparison.Ordinal)
            && site.EvidenceKind == RelationshipEvidence.StaticVirtualOrInterfaceTarget);
        Assert.Contains(references.References, site => site.Snippet.Contains("contractGroup", System.StringComparison.Ordinal)
            && site.EvidenceKind == RelationshipEvidence.MemberAccess);

        var outgoing = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(fixture.Solution, method,
            Direction: CallTreeDirection.Outgoing));
        Assert.Equal(6, outgoing.NodeCount);
        Assert.Equal(5, outgoing.EdgeCount);
        Assert.Equal(7, outgoing.EdgeSiteCount);
        Assert.Equal(0, outgoing.UnresolvedSiteCount);
        Assert.Contains("Graph: 6 nodes, 5 edges, 7 edge sites, 0 candidate/unresolved sites.", CallGraphTextRenderer.RenderAscii(outgoing));
        Assert.Contains("Graph: 6 nodes, 5 edges, 7 edge sites, 0 candidate/unresolved sites.", CallTreeMermaidRenderer.RenderMermaid(outgoing));
        var text = await project.Documents.Single().GetTextAsync();
        var sites = outgoing.Edges.SelectMany(edge => edge.CallSites).ToArray();
        Assert.Contains(sites, site => text.Lines[site.Line - 1].ToString().Contains("virtualGroup", System.StringComparison.Ordinal)
            && site.EvidenceKind == RelationshipEvidence.MemberAccess);
        Assert.Equal(2, sites.Count(site => text.Lines[site.Line - 1].ToString().Contains(".Value", System.StringComparison.Ordinal)
            && site.EvidenceKind == RelationshipEvidence.MemberAccess));
        Assert.Equal(2, sites.Count(site => site.EvidenceKind == RelationshipEvidence.StaticVirtualOrInterfaceTarget));
        Assert.Contains(sites, site => text.Lines[site.Line - 1].ToString().Contains("new Worker()", System.StringComparison.Ordinal)
            && site.EvidenceKind == RelationshipEvidence.Call);
        var incoming = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(fixture.Solution, contractMethod,
            Direction: CallTreeDirection.Incoming));
        Assert.Equal(2, incoming.NodeCount);
        Assert.Equal(1, incoming.EdgeCount);
        Assert.Equal(4, incoming.EdgeSiteCount);
        Assert.Contains(incoming.Edges.SelectMany(edge => edge.CallSites), site =>
            text.Lines[site.Line - 1].ToString().Contains("contractGroup", System.StringComparison.Ordinal)
            && site.EvidenceKind == RelationshipEvidence.MemberAccess);
    }

    [Fact]
    public async Task BuildGraphAsync_OutgoingCalls_ReportsCallMemberVirtualAndUnresolvedEvidence()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution("""
            namespace Calls;
            public interface IWorker { void Work(); }
            public class BaseWorker { public virtual void Run() { } }
            public sealed class Worker : BaseWorker, IWorker { public void Work() { } public int Value => 1; }
            public sealed class Box { public Box(int value) { } }
            public sealed class Operation { public void Execute() { } }
            public sealed class Holder { public Operation Value => new(); }
            public sealed class Caller
            {
                public void Invoke(IWorker contract, BaseWorker baseWorker, Worker worker, Holder holder)
                {
                    contract.Work(); baseWorker.Run(); worker.Work(); _ = worker.Value;
                    Consume(worker.Value); Consume(worker.Work); _ = new Box(worker.Value);
                    holder.Value.Execute(); Missing();
                }
                private void Consume(int value) { }
                private void Consume(System.Action action) { }
                private void MissingOverload() { Target.Run(null); }
            }
            public static class Target
            {
                public static void Run(string? value) { }
                public static void Run(System.Uri? value) { }
            }
            """);
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var caller = compilation.GetTypeByMetadataName("Calls.Caller")!;
        var invoke = caller.GetMembers("Invoke").OfType<IMethodSymbol>().Single();
        var ambiguous = caller.GetMembers("MissingOverload").OfType<IMethodSymbol>().Single();

        var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, invoke, Direction: CallTreeDirection.Outgoing));
        var ambiguousGraph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, ambiguous, Direction: CallTreeDirection.Outgoing));

        Assert.Contains(graph.Edges.SelectMany(edge => edge.CallSites), site => site.EvidenceKind == RelationshipEvidence.Call);
        Assert.Contains(graph.Edges.SelectMany(edge => edge.CallSites), site => site.EvidenceKind == RelationshipEvidence.MemberAccess);
        Assert.Contains(graph.Edges.SelectMany(edge => edge.CallSites), site => site.EvidenceKind == RelationshipEvidence.StaticVirtualOrInterfaceTarget);
        Assert.Contains(graph.Edges, edge => graph.Nodes.Single(node => node.NodeId == edge.ToNodeId).Name == "Worker.Work"
            && edge.CallSites.Any(site => site.EvidenceKind == RelationshipEvidence.MemberAccess));
        Assert.Contains(graph.Edges, edge => graph.Nodes.Single(node => node.NodeId == edge.ToNodeId).Name == "Holder.Value"
            && edge.CallSites.Any(site => site.EvidenceKind == RelationshipEvidence.MemberAccess));
        Assert.Contains(graph.UnresolvedCallSites!, site => site.EvidenceKind == RelationshipEvidence.Unresolved);
        Assert.Contains(ambiguousGraph.UnresolvedCallSites!, site => site.EvidenceKind == RelationshipEvidence.PossibleTarget);
        Assert.Contains("possible targets", CallGraphTextRenderer.RenderAscii(ambiguousGraph), System.StringComparison.Ordinal);
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
    public async Task BuildGraphAsync_OutgoingCalls_ExcludesBclButKeepsThirdPartyMetadataByDefault()
    {
        var thirdPartyCompilation = CSharpCompilation.Create(
            "ThirdParty.Library",
            [CSharpSyntaxTree.ParseText("namespace ThirdParty; public static class Client { public static void Send() { } }")],
            TestWorkspaceBuilder.CoreReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assemblyImage = new MemoryStream();
        var emitResult = thirdPartyCompilation.Emit(assemblyImage);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        using var fixture = TestWorkspaceBuilder.CreateSolution(new ProjectSpec(
            "App",
            [("Calls.cs", "namespace Calls; public sealed class Caller { public void Run() { ThirdParty.Client.Send(); System.Console.WriteLine(\"sent\"); } }")],
            AdditionalReferences: [
                MetadataReference.CreateFromImage(assemblyImage.ToArray()),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)]));
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var caller = compilation.GetTypeByMetadataName("Calls.Caller");
        Assert.NotNull(caller);
        var run = caller.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var defaultGraph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, run, Direction: CallTreeDirection.Outgoing));
        var includeBclGraph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, run, Direction: CallTreeDirection.Outgoing, IncludeBcl: true));

        Assert.Contains(defaultGraph.Nodes, node => node.Name == "Client.Send");
        Assert.DoesNotContain(defaultGraph.Nodes, node => node.Name.Contains("Console.WriteLine", System.StringComparison.Ordinal));
        Assert.Contains(includeBclGraph.Nodes, node => node.Name == "Client.Send");
        Assert.Contains(includeBclGraph.Nodes, node => node.Name.Contains("Console.WriteLine", System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildGraphAsync_OutgoingCalls_RetainsSourceSymbolsInFrameworkNamedNamespaces()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution("""
            namespace System.Local
            {
                public static class Api { public static void Call() { } }
            }
            namespace Calls
            {
                public sealed class Caller { public void Run() { System.Local.Api.Call(); } }
            }
            """);
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var caller = compilation.GetTypeByMetadataName("Calls.Caller");
        Assert.NotNull(caller);
        var run = caller.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, run, Direction: CallTreeDirection.Outgoing));

        Assert.Contains(graph.Nodes, node => node.Name == "Api.Call");
    }

    [Fact]
    public async Task BuildGraphAsync_BothDirection_TopNLimitsCombinedIncidentEdges()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution("""
            namespace Calls;
            public sealed class Target { public void Run() { new Helper().Work(); } }
            public sealed class Helper { public void Work() { } }
            public sealed class Caller { public void Invoke(Target target) { target.Run(); } }
            """);
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Calls.Target");
        Assert.NotNull(target);
        var run = target.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, run, TopN: 1, Direction: CallTreeDirection.Both));

        var incidentEdges = graph.Edges.Where(edge => edge.FromNodeId == graph.RootNodeId || edge.ToNodeId == graph.RootNodeId).ToList();
        Assert.Single(incidentEdges);
        Assert.Equal("Caller.Invoke", graph.Nodes.Single(node => node.NodeId == incidentEdges[0].FromNodeId).Name);
        Assert.True(graph.Truncated);
    }

    [Fact]
    public async Task BuildGraphAsync_Exactly250CompletedNodesAreNotTruncated()
    {
        var callers = string.Join("\n", Enumerable.Range(0, CallTreeBuilder.MaxCallTreeNodes - 1)
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
            fixture.Solution, run, RequestedDepth: 1, TopN: CallTreeBuilder.MaxCallTreeNodes, Direction: CallTreeDirection.Incoming));

        Assert.Equal(CallTreeBuilder.MaxCallTreeNodes, graph.Nodes.Count);
        Assert.False(graph.Truncated);
        Assert.Equal(0, graph.HiddenEdgeCount);
        Assert.Equal(0, graph.PendingNodeCount);
    }

    [Fact]
    public async Task BuildGraphAsync_NodeCapReportsQueuedUnexpandedNodesWithoutGuessingHiddenEdges()
    {
        var callers = string.Join("\n", Enumerable.Range(0, CallTreeBuilder.MaxCallTreeNodes - 1)
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
            fixture.Solution, run, RequestedDepth: 2, TopN: CallTreeBuilder.MaxCallTreeNodes, Direction: CallTreeDirection.Incoming));

        Assert.Equal(CallTreeBuilder.MaxCallTreeNodes, graph.Nodes.Count);
        Assert.True(graph.Truncated);
        Assert.Equal(0, graph.HiddenEdgeCount);
        Assert.Equal(CallTreeBuilder.MaxCallTreeNodes - 1, graph.PendingNodeCount);
        Assert.Contains("249 nodes not yet explored", CallGraphTextRenderer.RenderAscii(graph));
        Assert.Contains("249 nodes not explored", CallTreeMermaidRenderer.RenderMermaid(graph));
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
        Assert.All(graph.Nodes.Where(n => n.HandoffId is not null), node => Assert.StartsWith("src:", node.HandoffId));
        Assert.StartsWith("src:", graph.Nodes.Single(n => n.NodeId == graph.RootNodeId).HandoffId);

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
