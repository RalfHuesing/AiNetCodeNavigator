using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using AiNetCodeNavigator.Core.Assemblies;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class AssemblyProjectExporterTests
{
    [Fact]
    public void ValidateGeneratedOutput_PreservesSyntaxInvalidAndEmptySourcesAsPartialOutput()
    {
        using var temp = TestTempDirectory.Create("export-partial-validation-");
        var stage = temp.GetPath("stage");
        Directory.CreateDirectory(stage);
        var projectPath = Path.Combine(stage, "Root.csproj");
        var brokenPath = Path.Combine(stage, "Broken.cs");
        var emptyPath = Path.Combine(stage, "Empty.cs");
        File.WriteAllText(projectPath, "<Project />");
        File.WriteAllText(brokenPath, "public class Broken { void M() { ref } }");
        File.WriteAllText(emptyPath, string.Empty);
        var decompilation = new DecompilationResult(
            [new(brokenPath, "Broken", File.ReadAllText(brokenPath)), new(emptyPath, "Empty", string.Empty)],
            [new("syntax-diagnostic", "Broken.cs contains CS1525.", AssemblyDiagnosticSeverity.Warning),
                new("empty-diagnostic", "Empty.cs is empty.", AssemblyDiagnosticSeverity.Warning)],
            false,
            projectPath);

        var result = AssemblyProjectExporter.ValidateGeneratedOutput(stage, decompilation, new string('a', 64));

        Assert.False(result.IsComplete);
        Assert.Equal(["Broken.cs", "Empty.cs"], result.SourceRelativePaths);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic => Assert.False(diagnostic.IsError));
        Assert.True(File.Exists(Path.Combine(stage, "Broken.cs")));
        Assert.True(File.Exists(Path.Combine(stage, "Empty.cs")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<Project>")]
    [InlineData("<Other />")]
    public void ValidateGeneratedOutput_RepairsMalformedProjectAndPreservesSourcesAsPartialOutput(string projectXml)
    {
        using var temp = TestTempDirectory.Create("export-malformed-project-");
        var stage = temp.GetPath("stage");
        Directory.CreateDirectory(stage);
        var projectPath = Path.Combine(stage, "Root.csproj");
        var sourcePath = Path.Combine(stage, "Probe.cs");
        File.WriteAllText(projectPath, projectXml);
        File.WriteAllText(sourcePath, "public sealed class Probe { }");
        var decompilation = new DecompilationResult(
            [new(sourcePath, "Probe", File.ReadAllText(sourcePath))],
            [],
            true,
            projectPath);

        var result = AssemblyProjectExporter.ValidateGeneratedOutput(stage, decompilation, new string('a', 64));

        Assert.False(result.IsComplete);
        Assert.Equal(["Probe.cs"], result.SourceRelativePaths);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("assembly-export-project-repaired", diagnostic.Code);
        Assert.False(diagnostic.IsError);
        Assert.Contains("minimal project", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("public sealed class Probe { }", File.ReadAllText(sourcePath));
        using var reader = XmlReader.Create(projectPath,
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var project = XDocument.Load(reader);
        Assert.Equal("Project", project.Root?.Name.LocalName);
        Assert.Empty(project.Root!.Elements("PropertyGroup").Elements("TargetFramework"));
        Assert.Equal("Probe.cs", Assert.Single(project.Root!.Element("ItemGroup")!.Elements("Compile"))
            .Attribute("Include")?.Value);
    }

    [Fact]
    public void ValidateGeneratedOutput_RejectsMissingProjectOrSourceDocuments()
    {
        using var temp = TestTempDirectory.Create("export-no-usable-output-");
        var stage = temp.GetPath("stage");
        Directory.CreateDirectory(stage);

        var exception = Assert.Throws<InvalidDataException>(() => AssemblyProjectExporter.ValidateGeneratedOutput(
            stage, new DecompilationResult([], [], false), new string('a', 64)));

        Assert.Contains("no usable project or C# source", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

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
