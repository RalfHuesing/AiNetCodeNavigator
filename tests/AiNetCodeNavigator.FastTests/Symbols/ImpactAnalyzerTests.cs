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
public sealed class ImpactAnalyzerTests
{
    [Fact]
    public async Task AnalyzeSymbolImpactAsync_FindsTransitiveCallersAndAffectedProjects()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);

        var greeterType = coreCompilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);

        var greetMethod = greeterType.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(greetMethod, fixture.Solution, maxDepth: 2);

        Assert.Equal("Greet", impact.TargetSymbol);
        Assert.True(impact.DirectCallersCount >= 2);
        Assert.True(impact.TransitiveImpactCount >= 2);
        Assert.Contains(impact.AffectedProjects, p => p == "Sample.App");
        Assert.Contains(impact.AffectedFiles, f => f.Contains("Caller.cs"));
        Assert.Contains(impact.CallSites, s => s.CallingMember.Contains("ExecuteSingle"));
        Assert.Contains(impact.CallSites, s => s.CallingMember.Contains("ExecuteMultiple"));
    }

    [Fact]
    public async Task AnalyzeSymbolImpactAsync_PreservesCrossProjectCallChainAndHandoffs()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\ImpactChain.slnx",
            new ProjectSpec("Contracts", [("Api.cs", "namespace Contracts; public static class Api { public static void Run() { } }")], VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("Middle", [("Bridge.cs", "namespace Middle; public class Bridge { public void Step() => Contracts.Api.Run(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/Middle"),
            new ProjectSpec("App", [("Entry.cs", "namespace App; public class Entry { public void Start(Middle.Bridge bridge) => bridge.Step(); }")], ProjectReferences: ["Middle"], VirtualProjectDirectory: "src/App"));
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Contracts.Api")!.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(target, fixture.Solution, maxDepth: 2);

        Assert.Equal(1, impact.DirectCallersCount);
        Assert.Equal(2, impact.TransitiveImpactCount);
        Assert.Equal(1, impact.TransitiveCallSitesCount);
        Assert.Contains(impact.CallSites, site => site.Depth == 1 && site.ProjectName == "Middle");
        Assert.Contains(impact.CallSites, site => site.Depth == 2 && site.ProjectName == "App");
        foreach (var site in impact.CallSites)
        {
            Assert.StartsWith("h:", site.CallingMemberHandoffId);
            var resolved = await SourceSymbolResolver.ResolveAsync(fixture.Solution, site.CallingMemberHandoffId!);
            Assert.True(resolved.IsSuccess);
            Assert.Equal(site.ProjectName, resolved.Symbol!.ContainingAssembly!.Name);
        }
    }

    [Fact]
    public async Task AnalyzeSymbolImpactAsync_DistinguishesCallSitesInDifferentProjectsWithSamePath()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\ImpactSharedFile.slnx",
            new ProjectSpec("Contracts", [("Api.cs", "namespace Contracts; public static class Api { public static void Run() { } }")], VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("Host", [("Caller.cs", "namespace Shared; public class Client { public void Call() => Contracts.Api.Run(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/Shared"),
            new ProjectSpec("Tests", [("Caller.cs", "namespace Shared; public class Client { public void Call() => Contracts.Api.Run(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/Shared"));
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Contracts.Api")!.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(target, fixture.Solution, maxNodes: int.MaxValue);

        Assert.Equal(2, impact.DirectCallersCount);
        Assert.Equal(2, impact.CallSites.Count);
        Assert.Contains("Host", impact.AffectedProjects);
        Assert.Contains("Tests", impact.AffectedProjects);
        Assert.Equal(ImpactAnalyzer.MaxNodes, impact.EffectiveNodeLimit);
        foreach (var site in impact.CallSites)
        {
            Assert.StartsWith("h:", site.CallingMemberHandoffId);
            var resolved = await SourceSymbolResolver.ResolveAsync(fixture.Solution, site.CallingMemberHandoffId!);
            Assert.True(resolved.IsSuccess);
            Assert.Equal(site.ProjectName, resolved.Symbol!.ContainingAssembly!.Name);
        }

        var limited = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(target, fixture.Solution, maxResults: 1);
        Assert.Single(limited.CallSites);
        Assert.True(limited.IsTruncated);
        Assert.Equal(2, limited.DirectCallersCount);
        Assert.Contains("Host", limited.AffectedProjects);
        Assert.Contains("Tests", limited.AffectedProjects);
    }

    [Fact]
    public async Task AnalyzeSymbolImpactAsync_ReportsPendingNodesAtNodeLimit()
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

        var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(target, fixture.Solution, maxDepth: 2, maxResults: 500, maxNodes: 100);

        Assert.Equal(105, impact.DirectCallersCount);
        Assert.True(impact.IsTruncatedByNodeLimit);
        Assert.False(impact.IsComplete);
        Assert.Equal(100, impact.EffectiveNodeLimit);
    }

    [Fact]
    public async Task AnalyzeSymbolImpactAsync_NormalizesNonpositiveResultLimit()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("SampleNamespace.Greeter")!.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(target, fixture.Solution, maxResults: 0);

        Assert.Single(impact.CallSites);
        Assert.True(impact.IsTruncated);
    }

    [Fact]
    public async Task AnalyzeSymbolImpactAsync_ReportsRequestedAndEffectiveDepth()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("SampleNamespace.Greeter")!.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(target, fixture.Solution, maxDepth: 9);

        Assert.Equal(9, impact.RequestedDepth);
        Assert.Equal(ImpactAnalyzer.MaxAllowedDepth, impact.EffectiveDepth);
        Assert.True(impact.IsDepthClamped);
        Assert.False(impact.IsComplete);
    }

    [Fact]
    public async Task AnalyzeSymbolImpactAsync_ValidatesArgumentsAndNodeLimit()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("SampleNamespace.Greeter")!.GetMembers("Greet").OfType<IMethodSymbol>().First();

        await Assert.ThrowsAsync<System.ArgumentNullException>(() => ImpactAnalyzer.AnalyzeSymbolImpactAsync(null!, fixture.Solution));
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => ImpactAnalyzer.AnalyzeSymbolImpactAsync(target, null!));
        await Assert.ThrowsAsync<System.ArgumentOutOfRangeException>(() => ImpactAnalyzer.AnalyzeSymbolImpactAsync(target, fixture.Solution, maxNodes: 0));
    }

    [Fact]
    public async Task AnalyzeSymbolImpactAsync_StopsCyclesAndKeepsTheShortestCallDepth()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\ImpactCycle.slnx",
            new ProjectSpec("Contracts", [("Api.cs", "namespace Contracts; public static class Api { public static void Run() { } }")], VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("App", [("Cycle.cs", "namespace App; public class Alpha { public void First() { Contracts.Api.Run(); Beta beta = new(); beta.Second(this); } } public class Beta { public void Second(Alpha alpha) => alpha.First(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/App"));
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Contracts.Api")!.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(target, fixture.Solution, maxDepth: 3);

        Assert.Equal(1, impact.DirectCallersCount);
        Assert.Equal(2, impact.TransitiveCallSitesCount);
        Assert.Equal(3, impact.VisitedSymbolCount);
        Assert.False(impact.IsTruncatedByNodeLimit);
    }
}
