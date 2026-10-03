#nullable enable

using System;
using System.IO;
using AiNetCodeNavigator.Core.Symbols;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class SourceReferenceOwnerTests
{
    [Fact]
    public void ProjectCoordinate_UsesNearestGitMarkerWorkingDirectoryRoot()
    {
        WithTemporaryDirectory(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var solutionDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Navigator")).FullName;
            var projectPath = Path.Combine(root, "src", "Core", "Core.csproj");

            Assert.True(SourceReferenceOwner.TryCreateProjectCoordinate(
                Path.Combine(solutionDirectory, "Navigator.slnx"), projectPath, out var coordinate, out var reason), reason);
            Assert.Equal("src/Core/Core.csproj", coordinate);
        });
    }

    [Fact]
    public void ProjectCoordinate_UsesWorktreeMarkerDirectoryInsteadOfGitdirDestination()
    {
        WithTemporaryDirectory(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, ".gitdir", "worktrees", "checkout"));
            File.WriteAllText(Path.Combine(root, ".git"), "gitdir: .gitdir/worktrees/checkout\n");
            var solutionDirectory = Directory.CreateDirectory(Path.Combine(root, "nested", "solution")).FullName;
            var projectPath = Path.Combine(root, "nested", "App", "App.csproj");

            Assert.True(SourceReferenceOwner.TryCreateProjectCoordinate(
                Path.Combine(solutionDirectory, "Navigator.slnx"), projectPath, out var coordinate, out var reason), reason);
            Assert.Equal("nested/App/App.csproj", coordinate);
        });
    }

    [Theory]
    [InlineData("gitdir: .gitdir/worktrees/checkout\n")]
    [InlineData("gitdir: .gitdir/worktrees/checkout\r\n")]
    [InlineData("\uFEFFgitdir: .gitdir/worktrees/checkout")]
    public void ProjectCoordinate_AcceptsStrictUtf8PointerWithOptionalBomAndOneFinalNewline(string pointer)
    {
        WithTemporaryDirectory(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, ".gitdir", "worktrees", "checkout"));
            File.WriteAllText(Path.Combine(root, ".git"), pointer, new System.Text.UTF8Encoding(false));
            var solutionDirectory = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            Assert.True(SourceReferenceOwner.TryCreateProjectCoordinate(
                Path.Combine(solutionDirectory, "Navigator.slnx"),
                Path.Combine(root, "App", "App.csproj"), out var coordinate, out var reason), reason);
            Assert.Equal("App/App.csproj", coordinate);
        });
    }

    [Fact]
    public void ProjectCoordinate_UsesSolutionDirectoryWhenNoGitMarkerExists()
    {
        WithTemporaryDirectory(root =>
        {
            var solutionDirectory = Directory.CreateDirectory(Path.Combine(root, "solution")).FullName;
            var projectPath = Path.Combine(root, "shared", "App.csproj");

            Assert.True(SourceReferenceOwner.TryCreateProjectCoordinate(
                Path.Combine(solutionDirectory, "Navigator.slnx"), projectPath, out var coordinate, out var reason), reason);
            Assert.Equal("../shared/App.csproj", coordinate);
        });
    }

    [Fact]
    public void ProjectCoordinate_AllowsExternalLoadedProjectOnSameVolume()
    {
        WithTemporaryDirectory(root =>
        {
            var workingDirectory = Directory.CreateDirectory(Path.Combine(root, "repo")).FullName;
            Directory.CreateDirectory(Path.Combine(workingDirectory, ".git"));
            var solutionDirectory = Directory.CreateDirectory(Path.Combine(workingDirectory, "src")).FullName;
            var externalProject = Path.Combine(root, "external", "Library.csproj");
            Assert.True(SourceReferenceOwner.TryCreateProjectCoordinate(
                Path.Combine(solutionDirectory, "Navigator.slnx"), externalProject, out var coordinate, out var reason), reason);
            Assert.Equal("../external/Library.csproj", coordinate);
        });
    }

    [Theory]
    [InlineData("gitdir:  .gitdir/worktrees/checkout\n")]
    [InlineData(" gitdir: .gitdir/worktrees/checkout\n")]
    [InlineData("gitdir: .gitdir/worktrees/checkout\nextra\n")]
    [InlineData("gitdir: .gitdir/worktrees/checkout\r")]
    [InlineData("gitdir: .gitdir/worktrees/checkout\n\n")]
    [InlineData("gitdir: .gitdir/worktrees/checkout \n")]
    [InlineData("gitdir: \n")]
    [InlineData("gitdir: missing\n")]
    public void ProjectCoordinate_RejectsMalformedOrMissingGitPointer(string pointer)
    {
        WithTemporaryDirectory(root =>
        {
            File.WriteAllText(Path.Combine(root, ".git"), pointer);
            var solutionDirectory = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            var succeeded = SourceReferenceOwner.TryCreateProjectCoordinate(
                Path.Combine(solutionDirectory, "Navigator.slnx"),
                Path.Combine(root, "App", "App.csproj"),
                out _,
                out var reason);

            Assert.False(succeeded);
            Assert.Contains(".git", reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ProjectCoordinate_RejectsInvalidUtf8GitPointer()
    {
        WithTemporaryDirectory(root =>
        {
            File.WriteAllBytes(Path.Combine(root, ".git"), [0x67, 0x69, 0x74, 0x64, 0x69, 0x72, 0x3A, 0x20, 0xFF]);
            var solutionDirectory = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            Assert.False(SourceReferenceOwner.TryCreateProjectCoordinate(
                Path.Combine(solutionDirectory, "Navigator.slnx"), Path.Combine(root, "App", "App.csproj"), out _, out var reason));
            Assert.Contains("UTF-8", reason, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ProjectCoordinate_AcceptsGitDirectorySymlinkAsWorkingDirectoryMarker()
    {
        WithTemporaryDirectory(root =>
        {
            var workingDirectory = Directory.CreateDirectory(Path.Combine(root, "checkout")).FullName;
            var gitStore = Directory.CreateDirectory(Path.Combine(root, "shared-gitdir")).FullName;
            var markerPath = Path.Combine(workingDirectory, ".git");
            if (OperatingSystem.IsWindows())
            {
                using var junction = DirectoryJunction.Create(markerPath, gitStore);
                AssertGitMarkerCoordinate(workingDirectory);
            }
            else
            {
                Directory.CreateSymbolicLink(markerPath, gitStore);
                AssertGitMarkerCoordinate(workingDirectory);
            }
        });
    }

    [Fact]
    public void ProjectCoordinate_DoesNotSkipMalformedNearestGitMarker()
    {
        WithTemporaryDirectory(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
            File.WriteAllText(Path.Combine(nested, ".git"), "bad marker\n");
            var succeeded = SourceReferenceOwner.TryCreateProjectCoordinate(
                Path.Combine(nested, "Navigator.slnx"), Path.Combine(root, "App", "App.csproj"), out _, out var reason);
            Assert.False(succeeded);
            Assert.Contains(".git", reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ProjectCoordinate_RejectsDifferentWindowsDriveWhenRunningOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var currentRoot = Path.GetPathRoot(Path.GetTempPath())!;
        var currentDrive = char.ToUpperInvariant(currentRoot[0]);
        var otherDrive = currentDrive == 'Z' ? 'Y' : 'Z';
        var solutionPath = Path.Combine(currentRoot, "repo", "Navigator.slnx");
        var projectPath = $"{otherDrive}:\\external\\App\\App.csproj";

        Assert.False(SourceReferenceOwner.TryCreateProjectCoordinate(solutionPath, projectPath, out _, out var reason));
        Assert.Contains("different filesystem volume", reason, StringComparison.OrdinalIgnoreCase);
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator-R01-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void AssertGitMarkerCoordinate(string workingDirectory)
    {
        var solutionDirectory = Directory.CreateDirectory(Path.Combine(workingDirectory, "src")).FullName;
        Assert.True(SourceReferenceOwner.TryCreateProjectCoordinate(
            Path.Combine(solutionDirectory, "Navigator.slnx"),
            Path.Combine(workingDirectory, "App", "App.csproj"), out var coordinate, out var reason), reason);
        Assert.Equal("App/App.csproj", coordinate);
    }

    private sealed class DirectoryJunction(string junctionPath) : IDisposable
    {
        internal static DirectoryJunction Create(string junctionPath, string targetPath)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(junctionPath);
            startInfo.ArgumentList.Add(targetPath);
            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new Xunit.Sdk.XunitException("Could not start cmd.exe to create a temporary directory junction.");
            process.WaitForExit();
            Assert.True(process.ExitCode == 0 && Directory.Exists(junctionPath), "Could not create a temporary directory junction.");
            return new DirectoryJunction(junctionPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(junctionPath)) Directory.Delete(junctionPath);
        }
    }
}
