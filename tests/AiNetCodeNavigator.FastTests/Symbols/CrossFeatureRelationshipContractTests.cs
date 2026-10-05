#nullable enable

using System.Linq;
using System.IO;
using System.Threading.Tasks;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.Core.CallTree;
using AiNetCodeNavigator.Core.Hierarchy;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class CrossFeatureRelationshipContractTests
{
    [Fact]
    public async Task RelationshipEngines_AgreeAcrossProjectsAndKeepHandoffsNavigable()
    {
        using var fixture = CreateRelationshipSolution();
        var contracts = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        var middle = await fixture.Solution.Projects.Single(project => project.Name == "Middle").GetCompilationAsync();
        var app = await fixture.Solution.Projects.Single(project => project.Name == "App").GetCompilationAsync();
        Assert.NotNull(contracts);
        Assert.NotNull(middle);
        Assert.NotNull(app);

        var apiRecord = contracts.GetTypeByMetadataName("Contracts.Api")!.GetMembers("Record").OfType<IMethodSymbol>().Single();
        var handlerContract = contracts.GetTypeByMetadataName("Contracts.IHandler")!;
        var handlerContractMethod = handlerContract.GetMembers("Handle").OfType<IMethodSymbol>().Single();
        var handlerBase = contracts.GetTypeByMetadataName("Contracts.HandlerBase")!;
        var abstractHandle = handlerBase.GetMembers("Handle").OfType<IMethodSymbol>().Single();
        var handler = middle.GetTypeByMetadataName("Middle.Handler")!;
        var entry = app.GetTypeByMetadataName("App.Entry")!;
        var start = entry.GetMembers("Start").OfType<IMethodSymbol>().Single();

        var callers = await FindReferencesResolver.FindReferencesAsync(apiRecord, fixture.Solution, maxResults: 50, depth: 3);
        var impact = await FindReferencesResolver.FindReferencesAsync(apiRecord, fixture.Solution, depth: 3, maxResults: 50, includeSummary: true);
        Assert.Equal(callers.TotalCount, impact.Summary!.TotalReferenceSiteCount);
        Assert.False(callers.IsTruncated);
        Assert.False(callers.IsTruncatedByNodeLimit);
        Assert.False(callers.IsDepthClamped);
        Assert.True(callers.IsComplete);
        Assert.Equal(3, callers.EffectiveDepth);
        Assert.Equal(6, callers.TotalCount);
        Assert.Equal(6, impact.Summary!.TotalReferenceSiteCount);
        Assert.Equal(3, impact.Summary!.DirectReferenceSiteCount);
        Assert.Equal(3, impact.Summary!.DeeperReferenceSiteCount);
        Assert.Equal(3, impact.EffectiveDepth);
        Assert.True(impact.IsComplete);

        var callerSites = callers.References
            .Select(site => (site.ProjectName, site.FilePath, site.Line, site.Column, site.EnclosingSymbolName, site.EnclosingSymbolHandoffId, site.Depth, site.ReachedFromSymbolHandoffId))
            .ToArray();
        var impactSites = impact.References
            .Select(site => (site.ProjectName, site.FilePath, site.Line, site.Column, site.EnclosingSymbolName, site.EnclosingSymbolHandoffId, site.Depth, site.ReachedFromSymbolHandoffId))
            .ToArray();
        Assert.Equal(callerSites, impactSites);
        Assert.Contains(impact.References, site => site.Depth == 1 && site.ProjectName == "Middle" && site.EnclosingSymbolName == "Handler.Handle");
        Assert.Contains(impact.References, site => site.Depth == 1 && site.ProjectName == "Middle" && site.EnclosingSymbolName == "AlternateHandler.Handle");
        var repeatedCalls = impact.References.Where(site => site.Depth == 1 && site.EnclosingSymbolName == "Handler.Handle").ToList();
        Assert.Equal(2, repeatedCalls.Count);
        Assert.Equal(2, repeatedCalls.Select(site => site.Column).Distinct().Count());
        Assert.Equal(2, impact.References.Count(site => site.Depth == 2 && site.ProjectName == "Middle" && site.EnclosingSymbolName == "Dispatcher.Dispatch"));
        Assert.Contains(impact.References, site => site.Depth == 3 && site.ProjectName == "App" && site.EnclosingSymbolName == "Entry.Start");

        foreach (var site in impact.References)
        {
            Assert.StartsWith("src:", site.EnclosingSymbolHandoffId);
            Assert.StartsWith("src:", site.ReachedFromSymbolHandoffId);
            var callerResolution = await SourceSymbolResolver.ResolveAsync(fixture.Solution, site.EnclosingSymbolHandoffId!);
            var originResolution = await SourceSymbolResolver.ResolveAsync(fixture.Solution, site.ReachedFromSymbolHandoffId!);
            Assert.True(callerResolution.IsSuccess);
            Assert.True(originResolution.IsSuccess);
            Assert.Equal(site.ProjectName, callerResolution.Symbol!.ContainingAssembly!.Name);
            Assert.Equal(site.ReachedFromSymbolId, DocumentationCommentId.CreateDeclarationId(originResolution.Symbol!));
        }

        var callTree = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, start, RequestedDepth: 3, TopN: 10, Direction: CallTreeDirection.Outgoing));
        var incomingCallTree = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, apiRecord, RequestedDepth: 3, TopN: 10, Direction: CallTreeDirection.Incoming));
        Assert.False(callTree.Truncated);
        Assert.False(incomingCallTree.Truncated);
        Assert.Contains(callTree.Nodes, node => node.Name == "Dispatcher.Dispatch");
        Assert.Contains(callTree.Nodes, node => node.Name == "Handler.Handle");
        Assert.Contains(callTree.Nodes, node => node.Name == "Api.Record");
        Assert.Contains(incomingCallTree.Nodes, node => node.Name == "AlternateHandler.Handle");
        Assert.Contains(incomingCallTree.Nodes, node => node.Name == "Handler.Handle");
        Assert.Contains(incomingCallTree.Nodes, node => node.Name == "Dispatcher.Dispatch");
        Assert.Contains(incomingCallTree.Nodes, node => node.Name == "Entry.Start");
        foreach (var node in callTree.Nodes.Concat(incomingCallTree.Nodes).Where(node => node.HandoffId is not null))
        {
            var resolution = await SourceSymbolResolver.ResolveAsync(fixture.Solution, node.HandoffId!);
            Assert.True(resolution.IsSuccess);
            Assert.Equal(node.SymbolId, DocumentationCommentId.CreateDeclarationId(resolution.Symbol!));
        }

        var interfaceImplementations = await FindReferencesResolver.FindImplementationsAsync(handlerContractMethod, fixture.Solution);
        var abstractOverrides = await FindReferencesResolver.FindImplementationsAsync(abstractHandle, fixture.Solution);
        Assert.True(interfaceImplementations.IsSuccess);
        Assert.True(abstractOverrides.IsSuccess);
        Assert.False(interfaceImplementations.IsTruncated);
        Assert.Contains(interfaceImplementations.Implementations, entry => entry.SymbolName == "Handle" && entry.ProjectName == "Contracts");
        Assert.Contains(abstractOverrides.Implementations, entry => entry.SymbolName == "Handle" && entry.ProjectName == "Middle");
        Assert.Contains(abstractOverrides.Implementations, entry => entry.Signature.Contains("AlternateHandler", System.StringComparison.Ordinal));
        foreach (var implementation in interfaceImplementations.Implementations.Concat(abstractOverrides.Implementations))
        {
            var resolution = await SourceSymbolResolver.ResolveAsync(fixture.Solution, implementation.HandoffId!);
            Assert.True(resolution.IsSuccess);
            Assert.Equal(implementation.ProjectName, resolution.Symbol!.ContainingAssembly!.Name);
        }

        var handlerHierarchy = await TypeHierarchyScanner.ScanAsync(handler, fixture.Solution);
        var baseHierarchy = await TypeHierarchyScanner.ScanAsync(handlerBase, fixture.Solution);
        Assert.False(baseHierarchy.IsTruncated);
        Assert.Contains(handlerHierarchy.BaseTypes, item => item.Name == "Contracts.HandlerBase" && item.HandoffId is not null);
        Assert.Contains(handlerHierarchy.Interfaces, item => item.Name == "Contracts.IHandler" && item.HandoffId is not null);
        Assert.Contains(baseHierarchy.Subtypes, item => item.Name == "Middle.Handler");
        Assert.Contains(baseHierarchy.Subtypes, item => item.Name == "Middle.AlternateHandler");
        foreach (var item in handlerHierarchy.BaseTypes.Concat(handlerHierarchy.Interfaces).Where(item => item.HandoffId is not null))
        {
            var resolution = await SourceSymbolResolver.ResolveAsync(fixture.Solution, item.HandoffId!);
            Assert.True(resolution.IsSuccess);
            Assert.Equal("Contracts", resolution.Symbol!.ContainingAssembly!.Name);
        }
    }

    [Fact]
    public async Task RelationshipEngines_ReportTheirOwnResultAndTraversalLimits()
    {
        using var fixture = CreateRelationshipSolution();
        var contracts = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        var middle = await fixture.Solution.Projects.Single(project => project.Name == "Middle").GetCompilationAsync();
        var app = await fixture.Solution.Projects.Single(project => project.Name == "App").GetCompilationAsync();
        Assert.NotNull(contracts);
        Assert.NotNull(middle);
        Assert.NotNull(app);

        var apiRecord = contracts.GetTypeByMetadataName("Contracts.Api")!.GetMembers("Record").OfType<IMethodSymbol>().Single();
        var contractMethod = contracts.GetTypeByMetadataName("Contracts.IHandler")!.GetMembers("Handle").OfType<IMethodSymbol>().Single();
        var handlerBase = contracts.GetTypeByMetadataName("Contracts.HandlerBase")!;
        var start = app.GetTypeByMetadataName("App.Entry")!.GetMembers("Start").OfType<IMethodSymbol>().Single();
        var overrides = await FindReferencesResolver.FindImplementationsAsync(
            contracts.GetTypeByMetadataName("Contracts.HandlerBase")!.GetMembers("Handle").OfType<IMethodSymbol>().Single(),
            fixture.Solution,
            maxResults: 1);
        var hierarchy = await TypeHierarchyScanner.ScanAsync(handlerBase, fixture.Solution, maxResults: 1);
        var references = await FindReferencesResolver.FindReferencesAsync(apiRecord, fixture.Solution, maxResults: 1, depth: 3);
        var impact = await FindReferencesResolver.FindReferencesAsync(apiRecord, fixture.Solution, depth: 3, maxResults: 1, includeSummary: true);
        var callTree = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, start, RequestedDepth: 3, TopN: 1, Direction: CallTreeDirection.Outgoing));

        Assert.True(overrides.IsTruncated);
        Assert.Single(overrides.Implementations);
        Assert.Equal(2, overrides.TotalCount);
        Assert.True(hierarchy.IsTruncated);
        Assert.Single(hierarchy.Subtypes);
        Assert.Equal(2, hierarchy.TotalSubtypes);
        Assert.True(references.IsTruncated);
        Assert.False(references.IsComplete);
        Assert.Equal(6, references.TotalCount);
        Assert.True(impact.IsTruncated);
        Assert.False(impact.IsComplete);
        Assert.Equal(6, impact.Summary!.TotalReferenceSiteCount);
        Assert.Equal(
            references.References.Select(site => (site.ProjectName, site.FilePath, site.Line, site.Column, site.EnclosingSymbolHandoffId, site.Depth, site.ReachedFromSymbolHandoffId)),
            impact.References.Select(site => (site.ProjectName, site.FilePath, site.Line, site.Column, site.EnclosingSymbolHandoffId, site.Depth, site.ReachedFromSymbolHandoffId)));
        Assert.True(callTree.Truncated);
        Assert.True(callTree.HiddenEdgeCount > 0);
    }

    [Fact]
    public async Task ReferencesAndImpact_KeepSelfAndMutualRecursionWithMatchingEvidenceAndColumns()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution("""
            namespace Recursive;
            public sealed class Worker
            {
                public void First() { First(); First(); Second(); }
                public void Second() { First(); First(); }
            }
            """);
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var worker = compilation.GetTypeByMetadataName("Recursive.Worker")!;
        var first = worker.GetMembers("First").OfType<IMethodSymbol>().Single();

        var references = await FindReferencesResolver.FindReferencesAsync(first, fixture.Solution, maxResults: 20, depth: 3);
        var impact = await FindReferencesResolver.FindReferencesAsync(first, fixture.Solution, depth: 3, maxResults: 20, includeSummary: true);
        var callerTree = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, first, RequestedDepth: 3, Direction: CallTreeDirection.Incoming));
        var callTree = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, first, RequestedDepth: 3, Direction: CallTreeDirection.Outgoing));

        Assert.Equal(references.TotalCount, impact.Summary!.TotalReferenceSiteCount);
        Assert.Equal(references.References.Select(site =>
                (site.FilePath, site.Line, site.Column, site.EnclosingSymbolName, site.Depth, site.EvidenceKind)),
            impact.References.Select(site =>
                (site.FilePath, site.Line, site.Column, site.EnclosingSymbolName, site.Depth, site.EvidenceKind)));
        Assert.Contains(callerTree.Edges, edge => edge.FromNodeId == callerTree.RootNodeId && edge.ToNodeId == callerTree.RootNodeId);
        Assert.Contains(callTree.Edges, edge => edge.FromNodeId == callTree.RootNodeId && edge.ToNodeId == callTree.RootNodeId);
        Assert.Equal(2, callerTree.Edges.Single(edge => edge.FromNodeId == callerTree.RootNodeId
            && edge.ToNodeId == callerTree.RootNodeId).CallSites.Select(site => site.Column).Distinct().Count());
        Assert.False(callerTree.Truncated);
        Assert.False(callTree.Truncated);
    }

    [Fact]
    public async Task RelationshipScopeFiltersGeneratedAndTestDocumentsBeforeLimitsAndKeepsOriginalHandoffs()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\RelationshipScopes.slnx",
            new ProjectSpec("Contracts", [
                ("Counter.cs", "namespace Sample.Contracts; public interface ICounter { int Run(); } public sealed class Counter : ICounter { public int Run() => 1; }")]),
            new ProjectSpec("App", [
                ("Caller.cs", "namespace Sample.App; public sealed class ProdCaller { public int Invoke(Sample.Contracts.Counter target) => target.Run(); }") ,
                ("Generated.g.cs", "// <auto-generated/>\nnamespace Sample.App; public sealed class GeneratedCounter : Sample.Contracts.ICounter { public int Run() => 2; } public sealed class GeneratedCaller { public int Invoke(Sample.Contracts.Counter target) => target.Run(); }")], ProjectReferences: ["Contracts"]),
            new ProjectSpec("App.Tests", [
                ("TestCaller.cs", "using System;  namespace Sample.Tests { public sealed class TestImplementation : Sample.Contracts.ICounter { public int Run() => 3; } public sealed class TestCaller { public int Invoke(Sample.Contracts.Counter target) => target.Run(); } public sealed class CounterTests { [Xunit.Fact] public void Exercise(Sample.Contracts.Counter target) => target.Run(); } }") ,
                ("CounterTest.g.cs", "// <auto-generated/>\nnamespace Sample.Tests; public sealed class CounterTest { [Xunit.Fact] public void GeneratedExercise(Sample.Contracts.Counter target) => target.Run(); }")], ProjectReferences: ["Contracts"], AdditionalReferences: [TestFrameworkReferences.Reference]));
        var contracts = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(contracts);
        var counter = contracts.GetTypeByMetadataName("Sample.Contracts.Counter")!;
        var run = counter.GetMembers("Run").OfType<IMethodSymbol>().Single();
        var contract = contracts.GetTypeByMetadataName("Sample.Contracts.ICounter")!;

        var productionReferences = await FindReferencesResolver.FindReferencesAsync(run, fixture.Solution, maxResults: 1, depth: 1,
            scope: SymbolScopeType.Production, includeGenerated: false);
        var generatedProductionReferences = await FindReferencesResolver.FindReferencesAsync(run, fixture.Solution, maxResults: 1, depth: 1,
            scope: SymbolScopeType.Production, includeGenerated: true);
        var testReferences = await FindReferencesResolver.FindReferencesAsync(run, fixture.Solution, maxResults: 50, depth: 1, scope: SymbolScopeType.Tests);
        Assert.Equal(1, productionReferences.TotalCount);
        Assert.False(productionReferences.IsTruncated);
        Assert.Equal("ProdCaller.Invoke", Assert.Single(productionReferences.References).EnclosingSymbolName);
        Assert.Equal(2, generatedProductionReferences.TotalCount);
        Assert.True(generatedProductionReferences.IsTruncated);
        Assert.Equal(2, testReferences.TotalCount);
        Assert.All(testReferences.References, reference => Assert.Equal("App.Tests", reference.ProjectName));

        var productionTree = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, run, TopN: 10, Direction: CallTreeDirection.Incoming, Scope: SymbolScopeType.Production));
        var generatedProductionTree = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
            fixture.Solution, run, TopN: 10, Direction: CallTreeDirection.Incoming, Scope: SymbolScopeType.Production, IncludeGenerated: true));
        Assert.Contains(productionTree.Nodes, node => node.Name == "ProdCaller.Invoke");
        Assert.DoesNotContain(productionTree.Nodes, node => node.Name is "GeneratedCaller.Invoke" or "TestCaller.Invoke");
        Assert.Contains(generatedProductionTree.Nodes, node => node.Name == "GeneratedCaller.Invoke");

        var productionHierarchy = await TypeHierarchyScanner.ScanAsync(contract, fixture.Solution, maxResults: 1, scope: SymbolScopeType.Production);
        var generatedProductionHierarchy = await TypeHierarchyScanner.ScanAsync(contract, fixture.Solution, maxResults: 1,
            scope: SymbolScopeType.Production, includeGenerated: true);
        Assert.Equal(1, productionHierarchy.TotalSubtypes);
        Assert.False(productionHierarchy.IsTruncated);
        Assert.Equal("Sample.Contracts.Counter", Assert.Single(productionHierarchy.Subtypes).Name);
        Assert.Equal(2, generatedProductionHierarchy.TotalSubtypes);
        Assert.True(generatedProductionHierarchy.IsTruncated);
        var hierarchyHandoff = Assert.Single(productionHierarchy.Subtypes).HandoffId;
        Assert.StartsWith("src:", hierarchyHandoff);
        var hierarchyBody = await SourceSymbolBodyResolver.ResolveAsync(fixture.Solution, hierarchyHandoff!, maxBodyLines: 20);
        Assert.Null(hierarchyBody.Error);
        Assert.Contains("class Counter", hierarchyBody.Body!.Body, StringComparison.Ordinal);

        var productionImplementations = await FindReferencesResolver.FindImplementationsAsync(contract, fixture.Solution, maxResults: 1,
            scope: SymbolScopeType.Production);
        var generatedProductionImplementations = await FindReferencesResolver.FindImplementationsAsync(contract, fixture.Solution, maxResults: 1,
            scope: SymbolScopeType.Production, includeGenerated: true);
        Assert.Equal(1, productionImplementations.TotalCount);
        Assert.False(productionImplementations.IsTruncated);
        Assert.Equal(2, generatedProductionImplementations.TotalCount);
        Assert.True(generatedProductionImplementations.IsTruncated);
        var implementationHandoff = Assert.Single(productionImplementations.Implementations).HandoffId;
        var implementationBody = await SourceSymbolBodyResolver.ResolveAsync(fixture.Solution, implementationHandoff!, maxBodyLines: 20);
        Assert.Null(implementationBody.Error);
        Assert.Contains("class Counter", implementationBody.Body!.Body, StringComparison.Ordinal);

        var allRecommendations = await TestRecommendationBuilder.BuildAsync(counter, fixture.Solution);
        var generatedRecommendations = await TestRecommendationBuilder.BuildAsync(counter, fixture.Solution, includeGenerated: true);
        var productionRecommendations = await TestRecommendationBuilder.BuildAsync(counter, fixture.Solution, includeGenerated: true, scope: SymbolScopeType.Production);
        Assert.Equal(1, allRecommendations.TotalTestFixtures);
        Assert.Equal(2, generatedRecommendations.TotalTestFixtures);
        Assert.Equal(0, productionRecommendations.TotalTestFixtures);

    }

    [Fact]
    public async Task SourceTypeOriginPreservesOwningProjectAndFindsMetadataReferences()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\TypeOrigins.slnx",
            new ProjectSpec("First", [("Worker.cs", "namespace Shared { public sealed class Worker { } } namespace Nested { public class Outer<T> { public class Inner<U> { } } }")]),
            new ProjectSpec("Second", [("Worker.cs", "namespace Shared; public sealed class Worker { }")]));
        var second = fixture.Solution.Projects.Single(project => project.Name == "Second");
        var compilation = await second.GetCompilationAsync();
        Assert.NotNull(compilation);
        var worker = compilation.GetTypeByMetadataName("Shared.Worker");
        Assert.NotNull(worker);

        var local = await SourceTypeOriginScanner.ResolveAsync(fixture.Solution, fixture.Solution.FilePath!, worker, null);
        Assert.True(local.IsSuccess);
        Assert.True(local.Value!.Found);
        Assert.Equal("Second", local.Value.ProjectName);
        Assert.Equal("source", local.Value.AssemblyOrigin);
        Assert.All(local.Value.SourceLocations, location => Assert.Contains("Second", location.FilePath, StringComparison.Ordinal));

        var first = fixture.Solution.Projects.Single(project => project.Name == "First");
        var firstCompilation = await first.GetCompilationAsync();
        Assert.NotNull(firstCompilation);
        var nestedGeneric = firstCompilation.GetTypeByMetadataName("Nested.Outer`1+Inner`1");
        Assert.NotNull(nestedGeneric);
        var nestedOrigin = await SourceTypeOriginScanner.ResolveAsync(fixture.Solution, fixture.Solution.FilePath!, nestedGeneric, null);
        Assert.True(nestedOrigin.IsSuccess);
        Assert.True(nestedOrigin.Value!.Found);
        Assert.Equal("First", nestedOrigin.Value.ProjectName);
        Assert.Equal("source", nestedOrigin.Value.AssemblyOrigin);
        Assert.Contains("Nested.Outer<T>.Inner<U>", nestedOrigin.Value.TypeName);

        var metadata = await SourceTypeOriginScanner.ResolveAsync(fixture.Solution, fixture.Solution.FilePath!, null, "System.String");
        Assert.True(metadata.IsSuccess);
        Assert.True(metadata.Value!.Found);
        Assert.Equal("reference", metadata.Value.AssemblyOrigin);
        Assert.False(string.IsNullOrWhiteSpace(metadata.Value.OutputAssembly));
        Assert.Contains("System.Private.CoreLib", metadata.Value.SearchedAssemblies);
    }

    [Fact]
    public async Task SourceTypeOriginReportsAmbiguousMetadataReferences()
    {
        using var temp = TestTempDirectory.Create("source-type-origin-ambiguous-");
        var firstPath = AssemblyTestHelper.EmitAssembly(temp, "OriginCandidateOne", "namespace External; public sealed class SharedType { }");
        var secondPath = AssemblyTestHelper.EmitAssembly(temp, "OriginCandidateTwo", "namespace External; public sealed class SharedType { }");
        using var fixture = TestWorkspaceBuilder.CreateSolution(@"C:\VirtualRepo\SourceTypeOriginAmbiguous.slnx",
            new ProjectSpec("Consumer", [("Consumer.cs", "namespace Consumer; public sealed class UsesFramework { public string Value => string.Empty; }")],
                AdditionalReferences: [MetadataReference.CreateFromFile(firstPath), MetadataReference.CreateFromFile(secondPath)]));

        var result = await SourceTypeOriginScanner.ResolveAsync(fixture.Solution, fixture.Solution.FilePath!, null, "External.SharedType");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Found);
        Assert.True(result.Value.IsAmbiguous);
        Assert.Equal("ambiguous", result.Value.AssemblyOrigin);
        Assert.NotNull(result.Value.CandidatePaths);
        Assert.Contains(Path.GetFullPath(firstPath), result.Value.CandidatePaths!);
        Assert.Contains(Path.GetFullPath(secondPath), result.Value.CandidatePaths!);
    }

    [Fact]
    public async Task SourceTypeOriginMatchesNestedGenericMetadataTypeAndExactReferenceIdentity()
    {
        using var temp = TestTempDirectory.Create("source-type-origin-nested-metadata-");
        var oldPackage = temp.CreateSubdirectory(Path.Combine(".nuget", "packages", "shared.package", "1.0.0", "lib", "net10.0"));
        var newPackage = temp.CreateSubdirectory(Path.Combine(".nuget", "packages", "shared.package", "2.0.0", "lib", "net10.0"));
        using var oldTemp = TestTempDirectory.Create("source-type-origin-old-reference-");
        using var newTemp = TestTempDirectory.Create("source-type-origin-new-reference-");
        var oldGenerated = AssemblyTestHelper.EmitAssembly(oldTemp, "SharedDependency", "[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")] namespace External; public sealed class OtherType { }");
        var newGenerated = AssemblyTestHelper.EmitAssembly(newTemp, "SharedDependency", "[assembly: System.Reflection.AssemblyVersion(\"2.0.0.0\")] namespace External; public class Outer<T> { public class Inner<U> { } }");
        var oldPath = Path.Combine(oldPackage, "SharedDependency.dll");
        var newPath = Path.Combine(newPackage, "SharedDependency.dll");
        File.Move(oldGenerated, oldPath);
        File.Move(newGenerated, newPath);
        using var fixture = TestWorkspaceBuilder.CreateSolution(@"C:\VirtualRepo\SourceTypeOriginNestedMetadata.slnx",
            new ProjectSpec("Consumer", [("Consumer.cs", "namespace Consumer; public sealed class UsesFramework { public string Value => string.Empty; }")],
                AdditionalReferences: [MetadataReference.CreateFromFile(oldPath), MetadataReference.CreateFromFile(newPath)]));

        var result = await SourceTypeOriginScanner.ResolveAsync(fixture.Solution, fixture.Solution.FilePath!, null,
            "External.Outer<int>.Inner<string>");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Found);
        Assert.False(result.Value.IsAmbiguous);
        Assert.Equal("reference", result.Value.AssemblyOrigin);
        Assert.Equal(Path.GetFullPath(newPath), Path.GetFullPath(result.Value.OutputAssembly!));
    }

    private static TestSolutionHandle CreateRelationshipSolution() => TestWorkspaceBuilder.CreateSolution(
        @"C:\VirtualRepo\CrossFeatureRelationships.slnx",
        new ProjectSpec("Contracts", [
            ("Api.cs", "namespace Contracts; public interface IHandler { void Handle(); } public abstract class HandlerBase : IHandler { public abstract void Handle(); } public static class Api { public static void Record() { } } public static class Telemetry { public static void Touch() { } }")], VirtualProjectDirectory: "src/Contracts"),
        new ProjectSpec("Middle", [
            ("Handlers.cs", "namespace Middle; public sealed class Handler : Contracts.HandlerBase { public override void Handle() { Contracts.Api.Record(); Contracts.Api.Record(); } } public sealed class AlternateHandler : Contracts.HandlerBase { public override void Handle() => Contracts.Api.Record(); }"),
            ("Dispatcher.cs", "namespace Middle; public sealed class Dispatcher { public void Dispatch(Handler handler) => handler.Handle(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/Middle"),
        new ProjectSpec("App", [
            ("Entry.cs", "namespace App; public sealed class Entry { public void Start(Middle.Dispatcher dispatcher, Middle.Handler handler) { dispatcher.Dispatch(handler); Contracts.Telemetry.Touch(); } }")],
            ProjectReferences: ["Middle", "Contracts"], VirtualProjectDirectory: "src/App"));
}
