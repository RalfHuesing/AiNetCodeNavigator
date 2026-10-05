using System;
using System.IO;
using System.Threading;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Workspace;

public sealed class DesignTimeScratchMaintenanceTests
{
    [Fact]
    public void Cleanup_RemovesAbandonedLegacyOutputAndQuarantineButPreservesLiveAndUnknownDirectories()
    {
        using var tempDir = TestTempDirectory.Create("scratch-maintenance-");
        tempDir.CreateFile($"101/{Guid.NewGuid():N}/obj/artifact.cs", "abandoned");
        var liveFile = tempDir.CreateFile($"102/{Guid.NewGuid():N}/obj/artifact.cs", "live");
        var unknownFile = tempDir.CreateFile("103/user-file.txt", "unknown");
        var otherFile = tempDir.CreateFile("other/user-file.txt", "unknown");
        var quarantinedDirectory = $"orphan-{Guid.NewGuid():N}";
        tempDir.CreateFile($"{quarantinedDirectory}/artifact.cs", "leftover");
        var abandonedOwner = tempDir.CreateFile(".owner-104.lock", "");

        DesignTimeScratchMaintenance.CleanupAbandonedDirectories(tempDir.DirectoryPath, processId => processId == 102);

        Assert.False(Directory.Exists(tempDir.GetPath("101")));
        Assert.False(Directory.Exists(tempDir.GetPath(quarantinedDirectory)));
        Assert.True(File.Exists(liveFile));
        Assert.True(File.Exists(unknownFile));
        Assert.True(File.Exists(otherFile));
        Assert.False(File.Exists(abandonedOwner));
    }

    [Fact]
    public void Cleanup_PreservesLockedOwnerEvenWhenProcessProbeReportsDead()
    {
        using var tempDir = TestTempDirectory.Create("scratch-locked-owner-");
        var artifact = tempDir.CreateFile($"101/{Guid.NewGuid():N}/obj/artifact.cs", "active");
        var ownerPath = DesignTimeScratchMaintenance.OwnerFilePath(tempDir.DirectoryPath, 101);
        using var owner = new FileStream(ownerPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

        DesignTimeScratchMaintenance.CleanupAbandonedDirectories(tempDir.DirectoryPath, _ => false);

        Assert.True(File.Exists(artifact));
    }

    [Fact]
    public void Cleanup_RechecksProcessOwnershipBeforeClaimingDirectory()
    {
        using var tempDir = TestTempDirectory.Create("scratch-reused-process-");
        var artifact = tempDir.CreateFile($"101/{Guid.NewGuid():N}/obj/artifact.cs", "active");
        var probes = 0;

        DesignTimeScratchMaintenance.CleanupAbandonedDirectories(tempDir.DirectoryPath, _ => ++probes > 1);

        Assert.True(File.Exists(artifact));
        Assert.Equal(2, probes);
    }

    [Fact]
    public void Cleanup_CancellationPreservesPendingOutput()
    {
        using var tempDir = TestTempDirectory.Create("scratch-cancelled-");
        var artifact = tempDir.CreateFile($"101/{Guid.NewGuid():N}/obj/artifact.cs", "pending");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        DesignTimeScratchMaintenance.CleanupAbandonedDirectories(tempDir.DirectoryPath, _ => false, cancellation.Token);

        Assert.True(File.Exists(artifact));
    }
}
