#nullable enable

using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Assemblies;

// @covers AssemblyAnalysisSessionRegistry
[Trait("Category", "Component")]
public sealed class AssemblyNavigationSessionLeaseTests
{
    [Fact]
    public async Task SessionRegistry_RejectsThirtyThirdTargetWhenThirtyTwoLeasesAreActive()
    {
        using var temp = TestTempDirectory.Create("assembly-session-capacity-");
        var template = AssemblyTestHelper.EmitMetadataInterface(temp, "CapacityTarget", "Capacity", "Target");
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
