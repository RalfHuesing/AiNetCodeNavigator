#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Dependencies;

[Trait("Category", "Unit")]
public sealed class DependencyGraphCacheTests
{
    [Fact]
    public async Task CollectAsync_ReusesSuccessfulFactsWithStructurallyEqualDocumentFolders()
    {
        using var fixture = CreateFixture();
        var document = Assert.Single(fixture.Solution.Projects.Single().Documents);
        var solution = fixture.Solution.WithDocumentFolders(document.Id, ["Features", "Core"]);
        var scans = 0;
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(
            DocumentCollected: _ => Interlocked.Increment(ref scans)));

        var cold = await CollectAsync(cache, solution, "C:\\cache\\warm.slnx", ticket: 1);
        var warm = await CollectAsync(cache, solution, "C:\\cache\\warm.slnx", ticket: 1);

        Assert.Equal(1, cold.NewSemanticScanCount);
        Assert.Equal(1, cold.CoveredDocumentCount);
        Assert.Equal(0, warm.NewSemanticScanCount);
        Assert.Equal(1, warm.CoveredDocumentCount);
        Assert.Equal(1, scans);
        var retained = Assert.Single(cache.SnapshotRetainedFactsForTesting(
            "C:\\cache\\warm.slnx", 1, SymbolScopeType.All, includeGenerated: false));
        Assert.Equal(new[] { "Features", "Core" }, retained.Identity.Folders);
    }

    [Fact]
    public async Task CollectAsync_SeparatesSnapshotScopeAndGeneratedBuckets()
    {
        using var fixture = CreateFixture();
        var scans = 0;
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(
            DocumentCollected: _ => Interlocked.Increment(ref scans)));

        var baseline = await CollectAsync(cache, fixture.Solution, "C:\\cache\\identity.slnx", ticket: 1);
        var otherSnapshot = await CollectAsync(cache, fixture.Solution, "C:\\cache\\identity.slnx", ticket: 2);
        var otherScope = await CollectAsync(cache, fixture.Solution, "C:\\cache\\identity.slnx", ticket: 1,
            scope: SymbolScopeType.Production);
        var generated = await CollectAsync(cache, fixture.Solution, "C:\\cache\\identity.slnx", ticket: 1,
            includeGenerated: true);
        var warm = await CollectAsync(cache, fixture.Solution, "C:\\cache\\identity.slnx", ticket: 1);

        Assert.Equal(1, baseline.NewSemanticScanCount);
        Assert.Equal(1, otherSnapshot.NewSemanticScanCount);
        Assert.Equal(1, otherScope.NewSemanticScanCount);
        Assert.Equal(1, generated.NewSemanticScanCount);
        Assert.Equal(0, warm.NewSemanticScanCount);
        Assert.Equal(4, scans);
        Assert.Equal(4, cache.RetainedBucketCount);
    }

    [Fact]
    public async Task CollectAsync_DoesNotCacheIdentityErrorsAsSuccessfulCoverage()
    {
        using var fixture = CreateFixture();
        var document = Assert.Single(fixture.Solution.Projects.Single().Documents);
        var invalidText = fixture.Solution.WithDocumentText(document.Id, SourceText.From("\uD800"));
        var scans = 0;
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(
            DocumentCollected: _ => Interlocked.Increment(ref scans)));

        var first = await CollectAsync(cache, invalidText, "C:\\cache\\identity-error.slnx", ticket: 1);
        var second = await CollectAsync(cache, invalidText, "C:\\cache\\identity-error.slnx", ticket: 1);
        Assert.Empty(cache.SnapshotRetainedFactsForTesting("C:\\cache\\identity-error.slnx", 1,
            SymbolScopeType.All, includeGenerated: false));
        var corrected = invalidText.WithDocumentText(document.Id,
            SourceText.From("namespace App; public class Consumer { public Dependency Value = new(); } public class Dependency { }"));
        var recovered = await CollectAsync(cache, corrected, "C:\\cache\\identity-error.slnx", ticket: 2);

        Assert.Single(first.Errors);
        Assert.Single(second.Errors);
        Assert.Equal(0, first.CoveredDocumentCount);
        Assert.Equal(0, second.CoveredDocumentCount);
        Assert.Equal(1, recovered.CoveredDocumentCount);
        Assert.Equal(1, recovered.NewSemanticScanCount);
        Assert.Equal(1, scans);
    }

    [Fact]
    public async Task CollectAsync_RetainsSuccessfulDocumentsFromPartialCoverageAndRetriesOnlyTheFailure()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DependencyCachePartial.slnx",
            new ProjectSpec("App", [
                ("Broken.cs", "namespace App; public class Broken { }"),
                ("Good.cs", "namespace App; public class Good { }")], VirtualProjectDirectory: "src/App"));
        var scans = 0;
        var failedDocumentAttempts = 0;
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(
            DocumentCollected: document =>
            {
                Interlocked.Increment(ref scans);
                if (document.Name == "Broken.cs" && Interlocked.Increment(ref failedDocumentAttempts) == 1)
                    throw new IOException("Synthetic transient source scan failure.");
            }));

        var partial = await CollectAsync(cache, fixture.Solution, "C:\\cache\\partial.slnx", ticket: 1);
        var complete = await CollectAsync(cache, fixture.Solution, "C:\\cache\\partial.slnx", ticket: 1);
        var warm = await CollectAsync(cache, fixture.Solution, "C:\\cache\\partial.slnx", ticket: 1);

        Assert.Equal(2, partial.EligibleDocumentCount);
        Assert.Equal(1, partial.CoveredDocumentCount);
        Assert.Single(partial.Errors);
        Assert.Equal(1, complete.NewSemanticScanCount);
        Assert.Equal(2, complete.CoveredDocumentCount);
        Assert.Empty(complete.Errors);
        Assert.Equal(0, warm.NewSemanticScanCount);
        Assert.Equal(2, warm.CoveredDocumentCount);
        Assert.Equal(3, scans);
        Assert.Equal(2, failedDocumentAttempts);
    }

    [Fact]
    public async Task DisposeAsync_ClearsRetainedFactsAndIsIdempotent()
    {
        using var fixture = CreateFixture();
        var cache = new DependencyGraphCache();
        await CollectAsync(cache, fixture.Solution, "C:\\cache\\dispose.slnx", ticket: 1);
        Assert.Equal(1, cache.RetainedBucketCount);

        await cache.DisposeAsync();
        await cache.DisposeAsync();

        Assert.Equal(0, cache.RetainedBucketCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            CollectAsync(cache, fixture.Solution, "C:\\cache\\dispose.slnx", ticket: 1));
    }

    private static TestSolutionHandle CreateFixture() => TestWorkspaceBuilder.CreateSolution(
        @"C:\VirtualRepo\DependencyCache.slnx",
        new ProjectSpec("App", [
            ("Consumer.cs", "namespace App; public class Consumer { public Dependency Value = new(); } public class Dependency { }")],
            VirtualProjectDirectory: "src/App"));

    private static Task<DependencyGraphCollection> CollectAsync(
        DependencyGraphCache cache,
        Microsoft.CodeAnalysis.Solution solution,
        string target,
        long ticket,
        SymbolScopeType scope = SymbolScopeType.All,
        bool includeGenerated = false) =>
        cache.CollectAsync(solution, new DependencyGraphCollectionOptions(scope, includeGenerated), target, ticket);
}
