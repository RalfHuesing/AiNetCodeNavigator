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
    public void Cleanup_PreservesUnmarkedDirectoriesEvenWhenTheirNamesMatchScratchLayout()
    {
        using var tempDir = TestTempDirectory.Create("scratch-unmarked-");
        var artifact = tempDir.CreateFile($"101/{Guid.NewGuid():N}/important.txt", "unowned");

        DesignTimeScratchMaintenance.CleanupAbandonedDirectories(tempDir.DirectoryPath, _ => false);

        Assert.Equal("unowned", File.ReadAllText(artifact));
    }

    [Fact]
    public void Cleanup_RejectsJunctionAsScratchRoot()
    {
        using var tempDir = TestTempDirectory.Create("scratch-root-junction-");
        var artifact = tempDir.CreateFile($"outside/101/{Guid.NewGuid():N}/important.txt", "outside");
        var junction = tempDir.GetPath("scratch");
        CreateDirectoryLink(junction, tempDir.GetPath("outside"));
        try
        {
            Assert.True((File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0);
            DesignTimeScratchMaintenance.CleanupAbandonedDirectories(junction, _ => false);
            Assert.Equal("outside", File.ReadAllText(artifact));
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        var startInfo = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("New-Item -ItemType Junction -Path $env:NAVIGATOR_TEST_LINK -Value $env:NAVIGATOR_TEST_TARGET -ErrorAction Stop | Out-Null");
        startInfo.Environment["NAVIGATOR_TEST_LINK"] = link;
        startInfo.Environment["NAVIGATOR_TEST_TARGET"] = target;
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("Creating a directory junction did not finish within 30 seconds.");
        }
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    [Theory]
    [InlineData("root")]
    [InlineData("outside")]
    [InlineData("sibling-prefix")]
    [InlineData("parent-traversal")]
    public void Delete_RejectsRootAndPathsOutsideTheAllowedRoot(string selection)
    {
        using var tempDir = TestTempDirectory.Create("scratch-boundary-");
        var root = tempDir.GetPath("scratch");
        Directory.CreateDirectory(root);
        var sibling = tempDir.GetPath("scratch-other");
        Directory.CreateDirectory(sibling);
        var outside = tempDir.GetPath("outside");
        Directory.CreateDirectory(outside);
        var target = selection switch
        {
            "root" => root,
            "outside" => outside,
            "sibling-prefix" => sibling,
            _ => Path.Combine(root, "..", "outside"),
        };
        var sentinel = Path.Combine(Path.GetFullPath(target), "important.txt");
        File.WriteAllText(sentinel, "preserve");

        Assert.Throws<IOException>(() => DesignTimeScratchSafety.EnsureSafePath(root, target));
        Assert.False(DesignTimeScratchSafety.TryDeleteOwnedDirectory(root, target));

        Assert.Equal("preserve", File.ReadAllText(sentinel));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("wrong-version")]
    [InlineData("copied-from-another-directory")]
    public void Delete_RejectsMissingMalformedOrMismatchedOwnershipMarkers(string mutation)
    {
        using var tempDir = TestTempDirectory.Create("scratch-marker-");
        var workspace = CreateOwnedWorkspace(tempDir.DirectoryPath, 101);
        var marker = Path.Combine(workspace, DesignTimeScratchSafety.MarkerFileName);
        switch (mutation)
        {
            case "missing":
                File.Delete(marker);
                break;
            case "malformed":
                File.WriteAllText(marker, "not JSON");
                break;
            case "wrong-version":
                var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(marker))!;
                document["Version"] = 999;
                File.WriteAllText(marker, document.ToJsonString());
                break;
            default:
                var other = CreateOwnedWorkspace(tempDir.DirectoryPath, 101);
                File.Copy(Path.Combine(other, DesignTimeScratchSafety.MarkerFileName), marker, overwrite: true);
                break;
        }

        Assert.False(DesignTimeScratchSafety.TryDeleteOwnedDirectory(tempDir.DirectoryPath, workspace));

        Assert.Equal("owned", File.ReadAllText(Path.Combine(workspace, "artifact.cs")));
    }

    [Fact]
    public void Delete_RejectsNestedJunctionBeforeDeletingAnyOwnedContents()
    {
        using var tempDir = TestTempDirectory.Create("scratch-nested-junction-");
        var workspace = CreateOwnedWorkspace(tempDir.DirectoryPath, 101);
        var external = tempDir.CreateFile("outside/important.txt", "outside");
        var junction = Path.Combine(workspace, "linked");
        CreateDirectoryLink(junction, tempDir.GetPath("outside"));
        try
        {
            Assert.False(DesignTimeScratchSafety.TryDeleteOwnedDirectory(tempDir.DirectoryPath, workspace));
            Assert.Equal("outside", File.ReadAllText(external));
            Assert.Equal("owned", File.ReadAllText(Path.Combine(workspace, "artifact.cs")));
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public void Delete_RejectsJunctionInAncestorOfScratchRoot()
    {
        using var tempDir = TestTempDirectory.Create("scratch-ancestor-junction-");
        var realRoot = tempDir.GetPath("outside/scratch");
        var workspace = CreateOwnedWorkspace(realRoot, 101);
        var junction = tempDir.GetPath("alias");
        CreateDirectoryLink(junction, tempDir.GetPath("outside"));
        var aliasedRoot = Path.Combine(junction, "scratch");
        try
        {
            var aliasedWorkspace = Path.Combine(aliasedRoot, Path.GetRelativePath(realRoot, workspace));
            var error = Assert.Throws<IOException>(() => DesignTimeScratchSafety.EnsureSafePath(aliasedRoot, aliasedWorkspace));
            Assert.Contains("reparse point", error.Message, StringComparison.Ordinal);
            Assert.False(DesignTimeScratchSafety.TryDeleteOwnedDirectory(aliasedRoot, aliasedWorkspace));
            Assert.Equal("owned", File.ReadAllText(Path.Combine(workspace, "artifact.cs")));
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public void Cleanup_DoesNotAdoptUnmarkedOwnershipFiles()
    {
        using var tempDir = TestTempDirectory.Create("scratch-unmarked-lock-");
        var owner = tempDir.CreateFile(".owner-101.lock", "");

        DesignTimeScratchMaintenance.CleanupAbandonedDirectories(tempDir.DirectoryPath, _ => false);

        Assert.True(File.Exists(owner));
        Assert.Equal("", File.ReadAllText(owner));
    }

    [Fact]
    public void Cleanup_PreservesEntireProcessWhenAnUnownedWorkspaceIsPresent()
    {
        using var tempDir = TestTempDirectory.Create("scratch-mixed-ownership-");
        var owned = CreateOwnedWorkspace(tempDir.DirectoryPath, 101);
        var unowned = tempDir.CreateFile($"101/{Guid.NewGuid():N}/important.txt", "unowned");

        DesignTimeScratchMaintenance.CleanupAbandonedDirectories(tempDir.DirectoryPath, _ => false);

        Assert.Equal("unowned", File.ReadAllText(unowned));
        Assert.Equal("owned", File.ReadAllText(Path.Combine(owned, "artifact.cs")));
    }

    [Fact]
    public void Cleanup_RemovesOwnedAbandonedOutputButPreservesLiveLegacyAndUnknownDirectories()
    {
        using var tempDir = TestTempDirectory.Create("scratch-maintenance-");
        CreateOwnedWorkspace(tempDir.DirectoryPath, 101);
        var liveWorkspace = CreateOwnedWorkspace(tempDir.DirectoryPath, 102);
        var liveFile = Path.Combine(liveWorkspace, "artifact.cs");
        File.WriteAllText(liveFile, "live");
        var unknownFile = tempDir.CreateFile("103/user-file.txt", "unknown");
        var otherFile = tempDir.CreateFile("other/user-file.txt", "unknown");
        var quarantinedDirectory = $"orphan-{Guid.NewGuid():N}";
        tempDir.CreateFile($"{quarantinedDirectory}/artifact.cs", "leftover");
        using (DesignTimeScratchSafety.AcquireProcessOwnership(tempDir.DirectoryPath, 104, FileShare.Read)) { }
        var abandonedOwner = DesignTimeScratchMaintenance.OwnerFilePath(tempDir.DirectoryPath, 104);

        DesignTimeScratchMaintenance.CleanupAbandonedDirectories(tempDir.DirectoryPath, processId => processId == 102);

        Assert.False(Directory.Exists(tempDir.GetPath("101")));
        Assert.True(Directory.Exists(tempDir.GetPath(quarantinedDirectory)));
        Assert.True(File.Exists(liveFile));
        Assert.True(File.Exists(unknownFile));
        Assert.True(File.Exists(otherFile));
        Assert.False(File.Exists(abandonedOwner));
    }

    [Fact]
    public void Cleanup_PreservesLockedOwnerEvenWhenProcessProbeReportsDead()
    {
        using var tempDir = TestTempDirectory.Create("scratch-locked-owner-");
        var artifact = Path.Combine(CreateOwnedWorkspace(tempDir.DirectoryPath, 101), "artifact.cs");
        File.WriteAllText(artifact, "active");
        using var owner = DesignTimeScratchSafety.AcquireProcessOwnership(tempDir.DirectoryPath, 101, FileShare.Read);

        DesignTimeScratchMaintenance.CleanupAbandonedDirectories(tempDir.DirectoryPath, _ => false);

        Assert.True(File.Exists(artifact));
    }

    [Fact]
    public void Cleanup_RechecksProcessOwnershipBeforeClaimingDirectory()
    {
        using var tempDir = TestTempDirectory.Create("scratch-reused-process-");
        var artifact = Path.Combine(CreateOwnedWorkspace(tempDir.DirectoryPath, 101), "artifact.cs");
        File.WriteAllText(artifact, "active");
        var probes = 0;

        DesignTimeScratchMaintenance.CleanupAbandonedDirectories(tempDir.DirectoryPath, _ => ++probes > 1);

        Assert.True(File.Exists(artifact));
        Assert.Equal(2, probes);
    }

    [Fact]
    public void Cleanup_CancellationPreservesPendingOutput()
    {
        using var tempDir = TestTempDirectory.Create("scratch-cancelled-");
        var artifact = Path.Combine(CreateOwnedWorkspace(tempDir.DirectoryPath, 101), "artifact.cs");
        File.WriteAllText(artifact, "pending");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        DesignTimeScratchMaintenance.CleanupAbandonedDirectories(tempDir.DirectoryPath, _ => false, cancellation.Token);

        Assert.True(File.Exists(artifact));
    }

    private static string CreateOwnedWorkspace(string root, int processId)
    {
        var processPath = Path.Combine(root, processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(DesignTimeScratchSafety.TryInitializeDirectory(root, processPath));
        var workspace = Path.Combine(processPath, Guid.NewGuid().ToString("N"));
        Assert.True(DesignTimeScratchSafety.TryInitializeDirectory(root, workspace));
        File.WriteAllText(Path.Combine(workspace, "artifact.cs"), "owned");
        return workspace;
    }
}
