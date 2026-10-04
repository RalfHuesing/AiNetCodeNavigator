#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit.Builders;

namespace AiNetCodeNavigator.FastTests.Dependencies;

[Trait("Category", "Unit")]
public sealed class DependencyGraphCacheRetentionTests
{
    [Fact]
    public async Task CollectAsync_ExpiresEveryIdleBucketUsingMonotonicTime()
    {
        using var fixture = CreateSmallFixture();
        var clock = new ManualTimeProvider();
        await using var cache = new DependencyGraphCache(timeProvider: clock,
            maxBuckets: 4, maxRetainedBytes: 1024 * 1024, idleTtl: TimeSpan.FromSeconds(10));

        var first = await CollectAsync(cache, fixture.Solution, "C:\\cache\\A.slnx", ticket: 1);
        var second = await CollectAsync(cache, fixture.Solution, "C:\\cache\\B.slnx", ticket: 1);
        Assert.Equal(2, first.NewSemanticScanCount);
        Assert.Equal(2, second.NewSemanticScanCount);
        Assert.Equal(2, cache.RetainedBucketCount);

        clock.Advance(TimeSpan.FromSeconds(10));
        var afterExpiry = await CollectAsync(cache, fixture.Solution, "C:\\cache\\A.slnx", ticket: 1);

        Assert.Equal(2, afterExpiry.NewSemanticScanCount);
        Assert.Equal(1, cache.RetainedBucketCount);
        Assert.Empty(cache.SnapshotRetainedFactsForTesting("C:\\cache\\B.slnx", 1, SymbolScopeType.All, includeGenerated: false));
    }

    [Fact]
    public async Task CollectAsync_UsesDeterministicLruTieBreaksForTicketScopeAndGeneratedOption()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\CacheLru.slnx",
            new ProjectSpec("App", [
                ("Production.cs", "namespace CacheProbe; public sealed class Production { }"),
                ("Consumer.Tests.cs", "namespace CacheProbe; public sealed class TestConsumer { }")],
                VirtualProjectDirectory: "src/App"));
        var clock = new ManualTimeProvider();
        await using var cache = new DependencyGraphCache(timeProvider: clock,
            maxBuckets: 1, maxRetainedBytes: 1024 * 1024, idleTtl: TimeSpan.FromMinutes(1));

        await CollectAsync(cache, fixture.Solution, "C:\\cache\\same.slnx", ticket: 7,
            scope: SymbolScopeType.Production, includeGenerated: false);
        await CollectAsync(cache, fixture.Solution, "C:\\cache\\same.slnx", ticket: 7,
            scope: SymbolScopeType.Tests, includeGenerated: false);
        Assert.Empty(cache.SnapshotRetainedFactsForTesting("C:\\cache\\same.slnx", 7,
            SymbolScopeType.Production, includeGenerated: false));
        Assert.NotEmpty(cache.SnapshotRetainedFactsForTesting("C:\\cache\\same.slnx", 7,
            SymbolScopeType.Tests, includeGenerated: false));

        await CollectAsync(cache, fixture.Solution, "C:\\cache\\same.slnx", ticket: 7,
            scope: SymbolScopeType.Tests, includeGenerated: true);

        Assert.Empty(cache.SnapshotRetainedFactsForTesting("C:\\cache\\same.slnx", 7,
            SymbolScopeType.Tests, includeGenerated: false));
        Assert.NotEmpty(cache.SnapshotRetainedFactsForTesting("C:\\cache\\same.slnx", 7,
            SymbolScopeType.Tests, includeGenerated: true));

        await CollectAsync(cache, fixture.Solution, "C:\\cache\\same.slnx", ticket: 8,
            scope: SymbolScopeType.All, includeGenerated: false);
        Assert.Empty(cache.SnapshotRetainedFactsForTesting("C:\\cache\\same.slnx", 7,
            SymbolScopeType.Tests, includeGenerated: false));
        Assert.Empty(cache.SnapshotRetainedFactsForTesting("C:\\cache\\same.slnx", 7,
            SymbolScopeType.Tests, includeGenerated: true));
        Assert.NotEmpty(cache.SnapshotRetainedFactsForTesting("C:\\cache\\same.slnx", 8,
            SymbolScopeType.All, includeGenerated: false));

        await using var targetTieCache = new DependencyGraphCache(timeProvider: clock,
            maxBuckets: 1, maxRetainedBytes: 1024 * 1024, idleTtl: TimeSpan.FromMinutes(1));
        await CollectAsync(targetTieCache, fixture.Solution, "C:\\cache\\A.slnx", ticket: 9);
        await CollectAsync(targetTieCache, fixture.Solution, "C:\\cache\\B.slnx", ticket: 9);
        Assert.Empty(targetTieCache.SnapshotRetainedFactsForTesting("C:\\cache\\A.slnx", 9,
            SymbolScopeType.All, includeGenerated: false));
        Assert.NotEmpty(targetTieCache.SnapshotRetainedFactsForTesting("C:\\cache\\B.slnx", 9,
            SymbolScopeType.All, includeGenerated: false));
    }

    [Fact]
    public async Task CollectAsync_OversizedStandaloneBucketDoesNotEvictAnUnrelatedBucket()
    {
        using var smallFixture = CreateSmallFixture();
        using var largeFixture = CreateLargeFixture();
        long exactSmallBucketBudget;
        await using (var sizingCache = new DependencyGraphCache(maxBuckets: 4, maxRetainedBytes: long.MaxValue))
        {
            await CollectAsync(sizingCache, smallFixture.Solution, "C:\\cache\\small.slnx", ticket: 1);
            exactSmallBucketBudget = sizingCache.RetainedPayloadBytes;
        }

        await using var cache = new DependencyGraphCache(maxBuckets: 4,
            maxRetainedBytes: exactSmallBucketBudget);
        await CollectAsync(cache, smallFixture.Solution, "C:\\cache\\small.slnx", ticket: 1);
        var oversized = await CollectAsync(cache, largeFixture.Solution, "C:\\cache\\large.slnx", ticket: 1);

        Assert.Equal(1, cache.RetainedBucketCount);
        Assert.NotEmpty(cache.SnapshotRetainedFactsForTesting("C:\\cache\\small.slnx", 1,
            SymbolScopeType.All, includeGenerated: false));
        Assert.Empty(cache.SnapshotRetainedFactsForTesting("C:\\cache\\large.slnx", 1,
            SymbolScopeType.All, includeGenerated: false));
        Assert.Equal(1, oversized.CoveredDocumentCount);
        Assert.NotEmpty(oversized.TypeDeclarations);
    }

    [Fact]
    public async Task CollectAsync_AccountsUniqueStrictUtf8StringsAndInternsWithinBucket()
    {
        using var fixture = CreateSmallFixture();
        const string target = "C:\\VirtualRepo\\CacheAccounting.slnx";
        await using var cache = new DependencyGraphCache(maxBuckets: 4, maxRetainedBytes: 1024 * 1024);

        var collection = await CollectAsync(cache, fixture.Solution, target, ticket: 31);
        var facts = cache.SnapshotRetainedFactsForTesting(target, 31, SymbolScopeType.All, includeGenerated: false);

        Assert.Equal(2, facts.Length);
        Assert.Equal(collection.TypeDependencies.Length, facts.Sum(fact => fact.TypeDependencies.Length));
        Assert.True(AnalysisPathIdentity.TryNormalize(target, out var canonicalTarget));
        var expectedBytes = IndependentlyEstimatePayload(canonicalTarget, facts);
        Assert.Equal(expectedBytes, cache.RetainedPayloadBytes);
        Assert.Same(facts[0].Identity.OwnerProjectPath, facts[1].Identity.OwnerProjectPath);
        Assert.Same(facts[0].Identity.OwnerContextFingerprint, facts[1].Identity.OwnerContextFingerprint);
        var crossDocumentEdge = facts.SelectMany(fact => fact.TypeDependencies).Single();
        var targetDeclaration = facts.Single(fact => fact.Identity.Name == "Target.cs").TypeDeclarations
            .Single(declaration => declaration.Name == "Target");
        Assert.Equal(targetDeclaration.File, crossDocumentEdge.ToFile);
        Assert.Same(targetDeclaration.File, crossDocumentEdge.ToFile);
    }

    [Fact]
    public async Task RetireTargetAsync_RemovesOnlyRetiredTicketsAndAcquiredProjectionStaysUsable()
    {
        using var fixture = CreateSmallFixture();
        const string target = "C:\\cache\\retiring.slnx";
        await using var cache = new DependencyGraphCache(maxBuckets: 4, maxRetainedBytes: 1024 * 1024);

        var acquired = await CollectAsync(cache, fixture.Solution, target, ticket: 41);
        await CollectAsync(cache, fixture.Solution, target, ticket: 42);
        await cache.RetireTargetAsync(target, maximumRetiredSnapshotTicket: 41);

        Assert.Empty(cache.SnapshotRetainedFactsForTesting(target, 41, SymbolScopeType.All, includeGenerated: false));
        Assert.NotEmpty(cache.SnapshotRetainedFactsForTesting(target, 42, SymbolScopeType.All, includeGenerated: false));
        var projected = DependencyGraphScanner.Project(acquired,
            new DependencyGraphProjectionOptions(
                TargetTypeId: acquired.TypeDeclarations.Single(declaration => declaration.Name == "Consumer").TypeId,
                Direction: DependencyGraphDirection.Outgoing));
        Assert.Contains(projected.TypeDependencies!, edge => edge.FromTypeName == "Consumer" && edge.ToTypeName == "Target");
    }

    [Fact]
    public async Task CollectAsync_EvictsDestinationWhenUnionExceedsBudgetButEachFactFits()
    {
        using var fixture = CreateSmallFixture();
        const string target = "C:\\cache\\union.slnx";
        Assert.True(AnalysisPathIdentity.TryNormalize(target, out var canonicalTarget));
        long budget;
        await using (var sizingCache = new DependencyGraphCache(maxRetainedBytes: long.MaxValue))
        {
            await CollectAsync(sizingCache, fixture.Solution, target, ticket: 1);
            var facts = sizingCache.SnapshotRetainedFactsForTesting(target, 1, SymbolScopeType.All, false);
            Assert.Equal(2, facts.Length);
            budget = facts.Max(fact => IndependentlyEstimatePayload(canonicalTarget, [fact]));
            Assert.True(IndependentlyEstimatePayload(canonicalTarget, facts) > budget);
        }

        await using var cache = new DependencyGraphCache(maxRetainedBytes: budget);
        var acquired = await CollectAsync(cache, fixture.Solution, target, ticket: 1);

        Assert.Equal(2, acquired.CoveredDocumentCount);
        Assert.Contains(acquired.TypeDependencies, edge => edge.FromTypeName == "Consumer" && edge.ToTypeName == "Target");
        var retained = Assert.Single(cache.SnapshotRetainedFactsForTesting(target, 1, SymbolScopeType.All, false));
        Assert.Equal("Target.cs", retained.Identity.Name);
        Assert.Equal(IndependentlyEstimatePayload(canonicalTarget, [retained]), cache.RetainedPayloadBytes);
        Assert.True(cache.RetainedPayloadBytes <= budget);
    }

    private static async Task<DependencyGraphCollection> CollectAsync(
        DependencyGraphCache cache,
        Microsoft.CodeAnalysis.Solution solution,
        string target,
        long ticket,
        SymbolScopeType scope = SymbolScopeType.All,
        bool includeGenerated = false) =>
        await cache.CollectAsync(solution, new DependencyGraphCollectionOptions(scope, includeGenerated),
            target, ticket);

    private static long IndependentlyEstimatePayload(string target, IReadOnlyList<DependencyDocumentFact> facts)
    {
        var strings = new HashSet<string>(StringComparer.Ordinal) { target };
        foreach (var fact in facts)
        {
            AddIdentity(fact.Identity, strings);
            foreach (var edge in fact.TypeDependencies)
            {
                strings.Add(edge.FromTypeId);
                strings.Add(edge.ToTypeId);
                strings.Add(edge.FromType);
                strings.Add(edge.ToType);
                strings.Add(edge.FromTypeName);
                strings.Add(edge.ToTypeName);
                strings.Add(edge.FromNamespace);
                strings.Add(edge.ToNamespace);
                strings.Add(edge.FromProject);
                strings.Add(edge.ToProject);
                strings.Add(edge.FromFile);
                strings.Add(edge.ToFile);
            }
            foreach (var declaration in fact.TypeDeclarations)
            {
                strings.Add(declaration.TypeId);
                strings.Add(declaration.DisplayName);
                strings.Add(declaration.Name);
                strings.Add(declaration.Namespace);
                strings.Add(declaration.Project);
                strings.Add(declaration.File);
                strings.Add(declaration.OwnerProjectPath);
                strings.Add(declaration.OwnerContextFingerprint);
                strings.Add(declaration.OriginalTypeId);
                strings.Add(declaration.DocumentationCommentId);
                foreach (var document in declaration.DeclarationDocuments) AddIdentity(document, strings);
            }
        }

        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var utf8PayloadBytes = strings.Sum(value => (long)strictUtf8.GetByteCount(value));
        var successfulCoverageAndEdges = facts.Count + facts.Sum(fact => fact.TypeDependencies.Length);
        return utf8PayloadBytes + 17 + successfulCoverageAndEdges * 64L;
    }

    private static void AddIdentity(DependencyDocumentIdentity identity, ISet<string> strings)
    {
        strings.Add(identity.OwnerProjectPath);
        strings.Add(identity.OwnerContextFingerprint);
        strings.Add(identity.DocumentPath);
        strings.Add(identity.Name);
        foreach (var folder in identity.Folders) strings.Add(folder);
        strings.Add(identity.SourceCodeKind);
        strings.Add(identity.TextHash);
    }

    private static TestSolutionHandle CreateSmallFixture() => TestWorkspaceBuilder.CreateSolution(
        @"C:\VirtualRepo\CacheAccounting.slnx",
        new ProjectSpec("App", [
            ("Consumer.cs", "namespace CacheProbe; public sealed class Consumer { public Target Value { get; set; } = new(); }"),
            ("Target.cs", "namespace CacheProbe; public sealed class Target { }")],
            VirtualProjectDirectory: "src/CaféApp"));

    private static TestSolutionHandle CreateLargeFixture()
    {
        var longIdentifier = "Large" + new string('X', 4096);
        return TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\OversizedCache.slnx",
            new ProjectSpec("App", [("Large.cs", $"namespace CacheProbe; public sealed class {longIdentifier} {{ }}")],
                VirtualProjectDirectory: "src/App"));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamp;

        internal void Advance(TimeSpan duration) => timestamp += duration.Ticks;
    }
}
