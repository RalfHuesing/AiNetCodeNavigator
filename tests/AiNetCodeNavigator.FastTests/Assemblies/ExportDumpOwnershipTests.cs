using System.Diagnostics;
using System.Text;
using AiNetCodeNavigator.AssemblyExport;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class ExportDumpOwnershipTests
{
    [Fact]
    public void ResetRoot_DeletesMarkedContentsAndRecreatesExactMarker()
    {
        using var temp = TestTempDirectory.Create("export-reset-owned-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Root", "public class Root { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        var ownership = new ExportDumpOwnership(plan);
        ownership.ResetRoot();
        Directory.CreateDirectory(Path.Combine(plan.OutputDirectory, "foreign"));
        File.WriteAllText(Path.Combine(plan.OutputDirectory, "foreign", "data.txt"), "old data");
        File.WriteAllText(Path.Combine(plan.OutputDirectory, "last-run.json"), "old report");

        ownership.ResetRoot();

        Assert.False(File.Exists(Path.Combine(plan.OutputDirectory, "foreign", "data.txt")));
        Assert.False(File.Exists(Path.Combine(plan.OutputDirectory, "last-run.json")));
        Assert.Equal(Encoding.UTF8.GetBytes(ExportDumpOwnership.MarkerContent),
            File.ReadAllBytes(Path.Combine(plan.OutputDirectory, ExportDumpOwnership.MarkerName)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetRoot_RejectsMissingOrAlteredMarkerWithoutDeletingContents(bool alterMarker)
    {
        using var temp = TestTempDirectory.Create("export-reset-marker-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Root", "public class Root { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        var ownership = new ExportDumpOwnership(plan);
        ownership.ResetRoot();
        var marker = Path.Combine(plan.OutputDirectory, ExportDumpOwnership.MarkerName);
        var sentinel = Path.Combine(plan.OutputDirectory, "keep.txt");
        File.WriteAllText(sentinel, "keep");
        if (alterMarker) File.WriteAllText(marker, ExportDumpOwnership.MarkerContent + "extra");
        else File.Delete(marker);

        Assert.Throws<InvalidOperationException>(ownership.ResetRoot);
        Assert.Equal("keep", File.ReadAllText(sentinel));
    }

    [Fact]
    public void ResetRoot_RejectsVolumeRoot()
    {
        using var temp = TestTempDirectory.Create("export-reset-volume-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Root", "public class Root { }");
        var volumeRoot = Path.GetPathRoot(Path.GetFullPath(temp.DirectoryPath))!;
        var plan = new ExportPlan(new(volumeRoot, [source]), volumeRoot, [], [], []);

        Assert.Throws<InvalidOperationException>(() => new ExportDumpOwnership(plan).ResetRoot());
    }

    [Fact]
    public void AcquireRunLock_RejectsSecondOwnerUntilFirstLeaseIsDisposed()
    {
        using var temp = TestTempDirectory.Create("export-reset-lock-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Root", "public class Root { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        var firstOwner = new ExportDumpOwnership(plan);
        var secondOwner = new ExportDumpOwnership(plan);
        using (firstOwner.AcquireRunLock())
        {
            Assert.Throws<InvalidOperationException>(() => secondOwner.AcquireRunLock());
            Assert.False(Directory.Exists(plan.OutputDirectory));
        }

        using var nextRun = secondOwner.AcquireRunLock();
    }

    [Fact]
    public void TemporaryRoot_RequiresItsExactMarkerForStageOperationsAndCleanup()
    {
        using var temp = TestTempDirectory.Create("export-reset-temporary-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Root", "public class Root { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        var ownership = new ExportDumpOwnership(plan);
        ownership.ResetRoot();
        ownership.CreateTemporaryRoot();
        var temporaryRoot = Path.Combine(plan.OutputDirectory, ".assembly-export-tmp");
        var marker = Path.Combine(temporaryRoot, ExportDumpOwnership.TemporaryMarkerName);
        var stage = ownership.CreateStagingPath();
        Assert.True(Guid.TryParseExact(Path.GetFileName(stage)[".assembly-export-stage-".Length..], "N", out _));
        Assert.Throws<InvalidOperationException>(() => ownership.ValidateStagingPath(
            Path.Combine(plan.OutputDirectory, Path.GetFileName(stage))));
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "partial.txt"), "staged");
        File.WriteAllText(marker, "changed marker");

        Assert.Throws<InvalidOperationException>(() => ownership.ValidateStagingPath(stage));
        Assert.Throws<InvalidOperationException>(() => ownership.DeleteStaging(stage));
        Assert.Throws<InvalidOperationException>(ownership.DeleteTemporaryRoot);
        Assert.True(File.Exists(Path.Combine(stage, "partial.txt")));
    }

    [Fact]
    public void DeleteTemporaryRoot_RemovesOnlyTheMarkedTemporaryTree()
    {
        using var temp = TestTempDirectory.Create("export-reset-temp-delete-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Root", "public class Root { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        var ownership = new ExportDumpOwnership(plan);
        ownership.ResetRoot();
        ownership.CreateTemporaryRoot();
        var temporaryRoot = Path.Combine(plan.OutputDirectory, ".assembly-export-tmp");
        Directory.CreateDirectory(Path.Combine(temporaryRoot, ".assembly-export-stage-" + Guid.NewGuid().ToString("N")));

        ownership.DeleteTemporaryRoot();

        Assert.False(Directory.Exists(temporaryRoot));
        Assert.True(File.Exists(Path.Combine(plan.OutputDirectory, ExportDumpOwnership.MarkerName)));
    }

    [Fact]
    public void ResetRoot_RejectsReparseDescendantWithoutDeletingOutsideTarget()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = TestTempDirectory.Create("export-reset-reparse-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Root", "public class Root { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        var ownership = new ExportDumpOwnership(plan);
        ownership.ResetRoot();
        var outside = temp.GetPath("outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "keep.txt");
        File.WriteAllText(sentinel, "keep");
        var junction = Path.Combine(plan.OutputDirectory, "redirect");
        CreateJunction(junction, outside);
        try
        {
            Assert.Throws<InvalidOperationException>(ownership.ResetRoot);
            Assert.Equal("keep", File.ReadAllText(sentinel));
        }
        finally { Directory.Delete(junction); }
    }

    private static void CreateJunction(string path, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "/c", "mklink", "/J", path, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
