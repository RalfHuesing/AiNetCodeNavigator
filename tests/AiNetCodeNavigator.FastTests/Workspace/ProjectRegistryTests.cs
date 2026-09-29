#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Workspace;

[Trait("Category", "Unit")]
public sealed class ProjectRegistryTests
{
    [Fact]
    public async Task Lease_NormalizesSolutionSpellings_ToSingleResidentEntry()
    {
        using var tempDir = TestTempDirectory.Create("project-registry-keys-");
        var solutionPath = CreateSolutionPath(tempDir, "proj");
        var factory = new TrackingSolutionFactory();
        await using var registry = CreateRegistry(factory, new FakeClock());

        var first = registry.Lease(solutionPath);
        using var firstLease = first.Lease;
        var alternateSpelling = registry.Lease(Path.Combine(
            Path.GetDirectoryName(solutionPath)!, ".", Path.GetFileName(solutionPath)));
        using var alternateLease = alternateSpelling.Lease;
        var uppercase = registry.Lease(solutionPath.ToUpperInvariant());
        using var uppercaseLease = uppercase.Lease;
        var forwardSlashes = registry.Lease(solutionPath.Replace('\\', '/'));
        using var forwardLease = forwardSlashes.Lease;

        Assert.True(first.Succeeded);
        Assert.Equal(1, factory.InstancesCreated);
        Assert.Same(firstLease!.ResidentSolution, alternateLease!.ResidentSolution);
        Assert.Same(firstLease.ResidentSolution, uppercaseLease!.ResidentSolution);
        Assert.Same(firstLease.ResidentSolution, forwardLease!.ResidentSolution);
    }

    [Fact]
    public async Task Lease_HitTouchesLastUsedUtc_AndSurvivesTotalAgeBeyondTtl()
    {
        using var tempDir = TestTempDirectory.Create("project-registry-touch-");
        var solutionPath = CreateSolutionPath(tempDir, "proj");
        var factory = new TrackingSolutionFactory();
        var clock = new FakeClock();
        await using var registry = CreateRegistry(factory, clock, idleTtlMinutes: 15);

        var initial = registry.Lease(solutionPath);
        var solutionInitial = initial.Lease!.ResidentSolution;
        initial.Lease.Dispose();
        clock.AdvanceMinutes(14);
        await registry.RunEvictionTickAsync();
        Assert.Equal(0, factory.LoadsCancelled);
        Assert.Equal(1, factory.InstancesCreated);

        var touched = registry.Lease(solutionPath);
        var solutionTouched = touched.Lease!.ResidentSolution;
        touched.Lease.Dispose();
        clock.AdvanceMinutes(16);
        await registry.RunEvictionTickAsync();

        var reloaded = registry.Lease(solutionPath);
        using var reloadedLease = reloaded.Lease;

        Assert.Same(solutionInitial, solutionTouched);
        Assert.Equal(1, factory.LoadsCancelled);
        Assert.Equal(2, factory.InstancesCreated);
        Assert.NotSame(solutionInitial, reloadedLease!.ResidentSolution);
    }

    [Fact]
    public async Task Lease_MissingSolutionFile_ReturnsLoaderErrorWithoutResidentEntry()
    {
        using var tempDir = TestTempDirectory.Create("project-registry-uninit-");
        var solutionPath = Path.Combine(tempDir.DirectoryPath, "proj", "app.slnx");
        var factory = new TrackingSolutionFactory();
        await using var registry = CreateRegistry(factory, new FakeClock());

        var failed = registry.Lease(solutionPath);

        Assert.False(failed.Succeeded);
        Assert.Null(failed.Lease);
        Assert.Equal(ProjectErrorCodes.SolutionNotFound, failed.ErrorCode);
        Assert.Equal(0, factory.InstancesCreated);

        CreateSolutionPath(tempDir, "proj");
        var retry = registry.Lease(solutionPath);
        using var retryLease = retry.Lease;

        Assert.True(retry.Succeeded);
        Assert.Equal(1, factory.InstancesCreated);
    }

    [Fact]
    public async Task Lease_ParallelCallersOnSameRoot_CreateExactlyOneInstance()
    {
        using var tempDir = TestTempDirectory.Create("project-registry-dedupe-");
        var solutionPath = CreateSolutionPath(tempDir, "proj");
        var clock = new FakeClock();
        var factory = new TrackingSolutionFactory();
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFactory = new ManualResetEventSlim(false);
        await using var registry = new ProjectRegistry(new ProjectRegistryOptions(
            definition =>
            {
                factoryEntered.TrySetResult();
                releaseFactory.Wait(TimeSpan.FromSeconds(30));
                return ResidentSolutionCreation.Resident(factory.CreateSolution(definition));
            },
            clock));

        var firstCall = Task.Run(() => registry.Lease(solutionPath));
        await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var secondCall = Task.Run(() => registry.Lease(solutionPath));
        Assert.True(SpinWait.SpinUntil(() => registry.PendingCreationWaiters(solutionPath) >= 2, TimeSpan.FromSeconds(10)));

        releaseFactory.Set();
        var firstResult = await firstCall;
        var secondResult = await secondCall;
        using var firstLease = firstResult.Lease;
        using var secondLease = secondResult.Lease;

        Assert.True(firstResult.Succeeded);
        Assert.True(secondResult.Succeeded);
        Assert.Same(firstLease!.ResidentSolution, secondLease!.ResidentSolution);
        Assert.Equal(1, factory.InstancesCreated);
    }

    [Fact]
    public async Task Lease_LRUEviction_RespectsMaxProjects()
    {
        using var tempDir = TestTempDirectory.Create("project-registry-lru-");
        var clock = new FakeClock();
        var factory = new TrackingSolutionFactory();
        await using var registry = new ProjectRegistry(new ProjectRegistryOptions(
            factory.Factory,
            clock,
            MaxProjects: 2,
            IdleTtl: TimeSpan.FromHours(1)));

        var p1 = CreateSolutionPath(tempDir, "p1");
        var p2 = CreateSolutionPath(tempDir, "p2");
        var p3 = CreateSolutionPath(tempDir, "p3");

        var l1 = registry.Lease(p1);
        l1.Lease!.Dispose();
        clock.AdvanceMinutes(5);

        var l2 = registry.Lease(p2);
        l2.Lease!.Dispose();
        clock.AdvanceMinutes(5);

        // p3 should evict p1 (least recently used)
        var l3 = registry.Lease(p3);
        l3.Lease!.Dispose();

        var snapshots = registry.Snapshots();
        Assert.Equal(2, snapshots.Count);
        Assert.Null(registry.FindSnapshot(p1));
        Assert.NotNull(registry.FindSnapshot(p2));
        Assert.NotNull(registry.FindSnapshot(p3));
    }

    private static string CreateSolutionPath(TestTempDirectory tempDir, string name) =>
        tempDir.CreateFile($"{name}/app.slnx", "");

    private static ProjectRegistry CreateRegistry(
        TrackingSolutionFactory factory,
        TimeProvider clock,
        int idleTtlMinutes = 45) =>
        new(new ProjectRegistryOptions(
            factory.Factory,
            clock,
            IdleTtl: TimeSpan.FromMinutes(idleTtlMinutes)));
}
