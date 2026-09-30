#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.CallTree;
using AiNetCodeNavigator.Core.Hierarchy;
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
        var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(apiRecord, fixture.Solution, maxDepth: 3, maxResults: 50);
        Assert.False(callers.IsTruncated);
        Assert.False(callers.IsTruncatedByNodeLimit);
        Assert.False(callers.IsDepthClamped);
        Assert.True(callers.IsComplete);
        Assert.Equal(3, callers.EffectiveDepth);
        Assert.Equal(5, callers.TotalCount);
        Assert.Equal(5, impact.TransitiveImpactCount);
        Assert.Equal(2, impact.DirectCallersCount);
        Assert.Equal(3, impact.TransitiveCallSitesCount);
        Assert.Equal(3, impact.EffectiveDepth);
        Assert.True(impact.IsComplete);

        var callerSites = callers.References
            .Select(site => (site.ProjectName, site.FilePath, site.Line, site.EnclosingSymbolName, site.EnclosingSymbolHandoffId, site.Depth, site.ReachedFromSymbolHandoffId))
            .ToArray();
        var impactSites = impact.CallSites
            .Select(site => (site.ProjectName, site.FilePath, site.Line, site.CallingMember, site.CallingMemberHandoffId, site.Depth, site.ReachedFromSymbolHandoffId))
            .ToArray();
        Assert.Equal(callerSites, impactSites);
        Assert.Contains(impact.CallSites, site => site.Depth == 1 && site.ProjectName == "Middle" && site.CallingMember == "Handler.Handle");
        Assert.Contains(impact.CallSites, site => site.Depth == 1 && site.ProjectName == "Middle" && site.CallingMember == "AlternateHandler.Handle");
        Assert.Equal(2, impact.CallSites.Count(site => site.Depth == 2 && site.ProjectName == "Middle" && site.CallingMember == "Dispatcher.Dispatch"));
        Assert.Contains(impact.CallSites, site => site.Depth == 3 && site.ProjectName == "App" && site.CallingMember == "Entry.Start");

        foreach (var site in impact.CallSites)
        {
            Assert.StartsWith("h:", site.CallingMemberHandoffId);
            Assert.StartsWith("h:", site.ReachedFromSymbolHandoffId);
            var callerResolution = await SourceSymbolResolver.ResolveAsync(fixture.Solution, site.CallingMemberHandoffId!);
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
        var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(apiRecord, fixture.Solution, maxDepth: 3, maxResults: 1);
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
        Assert.Equal(5, references.TotalCount);
        Assert.True(impact.IsTruncated);
        Assert.False(impact.IsComplete);
        Assert.Equal(5, impact.TransitiveImpactCount);
        Assert.True(callTree.Truncated);
        Assert.True(callTree.HiddenEdgeCount > 0);
    }

    private static TestSolutionHandle CreateRelationshipSolution() => TestWorkspaceBuilder.CreateSolution(
        @"C:\VirtualRepo\CrossFeatureRelationships.slnx",
        new ProjectSpec("Contracts", [
            ("Api.cs", "namespace Contracts; public interface IHandler { void Handle(); } public abstract class HandlerBase : IHandler { public abstract void Handle(); } public static class Api { public static void Record() { } } public static class Telemetry { public static void Touch() { } }")], VirtualProjectDirectory: "src/Contracts"),
        new ProjectSpec("Middle", [
            ("Handlers.cs", "namespace Middle; public sealed class Handler : Contracts.HandlerBase { public override void Handle() => Contracts.Api.Record(); } public sealed class AlternateHandler : Contracts.HandlerBase { public override void Handle() => Contracts.Api.Record(); }"),
            ("Dispatcher.cs", "namespace Middle; public sealed class Dispatcher { public void Dispatch(Handler handler) => handler.Handle(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/Middle"),
        new ProjectSpec("App", [
            ("Entry.cs", "namespace App; public sealed class Entry { public void Start(Middle.Dispatcher dispatcher, Middle.Handler handler) { dispatcher.Dispatch(handler); Contracts.Telemetry.Touch(); } }")],
            ProjectReferences: ["Middle", "Contracts"], VirtualProjectDirectory: "src/App"));
}
