#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.FastTests.Dependencies;

[Trait("Category", "Unit")]
public sealed class DependencyGraphOutgoingCollectorTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    public async Task CollectAsync_PartialCrossProjectFrontiersMatchBroadWithoutTerminalOrUnrelatedScans(int depth, int expectedScans)
    {
        using var fixture = CreateFixture();
        var scanned = new List<string>();
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(
            DocumentCollected: document => scanned.Add(document.Name)));
        var root = await TypeAsync(fixture.Solution, "App", "Probe.Root");
        var options = Options(fixture.Solution, [root], depth);

        var targeted = await CollectAsync(cache, fixture.Solution, [root], options);
        var broad = await DependencyGraphScanner.CollectAsync(fixture.Solution, new DependencyGraphCollectionOptions());

        Assert.Equal(expectedScans, targeted.NewSemanticScanCount);
        Assert.Equal(expectedScans, targeted.RequiredDocumentCount);
        Assert.Equal(expectedScans, targeted.CoveredDocumentCount);
        Assert.True(targeted.CoveredDocumentCount < targeted.EligibleDocumentCount);
        Assert.DoesNotContain("Noise.cs", scanned);
        Assert.DoesNotContain("Terminal.cs", scanned);
        AssertEquivalent(DependencyGraphScanner.Project(broad, options), DependencyGraphScanner.Project(targeted, options));
        Assert.False(DependencyGraphScanner.Project(targeted, options).DocumentLimitReached);
        Assert.False(targeted.ContinuationInputIncomplete);
        Assert.Null(targeted.NextDocumentOffset);
    }

    [Fact]
    public async Task CollectAsync_ReusesPartialFactsAndBroadFillsRemainingCoverage()
    {
        using var fixture = CreateFixture();
        await using var cache = new DependencyGraphCache();
        var root = await TypeAsync(fixture.Solution, "App", "Probe.Root");
        var options = Options(fixture.Solution, [root], 1);
        var cold = await CollectAsync(cache, fixture.Solution, [root], options);
        var warm = await CollectAsync(cache, fixture.Solution, [root], options);
        var broad = await cache.CollectAsync(fixture.Solution, new DependencyGraphCollectionOptions(), Target, 1);
        var fullWarm = await CollectAsync(cache, fixture.Solution, [root], options with { Depth = 3 });

        Assert.Equal(2, cold.NewSemanticScanCount);
        Assert.Equal(0, warm.NewSemanticScanCount);
        Assert.Equal(broad.EligibleDocumentCount - 2, broad.NewSemanticScanCount);
        Assert.Equal(broad.EligibleDocumentCount, broad.CoveredDocumentCount);
        Assert.Equal(0, fullWarm.NewSemanticScanCount);
        Assert.Equal(broad.EligibleDocumentCount, fullWarm.CoveredDocumentCount);
        AssertEquivalent(DependencyGraphScanner.Project(broad, options with { Depth = 3 }),
            DependencyGraphScanner.Project(fullWarm, options with { Depth = 3 }));
    }

    [Fact]
    public async Task CollectAsync_ScopeAndGeneratedFiltersRetainExcludedAnchorsAndEligiblePartials()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(Target,
            new ProjectSpec("App", [
                ("Root.Tests.cs", "namespace Probe; public partial class Root { public Target TestValue = new(); } public class TestOnly { public Target Value = new(); }"),
                ("Root.cs", "namespace Probe; public partial class Root { public Target Value = new(); }"),
                ("Root.g.cs", "namespace Probe; public partial class Root { public Target GeneratedValue = new(); }"),
                ("Target.cs", "namespace Probe; public sealed class Target { }")], VirtualProjectDirectory: "src/App"));
        var root = await TypeAsync(fixture.Solution, "App", "Probe.Root");
        var options = Options(fixture.Solution, [root], 1);
        await using var cache = new DependencyGraphCache();
        var production = await CollectAsync(cache, fixture.Solution, [root], options, SymbolScopeType.Production);
        var tests = await CollectAsync(cache, fixture.Solution, [root], options, SymbolScopeType.Tests);
        var generated = await CollectAsync(cache, fixture.Solution, [root], options, SymbolScopeType.Production, true);

        Assert.Equal(1, production.NewSemanticScanCount);
        Assert.Equal("Root.cs", Assert.Single(production.RequiredDocuments).Name);
        Assert.Equal(1, tests.NewSemanticScanCount);
        Assert.Equal("Root.Tests.cs", Assert.Single(tests.RequiredDocuments).Name);
        Assert.Equal(2, generated.NewSemanticScanCount);
        Assert.Equal(new[] { "Root.cs", "Root.g.cs" }, generated.RequiredDocuments.Select(document => document.Name).OrderBy(name => name, StringComparer.Ordinal));
        foreach (var result in new[] { production, tests, generated })
        {
            var graph = DependencyGraphScanner.Project(result, options);
            Assert.Contains(graph.TypeDependencies!, edge => edge.FromTypeName == "Root" && edge.ToTypeName == "Target");
            Assert.False(graph.DocumentLimitReached);
            Assert.False(graph.ContinuationInputIncomplete);
        }
        var excluded = await TypeAsync(fixture.Solution, "App", "Probe.TestOnly");
        var excludedOptions = Options(fixture.Solution, [excluded], 1);
        var anchored = await CollectAsync(cache, fixture.Solution, [excluded], excludedOptions, SymbolScopeType.Production);
        Assert.Equal(0, anchored.RequiredDocumentCount);
        Assert.Equal(0, anchored.NewSemanticScanCount);
        var anchoredGraph = DependencyGraphScanner.Project(anchored, excludedOptions);
        Assert.Equal(1, anchoredGraph.VisitedTypeCount);
        Assert.Empty(anchoredGraph.TypeDependencies!);
        Assert.True(anchoredGraph.IsComplete);
    }

    [Fact]
    public async Task CollectAsync_FileSeedsIncludeEveryNamedKindAndEmptyFilesScanNothing()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(Target,
            new ProjectSpec("App", [
                ("Seeds.cs", "namespace Probe; public class Outer { public class Nested { } } public record R; public struct S { } public interface I { } public enum E { One } public delegate void D();"),
                ("Empty.cs", "namespace Probe; // no named types")], VirtualProjectDirectory: "src/App"));
        var seedsDocument = fixture.Solution.Projects.Single().Documents.Single(document => document.Name == "Seeds.cs");
        var emptyDocument = fixture.Solution.Projects.Single().Documents.Single(document => document.Name == "Empty.cs");
        var roots = await DependencyGraphScanner.GetDocumentNamedTypesAsync(seedsDocument, CancellationToken.None);
        var emptyRoots = await DependencyGraphScanner.GetDocumentNamedTypesAsync(emptyDocument, CancellationToken.None);
        await using var cache = new DependencyGraphCache();
        var options = Options(fixture.Solution, roots, 1);
        var selected = await CollectAsync(cache, fixture.Solution, roots, options);
        var emptyOptions = Options(fixture.Solution, emptyRoots, 1);
        var empty = await CollectAsync(cache, fixture.Solution, emptyRoots, emptyOptions);

        Assert.Equal(7, roots.Count);
        Assert.Equal(1, selected.NewSemanticScanCount);
        Assert.Equal(7, DependencyGraphScanner.Project(selected, options).VisitedTypeCount);
        Assert.Empty(emptyRoots);
        Assert.Equal(0, empty.RequiredDocumentCount);
        Assert.Equal(0, empty.NewSemanticScanCount);
        var graph = DependencyGraphScanner.Project(empty, emptyOptions);
        Assert.Equal(0, graph.VisitedTypeCount);
        Assert.Empty(graph.TypeDependencies!);
        Assert.True(graph.IsComplete);
    }

    [Fact]
    public async Task CollectAsync_AdmitsSeedsAndNeighborsBeforeSchedulingTheirDocuments()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(Target,
            new ProjectSpec("App", [
                ("Root.cs", "namespace Probe; public class Root { public A First = new(); public B Second = new(); }"),
                ("A.cs", "namespace Probe; public class A { public Root Back = new(); }"),
                ("B.cs", "namespace Probe; public class B { public Root Back = new(); }")], VirtualProjectDirectory: "src/App"));
        var root = await TypeAsync(fixture.Solution, "App", "Probe.Root");
        await using var cache = new DependencyGraphCache();
        var options = Options(fixture.Solution, [root], 3) with { MaxNodes = 2 };
        var collected = await CollectAsync(cache, fixture.Solution, [root], options);
        var broad = await DependencyGraphScanner.CollectAsync(fixture.Solution, new DependencyGraphCollectionOptions());
        var graph = DependencyGraphScanner.Project(collected, options);
        Assert.Equal(new[] { "A.cs", "Root.cs" }, collected.RequiredDocuments.Select(document => document.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(2, collected.NewSemanticScanCount);
        Assert.Equal(2, graph.VisitedTypeCount);
        Assert.Equal(1, graph.HiddenTypeDependencyCount);
        Assert.True(graph.NodeLimitReached);
        AssertEquivalent(DependencyGraphScanner.Project(broad, options), graph);

        var seeds = new[] { await TypeAsync(fixture.Solution, "App", "Probe.A"), await TypeAsync(fixture.Solution, "App", "Probe.B") };
        await using var seedCache = new DependencyGraphCache();
        var seedOptions = Options(fixture.Solution, seeds, 1) with { MaxNodes = 1 };
        var seeded = await CollectAsync(seedCache, fixture.Solution, seeds, seedOptions);
        Assert.Equal("A.cs", Assert.Single(seeded.RequiredDocuments).Name);
        Assert.True(DependencyGraphScanner.Project(seeded, seedOptions).NodeLimitReached);
    }

    [Fact]
    public async Task CollectAsync_ColdLateRootScansOneDocumentBeyondThousandDocumentPlan()
    {
        var documents = Enumerable.Range(0, 1001).Select(index => ($"Filler{index:D4}.cs", $"namespace Probe; public class Filler{index:D4} {{ }}"))
            .Append(("ZRoot.cs", "namespace Probe; public class LateRoot { public Target Value = new(); }"))
            .Append(("Target.cs", "namespace Probe; public class Target { }")).ToArray();
        using var fixture = TestWorkspaceBuilder.CreateSolution(Target, new ProjectSpec("App", documents, VirtualProjectDirectory: "src/App"));
        var root = await TypeAsync(fixture.Solution, "App", "Probe.LateRoot");
        await using var cache = new DependencyGraphCache();
        var options = Options(fixture.Solution, [root], 1);
        var selected = await CollectAsync(cache, fixture.Solution, [root], options);
        var broad = await DependencyGraphScanner.CollectAsync(fixture.Solution, new DependencyGraphCollectionOptions());
        Assert.Equal(1003, selected.EligibleDocumentCount);
        Assert.Equal(1, selected.NewSemanticScanCount);
        Assert.Equal("ZRoot.cs", Assert.Single(selected.RequiredDocuments).Name);
        AssertEquivalent(DependencyGraphScanner.Project(broad, options), DependencyGraphScanner.Project(selected, options));
    }

    [Fact]
    public async Task CollectAsync_FailedRequiredDocumentIsAttemptedOnceAndRetriedByNextRequest()
    {
        using var fixture = CreateFixture();
        var attempts = 0;
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(DocumentCollected: document =>
        {
            if (document.Name == "Root.cs" && Interlocked.Increment(ref attempts) == 1) throw new InvalidOperationException("recoverable fixture failure");
        }));
        var root = await TypeAsync(fixture.Solution, "App", "Probe.Root");
        var options = Options(fixture.Solution, [root], 3);
        var partial = await CollectAsync(cache, fixture.Solution, [root], options);
        Assert.Equal(1, attempts);
        Assert.Single(partial.Errors);
        Assert.Equal(3, partial.CoveredDocumentCount);
        Assert.False(DependencyGraphScanner.Project(partial, options).IsComplete);
        var retry = await CollectAsync(cache, fixture.Solution, [root], options);
        Assert.Equal(2, attempts);
        Assert.Empty(retry.Errors);
        Assert.Equal(1, retry.NewSemanticScanCount);
        Assert.Equal(4, retry.CoveredDocumentCount);
    }

    [Fact]
    public async Task CollectAsync_LinkedPhysicalDocumentsRemainSeparateExactSemanticOwners()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(Target,
            new ProjectSpec("First", [
                ("Root.cs", "namespace Shared; public class Root { public Next Value = new(); }"),
                ("Next.cs", "namespace Shared; public class Next { }")], VirtualProjectDirectory: "src/First"),
            new ProjectSpec("Second", [
                ("Root.cs", "namespace Shared; public class Root { public Next Value = new(); }"),
                ("Next.cs", "namespace Shared; public class Next { }")], VirtualProjectDirectory: "src/Second"));
        var solution = fixture.Solution;
        foreach (var document in solution.Projects.SelectMany(project => project.Documents).Where(document => document.Name == "Root.cs").ToArray())
            solution = solution.WithDocumentFilePath(document.Id, @"C:\VirtualRepo\shared\Root.cs");
        var firstRoot = await TypeAsync(solution, "First", "Shared.Root");
        var secondRoot = await TypeAsync(solution, "Second", "Shared.Root");
        await using var cache = new DependencyGraphCache();
        var first = await CollectAsync(cache, solution, [firstRoot], Options(solution, [firstRoot], 1));
        var second = await CollectAsync(cache, solution, [secondRoot], Options(solution, [secondRoot], 1));

        Assert.Equal(1, first.NewSemanticScanCount);
        Assert.Equal(1, second.NewSemanticScanCount);
        Assert.Equal(Assert.Single(first.RequiredDocuments).DocumentPath, Assert.Single(second.RequiredDocuments).DocumentPath);
        Assert.NotEqual(Assert.Single(first.RequiredDocuments).OwnerProjectPath, Assert.Single(second.RequiredDocuments).OwnerProjectPath);
        Assert.NotEqual(Assert.Single(first.TypeDependencies).FromTypeId, Assert.Single(second.TypeDependencies).FromTypeId);
        Assert.Equal("First", Assert.Single(first.TypeDependencies).FromProject);
        Assert.Equal("Second", Assert.Single(second.TypeDependencies).FromProject);
        var broad = await cache.CollectAsync(solution, new DependencyGraphCollectionOptions(), Target, 1);
        Assert.Equal(2, broad.NewSemanticScanCount);
        Assert.Equal(4, broad.CoveredDocumentCount);
    }

    private const string Target = @"C:\VirtualRepo\Outgoing.slnx";

    [Fact]
    public async Task CollectAsync_DuplicateOrdinalsStayStableBetweenSubsetAndBroadCollection()
    {
        const string duplicate = "namespace Probe; public partial class Root { }";
        using var fixture = TestWorkspaceBuilder.CreateSolution(Target,
            new ProjectSpec("App", [
                ("Duplicate.cs", duplicate), ("Duplicate.cs", duplicate),
                ("Unused.cs", "namespace Probe; public class Unused { }")], VirtualProjectDirectory: "src/App"));
        var root = await TypeAsync(fixture.Solution, "App", "Probe.Root");
        var options = Options(fixture.Solution, [root], 1);
        await using var cache = new DependencyGraphCache();
        var cold = await CollectAsync(cache, fixture.Solution, [root], options);
        var warm = await CollectAsync(cache, fixture.Solution, [root], options);
        var broad = await cache.CollectAsync(fixture.Solution, new DependencyGraphCollectionOptions(), Target, 1);
        Assert.Equal(new[] { 0, 1 }, cold.RequiredDocuments.Select(document => document.DuplicateOrdinal));
        Assert.Equal(2, cold.NewSemanticScanCount);
        Assert.Equal(0, warm.NewSemanticScanCount);
        Assert.Equal(1, broad.NewSemanticScanCount);
        Assert.Equal(3, broad.CoveredDocumentCount);
    }

    private static TestSolutionHandle CreateFixture() => TestWorkspaceBuilder.CreateSolution(Target,
        new ProjectSpec("App", [
            ("Root.cs", "namespace Probe; public partial class Root { public Library.Next Value = new(); } public class Unrelated { public Noise Value = new(); }"),
            ("Root.Part.cs", "namespace Probe; public partial class Root { public Library.Next Other = new(); }"),
            ("Noise.cs", "namespace Probe; public class Noise { }")], ProjectReferences: ["Library"], VirtualProjectDirectory: "src/App"),
        new ProjectSpec("Library", [
            ("Next.cs", "namespace Library; public class Next { public Leaf Value = new(); }"),
            ("Leaf.cs", "namespace Library; public class Leaf { public Terminal Value = new(); public Next Back = new(); }"),
            ("Terminal.cs", "namespace Library; public class Terminal { }")], VirtualProjectDirectory: "src/Library"));

    private static async Task<INamedTypeSymbol> TypeAsync(Solution solution, string project, string name)
    {
        var compilation = await solution.Projects.Single(candidate => candidate.Name == project).GetCompilationAsync();
        Assert.NotNull(compilation);
        var type = compilation.GetTypeByMetadataName(name);
        Assert.NotNull(type);
        return type;
    }

    private static DependencyGraphProjectionOptions Options(Solution solution, IReadOnlyList<INamedTypeSymbol> roots, int depth) =>
        new(Direction: DependencyGraphDirection.Outgoing, Depth: depth, PageSize: 500,
            TargetTypeIds: roots.Select(type => DependencyGraphScanner.GetSourceTypeId(solution, type)).ToArray());

    private static Task<DependencyGraphCollection> CollectAsync(DependencyGraphCache cache, Solution solution,
        IReadOnlyList<INamedTypeSymbol> roots, DependencyGraphProjectionOptions options,
        SymbolScopeType scope = SymbolScopeType.All, bool includeGenerated = false) =>
        DependencyGraphOutgoingCollector.CollectAsync(cache, solution, new DependencyGraphCollectionOptions(scope, includeGenerated),
            options, roots, Target, 1);

    private static void AssertEquivalent(DependencyGraphPayload expected, DependencyGraphPayload actual)
    {
        Assert.Equal(expected.TypeDependencies, actual.TypeDependencies);
        Assert.Equal(expected.ProjectDependencies, actual.ProjectDependencies);
        Assert.Equal(expected.NamespaceDependencies.Select(edge => (edge.FromProject, edge.FromNamespace, edge.ToProject, edge.ToNamespace, string.Join("|", edge.ReferencedTypes))),
            actual.NamespaceDependencies.Select(edge => (edge.FromProject, edge.FromNamespace, edge.ToProject, edge.ToNamespace, string.Join("|", edge.ReferencedTypes))));
        Assert.Equal(expected.FileDependencies.Select(edge => (edge.FromProject, edge.FromFile, edge.ToProject, edge.ToFile, string.Join("|", edge.CrossingTypes))),
            actual.FileDependencies.Select(edge => (edge.FromProject, edge.FromFile, edge.ToProject, edge.ToFile, string.Join("|", edge.CrossingTypes))));
        Assert.Equal(expected.VisitedTypeCount, actual.VisitedTypeCount);
        Assert.Equal(expected.HiddenTypeDependencyCount, actual.HiddenTypeDependencyCount);
        Assert.Equal(expected.NodeLimitReached, actual.NodeLimitReached);
        Assert.Equal(expected.TotalTypeDependencyCount, actual.TotalTypeDependencyCount);
    }
}
