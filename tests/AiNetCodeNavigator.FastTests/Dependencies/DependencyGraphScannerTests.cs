#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Dependencies;

[Trait("Category", "Unit")]
public sealed class DependencyGraphScannerTests
{
    [Fact]
    public void ProjectReferences_FollowDirectedDepthDeduplicateCyclesAndKeepExactOwnerBounds()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(@"C:\VirtualRepo\Projects.slnx",
            new ProjectSpec("A", [], VirtualProjectDirectory: "src/A"),
            new ProjectSpec("B", [], VirtualProjectDirectory: "src/B"),
            new ProjectSpec("C", [], VirtualProjectDirectory: "src/C"),
            new ProjectSpec("D", [], VirtualProjectDirectory: "src/D"));
        var owners = fixture.Solution.Projects.ToDictionary(project => project.Name, project => project.Id);
        using var cycleWorkspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var references = new Dictionary<string, string[]> { ["A"] = ["B"], ["B"] = ["C"], ["C"] = ["A"], ["D"] = ["B"] };
        var solution = cycleWorkspace.AddSolution(Microsoft.CodeAnalysis.SolutionInfo.Create(
            Microsoft.CodeAnalysis.SolutionId.CreateNewId(), Microsoft.CodeAnalysis.VersionStamp.Create(),
            projects: fixture.Solution.Projects.Select(project => Microsoft.CodeAnalysis.ProjectInfo.Create(project.Id,
                Microsoft.CodeAnalysis.VersionStamp.Create(), project.Name == "C" ? "B" : project.Name, project.AssemblyName!,
                Microsoft.CodeAnalysis.LanguageNames.CSharp, filePath: project.FilePath,
                projectReferences: references[project.Name].Select(name => new Microsoft.CodeAnalysis.ProjectReference(owners[name]))))));
        var contexts = owners.Values.ToDictionary(id => id, id => "context-" + id.Id);
        var direct = DependencyProjectTraversal.Traverse(solution, owners["A"], DependencyGraphDirection.Outgoing, 1, 50, contexts);
        Assert.Single(direct.ProjectDependencies);
        Assert.Equal("ProjectReference", direct.ProjectDependencies[0].Origin);
        Assert.Equal(owners["A"].Id.ToString("D"), direct.Root.ProjectId);
        Assert.Equal(contexts[owners["A"]], direct.Root.OwnerContextFingerprint);
        var outgoing = DependencyProjectTraversal.Traverse(solution, owners["A"], DependencyGraphDirection.Outgoing, 3, 50, contexts);
        Assert.Equal(3, outgoing.ProjectDependencies.Count);
        Assert.DoesNotContain(outgoing.Projects, project => project.Name == "D");
        Assert.Equal(2, outgoing.Projects.Count(project => project.Name == "B"));
        Assert.Equal(3, outgoing.Projects.Select(project => project.ProjectId).Distinct().Count());
        var incoming = DependencyProjectTraversal.Traverse(solution, owners["A"], DependencyGraphDirection.Incoming, 3, 50, contexts);
        Assert.Equal(4, incoming.ProjectDependencies.Count);
        var both = DependencyProjectTraversal.Traverse(solution, owners["A"], DependencyGraphDirection.Both, 3, 50, contexts);
        Assert.Equal(incoming.ProjectDependencies.Select(edge => (edge.FromProjectId, edge.ToProjectId)).OrderBy(edge => edge).ToArray(),
            both.ProjectDependencies.Select(edge => (edge.FromProjectId, edge.ToProjectId)).OrderBy(edge => edge).ToArray());
        var page = DependencyProjectTraversal.Traverse(solution, owners["A"], DependencyGraphDirection.Both, 3, 1, contexts);
        Assert.Equal(4, page.TotalProjectDependencyCount);
        Assert.Single(page.ProjectDependencies);
        Assert.True(page.HasMore);
        Assert.False(page.IsComplete);
        var bounded = DependencyProjectTraversal.Traverse(solution, owners["A"], DependencyGraphDirection.Outgoing, 3, 50, contexts, maxNodes: 2);
        Assert.Equal(2, bounded.VisitedProjectCount);
        Assert.True(bounded.NodeLimitReached);
        Assert.Equal(1, bounded.HiddenProjectDependencyCount);
        Assert.Single(bounded.ProjectDependencies);
        var isolated = DependencyProjectTraversal.Traverse(solution, owners["D"], DependencyGraphDirection.Incoming, 1, 50);
        Assert.Equal("D", isolated.Root.Name);
        Assert.Empty(isolated.ProjectDependencies);
        Assert.Empty(isolated.Projects);
        Assert.True(isolated.IsComplete);
    }

    [Fact]
    public async Task Project_SelectedLevelKeepsOnlyItsTotalsLimitsAndRepresentativeEvidence()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(@"C:\VirtualRepo\SelectedDependencies.slnx",
            new ProjectSpec("App", [("Caller.cs", "namespace App; public class Caller { public Contracts.First First; public Contracts.Second Second; }")],
                ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/App"),
            new ProjectSpec("Contracts", [("Types.cs", "namespace Contracts; public class First { } public class Second { }")],
                VirtualProjectDirectory: "src/Contracts"));
        var collection = await DependencyGraphScanner.CollectAsync(fixture.Solution, new DependencyGraphCollectionOptions());
        foreach (var level in new[] { DependencyGraphLevel.Type, DependencyGraphLevel.File, DependencyGraphLevel.Namespace })
        {
            var graph = DependencyGraphScanner.Project(collection, new DependencyGraphProjectionOptions(
                PageSize: 1, TargetTypeName: "App.Caller", Direction: DependencyGraphDirection.Outgoing, Level: level));
            Assert.Empty(graph.ProjectDependencies);
            Assert.Equal(0, graph.TotalProjectDependencyCount);
            var evidence = level switch
            {
                DependencyGraphLevel.Type => Assert.Single(graph.TypeDependencies!).Evidence,
                DependencyGraphLevel.File => Assert.Single(graph.FileDependencies).Evidence,
                _ => Assert.Single(graph.NamespaceDependencies).Evidence,
            };
            Assert.NotNull(evidence);
            Assert.Equal("src/App/Caller.cs", evidence.FilePath);
            Assert.Equal(1, evidence.Line);
            Assert.True(evidence.Column > 1);
            Assert.Contains("App", evidence.FromProjectIdentity, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Contracts", evidence.ToProjectIdentity, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(level == DependencyGraphLevel.Type, graph.IsTruncated);
            Assert.Equal(level == DependencyGraphLevel.Type ? 2 : 0, graph.TotalTypeDependencyCount);
            Assert.Equal(level == DependencyGraphLevel.File ? 1 : 0, graph.TotalFileDependencyCount);
            Assert.Equal(level == DependencyGraphLevel.Namespace ? 1 : 0, graph.TotalNamespaceDependencyCount);
        }
    }

    [Fact]
    public async Task ScanSolutionAsync_FormatsHandoffsOnlyForTheVisibleTypePage()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\\VirtualRepo\\DependencyHandoffBudget.slnx",
            new ProjectSpec("App", [
                ("Consumers.cs", "namespace App; public class Consumer { public Contracts.First First = new(); public Contracts.Second Second = new(); }"),
            ], ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/App"),
            new ProjectSpec("Contracts", [
                ("Types.cs", "namespace Contracts; public class First { } public class Second { }"),
            ], VirtualProjectDirectory: "src/Contracts"));
        var handoffCalls = 0;

        var page = await DependencyGraphScanner.ScanSolutionAsync(fixture.Solution,
            options: new DependencyGraphScanOptions(PageSize: 1),
            handoffFormatter: symbol => $"handoff:{++handoffCalls}:{symbol.Name}");

        var edge = Assert.Single(page.TypeDependencies!);
        Assert.Equal(2, handoffCalls);
        Assert.StartsWith("handoff:", edge.FromHandoffId, StringComparison.Ordinal);
        Assert.StartsWith("handoff:", edge.ToHandoffId, StringComparison.Ordinal);
        Assert.Equal(2, page.TotalTypeDependencyCount);
        Assert.True(page.IsTruncated);
    }

    [Fact]
    public async Task ScanSolutionAsync_FiltersScopeAndGeneratedDocumentsBeforeDocumentLimits()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyScope.slnx",
            new ProjectSpec("App", [
                ("Consumer.cs", "namespace App; public class Consumer { public Target Value = new(); }"),
                ("Generated.g.cs", "namespace App; public class GeneratedConsumer { public Target Value = new(); }"),
                ("Target.cs", "namespace App; public class Target { }")], VirtualProjectDirectory: "src/App"),
            new ProjectSpec("App.Tests", [
                ("TestConsumer.cs", "namespace App.Tests; public class TestConsumer { public App.Target Value = new(); }")],
                ProjectReferences: ["App"], VirtualProjectDirectory: "tests/App.Tests"));

        var production = await DependencyGraphScanner.ScanSolutionAsync(fixture.Solution,
            options: new DependencyGraphScanOptions(MaxDocuments: 1, ScopeType: SymbolScopeType.Production));
        var productionWithGenerated = await DependencyGraphScanner.ScanSolutionAsync(fixture.Solution,
            options: new DependencyGraphScanOptions(MaxDocuments: 1, ScopeType: SymbolScopeType.Production, IncludeGenerated: true));
        var tests = await DependencyGraphScanner.ScanSolutionAsync(fixture.Solution,
            options: new DependencyGraphScanOptions(MaxDocuments: 1, ScopeType: SymbolScopeType.Tests));

        Assert.Equal(2, production.TotalDocumentCount);
        Assert.Equal(3, productionWithGenerated.TotalDocumentCount);
        Assert.Equal(1, tests.TotalDocumentCount);
        Assert.Equal(1, production.ScannedDocumentCount);
        Assert.Equal(1, productionWithGenerated.ScannedDocumentCount);
        Assert.Equal(1, tests.ScannedDocumentCount);
        Assert.Equal(1, production.NextDocumentOffset);
        Assert.Equal(1, productionWithGenerated.NextDocumentOffset);
        Assert.Null(tests.NextDocumentOffset);
    }

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
    public async Task CollectAsync_ProjectsRelationshipPagesWithoutRescanningDocuments()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyPageCollection.slnx",
            new ProjectSpec("Contracts", [
                ("Types.cs", "namespace Contracts; public class First { } public class Second { }")], VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("App", [
                ("Consumer.cs", "namespace App; public class Consumer { public Contracts.First First = new(); public Contracts.Second Second = new(); }")],
                ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/App"));
        var compilationAcquisitions = new List<string>();
        var documentCollections = new List<string>();

        var collection = await DependencyGraphScanner.CollectAsync(
            fixture.Solution,
            new DependencyGraphCollectionOptions(),
            observer: new DependencyGraphCollectionObserver(
                project => compilationAcquisitions.Add(project.Name),
                document => documentCollections.Add(document.FilePath ?? document.Name)));
        var firstPage = DependencyGraphScanner.Project(collection,
            new DependencyGraphProjectionOptions(Offset: 0, PageSize: 1));
        var secondPage = DependencyGraphScanner.Project(collection,
            new DependencyGraphProjectionOptions(Offset: 1, PageSize: 1));

        Assert.Single(firstPage.TypeDependencies!);
        Assert.Single(secondPage.TypeDependencies!);
        Assert.Equal(2, firstPage.TotalTypeDependencyCount);
        Assert.Equal(2, secondPage.TotalTypeDependencyCount);
        Assert.Equal(2, collection.NewSemanticScanCount);
        Assert.Equal(2, collection.CoveredDocumentCount);
        Assert.Equal(2, compilationAcquisitions.Distinct(StringComparer.Ordinal).Count());
        Assert.All(compilationAcquisitions.GroupBy(name => name), group => Assert.Single(group));
        Assert.Equal(2, documentCollections.Distinct(StringComparer.Ordinal).Count());
        Assert.All(documentCollections.GroupBy(path => path), group => Assert.Single(group));
    }

    [Fact]
    public async Task CollectAsync_RetainsIdenticalDocumentMultiplicityWithDeterministicOrdinals()
    {
        const string partialClass = "namespace App; public partial class Duplicate { }";
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyDuplicateDocuments.slnx",
            new ProjectSpec("App", [
                ("Duplicate.cs", partialClass),
                ("Duplicate.cs", partialClass)], VirtualProjectDirectory: "src/App"));

        var collection = await DependencyGraphScanner.CollectAsync(fixture.Solution, new DependencyGraphCollectionOptions());

        Assert.Equal(2, collection.EligibleDocumentCount);
        Assert.Equal(2, collection.RequiredDocumentCount);
        Assert.Equal(2, collection.AttemptedDocuments.Length);
        Assert.Equal(2, collection.CoveredDocuments.Length);
        Assert.Equal(new[] { 0, 1 }, collection.RequiredDocuments.Select(document => document.DuplicateOrdinal).ToArray());
    }

    [Fact]
    public async Task CollectAsync_DrainsMoreThanOneThousandDocumentsAndFindsLateRoot()
    {
        var appDocuments = Enumerable.Range(0, 1000)
            .Select(index => ($"Filler{index:D4}.cs", $"namespace App; public class Filler{index:D4} {{ }}"))
            .Append(("ZRoot.cs", "namespace App; public class LateRoot { public Contracts.Target Value = new(); }"))
            .ToList();
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyCollectionDrain.slnx",
            new ProjectSpec("App", appDocuments, ProjectReferences: ["Contracts"], VirtualProjectDirectory: "src/App"),
            new ProjectSpec("Contracts", [("Target.cs", "namespace Contracts; public class Target { }")], VirtualProjectDirectory: "src/Contracts"));
        var compilationAcquisitions = new List<string>();
        var semanticScans = 0;

        var collection = await DependencyGraphScanner.CollectAsync(
            fixture.Solution,
            new DependencyGraphCollectionOptions(),
            observer: new DependencyGraphCollectionObserver(
                project => compilationAcquisitions.Add(project.Name),
                _ => semanticScans++));
        var graph = DependencyGraphScanner.Project(collection, new DependencyGraphProjectionOptions(
            TargetTypeName: "App.LateRoot", Direction: DependencyGraphDirection.Outgoing, Depth: 1));

        Assert.Equal(1002, collection.EligibleDocumentCount);
        Assert.Equal(1002, collection.NewSemanticScanCount);
        Assert.Equal(1002, semanticScans);
        Assert.Equal(1002, collection.CoveredDocumentCount);
        Assert.Equal(2, compilationAcquisitions.Distinct(StringComparer.Ordinal).Count());
        Assert.All(compilationAcquisitions.GroupBy(name => name), group => Assert.Single(group));
        Assert.Equal(0, graph.DocumentOffset);
        Assert.Null(graph.NextDocumentOffset);
        Assert.Equal(1002, graph.ScannedDocumentCount);
        Assert.Contains(graph.TypeDependencies!, edge => edge.FromType == "global::App.LateRoot" && edge.ToType == "global::Contracts.Target");
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
    public async Task ScanSolutionAsync_CollectsNestedRecordPrimaryConstructorParameterDependency()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyPrimaryConstructor.slnx",
            new ProjectSpec("App", [(
                "Root.cs",
                "namespace RootProbe; public class Outer { public sealed record NestedRecord(Dependency Value); } public sealed class Dependency { }")],
                VirtualProjectDirectory: "src/App"));

        var graph = await DependencyGraphScanner.ScanSolutionAsync(fixture.Solution,
            options: new DependencyGraphScanOptions(
                TargetFilePath: "src/App/Root.cs",
                Direction: DependencyGraphDirection.Outgoing,
                Depth: 1));

        Assert.Contains(graph.TypeDependencies!, edge => edge.FromType == "global::RootProbe.Outer.NestedRecord"
            && edge.ToType == "global::RootProbe.Dependency");
    }

    [Fact]
    public void Traverse_AdmitsSameDepthNeighborsInOrdinalOrderBeforeApplyingNodeCap()
    {
        var edges = new[]
        {
            CreateEdge("R", "A"),
            CreateEdge("R", "B"),
            CreateEdge("A", "Z"),
            CreateEdge("B", "C"),
            CreateEdge("Z", "X"),
            CreateEdge("C", "D"),
        };

        var traversal = DependencyGraphScanner.Traverse(
            edges,
            targetFilePath: null,
            targetTypeName: "App.R",
            targetProject: null,
            targetTypeId: null,
            targetTypeIds: null,
            DependencyGraphDirection.Outgoing,
            solutionDir: string.Empty,
            maxDepth: 3,
            maxNodes: 6);

        Assert.Equal(6, traversal.VisitedTypeCount);
        Assert.True(traversal.NodeLimitReached);
        Assert.Equal(1, traversal.HiddenTypeDependencyCount);
        Assert.Contains(traversal.Edges, edge => edge.FromTypeId == "C" && edge.ToTypeId == "D" && edge.Depth == 3);
        Assert.DoesNotContain(traversal.Edges, edge => edge.FromTypeId == "Z" && edge.ToTypeId == "X");
    }

    private static DependencyTypeReference CreateEdge(string from, string to) => new(
        from,
        to,
        $"global::App.{from}",
        $"global::App.{to}",
        from,
        to,
        "App",
        "App",
        "App",
        "App",
        $"src/App/{from}.cs",
        $"src/App/{to}.cs");

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

    [Fact]
    public void MergeAndTraverse_AcceptsSharedOffsetPagesWhenShortCollectionsAreExhausted()
    {
        var typeEdges = Enumerable.Range(0, 101)
            .Select(index => new DependencyTypeReference(
                "app-root",
                $"contracts-target-{index:D3}",
                "global::App.Root",
                $"global::Contracts.Target{index:D3}",
                "Root",
                $"Target{index:D3}",
                "App",
                "Contracts",
                "App",
                "Contracts",
                "src/App/Root.cs",
                "src/Contracts/Targets.cs"))
            .ToList();

        var firstPage = new DependencyGraphPayload(
            ProjectDependencies: [new ProjectDependency("App", "Contracts")],
            NamespaceDependencies: [],
            FileDependencies: [],
            TotalProjectDependencyCount: 1,
            Offset: 0,
            PageSize: 100,
            ScannedDocumentCount: 2,
            TotalDocumentCount: 2,
            TypeDependencies: typeEdges.Take(100).ToList(),
            TotalTypeDependencyCount: typeEdges.Count);
        var secondPage = firstPage with
        {
            ProjectDependencies = [],
            Offset = 100,
            TypeDependencies = typeEdges.Skip(100).ToList()
        };

        var options = new DependencyGraphTraversalOptions(
            TargetTypeName: "App.Root",
            Direction: DependencyGraphDirection.Outgoing,
            Depth: 1,
            PageSize: 200);
        var merged = DependencyGraphTraversal.MergeAndTraverse([firstPage, secondPage], options);
        var missingTypePage = DependencyGraphTraversal.MergeAndTraverse([firstPage], options);

        Assert.False(merged.ContinuationInputIncomplete);
        Assert.True(merged.IsComplete);
        Assert.Equal(101, merged.TypeDependencies?.Count);
        Assert.Single(merged.ProjectDependencies);
        Assert.False(missingTypePage.IsComplete);
        Assert.True(missingTypePage.ContinuationInputIncomplete);
    }

    [Fact]
    public void MergeAndTraverse_DoesNotInventDocumentCursorForFullyAttemptedPartialCoverage()
    {
        var scanPage = new DependencyGraphPayload(
            ProjectDependencies: [],
            NamespaceDependencies: [],
            FileDependencies: [],
            TotalProjectDependencyCount: 0,
            TotalNamespaceDependencyCount: 0,
            TotalFileDependencyCount: 0,
            ScannedDocumentCount: 1,
            TotalDocumentCount: 2,
            Errors: [new DependencyGraphScanError("App", "Broken.cs", "Source was unavailable.")],
            TypeDependencies: [],
            TotalTypeDependencyCount: 0,
            DocumentOffset: 0,
            NextDocumentOffset: null);

        var result = DependencyGraphTraversal.MergeAndTraverse(
            [scanPage],
            new DependencyGraphTraversalOptions(TargetTypeName: "App.Root", Direction: DependencyGraphDirection.Outgoing));

        Assert.Equal(1, result.ScannedDocumentCount);
        Assert.Equal(2, result.TotalDocumentCount);
        Assert.Null(result.NextDocumentOffset);
        Assert.False(result.DocumentLimitReached);
        Assert.False(result.ContinuationInputIncomplete);
        Assert.Equal("Broken.cs", Assert.Single(result.Errors!).Document);
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public void MergeAndTraverse_UsesSharedPageOffsetsWhenACollectionIsShorterThanThePageWindow()
    {
        var projectDependencies = Enumerable.Range(0, 101)
            .Select(index => new ProjectDependency($"Project{index:D3}", "Dependency"))
            .ToArray();
        var edge = new DependencyTypeReference(
            "app-root", "contracts-target", "global::App.Root", "global::Contracts.Target", "Root", "Target",
            "App", "Contracts", "App", "Contracts", "src/App/Root.cs", "src/Contracts/Target.cs");
        var firstPage = new DependencyGraphPayload(
            ProjectDependencies: projectDependencies.Take(100).ToArray(),
            NamespaceDependencies: [],
            FileDependencies: [],
            TotalProjectDependencyCount: 101,
            Offset: 0,
            PageSize: 100,
            ScannedDocumentCount: 2,
            TotalDocumentCount: 2,
            TypeDependencies: [edge],
            TotalTypeDependencyCount: 1,
            DocumentOffset: 0);
        var secondPage = firstPage with
        {
            ProjectDependencies = projectDependencies.Skip(100).ToArray(),
            Offset = 100,
            TypeDependencies = []
        };

        var result = DependencyGraphTraversal.MergeAndTraverse(
            [firstPage, secondPage],
            new DependencyGraphTraversalOptions(TargetTypeName: "App.Root", Direction: DependencyGraphDirection.Outgoing));

        Assert.Equal(101, result.TotalProjectDependencyCount);
        Assert.Single(result.TypeDependencies!);
        Assert.False(result.ContinuationInputIncomplete);
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public async Task MergeAndTraverse_PreservesEdgeFreeTypeSeedsFromSourceScan()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyEdgeFreeType.slnx",
            new ProjectSpec("App", [("Empty.cs", "namespace App; public sealed class Empty { }")], VirtualProjectDirectory: "src/App"));
        var scanPage = await DependencyGraphScanner.ScanSolutionAsync(fixture.Solution);

        var result = DependencyGraphTraversal.MergeAndTraverse(
            [scanPage],
            new DependencyGraphTraversalOptions(TargetTypeName: "App.Empty", Direction: DependencyGraphDirection.Outgoing));

        Assert.Equal(1, result.VisitedTypeCount);
        Assert.Empty(result.TypeDependencies!);
    }
}
