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
        Assert.False(firstPage.IsComplete);
        Assert.True(secondPage.IsTruncated);
        Assert.False(secondPage.IsComplete);
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

    [Fact]
    public async Task ScanSolutionAsync_FindsGenericAndQualifiedTypeDependencies()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyGeneric.slnx",
            new ProjectSpec("Contracts", [("Types.cs", "namespace Contracts; public class Box<T> { } public class Item { }")], VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("App", [("Use.cs", "namespace App; public class Use { public Contracts.Box<Contracts.Item> Value { get; set; } = new(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/App"));

        var graph = await DependencyGraphScanner.ScanSolutionAsync(fixture.Solution);

        Assert.Contains(graph.FileDependencies, edge => edge.CrossingTypes.Contains("Box"));
        Assert.Contains(graph.FileDependencies, edge => edge.CrossingTypes.Contains("Item"));

        var targetGraph = await DependencyGraphScanner.ScanSolutionAsync(
            fixture.Solution,
            options: new DependencyGraphScanOptions(TargetTypeName: "Contracts.Box", Direction: DependencyGraphDirection.Incoming));
        Assert.Single(targetGraph.TypeDependencies!);
        Assert.Equal("global::Contracts.Box<T>", targetGraph.TypeDependencies![0].ToType);
    }

    [Fact]
    public async Task ScanSolutionAsync_FollowsTargetTypeDirectionAndDepthWithPerTypeProvenance()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyTarget.slnx",
            new ProjectSpec("Further", [("Dependency.cs", "namespace Further; public class Dependency { }")], VirtualProjectDirectory: "src/Further"),
            new ProjectSpec("Contracts", [("Types.cs", "namespace Contracts; public class Target { public Further.Dependency Link { get; set; } = new(); } public class Other { }")], ProjectReferences: ["Further"], VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("App", [("Callers.cs", "namespace App; public class CallerA { public Contracts.Target Value { get; set; } = new(); } public class CallerB { public Contracts.Other Value { get; set; } = new(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/App"));

        var graph = await DependencyGraphScanner.ScanSolutionAsync(
            fixture.Solution,
            options: new DependencyGraphScanOptions(
                TargetTypeName: "App.CallerA",
                Direction: DependencyGraphDirection.Outgoing,
                Depth: 2));

        Assert.Equal(2, graph.TypeDependencies?.Count);
        Assert.Contains(graph.TypeDependencies!, edge => edge.FromType == "global::App.CallerA" && edge.ToType == "global::Contracts.Target" && edge.Depth == 1);
        Assert.Contains(graph.TypeDependencies!, edge => edge.FromType == "global::Contracts.Target" && edge.ToType == "global::Further.Dependency" && edge.Depth == 2);
        Assert.DoesNotContain(graph.TypeDependencies!, edge => edge.FromType == "global::App.CallerB");

        var incoming = await DependencyGraphScanner.ScanSolutionAsync(
            fixture.Solution,
            options: new DependencyGraphScanOptions(
                TargetTypeName: "Contracts.Target",
                Direction: DependencyGraphDirection.Incoming,
                Depth: 1));
        Assert.Single(incoming.TypeDependencies!);
        Assert.Equal("global::App.CallerA", incoming.TypeDependencies![0].FromType);
    }

    [Fact]
    public async Task ScanSolutionAsync_ContinuesDocumentScanWithoutRepeatingPriorPage()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyContinuation.slnx",
            new ProjectSpec("App", [
                ("A.cs", "namespace App; public class A { }"),
                ("B.cs", "namespace App; public class B { }")], VirtualProjectDirectory: "src/App"));

        var first = await DependencyGraphScanner.ScanSolutionAsync(
            fixture.Solution,
            options: new DependencyGraphScanOptions(MaxDocuments: 1));
        var nextOffset = Assert.IsType<int>(first.NextDocumentOffset);
        var second = await DependencyGraphScanner.ScanSolutionAsync(
            fixture.Solution,
            options: new DependencyGraphScanOptions(MaxDocuments: 1, DocumentOffset: nextOffset));

        Assert.Equal(1, first.ScannedDocumentCount);
        Assert.Equal(1, second.ScannedDocumentCount);
        Assert.Equal(0, first.DocumentOffset);
        Assert.Equal(1, second.DocumentOffset);
        Assert.Equal(1, first.NextDocumentOffset);
        Assert.Null(second.NextDocumentOffset);
        Assert.False(second.DocumentLimitReached);
        Assert.True(second.IsTruncated);
        Assert.False(second.IsComplete);
    }

    [Fact]
    public async Task ScanSolutionAsync_FiltersByTargetFileAndClampsDepth()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyTargetFile.slnx",
            new ProjectSpec("Contracts", [("Types.cs", "namespace Contracts; public class Target { } public class Other { }")], VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("App", [("Callers.cs", "namespace App; public class CallerA { public Contracts.Target Value { get; set; } = new(); } public class CallerB { public Contracts.Other Value { get; set; } = new(); }")], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/App"));

        var graph = await DependencyGraphScanner.ScanSolutionAsync(
            fixture.Solution,
            options: new DependencyGraphScanOptions(
                TargetFilePath: "src/App/Callers.cs",
                Direction: DependencyGraphDirection.Outgoing,
                Depth: 9));

        Assert.Equal(2, graph.TypeDependencies?.Count);
        Assert.True(graph.IsDepthClamped);
        Assert.Equal(DependencyGraphScanner.MaximumDepth, graph.EffectiveDepth);
        Assert.All(graph.TypeDependencies!, edge => Assert.Equal("src/App/Callers.cs", edge.FromFile));
    }

    [Fact]
    public async Task ScanAndMergeAsync_TraversesAcrossMoreThanOneThousandDocumentsAndReportsNodeCap()
    {
        var appDocuments = new System.Collections.Generic.List<(string FileName, string Content)>
        {
            ("AStart.cs", "namespace App; public class Caller { public Contracts.Target Value { get; set; } = new(); }")
        };
        appDocuments.AddRange(Enumerable.Range(0, 999).Select(index =>
            ($"M{index:D4}.cs", $"namespace App; public class Filler{index:D4} {{ }}")));

        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyLargeContinuation.slnx",
            new ProjectSpec("App", appDocuments, ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/App"),
            new ProjectSpec("Contracts", [("Types.cs", "namespace Contracts; public class Target { public Further.Dependency Link { get; set; } = new(); }")], ProjectReferences: ["Further"], VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("Further", [("Dependency.cs", "namespace Further; public class Dependency { }")], VirtualProjectDirectory: "src/Further"));

        var firstWindow = await DependencyGraphScanner.ScanSolutionAsync(fixture.Solution);
        var nextOffset = Assert.IsType<int>(firstWindow.NextDocumentOffset);
        var secondWindow = await DependencyGraphScanner.ScanSolutionAsync(
            fixture.Solution,
            options: new DependencyGraphScanOptions(DocumentOffset: nextOffset));

        Assert.Equal(1002, firstWindow.TotalDocumentCount);
        Assert.Equal(1000, firstWindow.ScannedDocumentCount);
        Assert.Single(firstWindow.TypeDependencies!);
        Assert.Equal("global::Contracts.Target", firstWindow.TypeDependencies![0].ToType);
        Assert.Equal(1000, secondWindow.DocumentOffset);
        Assert.Equal(2, secondWindow.ScannedDocumentCount);
        Assert.Null(secondWindow.NextDocumentOffset);
        Assert.Contains(secondWindow.TypeDependencies!, edge =>
            edge.FromType == "global::Contracts.Target" && edge.ToType == "global::Further.Dependency");

        var partial = DependencyGraphTraversal.MergeAndTraverse(
            [firstWindow],
            new DependencyGraphTraversalOptions(TargetTypeName: "App.Caller", Direction: DependencyGraphDirection.Outgoing, Depth: 2));
        Assert.Equal(1000, partial.NextDocumentOffset);
        Assert.True(partial.DocumentLimitReached);
        Assert.False(partial.IsComplete);

        var complete = DependencyGraphTraversal.MergeAndTraverse(
            [firstWindow, secondWindow],
            new DependencyGraphTraversalOptions(
                TargetTypeName: "App.Caller",
                Direction: DependencyGraphDirection.Outgoing,
                Depth: 2));
        Assert.Equal(2, complete.TypeDependencies?.Count);
        Assert.Contains(complete.TypeDependencies!, edge => edge.Depth == 1 && edge.FromType == "global::App.Caller");
        Assert.Contains(complete.TypeDependencies!, edge => edge.Depth == 2 && edge.FromType == "global::Contracts.Target");
        Assert.True(complete.IsComplete);

        var incoming = DependencyGraphTraversal.MergeAndTraverse(
            [firstWindow, secondWindow],
            new DependencyGraphTraversalOptions(
                TargetTypeName: "Further.Dependency",
                Direction: DependencyGraphDirection.Incoming,
                Depth: 2));
        Assert.Equal(2, incoming.TypeDependencies?.Count);
        Assert.Contains(incoming.TypeDependencies!, edge => edge.Depth == 1 && edge.ToType == "global::Further.Dependency");
        Assert.Contains(incoming.TypeDependencies!, edge => edge.Depth == 2 && edge.FromType == "global::App.Caller");
        Assert.True(incoming.IsComplete);

        var fileTarget = DependencyGraphTraversal.MergeAndTraverse(
            [firstWindow, secondWindow],
            new DependencyGraphTraversalOptions(
                TargetFilePath: "src/App/AStart.cs",
                Direction: DependencyGraphDirection.Outgoing,
                Depth: 2));
        Assert.Equal(2, fileTarget.TypeDependencies?.Count);
        Assert.True(fileTarget.IsComplete);

        var capped = DependencyGraphTraversal.MergeAndTraverse(
            [firstWindow, secondWindow],
            new DependencyGraphTraversalOptions(
                TargetTypeName: "App.Caller",
                Direction: DependencyGraphDirection.Outgoing,
                Depth: 2,
                MaxNodes: 1));
        Assert.True(capped.NodeLimitReached);
        Assert.Equal(1, capped.VisitedTypeCount);
        Assert.Equal(1, capped.HiddenTypeDependencyCount);
        Assert.True(capped.IsTruncated);
        Assert.False(capped.IsComplete);
    }
}
