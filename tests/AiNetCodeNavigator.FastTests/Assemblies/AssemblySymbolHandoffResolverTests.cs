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
        var typeHandle = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(type.Id!);
        var memberHandle = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(member.Id!);
        Assert.Contains($"handoffId: `{typeHandle}`", inspected.Value.FormattedText, StringComparison.Ordinal);
        Assert.Contains($"handoffId: `{memberHandle}`", inspected.Value.FormattedText, StringComparison.Ordinal);

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
}
