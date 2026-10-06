using System.Diagnostics;
using System.Text.Json;

namespace AiNetCodeNavigator.IntegrationTests.Assemblies;

[Trait("Category", "Integration")]
public sealed class AssemblyExportExecutableTests
{
    [Fact]
    public async Task Executable_RecursivelyExportsManagedDllAndExeFromDirectory()
    {
        using var temp = TestTempDirectory.Create("export-recursive-executable-");
        var sources = temp.GetPath("sources");
        var nested = Path.Combine(sources, "nested");
        Directory.CreateDirectory(nested);
        var library = AssemblyTestHelper.EmitAssembly(temp, "RecursiveLibrary", "namespace Recursive; public sealed class Library { public int Value => 7; }");
        var application = AssemblyTestHelper.EmitExecutable(temp, "RecursiveApplication", "public static class App { public static void Main() { } }");
        File.Move(library, Path.Combine(sources, "RecursiveLibrary.dll"));
        File.Move(application, Path.Combine(nested, "RecursiveApplication.exe"));
        await File.WriteAllTextAsync(Path.Combine(nested, "native.dll"), "not a managed assembly");

        var output = temp.GetPath("dump");
        var result = await RunAsync(output, sources);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(output, "RecursiveLibrary.dll", "export-manifest.json")));
        Assert.True(File.Exists(Path.Combine(output, "RecursiveApplication.exe", "export-manifest.json")));
        Assert.Contains("class Library", ReadSources(Path.Combine(output, "RecursiveLibrary.dll")), StringComparison.Ordinal);
        Assert.Contains("class App", ReadSources(Path.Combine(output, "RecursiveApplication.exe")), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(output, "native.dll")));
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "last-run.json")));
        Assert.Equal("complete", report.RootElement.GetProperty("completionState").GetString());
        Assert.Equal(2, report.RootElement.GetProperty("selectedChildren").GetArrayLength());
        Assert.Equal(sources, Assert.Single(report.RootElement.GetProperty("exactInputs").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Executable_FiltersRecursiveSourceDirectoryWithMultipleFilenamePatterns()
    {
        using var temp = TestTempDirectory.Create("export-filtered-executable-");
        var sources = temp.GetPath("sources");
        var fooExe = AssemblyTestHelper.EmitExecutable(temp, "fooApplication", "public static class App { public static void Main() { } }");
        var barDll = AssemblyTestHelper.EmitAssembly(temp, "mybarLibrary", "public sealed class Library { public int Value => 7; }");
        var unrelatedDll = AssemblyTestHelper.EmitAssembly(temp, "UnrelatedLibrary", "public sealed class Unrelated { }");
        var nested = Path.Combine(sources, "nested");
        var deep = Path.Combine(nested, "deep");
        Directory.CreateDirectory(deep);
        File.Move(fooExe, Path.Combine(nested, "fooApplication.exe"));
        File.Move(barDll, Path.Combine(deep, "mybarLibrary.dll"));
        File.Move(unrelatedDll, Path.Combine(sources, "UnrelatedLibrary.dll"));

        var output = temp.GetPath("dump");
        var result = await RunAsync(output, sources, "foo*.exe", "*bar*.dll");

        Assert.True(result.ExitCode == 0, result.Output + result.Errors);
        Assert.True(File.Exists(Path.Combine(output, "fooApplication.exe", "export-manifest.json")));
        Assert.True(File.Exists(Path.Combine(output, "mybarLibrary.dll", "export-manifest.json")));
        Assert.False(Directory.Exists(Path.Combine(output, "UnrelatedLibrary.dll")));
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "last-run.json")));
        Assert.Equal("complete", report.RootElement.GetProperty("completionState").GetString());
        Assert.Equal(2, report.RootElement.GetProperty("selectedChildren").GetArrayLength());
        Assert.Equal(new[] { sources, "foo*.exe", "*bar*.dll" }, report.RootElement.GetProperty("exactInputs").EnumerateArray()
            .Select(item => item.GetString()!).ToArray());
    }

    [Fact]
    public async Task Executable_ExportsDependencyAndReadableSolutionThenReplacesSelectedChildren()
    {
        using var temp = TestTempDirectory.Create("export-executable-");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "CliDependency", "namespace Vendor; public class Api { public int Read() => 42; }");
        var root = AssemblyTestHelper.EmitAssembly(temp, "CliRoot", "public class Root { public Vendor.Api OldMember = new(); }", dependency);
        var output = temp.GetPath("dump");
        var pattern = temp.GetPath("CliRoot*.dll");
        var inputBytes = await File.ReadAllBytesAsync(root);
        var dependencyBytes = await File.ReadAllBytesAsync(dependency);
        var first = await RunAsync(output, pattern);
        Assert.True(first.ExitCode == 0, first.Output + first.Errors);
        Assert.Equal(inputBytes, await File.ReadAllBytesAsync(root));
        Assert.Equal(dependencyBytes, await File.ReadAllBytesAsync(dependency));
        var child = Path.Combine(output, "CliRoot.dll");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(child, "export-manifest.json")));
        var project = manifest.RootElement.GetProperty("projectPath").GetString()!;
        Assert.False(Path.IsPathRooted(project));
        Assert.True(File.Exists(Path.Combine(child, project)));
        var solution = Assert.Single(Directory.GetFiles(child, "*.sln"));
        Assert.Contains(project.Replace('/', '\\'), await File.ReadAllTextAsync(solution), StringComparison.Ordinal);
        Assert.Contains("OldMember", ReadSources(child), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(output, "CliDependency.dll", "export-manifest.json")));
        Assert.Contains(manifest.RootElement.GetProperty("filteredEdges").EnumerateArray(), edge => edge.GetProperty("rule").GetString()!.StartsWith("prefix:System.", StringComparison.Ordinal));
        var stale = Path.Combine(child, "stale.cs");
        await File.WriteAllTextAsync(stale, "obsolete");
        var untouched = Path.Combine(output, "Unselected.dll");
        Directory.CreateDirectory(untouched);
        await File.WriteAllTextAsync(Path.Combine(untouched, "keep.cs"), "keep");
        AssemblyTestHelper.EmitAssembly(temp, "CliRoot", "public class Root { public Vendor.Api NewMember = new(); }", dependency);
        var changedBytes = await File.ReadAllBytesAsync(root);
        var second = await RunAsync(output, pattern);
        Assert.True(second.ExitCode == 0, second.Output + second.Errors);
        Assert.False(File.Exists(stale));
        Assert.Contains("NewMember", ReadSources(child), StringComparison.Ordinal);
        Assert.DoesNotContain("OldMember", ReadSources(child), StringComparison.Ordinal);
        Assert.Equal(changedBytes, await File.ReadAllBytesAsync(root));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(untouched, "keep.cs")));
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "last-run.json")));
        Assert.Equal("complete", report.RootElement.GetProperty("completionState").GetString());
        Assert.Equal(pattern, Assert.Single(report.RootElement.GetProperty("exactInputs").EnumerateArray()).GetString());
        Assert.Equal(2, report.RootElement.GetProperty("selectedChildren").GetArrayLength());
        Assert.Empty(report.RootElement.GetProperty("failures").EnumerateArray());
        Assert.Contains("succeeded=2", second.Output, StringComparison.Ordinal);
    }

    private static string ReadSources(string path) => string.Join("\n", Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

    private static async Task<(int ExitCode, string Output, string Errors)> RunAsync(params string[] arguments)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var executable = Path.Combine(SolutionRootLocator.Find(), "src", "AiNetCodeNavigator.AssemblyExport", "bin",
            configuration, "net10.0", "AiNetCodeNavigator.AssemblyExport" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        Assert.True(File.Exists(executable), executable);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
        return (process.ExitCode, await output, await errors);
    }
}
