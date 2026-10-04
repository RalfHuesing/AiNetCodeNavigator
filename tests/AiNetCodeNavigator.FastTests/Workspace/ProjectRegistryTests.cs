#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Builders;
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

    [Fact]
    public async Task Lease_LruRetirementReportsOldSnapshotTicketWithoutBlockingFreshOwner()
    {
        using var tempDir = TestTempDirectory.Create("project-registry-retirement-ticket-");
        var factory = new TrackingSolutionFactory();
        await using var registry = new ProjectRegistry(new ProjectRegistryOptions(
            factory.Factory,
            new FakeClock(),
            MaxProjects: 1,
            IdleTtl: TimeSpan.FromHours(1)));
        var firstPath = CreateSolutionPath(tempDir, "first");
        var secondPath = CreateSolutionPath(tempDir, "second");
        var retirementStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRetirement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? retiredPath = null;
        long retiredTicket = 0;
        registry.SourceOwnerRetiring = async (path, ticket) =>
        {
            if (!string.Equals(path, Path.GetFullPath(firstPath), StringComparison.OrdinalIgnoreCase)) return;
            retiredPath = path;
            retiredTicket = ticket;
            retirementStarted.TrySetResult();
            await allowRetirement.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };

        var first = registry.Lease(firstPath);
        using var firstLease = first.Lease;
        Assert.NotNull(firstLease);
        registry.RecordValidatedSourceSnapshot(firstLease, 41);
        firstLease.Dispose();

        var secondTask = Task.Run(() => registry.Lease(secondPath));
        await retirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var freshFirst = await Task.Run(() => registry.Lease(firstPath)).WaitAsync(TimeSpan.FromSeconds(10));
        using var freshFirstLease = freshFirst.Lease;
        Assert.NotNull(freshFirstLease);
        registry.RecordValidatedSourceSnapshot(freshFirstLease, 42);

        allowRetirement.TrySetResult();
        var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(10));
        using var secondLease = second.Lease;

        Assert.Equal(Path.GetFullPath(firstPath), retiredPath);
        Assert.Equal(41, retiredTicket);
        Assert.True(freshFirst.Succeeded);
        Assert.NotSame(firstLease.ResidentSolution, freshFirstLease.ResidentSolution);
        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task Lease_WhileBackgroundLoadIsPending_ReturnsRetryableLoadingState()
    {
        using var tempDir = TestTempDirectory.Create("project-registry-background-load-");
        var solutionPath = CreateSolutionPath(tempDir, "loading");
        var otherPath = CreateSolutionPath(tempDir, "other");
        using var solutionHandle = TestWorkspaceBuilder.Create().WithProject("App").Build();
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishLoad = new TaskCompletionSource<ResidentLoadedState?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registry = new ProjectRegistry(new ProjectRegistryOptions(
            _ => ResidentSolutionCreation.Resident(new ResidentSolution(async cancellationToken =>
            {
                loadStarted.TrySetResult();
                return await finishLoad.Task.WaitAsync(cancellationToken);
            })),
            TimeProvider.System));

        var result = registry.Lease(solutionPath);
        using var lease = result.Lease;
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Succeeded);
        Assert.Equal(ServerLoadState.Loading, lease!.ResidentSolution.LoadState);
        Assert.Equal(1, registry.ActiveLoadCount);
        Assert.Null(lease.ResidentSolution.GetCurrentSolution());

        var other = registry.Lease(otherPath);
        using var otherLease = other.Lease;
        Assert.True(other.Succeeded);
        Assert.Equal(ServerLoadState.Loading, otherLease!.ResidentSolution.LoadState);
        Assert.Equal(2, registry.ActiveLoadCount);

        finishLoad.TrySetResult(new ResidentLoadedState(solutionHandle.Solution, null));
        await Task.WhenAll(
            lease.ResidentSolution.LoadTask!.WaitAsync(TimeSpan.FromSeconds(10)),
            otherLease.ResidentSolution.LoadTask!.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(ServerLoadState.Loaded, lease.ResidentSolution.LoadState);
        Assert.Equal(0, registry.ActiveLoadCount);
        var firstCurrentSolution = lease.ResidentSolution.GetCurrentSolution();
        Assert.NotNull(firstCurrentSolution);
        Assert.Equal(solutionHandle.Solution.Id, firstCurrentSolution.Id);
        Assert.Equal(solutionHandle.Solution.ProjectIds, firstCurrentSolution.ProjectIds);
        foreach (var sourceProject in solutionHandle.Solution.Projects)
        {
            var currentProject = firstCurrentSolution.GetProject(sourceProject.Id);
            Assert.NotNull(currentProject);
            Assert.Equal(sourceProject.FilePath, currentProject.FilePath);
            Assert.Equal(sourceProject.DocumentIds, currentProject.DocumentIds);
            Assert.Equal(sourceProject.AdditionalDocumentIds, currentProject.AdditionalDocumentIds);
            foreach (var documentId in sourceProject.DocumentIds)
            {
                var sourceDocument = sourceProject.GetDocument(documentId)!;
                var currentDocument = currentProject.GetDocument(documentId);
                Assert.NotNull(currentDocument);
                Assert.Equal(sourceDocument.FilePath, currentDocument.FilePath);
                Assert.Equal(sourceDocument.Name, currentDocument.Name);
                Assert.Equal(sourceDocument.Folders, currentDocument.Folders);
                Assert.Equal(await sourceDocument.GetTextAsync(), await currentDocument.GetTextAsync());
            }
        }

        Assert.Same(firstCurrentSolution, lease.ResidentSolution.GetCurrentSolution());
    }

    [Fact]
    public async Task Lease_AfterFailedBackgroundLoad_RetryCreatesFreshResidentSolution()
    {
        using var tempDir = TestTempDirectory.Create("project-registry-load-retry-");
        var solutionPath = CreateSolutionPath(tempDir, "retry");
        var factory = new TrackingSolutionFactory { FailLoads = true };
        await using var registry = CreateRegistry(factory, new FakeClock());

        var first = registry.Lease(solutionPath);
        var failedResident = first.Lease!.ResidentSolution;
        await failedResident.LoadTask!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ServerLoadState.LoadFailed, failedResident.LoadState);

        first.Lease.MarkLoadFailedResponseEmitted();
        first.Lease.Dispose();
        factory.FailLoads = false;

        var retry = registry.Lease(solutionPath);
        using var retryLease = retry.Lease;

        Assert.True(retry.Succeeded);
        Assert.NotSame(failedResident, retryLease!.ResidentSolution);
        Assert.Equal(ServerLoadState.Loading, retryLease.ResidentSolution.LoadState);
        Assert.Equal(2, factory.InstancesCreated);
    }

    [Fact]
    public async Task Lease_LruEviction_SkipsBusyEntriesUntilReleased()
    {
        using var tempDir = TestTempDirectory.Create("project-registry-busy-lru-");
        var factory = new TrackingSolutionFactory();
        await using var registry = new ProjectRegistry(new ProjectRegistryOptions(
            factory.Factory,
            new FakeClock(),
            MaxProjects: 1,
            IdleTtl: TimeSpan.FromHours(1)));
        var firstPath = CreateSolutionPath(tempDir, "first");
        var secondPath = CreateSolutionPath(tempDir, "second");
        var thirdPath = CreateSolutionPath(tempDir, "third");

        var first = registry.Lease(firstPath);
        Assert.True(first.Succeeded);
        var second = registry.Lease(secondPath);
        using var secondLease = second.Lease;

        Assert.True(second.Succeeded);
        Assert.NotNull(registry.FindSnapshot(firstPath));
        Assert.NotNull(registry.FindSnapshot(secondPath));

        first.Lease!.Dispose();
        var third = registry.Lease(thirdPath);
        using var thirdLease = third.Lease;

        Assert.True(third.Succeeded);
        Assert.Null(registry.FindSnapshot(firstPath));
        Assert.NotNull(registry.FindSnapshot(secondPath));
        Assert.NotNull(registry.FindSnapshot(thirdPath));
    }

    [Fact]
    public async Task DisposeAsync_DuringCreationWaitsAndDisposesUnpublishedResident()
    {
        using var tempDir = TestTempDirectory.Create("project-registry-dispose-creation-");
        var solutionPath = CreateSolutionPath(tempDir, "pending");
        var factory = new TrackingSolutionFactory();
        var creationReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releasePublish = new ManualResetEventSlim(false);
        ProjectCreationAttempt? pendingAttempt = null;
        await using var registry = new ProjectRegistry(new ProjectRegistryOptions(
            factory.Factory,
            TimeProvider.System)
        {
            BeforePublishCreation = (_, attempt) =>
            {
                pendingAttempt = attempt;
                creationReady.TrySetResult();
                Assert.True(releasePublish.Wait(TimeSpan.FromSeconds(30)));
                return null;
            },
        });

        var leaseTask = Task.Run(() => registry.Lease(solutionPath));
        await creationReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var disposeTask = registry.DisposeAsync().AsTask();
        var disposeCompletedBeforePublish = disposeTask.IsCompleted;

        releasePublish.Set();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));
        var result = await leaseTask.WaitAsync(TimeSpan.FromSeconds(10));
        var succeeded = result.Succeeded;
        var errorCode = result.ErrorCode;
        var snapshotCount = registry.Snapshots().Count;
        var disposalCountBeforeFallbackCleanup = factory.SolutionsDisposed;

        if (result.Lease is { } orphanLease)
        {
            await orphanLease.ResidentSolution.DisposeAsync();
            orphanLease.Dispose();
        }
        else
        {
            if (pendingAttempt?.Creation.Solution is { } unpublished)
            {
                await unpublished.DisposeAsync();
            }
        }

        Assert.False(disposeCompletedBeforePublish);
        Assert.False(succeeded);
        Assert.Equal(ProjectErrorCodes.RegistryDisposed, errorCode);
        Assert.Empty(registry.Snapshots());
        Assert.Equal(0, snapshotCount);
        Assert.Equal(1, disposalCountBeforeFallbackCleanup);
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
