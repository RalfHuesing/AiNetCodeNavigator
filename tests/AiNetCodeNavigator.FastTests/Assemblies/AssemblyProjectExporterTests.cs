using System.Diagnostics;
using AiNetCodeNavigator.Core.Assemblies;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class AssemblyProjectExporterTests
{
    [Fact]
    public void ValidateArtifacts_RejectsSourceWithReparseAncestorWithoutTouchingOutsideFile()
    {
        using var temp = TestTempDirectory.Create("export-artifact-reparse-");
        var stage = temp.GetPath("stage");
        var outside = temp.GetPath("outside");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(stage, "Root.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(outside, "Outside.cs"), "public class Outside { }");
        var link = Path.Combine(stage, "redirect");
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/c", "mklink", "/J", link, outside }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, outside);
        try
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                AssemblyProjectExporter.ValidateArtifacts(stage, "Root.csproj", ["redirect/Outside.cs"]));
            Assert.Contains("reparse-point", exception.Message, StringComparison.Ordinal);
            Assert.Equal("public class Outside { }", File.ReadAllText(Path.Combine(outside, "Outside.cs")));
        }
        finally { Directory.Delete(link); }
    }
}
