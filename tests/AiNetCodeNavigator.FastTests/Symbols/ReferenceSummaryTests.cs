#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class ReferenceSummaryTests
{
    [Fact]
    public async Task ReferenceSummary_FindsTransitiveCallersAndAffectedProjects()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);

        var greeterType = coreCompilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);

        var greetMethod = greeterType.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var impact = await FindReferencesResolver.FindReferencesAsync(greetMethod, fixture.Solution, depth: 2, maxResults: 50, includeSummary: true);

        Assert.Equal("Greet", impact.TargetSymbolName);
        Assert.True(impact.Summary!.DirectReferenceSiteCount >= 2);
        Assert.True(impact.Summary!.TotalReferenceSiteCount >= 2);
        Assert.Contains(impact.Summary!.Projects.Select(project => project.ProjectName), p => p == "Sample.App");
        Assert.Contains(impact.Summary!.Files.Select(file => file.FilePath), f => f.Contains("Caller.cs"));
        Assert.Contains(impact.References, s => s.EnclosingSymbolName.Contains("ExecuteSingle"));
        Assert.Contains(impact.References, s => s.EnclosingSymbolName.Contains("ExecuteMultiple"));
    }

    [Fact]
    public async Task ReferenceSummary_PreservesCrossProjectCallChainAndHandoffs()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\ImpactChain.slnx",
            new ProjectSpec("Contracts", [("Api.cs", "namespace Contracts; public static class Api { public static void Run() { } }")], VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("Middle", [("Bridge.cs", "namespace Middle; public class Bridge { public void Step() => Contracts.Api.Run(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/Middle"),
            new ProjectSpec("App", [("Entry.cs", "namespace App; public class Entry { public void Start(Middle.Bridge bridge) => bridge.Step(); }")], ProjectReferences: ["Middle"], VirtualProjectDirectory: "src/App"));
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Contracts.Api")!.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var impact = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, depth: 2, maxResults: 50, includeSummary: true);

        Assert.Equal(1, impact.Summary!.DirectReferenceSiteCount);
        Assert.Equal(2, impact.Summary!.TotalReferenceSiteCount);
        Assert.Equal(1, impact.Summary!.DeeperReferenceSiteCount);
        Assert.Contains(impact.References, site => site.Depth == 1 && site.ProjectName == "Middle");
        Assert.Contains(impact.References, site => site.Depth == 2 && site.ProjectName == "App");
        foreach (var site in impact.References)
        {
            Assert.StartsWith("src:", site.EnclosingSymbolHandoffId);
            var resolved = await SourceSymbolResolver.ResolveAsync(fixture.Solution, site.EnclosingSymbolHandoffId!);
            Assert.True(resolved.IsSuccess);
            Assert.Equal(site.ProjectName, resolved.Symbol!.ContainingAssembly!.Name);
        }
    }

    [Fact]
    public async Task ReferenceSummary_DistinguishesCallSitesInDifferentProjectsWithSamePath()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\ImpactSharedFile.slnx",
            new ProjectSpec("Contracts", [("Api.cs", "namespace Contracts; public static class Api { public static void Run() { } }")], VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("Host", [("Caller.cs", "namespace Shared; public class Client { public void Call() => Contracts.Api.Run(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/Shared"),
            new ProjectSpec("Tests", [("Caller.cs", "namespace Shared; public class Client { public void Call() => Contracts.Api.Run(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/Shared"));
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Contracts.Api")!.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var impact = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, maxNodes: int.MaxValue, maxResults: 50, depth: 1, includeSummary: true);

        Assert.Equal(2, impact.Summary!.DirectReferenceSiteCount);
        Assert.Equal(2, impact.References.Count);
        Assert.Contains("Host", impact.Summary!.Projects.Select(project => project.ProjectName));
        Assert.Contains("Tests", impact.Summary!.Projects.Select(project => project.ProjectName));
        Assert.Equal(FindReferencesResolver.DefaultMaxVisitedSymbols, impact.EffectiveNodeLimit);
        foreach (var site in impact.References)
        {
            Assert.StartsWith("src:", site.EnclosingSymbolHandoffId);
            var resolved = await SourceSymbolResolver.ResolveAsync(fixture.Solution, site.EnclosingSymbolHandoffId!);
            Assert.True(resolved.IsSuccess);
            Assert.Equal(site.ProjectName, resolved.Symbol!.ContainingAssembly!.Name);
        }

        var limited = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, maxResults: 1, depth: 1, includeSummary: true);
        Assert.Single(limited.References);
        Assert.True(limited.IsTruncated);
        Assert.Equal(2, limited.Summary!.DirectReferenceSiteCount);
        Assert.True(limited.Summary.AnalysisComplete);
        Assert.Equal(impact.Summary.Files, limited.Summary.Files);
        Assert.Equal(impact.Summary.Projects, limited.Summary.Projects);
        Assert.Contains("Host", limited.Summary!.Projects.Select(project => project.ProjectName));
        Assert.Contains("Tests", limited.Summary!.Projects.Select(project => project.ProjectName));
    }

    [Fact]
    public async Task ReferenceSummary_ReportsPendingNodesAtNodeLimit()
    {
        var callers = Enumerable.Range(0, 105)
            .Select(index => ($"Caller{index}.cs", $"namespace Fanout; public class Caller{index} {{ public void Call() => Contracts.Api.Run(); }}"))
            .ToArray();
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\ImpactFanout.slnx",
            new ProjectSpec("Contracts", [("Api.cs", "namespace Contracts; public static class Api { public static void Run() { } }")], VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("App", callers, ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/App"));
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Contracts.Api")!.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var impact = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, depth: 2, maxResults: 500, maxNodes: 100, includeSummary: true);

        Assert.Equal(105, impact.Summary!.DirectReferenceSiteCount);
        Assert.True(impact.IsTruncatedByNodeLimit);
        Assert.False(impact.IsComplete);
        Assert.False(impact.Summary!.AnalysisComplete);
        Assert.Contains("nodeLimit", impact.Summary.Omissions);
        Assert.Equal(100, impact.EffectiveNodeLimit);
    }

    [Fact]
    public async Task ReferenceSummary_NormalizesNonpositiveResultLimit()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("SampleNamespace.Greeter")!.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var impact = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, maxResults: 0, depth: 1, includeSummary: true);

        Assert.Single(impact.References);
        Assert.True(impact.IsTruncated);
    }

    [Fact]
    public async Task ReferenceSummary_ReportsRequestedAndEffectiveDepth()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("SampleNamespace.Greeter")!.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var impact = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, depth: 9, maxResults: 50, includeSummary: true);

        Assert.Equal(9, impact.RequestedDepth);
        Assert.Equal(FindReferencesResolver.MaxReferenceDepth, impact.EffectiveDepth);
        Assert.True(impact.IsDepthClamped);
        Assert.False(impact.Summary!.AnalysisComplete);
        Assert.Contains("depthLimit", impact.Summary.Omissions);
        Assert.False(impact.IsComplete);
    }

    [Fact]
    public async Task ReferenceSummary_ValidatesArgumentsAndNodeLimit()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("SampleNamespace.Greeter")!.GetMembers("Greet").OfType<IMethodSymbol>().First();

        await Assert.ThrowsAsync<System.ArgumentNullException>(() => FindReferencesResolver.FindReferencesAsync(null!, fixture.Solution, maxResults: 50, depth: 1, includeSummary: true));
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => FindReferencesResolver.FindReferencesAsync(target, null!, maxResults: 50, depth: 1, includeSummary: true));
        await Assert.ThrowsAsync<System.ArgumentOutOfRangeException>(() => FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, maxNodes: 0, maxResults: 50, depth: 1, includeSummary: true));
    }

    [Fact]
    public async Task ReferenceSummary_StopsCyclesAndKeepsTheShortestCallDepth()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\ImpactCycle.slnx",
            new ProjectSpec("Contracts", [("Api.cs", "namespace Contracts; public static class Api { public static void Run() { } }")], VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("App", [("Cycle.cs", "namespace App; public class Alpha { public void First() { Contracts.Api.Run(); Beta beta = new(); beta.Second(this); } } public class Beta { public void Second(Alpha alpha) => alpha.First(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/App"));
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Contracts.Api")!.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var impact = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, depth: 3, maxResults: 50, includeSummary: true);

        Assert.Equal(1, impact.Summary!.DirectReferenceSiteCount);
        Assert.Equal(2, impact.Summary!.DeeperReferenceSiteCount);
        Assert.Equal(3, impact.VisitedSymbolCount);
        Assert.False(impact.IsTruncatedByNodeLimit);
    }

    [Fact]
    public async Task ReferenceSummary_PreservesConvergingCallersOnTheSameLineAcrossProjects()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\ImpactConverging.slnx",
            new ProjectSpec("Contracts", [("Api.cs", "namespace Contracts; public static class Api { public static void Run() { } }")], VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("BranchB", [("B.cs", "namespace BranchB; public class B { public void Call() => Contracts.Api.Run(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/BranchB"),
            new ProjectSpec("BranchC", [("C.cs", "namespace BranchC; public class C { public void Call() => Contracts.Api.Run(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/BranchC"),
            new ProjectSpec("Top", [("Top.cs", "namespace Top; public class Entry { public void Go(BranchB.B b, BranchC.C c) { b.Call(); c.Call(); } }")], ProjectReferences: ["BranchB", "BranchC"], VirtualProjectDirectory: "src/Top"));
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Contracts.Api")!.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var impact = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, depth: 2, maxResults: 50, includeSummary: true);

        Assert.Equal(2, impact.Summary!.DirectReferenceSiteCount);
        Assert.Equal(4, impact.Summary!.TotalReferenceSiteCount);
        Assert.Equal(2, impact.Summary!.DeeperReferenceSiteCount);
        Assert.Equal(4, impact.References.Count);
        Assert.Contains("BranchB", impact.Summary!.Projects.Select(project => project.ProjectName));
        Assert.Contains("BranchC", impact.Summary!.Projects.Select(project => project.ProjectName));
        Assert.Contains("Top", impact.Summary!.Projects.Select(project => project.ProjectName));
        var convergedSites = impact.References.Where(site => site.Depth == 2 && site.ProjectName == "Top").ToArray();
        Assert.Equal(2, convergedSites.Length);
        Assert.Equal(2, convergedSites.Select(site => site.ReachedFromSymbolId).Distinct().Count());
        Assert.Equal(2, convergedSites.Select(site => site.ReachedFromSymbolHandoffId).Distinct().Count());
        foreach (var site in convergedSites)
        {
            Assert.NotEmpty(site.ReachedFromSymbolId);
            Assert.StartsWith("src:", site.ReachedFromSymbolHandoffId);
            var resolvedReachedFrom = await SourceSymbolResolver.ResolveAsync(fixture.Solution, site.ReachedFromSymbolHandoffId!);
            Assert.True(resolvedReachedFrom.IsSuccess);
            Assert.Contains(resolvedReachedFrom.Symbol!.ContainingAssembly!.Name, new[] { "BranchB", "BranchC" });
            Assert.StartsWith("src:", site.EnclosingSymbolHandoffId);
            var resolvedCaller = await SourceSymbolResolver.ResolveAsync(fixture.Solution, site.EnclosingSymbolHandoffId!);
            Assert.True(resolvedCaller.IsSuccess);
            Assert.Equal("Top", resolvedCaller.Symbol!.ContainingAssembly!.Name);
        }

        var limited = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, depth: 2, maxResults: 3, includeSummary: true);
        Assert.Equal(3, limited.References.Count);
        Assert.True(limited.IsTruncated);
        Assert.Equal(2, limited.Summary!.DeeperReferenceSiteCount);
        Assert.Contains("BranchB", limited.Summary!.Projects.Select(project => project.ProjectName));
        Assert.Contains("BranchC", limited.Summary!.Projects.Select(project => project.ProjectName));
        Assert.Contains("Top", limited.Summary!.Projects.Select(project => project.ProjectName));
    }
}
