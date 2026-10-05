#nullable enable

using System;
using System.IO;
using System.Linq;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.TestKit;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class AssemblyReferenceSnapshotValidatorTests
{
    [Theory]
    [InlineData("deduplicated")]
    [InlineData("cycle")]
    public void ImmutableEquivalentEdgesMatchDespiteRootTraversalLabels(string traversalState)
    {
        using var fixture = TestTempDirectory.Create("equivalent-reference-edges-");
        var owner = fixture.GetPath("Owner.dll");
        var dependency = fixture.GetPath("Dependency.dll");
        var edge = new AssemblyReferenceDto("Dependency", "1.0.0.0", "neutral", true,
            dependency, ContentHash: "same-immutable-pe-sha256", SourceAssemblyPath: owner);
        var comparison = AssemblyReferenceSnapshotValidator.ReferenceSubgraphMatches(
            [edge with { ResolutionState = traversalState }], owner, [edge]);
        Assert.True(comparison.Matches);
        Assert.False(comparison.RootHasBoundary);
        Assert.False(AssemblyReferenceSnapshotValidator.ReferenceSubgraphMatches(
            [edge with { ResolutionState = traversalState }], owner, [edge with { ContentHash = "changed-pe" }]).Matches);
        Assert.False(AssemblyReferenceSnapshotValidator.ReferenceSubgraphMatches(
            [edge with { ResolutionState = traversalState }], owner, [edge with { ResolvedPath = fixture.GetPath("Other.dll") }]).Matches);
    }

    [Fact]
    public void UnrepresentedAndDepthLimitedEdgesAreNotVerified()
    {
        using var fixture = TestTempDirectory.Create("reference-boundary-");
        var owner = fixture.GetPath("Owner.dll");
        var edge = new AssemblyReferenceDto("Dependency", "1.0.0.0", "neutral", true,
            fixture.GetPath("Dependency.dll"), ContentHash: "captured-pe", SourceAssemblyPath: owner);
        Assert.False(AssemblyReferenceSnapshotValidator.ReferenceSubgraphMatches([], owner, [edge]).Matches);
        var bounded = AssemblyReferenceSnapshotValidator.ReferenceSubgraphMatches(
            [edge with { Resolved = false, ResolutionState = "depth_limit" }], owner, [edge]);
        Assert.False(bounded.Matches);
        Assert.True(bounded.RootHasBoundary);
    }

    [Fact]
    public void ResolverCapturesActualOutgoingEdgesOfTrustedPlatformOwners()
    {
        using var fixture = TestTempDirectory.Create("trusted-owner-subgraph-");
        var uriOwner = typeof(Uri).Assembly.Location;
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        Assert.Contains(uriOwner, trusted, StringComparer.OrdinalIgnoreCase);
        var root = AssemblyTestHelper.EmitAssembly(fixture, "TrustedOwnerProbe",
            "public sealed class Probe { public System.Uri? Value; }", uriOwner);
        var references = new AssemblyReferenceResolver().Resolve(root).References;
        Assert.Contains(references, edge => string.Equals(edge.ResolvedPath, uriOwner, StringComparison.OrdinalIgnoreCase)
            && edge.Resolved && !string.IsNullOrWhiteSpace(edge.ContentHash));
        Assert.Contains(references, edge => string.Equals(edge.SourceAssemblyPath, uriOwner, StringComparison.OrdinalIgnoreCase)
            && edge.Resolved && !string.IsNullOrWhiteSpace(edge.ContentHash));
    }
}
