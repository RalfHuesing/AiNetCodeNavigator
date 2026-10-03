#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Assemblies;

// @covers AssemblyAnalysisSessionRegistry
// @covers AssemblySymbolHandoffResolver
// @covers AssemblySymbolBodyScanner
[Trait("Category", "Component")]
public sealed class AssemblySymbolHandoffResolverTests
{
    [Fact]
    public async Task InspectHandles_RoundtripTypeAndMemberThroughResidentSession()
    {
        using var temp = TestTempDirectory.Create("assembly-handoff-roundtrip-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "HandoffProbe", """
            namespace Probe.Handoff;
            public sealed class Target { public string Read(int count) => count.ToString(); }
            """);

        var inspected = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(path, TypeName: "Target"));
        Assert.True(inspected.IsSuccess, inspected.Error?.ToString());
        var type = Assert.Single(inspected.Value!.Types);
        var member = Assert.Single(type.Members.Where(item => item.Name == "Read"));
        Assert.Equal(["get_symbol_body"], type.AllowedFollowUpTools);
        Assert.Equal(["get_symbol_body"], member.AllowedFollowUpTools);
        var typeHandle = type.HandoffId!;
        var memberHandle = member.HandoffId!;
        Assert.StartsWith("h:", typeHandle, StringComparison.Ordinal);
        Assert.StartsWith("h:", memberHandle, StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(path), type.OwnerTargetPath);
        Assert.Equal(Path.GetFullPath(path), member.OwnerTargetPath);

        var scopeResult = await AssemblyNavigationSessionScope.OpenAsync(path, default);
        Assert.True(scopeResult.IsSuccess, scopeResult.Error?.ToString());
        await using var scope = scopeResult.Value!;
        Assert.True(HandoffHandleRegistry.Default.RestoreInternalHandoffForInput(typeHandle).IsSuccess);
        Assert.True(SymbolHandoffIdentifier.TryParse(type.Id!, out var identifier));
        Assert.True(SymbolHandoffToken.TryCreateTarget(Path.GetFullPath(path), out var expectedTargetToken));
        Assert.True(SymbolHandoffToken.TryCreateContent(
            AnalysisSymbolIdentity.CreateAssemblyHandoffContentHash(scope.Context.Origin.ContentHash, scope.Context.ReferenceSnapshotHash, scope.Context.Generation),
            out var expectedContentToken));
        Assert.Equal(expectedTargetToken, identifier.TargetToken);
        Assert.Equal(expectedContentToken, identifier.ContentToken);
        var nextGenerationIdentity = AnalysisSymbolIdentity.ForAssembly(path, scope.Context.Origin.ContentHash,
            scope.Context.Generation + 1, scope.Context.ReferenceSnapshotHash);
        Assert.NotEqual(AnalysisSymbolIdentity.ForAssembly(path, scope.Context.Origin.ContentHash,
            scope.Context.Generation, scope.Context.ReferenceSnapshotHash).ContentHash, nextGenerationIdentity.ContentHash);

        var resolvedType = await AssemblySymbolBodyScanner.GetAsync(typeHandle);
        var resolvedMember = await AssemblySymbolBodyScanner.GetAsync(memberHandle);
        var structuredId = await AssemblySymbolBodyScanner.GetAsync(type.Id!);

        Assert.Null(resolvedType.Error);
        Assert.Equal("decompiled", resolvedType.Body!.ContentMode);
        Assert.Contains("Target", resolvedType.Body.Body, StringComparison.Ordinal);
        Assert.Equal(typeHandle, resolvedType.Body.HandoffId);
        Assert.Null(resolvedMember.Error);
        Assert.Equal("decompiled", resolvedMember.Body!.ContentMode);
        Assert.Contains("Read", resolvedMember.Body.Body, StringComparison.Ordinal);
        Assert.Equal(memberHandle, resolvedMember.Body.HandoffId);
        Assert.Null(structuredId.Error);
        Assert.Contains("Target", structuredId.Body!.Body, StringComparison.Ordinal);

        var inspectedAgain = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(path, TypeName: "Target"));
        Assert.True(inspectedAgain.IsSuccess);
        Assert.Equal(inspected.Value.Generation, inspectedAgain.Value!.Generation);
    }

    [Fact]
    public async Task HandoffResolver_RejectsUnknownForeignAndStaleHandles()
    {
        using var temp = TestTempDirectory.Create("assembly-handoff-errors-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "HandoffTarget", "public sealed class Target { }");
        var inspected = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(path, TypeName: "Target"));
        Assert.True(inspected.IsSuccess);
        var type = Assert.Single(inspected.Value!.Types);
        var handle = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(type.Id!);

        var unknown = await AssemblySymbolHandoffResolver.ResolveAsync("h:zzzzzzzz");
        Assert.False(unknown.IsSuccess);
        Assert.Equal(NavigationErrorCodes.HandoffUnknown, unknown.Error!.Value.Code);

        using var sourceFixture = SampleCodeFixtures.CreateStandardTestSolution();
        var sourceIdentity = await AnalysisSymbolIdentity.ForSourceAsync(sourceFixture.Solution);
        var sourceSymbol = (await sourceFixture.Solution.Projects.First().GetCompilationAsync())!
            .GetTypeByMetadataName("SampleNamespace.Greeter")!;
        var sourceInternal = sourceIdentity!.FormatHandoff(sourceSymbol, sourceFixture.Solution)!;
        var sourceHandle = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(sourceInternal);
        var foreign = await AssemblySymbolHandoffResolver.ResolveAsync(sourceHandle);
        Assert.False(foreign.IsSuccess);
        Assert.Equal(NavigationErrorCodes.TargetMismatch, foreign.Error!.Value.Code);

        var replacement = AssemblyTestHelper.EmitAssembly(temp, "Replacement", "public sealed class Replacement { }");
        File.Copy(replacement, path, overwrite: true);
        var stale = await AssemblySymbolHandoffResolver.ResolveAsync(handle);
        Assert.False(stale.IsSuccess);
        Assert.Equal(NavigationErrorCodes.StaleSnapshot, stale.Error!.Value.Code);
    }

    [Fact]
    public async Task InspectAndOldHandoffs_FailAfterResidentTargetBecomesInvalid()
    {
        using var temp = TestTempDirectory.Create("assembly-handoff-invalid-replacement-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "InvalidReplacement", "public sealed class OriginalApi { }");
        var first = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(path));
        Assert.True(first.IsSuccess, first.Error?.ToString());
        var oldType = Assert.Single(first.Value!.Types);
        var oldHandle = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(oldType.Id!);

        File.WriteAllBytes(path, [0, 1, 2, 3, 4, 5]);
        var reinspection = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(path));
        var oldHandoff = await AssemblySymbolBodyScanner.GetAsync(oldHandle);

        Assert.False(reinspection.IsSuccess);
        Assert.Equal(NavigationErrorCodes.InvalidAssembly, reinspection.Error!.Value.Code);
        Assert.Null(reinspection.Value);
        Assert.NotNull(oldHandoff.Error);
        Assert.Equal(NavigationErrorCodes.InvalidAssembly, oldHandoff.Error!.Value.Code);
        Assert.NotEqual("OriginalApi", oldHandoff.Body?.Body);
    }

    [Fact]
    public async Task ResidentSession_RecoversWhenOriginalTargetBytesAreRestored()
    {
        using var temp = TestTempDirectory.Create("assembly-handoff-invalid-restore-");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "RestoreDependency", "namespace Probe.Restore; public sealed class Dependency { }");
        var path = AssemblyTestHelper.EmitAssembly(temp, "RestoreTarget", "public sealed class OriginalApi { public Probe.Restore.Dependency? Value; }", dependency);
        var originalBytes = await File.ReadAllBytesAsync(path);
        var first = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(path));
        Assert.True(first.IsSuccess, first.Error?.ToString());
        var originalGeneration = first.Value!.Generation;
        var originalStatus = first.Value.SessionStatus;
        var originalDiagnostics = first.Value.Diagnostics;
        var originalType = Assert.Single(first.Value.Types);
        var originalHandle = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(originalType.Id!);

        await File.WriteAllBytesAsync(path, [0, 1, 2, 3, 4, 5]);
        var invalidInspection = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(path));
        var invalidHandoff = await AssemblySymbolBodyScanner.GetAsync(originalHandle);
        Assert.False(invalidInspection.IsSuccess);
        Assert.Equal(NavigationErrorCodes.InvalidAssembly, invalidInspection.Error!.Value.Code);
        Assert.Null(invalidInspection.Value);
        Assert.Equal(NavigationErrorCodes.InvalidAssembly, invalidHandoff.Error!.Value.Code);
        Assert.Null(invalidHandoff.Body);

        await File.WriteAllBytesAsync(path, originalBytes);
        var restoredInspection = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(path));
        var restoredHandoff = await AssemblySymbolBodyScanner.GetAsync(originalHandle);

        Assert.True(restoredInspection.IsSuccess, restoredInspection.Error?.ToString());
        Assert.Equal(originalGeneration, restoredInspection.Value!.Generation);
        Assert.Equal(originalStatus, restoredInspection.Value.SessionStatus);
        Assert.Equal(originalDiagnostics, restoredInspection.Value.Diagnostics);
        Assert.Null(restoredHandoff.Error);
        Assert.Equal(originalHandle, restoredHandoff.Body!.HandoffId);
        Assert.Contains("OriginalApi", restoredHandoff.Body.Body, StringComparison.Ordinal);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ResidentSession_RefreshesRemovedAndReplacedReferencesWithoutTargetChanges()
    {
        using var temp = TestTempDirectory.Create("assembly-reference-refresh-");
        var leaf = AssemblyTestHelper.EmitAssembly(temp, "RefreshLeaf", "namespace Probe.Leaf; public sealed class Leaf { public int Version => 1; }");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "RefreshDependency", "namespace Probe.Dependency; public sealed class Dependency { public Probe.Leaf.Leaf? Value; }", leaf);
        var consumer = AssemblyTestHelper.EmitAssembly(temp, "RefreshConsumer", "public sealed class Consumer { public Probe.Dependency.Dependency? Value; }", dependency);

        var initial = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(consumer));
        Assert.True(initial.IsSuccess, initial.Error?.ToString());
        var initialGeneration = initial.Value!.Generation;
        var consumerType = Assert.Single(initial.Value.Types);
        var handoff = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(consumerType.Id!);
        var originalBytes = await File.ReadAllBytesAsync(consumer);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        File.Delete(leaf);
        var missing = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(consumer));
        Assert.True(missing.IsSuccess, missing.Error?.ToString());
        Assert.True(missing.Value!.Generation > initialGeneration);
        Assert.Contains(missing.Value.Diagnostics, diagnostic => diagnostic.Contains("RefreshLeaf", StringComparison.Ordinal));
        var bodyWithMissingReference = await AssemblySymbolBodyScanner.GetAsync(handoff);
        Assert.NotNull(bodyWithMissingReference.Error);
        Assert.Equal(NavigationErrorCodes.StaleSnapshot, bodyWithMissingReference.Error!.Value.Code);
        var missingReferenceType = Assert.Single(missing.Value.Types.Where(type => type.Name == "Consumer"));
        var missingReferenceHandle = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(missingReferenceType.Id!);
        var currentBodyWithMissingReference = await AssemblySymbolBodyScanner.GetAsync(missingReferenceHandle);
        Assert.Null(currentBodyWithMissingReference.Error);
        Assert.Equal(missingReferenceHandle, currentBodyWithMissingReference.Body!.HandoffId);
        Assert.Contains("Consumer", currentBodyWithMissingReference.Body.Body, StringComparison.Ordinal);

        using var replacementTemp = TestTempDirectory.Create("assembly-reference-replacement-");
        var replacement = AssemblyTestHelper.EmitAssembly(replacementTemp, "RefreshLeaf", "namespace Probe.Leaf; public sealed class Leaf { public int Version => 2; public int Added => 3; }");
        File.Copy(replacement, leaf, overwrite: true);
        var restored = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(consumer));

        Assert.True(restored.IsSuccess, restored.Error?.ToString());
        Assert.True(restored.Value!.Generation > missing.Value.Generation);
        Assert.DoesNotContain(restored.Value.Diagnostics, diagnostic => diagnostic.Contains("Dependency not resolvable", StringComparison.Ordinal));
        var staleMissingReferenceHandle = await AssemblySymbolBodyScanner.GetAsync(missingReferenceHandle);
        Assert.NotNull(staleMissingReferenceHandle.Error);
        Assert.Equal(NavigationErrorCodes.StaleSnapshot, staleMissingReferenceHandle.Error!.Value.Code);
        var restoredType = Assert.Single(restored.Value.Types.Where(type => type.Name == "Consumer"));
        var restoredHandle = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(restoredType.Id!);
        var bodyWithReplacementReference = await AssemblySymbolBodyScanner.GetAsync(restoredHandle);
        Assert.Null(bodyWithReplacementReference.Error);
        Assert.Equal(restoredHandle, bodyWithReplacementReference.Body!.HandoffId);
        Assert.Contains("Consumer", bodyWithReplacementReference.Body.Body, StringComparison.Ordinal);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(consumer));
    }

    [Fact]
    public async Task ReferenceReplacement_InvalidatesCachedDecompilerOutputAcrossInspectSearchAndBody()
    {
        using var temp = TestTempDirectory.Create("assembly-reference-cache-refresh-");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "CacheSensitiveDependency", "namespace Probe.Reference; public enum State { OriginalValue = 1 }\n[System.AttributeUsage(System.AttributeTargets.All)] public sealed class MarkerAttribute : System.Attribute { public MarkerAttribute(State value) { } }");
        var consumer = AssemblyTestHelper.EmitAssembly(temp, "CacheSensitiveConsumer", "[Probe.Reference.Marker(Probe.Reference.State.OriginalValue)] public static class Consumer { public static Probe.Reference.State Get() => Probe.Reference.State.OriginalValue; }", dependency);
        var originalConsumerBytes = await File.ReadAllBytesAsync(consumer);
        var initial = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(consumer, TypeName: "Consumer"));
        Assert.True(initial.IsSuccess, initial.Error?.ToString());
        var consumerType = Assert.Single(initial.Value!.Types);
        var getHandle = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(Assert.Single(consumerType.Members.Where(member => member.Name == "Get")).Id!);
        var initialSearch = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(consumer, Query: "OriginalValue"));
        Assert.True(initialSearch.IsSuccess, initialSearch.Error?.ToString());
        Assert.NotEmpty(initialSearch.Value!.Results);

        using var replacementTemp = TestTempDirectory.Create("assembly-reference-cache-replacement-");
        var replacement = AssemblyTestHelper.EmitAssembly(replacementTemp, "CacheSensitiveDependency", "namespace Probe.Reference; public enum State { ReplacementValue = 1 }\n[System.AttributeUsage(System.AttributeTargets.All)] public sealed class MarkerAttribute : System.Attribute { public MarkerAttribute(State value) { } }");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        File.Copy(replacement, dependency, overwrite: true);

        var refreshed = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(consumer, TypeName: "Consumer"));
        var refreshedSearch = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(consumer, Query: "ReplacementValue"));
        var staleSearch = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(consumer, Query: "OriginalValue"));
        var refreshedType = Assert.Single(refreshed.Value!.Types);
        var refreshedHandle = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(
            Assert.Single(refreshedType.Members.Where(member => member.Name == "Get")).Id!);
        var refreshedBody = await AssemblySymbolBodyScanner.GetAsync(refreshedHandle);
        var staleBody = await AssemblySymbolBodyScanner.GetAsync(getHandle);

        Assert.True(refreshed.IsSuccess, refreshed.Error?.ToString());
        Assert.True(refreshed.Value!.Generation > initial.Value.Generation);
        Assert.DoesNotContain(refreshed.Value.Diagnostics, diagnostic => diagnostic.Contains("CS0117", StringComparison.Ordinal));
        Assert.True(refreshedSearch.IsSuccess, refreshedSearch.Error?.ToString());
        Assert.NotEmpty(refreshedSearch.Value!.Results);
        Assert.True(refreshedSearch.Value.TotalCount > 0);
        Assert.True(staleSearch.IsSuccess, staleSearch.Error?.ToString());
        Assert.Empty(staleSearch.Value!.Results);
        Assert.Equal(0, staleSearch.Value.TotalCount);
        Assert.Null(refreshedBody.Error);
        Assert.Contains("ReplacementValue", refreshedBody.Body!.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("OriginalValue", refreshedBody.Body.Body, StringComparison.Ordinal);
        Assert.Equal(NavigationErrorCodes.StaleSnapshot, staleBody.Error!.Value.Code);
        Assert.Null(staleBody.Body);
        Assert.Equal(originalConsumerBytes, await File.ReadAllBytesAsync(consumer));
    }

    [Fact]
    public async Task HandoffResolver_AllowsSymbolsWhenReferencesAreMissing()
    {
        using var temp = TestTempDirectory.Create("assembly-handoff-missing-reference-");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "HandoffDependency", "namespace Probe.Dependency; public sealed class Dependency { }");
        var path = AssemblyTestHelper.EmitAssembly(temp, "HandoffConsumer", "public sealed class Consumer { public Probe.Dependency.Dependency? Value; }", dependency);
        File.Delete(dependency);

        var inspected = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(path, TypeName: "Consumer"));
        Assert.True(inspected.IsSuccess, inspected.Error?.ToString());
        Assert.NotEmpty(inspected.Value!.Diagnostics);
        var type = Assert.Single(inspected.Value.Types);
        var handle = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(type.Id!);
        var resolved = await AssemblySymbolHandoffResolver.ResolveAsync(handle);

        Assert.True(resolved.IsSuccess, resolved.Error?.ToString());
        Assert.Equal("Consumer", resolved.Value!.Symbol.Name);
        await resolved.Value.DisposeAsync();
    }

    [Fact]
    public async Task SessionRegistry_ExpiresIdleTargetBindings()
    {
        using var temp = TestTempDirectory.Create("assembly-session-expiry-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "ExpiringTarget", "public sealed class Target { }");
        await using var registry = new AssemblyAnalysisSessionRegistry();
        var acquired = await registry.AcquireAsync(path, default);
        Assert.True(acquired.IsSuccess, acquired.Error?.ToString());
        var targetTokenValid = SymbolHandoffToken.TryCreateTarget(path, out var targetToken);
        Assert.True(targetTokenValid);
        await acquired.Value!.DisposeAsync();

        await registry.ExpireIdleSessionsAsync(DateTime.UtcNow.AddMinutes(11));
        var expired = await registry.AcquireByTargetTokenAsync(targetToken, default);

        Assert.False(expired.IsSuccess);
        Assert.Equal(NavigationErrorCodes.TargetMismatch, expired.Error!.Value.Code);
    }

    [Fact]
    public async Task SessionRegistry_RejectsThirtyThirdTargetWhenThirtyTwoLeasesAreActive()
    {
        using var temp = TestTempDirectory.Create("assembly-session-capacity-");
        var template = AssemblyTestHelper.EmitAssembly(temp, "CapacityTarget", "public sealed class Target { }");
        await using var registry = new AssemblyAnalysisSessionRegistry();
        var leases = new System.Collections.Generic.List<AssemblyAnalysisSessionRegistry.AssemblySessionAccess>();
        AssemblyAnalysisSessionRegistry.AssemblySessionAccess? overflowLease = null;

        try
        {
            for (var index = 0; index < AssemblyAnalysisSessionRegistry.MaxResidentSessions; index++)
            {
                var path = temp.GetPath($"target-{index:D2}.dll");
                File.Copy(template, path);
                var acquired = await registry.AcquireAsync(path, default);
                Assert.True(acquired.IsSuccess, acquired.Error?.ToString());
                leases.Add(acquired.Value!);
            }

            var overflowPath = temp.GetPath("target-overflow.dll");
            File.Copy(template, overflowPath);
            var overflow = await registry.AcquireAsync(overflowPath, default);
            overflowLease = overflow.Value;

            Assert.False(overflow.IsSuccess);
            Assert.Equal("ASSEMBLY_SESSION_LIMIT", overflow.Error!.Value.Code);
            Assert.Contains(AssemblyAnalysisSessionRegistry.MaxResidentSessions.ToString(), overflow.Error.Value.Message, StringComparison.Ordinal);

            await leases[0].DisposeAsync();
            leases.RemoveAt(0);
            var retried = await registry.AcquireAsync(overflowPath, default);
            Assert.True(retried.IsSuccess, retried.Error?.ToString());
            overflowLease = retried.Value;
        }
        finally
        {
            if (overflowLease is not null) await overflowLease.DisposeAsync();
            foreach (var lease in leases) await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task SessionRegistry_DoesNotExpireAnActiveOwnerLeaseUnderSessionPressure()
    {
        using var temp = TestTempDirectory.Create("assembly-session-active-owner-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "ActiveOwner", "public sealed class Owner { public int Value => 1; }");
        await using var registry = new AssemblyAnalysisSessionRegistry();

        var first = await registry.AcquireAsync(path, default);
        Assert.True(first.IsSuccess, first.Error?.ToString());
        var pinnedGeneration = first.Value!.Generation.Number;
        await registry.ExpireIdleSessionsAsync(DateTime.UtcNow.AddMinutes(11));

        Assert.Equal(1, registry.GetActiveAccessCount(path));
        var second = await registry.AcquireAsync(path, default);
        Assert.True(second.IsSuccess, second.Error?.ToString());
        Assert.Equal(pinnedGeneration, second.Value!.Generation.Number);
        Assert.Equal(2, registry.GetActiveAccessCount(path));

        await second.Value.DisposeAsync();
        await first.Value.DisposeAsync();
        Assert.Equal(0, registry.GetActiveAccessCount(path));
    }
}
