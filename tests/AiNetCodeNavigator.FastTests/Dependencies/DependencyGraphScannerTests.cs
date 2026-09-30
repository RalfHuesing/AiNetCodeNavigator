#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.TestKit.Builders;
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

    [Fact]
    public async Task ScanSolutionAsync_MapsSameFullTypeNameToTheCorrectCrossProjectDeclaration()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyCollision.slnx",
            new ProjectSpec("ContractsOne", [("WidgetOne.cs", "namespace Shared.Models; public class Widget { }")], VirtualProjectDirectory: "src/ContractsOne"),
            new ProjectSpec("ContractsTwo", [("WidgetTwo.cs", "namespace Shared.Models; public class Widget { }")], VirtualProjectDirectory: "src/ContractsTwo"),
            new ProjectSpec("ConsumersOne", [("ConsumerOne.cs", "namespace Consumers.One; public class UsesOne { public Shared.Models.Widget? Value; }")], ProjectReferences: ["ContractsOne"], VirtualProjectDirectory: "src/ConsumersOne"),
            new ProjectSpec("ConsumersTwo", [("ConsumerTwo.cs", "namespace Consumers.Two; public class UsesTwo { public Shared.Models.Widget? Value; }")], ProjectReferences: ["ContractsTwo"], VirtualProjectDirectory: "src/ConsumersTwo"));

        var graph = await DependencyGraphScanner.ScanSolutionAsync(fixture.Solution);

        Assert.Contains(graph.ProjectDependencies, edge => edge.FromProject == "ConsumersOne" && edge.ToProject == "ContractsOne");
        Assert.Contains(graph.ProjectDependencies, edge => edge.FromProject == "ConsumersTwo" && edge.ToProject == "ContractsTwo");
        Assert.Contains(graph.FileDependencies, edge => edge.FromFile == "src/ConsumersOne/ConsumerOne.cs" && edge.ToFile == "src/ContractsOne/WidgetOne.cs");
        Assert.Contains(graph.FileDependencies, edge => edge.FromFile == "src/ConsumersTwo/ConsumerTwo.cs" && edge.ToFile == "src/ContractsTwo/WidgetTwo.cs");
        Assert.Contains(graph.FileDependencies, edge => edge.FromProject == "ConsumersOne" && edge.ToProject == "ContractsOne");
    }

    [Fact]
    public async Task ScanSolutionAsync_ExcludesExternalFrameworkTypeNoise()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyExternal.slnx",
            new ProjectSpec("App", [("Program.cs", "using System.Collections.Generic; namespace App; public class Program { public List<string> Values = new(); }")], VirtualProjectDirectory: "src/App"));

        var graph = await DependencyGraphScanner.ScanSolutionAsync(fixture.Solution);

        Assert.Empty(graph.ProjectDependencies);
        Assert.Empty(graph.NamespaceDependencies);
        Assert.Empty(graph.FileDependencies);
    }

    [Fact]
    public async Task ScanSolutionAsync_PaginatesRelationshipsAndReportsTotals()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyPagination.slnx",
            new ProjectSpec("Core", [("Core.cs", "namespace Core; public class Item { }")], VirtualProjectDirectory: "src/Core"),
            new ProjectSpec("AppOne", [("One.cs", "namespace AppOne; public class One { public Core.Item Value = new(); }")], ProjectReferences: ["Core"], VirtualProjectDirectory: "src/AppOne"),
            new ProjectSpec("AppTwo", [("Two.cs", "namespace AppTwo; public class Two { public Core.Item Value = new(); }")], ProjectReferences: ["Core"], VirtualProjectDirectory: "src/AppTwo"));

        var firstPage = await DependencyGraphScanner.ScanSolutionAsync(
            fixture.Solution, options: new DependencyGraphScanOptions(Offset: 0, PageSize: 1));
        var secondPage = await DependencyGraphScanner.ScanSolutionAsync(
            fixture.Solution, options: new DependencyGraphScanOptions(Offset: 1, PageSize: 1));

        Assert.Equal(2, firstPage.TotalProjectDependencyCount);
        Assert.Single(firstPage.ProjectDependencies);
        Assert.Single(secondPage.ProjectDependencies);
        Assert.True(firstPage.HasMoreProjectDependencies);
        Assert.False(secondPage.HasMoreProjectDependencies);
        Assert.True(firstPage.IsTruncated);
        Assert.True(firstPage.IsComplete);
    }

    [Fact]
    public async Task ScanSolutionAsync_ReportsDocumentLimitAndClampedBounds()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyBounds.slnx",
            new ProjectSpec("App", [
                ("A.cs", "namespace App; public class A { }"),
                ("B.cs", "namespace App; public class B { }")], VirtualProjectDirectory: "src/App"));

        var limited = await DependencyGraphScanner.ScanSolutionAsync(
            fixture.Solution,
            options: new DependencyGraphScanOptions(PageSize: DependencyGraphScanner.MaximumPageSize + 1, MaxDocuments: 1));

        Assert.Equal(1, limited.ScannedDocumentCount);
        Assert.Equal(2, limited.TotalDocumentCount);
        Assert.True(limited.DocumentLimitReached);
        Assert.True(limited.PageSizeWasClamped);
        Assert.True(limited.IsTruncated);
        Assert.False(limited.IsComplete);
        Assert.Empty(limited.Errors ?? []);
    }

    [Fact]
    public async Task ScanSolutionAsync_RejectsNegativeOffsets()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyInvalid.slnx",
            new ProjectSpec("App", [("A.cs", "namespace App; public class A { }")], VirtualProjectDirectory: "src/App"));

        await Assert.ThrowsAsync<System.ArgumentOutOfRangeException>(() =>
            DependencyGraphScanner.ScanSolutionAsync(fixture.Solution, options: new DependencyGraphScanOptions(Offset: -1)));
    }
}
