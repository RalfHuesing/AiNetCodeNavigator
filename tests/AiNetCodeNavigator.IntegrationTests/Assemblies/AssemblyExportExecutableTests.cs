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
        var result = await RunAsync("--output", output, "--source", sources);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(output, "_misc", "RecursiveLibrary.dll", "export-manifest.json")));
        Assert.True(File.Exists(Path.Combine(output, "_misc", "RecursiveApplication.exe", "export-manifest.json")));
        Assert.Contains("class Library", ReadSources(Path.Combine(output, "_misc", "RecursiveLibrary.dll")), StringComparison.Ordinal);
        Assert.Contains("class App", ReadSources(Path.Combine(output, "_misc", "RecursiveApplication.exe")), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(output, "_misc", "native.dll")));
        Assert.False(File.Exists(Path.Combine(output, "last-run.json")));
        Assert.True(File.Exists(Path.Combine(output, "last-run.log")));
        Assert.Equal(2, Directory.GetFiles(output, "export-manifest.json", SearchOption.AllDirectories).Length);
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
        var result = await RunAsync("--include", "foo*.exe", "--output", output, "--source", sources, "--include", "*bar*.dll");

        Assert.True(result.ExitCode == 0, result.Output + result.Errors);
        Assert.True(File.Exists(Path.Combine(output, "_misc", "fooApplication.exe", "export-manifest.json")));
        Assert.True(File.Exists(Path.Combine(output, "_misc", "mybarLibrary.dll", "export-manifest.json")));
        Assert.False(Directory.Exists(Path.Combine(output, "_misc", "UnrelatedLibrary.dll")));
        Assert.False(File.Exists(Path.Combine(output, "last-run.json")));
        Assert.Equal(2, Directory.GetFiles(output, "export-manifest.json", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Executable_ExportsDependencyAndReadableSolutionThenReplacesSelectedChildren()
    {
        using var temp = TestTempDirectory.Create("export-executable-");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "CliDependency", "namespace Vendor; public class Api { public int Read() => 42; }");
        var root = AssemblyTestHelper.EmitAssembly(temp, "CliRoot", "public class Root { public Vendor.Api OldMember = new(); }", dependency);
        var output = temp.GetPath("dump");
        var pattern = root;
        var inputBytes = await File.ReadAllBytesAsync(root);
        var dependencyBytes = await File.ReadAllBytesAsync(dependency);
        var first = await RunAsync("--output", output, "--source", pattern);
        Assert.True(first.ExitCode == 0, first.Output + first.Errors);
        Assert.Equal(inputBytes, await File.ReadAllBytesAsync(root));
        Assert.Equal(dependencyBytes, await File.ReadAllBytesAsync(dependency));
        var child = Path.Combine(output, "_misc", "CliRoot.dll");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(child, "export-manifest.json")));
        Assert.Equal(2, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(manifest.RootElement.TryGetProperty("sourceFiles", out _));
        var sourcesPath = manifest.RootElement.GetProperty("sourceFilesPath").GetString()!;
        Assert.Equal("source-files.json", sourcesPath);
        using var sources = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(child, sourcesPath)));
        Assert.Equal(manifest.RootElement.GetProperty("counts").GetProperty("sourceFiles").GetInt32(), sources.RootElement.GetArrayLength());
        var project = manifest.RootElement.GetProperty("projectPath").GetString()!;
        Assert.False(Path.IsPathRooted(project));
        Assert.True(File.Exists(Path.Combine(child, project)));
        var solution = Assert.Single(Directory.GetFiles(child, "*.sln"));
        Assert.Contains(project.Replace('/', '\\'), await File.ReadAllTextAsync(solution), StringComparison.Ordinal);
        Assert.Contains("OldMember", ReadSources(child), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(output, "_misc", "CliDependency.dll", "export-manifest.json")));
        using var dependencies = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(child, manifest.RootElement.GetProperty("dependenciesPath").GetString()!)));
        Assert.Contains(dependencies.RootElement.GetProperty("filteredEdges").EnumerateArray(), edge => edge.GetProperty("rule").GetString()!.StartsWith("prefix:System.", StringComparison.Ordinal));
        var stale = Path.Combine(child, "stale.cs");
        await File.WriteAllTextAsync(stale, "obsolete");
        var untouched = Path.Combine(output, "_misc", "Unselected.dll");
        Directory.CreateDirectory(untouched);
        await File.WriteAllTextAsync(Path.Combine(untouched, "keep.cs"), "keep");
        AssemblyTestHelper.EmitAssembly(temp, "CliRoot", "public class Root { public Vendor.Api NewMember = new(); }", dependency);
        var changedBytes = await File.ReadAllBytesAsync(root);
        var second = await RunAsync("--output", output, "--source", pattern);
        Assert.True(second.ExitCode == 0, second.Output + second.Errors);
        Assert.False(File.Exists(stale));
        Assert.Contains("NewMember", ReadSources(child), StringComparison.Ordinal);
        Assert.DoesNotContain("OldMember", ReadSources(child), StringComparison.Ordinal);
        Assert.Equal(changedBytes, await File.ReadAllBytesAsync(root));
        Assert.False(Directory.Exists(untouched));
        Assert.False(File.Exists(Path.Combine(output, "last-run.json")));
        Assert.Equal(2, Directory.GetFiles(output, "export-manifest.json", SearchOption.AllDirectories).Length);
        Assert.Contains("exported=2", second.Output, StringComparison.Ordinal);
    }

    private static string ReadSources(string path) => string.Join("\n", Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

    [Fact]
    public async Task Executable_ExcludesVendorRootAndDependencyAndKeepsReadableReferences()
    {
        using var temp = TestTempDirectory.Create("export-exclude-executable-");
        var leaf = AssemblyTestHelper.EmitAssembly(temp, "VendorLeaf", "namespace Vendor; public class Base { }");
        var vendor = AssemblyTestHelper.EmitAssembly(temp, "DevExpressA", "public class ThirdParty : Vendor.Base { }", leaf);
        var root = AssemblyTestHelper.EmitAssembly(temp, "OwnApp", "public class Root : ThirdParty { public int Value => 7; }", vendor, leaf);
        var sources = temp.CreateSubdirectory("sources");
        foreach (var path in new[] { leaf, vendor, root }) File.Move(path, Path.Combine(sources, Path.GetFileName(path)));
        root = Path.Combine(sources, Path.GetFileName(root));
        vendor = Path.Combine(sources, Path.GetFileName(vendor));
        var output = temp.GetPath("dump");
        var preview = await RunAsync("--output", output, "--source", sources, "--include", "Own*.dll",
            "--source", vendor, "--exclude", "devexpress?.DLL", "--dry-run");
        Assert.True(preview.ExitCode == 0, preview.Output + preview.Errors);
        Assert.Contains("DevExpressA.dll", preview.Output, StringComparison.Ordinal);
        Assert.Contains("exclude:devexpress?.DLL", preview.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));
        var result = await RunAsync("--output", output, "--source", sources, "--include", "Own*.dll",
            "--source", vendor, "--exclude", "devexpress?.DLL", "--exclude", "Unmatched*.dll");

        Assert.True(result.ExitCode == 0, result.Output + result.Errors);
        var manifestPath = Assert.Single(Directory.GetFiles(output, "export-manifest.json", SearchOption.AllDirectories));
        var child = Path.GetDirectoryName(manifestPath)!;
        Assert.Contains("class Root", ReadSources(child), StringComparison.Ordinal);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        Assert.Equal(root, manifest.RootElement.GetProperty("sourcePath").GetString());
        using var dependencies = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(child,
            manifest.RootElement.GetProperty("dependenciesPath").GetString()!)));
        Assert.Contains(dependencies.RootElement.GetProperty("filteredEdges").EnumerateArray(), edge =>
            edge.GetProperty("rule").GetString() == "exclude:devexpress?.DLL");
    }

    [Fact]
    public async Task Executable_DependencyModeNoneExportsOnlySelectedAssembly()
    {
        using var temp = TestTempDirectory.Create("export-none-executable-");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "VendorLibrary", "namespace Vendor; public class Api { }");
        var root = AssemblyTestHelper.EmitAssembly(temp, "OwnApp", "public class Root { public Vendor.Api Value = new(); }", dependency);
        var output = temp.GetPath("dump");
        var result = await RunAsync("--dependencies", "none", "--source", root, "--output", output);

        Assert.True(result.ExitCode == 0, result.Output + result.Errors);
        var child = Path.GetDirectoryName(Assert.Single(Directory.GetFiles(output, "export-manifest.json", SearchOption.AllDirectories)))!;
        var sources = ReadSources(child);
        Assert.Contains("using Vendor;", sources, StringComparison.Ordinal);
        Assert.Contains("public class Root", sources, StringComparison.Ordinal);
        Assert.Contains("public Api Value", sources, StringComparison.Ordinal);
        using var dependencies = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(child, "dependencies.json")));
        Assert.Contains(dependencies.RootElement.GetProperty("dependencies").EnumerateArray(), edge =>
            edge.GetProperty("name").GetString() == "VendorLibrary"
            && edge.GetProperty("childRelativePath").ValueKind == JsonValueKind.Null);
        Assert.Contains(dependencies.RootElement.GetProperty("filteredEdges").EnumerateArray(), edge =>
            edge.GetProperty("rule").GetString() == "dependencies:none"
            && edge.GetProperty("reference").GetProperty("name").GetString() == "VendorLibrary"
            && edge.GetProperty("reference").GetProperty("resolved").GetBoolean()
            && edge.GetProperty("reference").GetProperty("resolvedPath").GetString() == dependency);
    }

    [Fact]
    public async Task Executable_DryRunPreservesExistingDumpAndDoesNotCreateAbsentDump()
    {
        using var temp = TestTempDirectory.Create("export-dry-executable-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "OwnApp", "public class Root { }");
        var output = temp.GetPath("dump");
        var initialFiles = Snapshot(temp.DirectoryPath);
        var absent = await RunAsync("--output", output, "--source", source, "--dry-run");
        Assert.True(absent.ExitCode == 0, absent.Output + absent.Errors);
        Assert.Contains("OwnApp.dll", absent.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));
        Assert.Equal(initialFiles, Snapshot(temp.DirectoryPath));

        var export = await RunAsync("--output", output, "--source", source);
        Assert.True(export.ExitCode == 0, export.Output + export.Errors);
        await File.WriteAllTextAsync(Path.Combine(output, "sentinel.txt"), "preserved");
        var snapshot = Snapshot(output);
        var existing = await RunAsync("--output", output, "--source", source, "--dry-run", "--exclude", "NoMatch*.dll");
        Assert.True(existing.ExitCode == 0, existing.Output + existing.Errors);
        Assert.Equal(snapshot, Snapshot(output));
    }

    [Fact]
    public async Task Executable_EmptySelectionAndInvalidParametersPreserveMarkedDump()
    {
        using var temp = TestTempDirectory.Create("export-invalid-executable-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "OwnApp", "public class Root { }");
        var sources = temp.CreateSubdirectory("sources");
        File.Move(source, Path.Combine(sources, Path.GetFileName(source)));
        source = Path.Combine(sources, Path.GetFileName(source));
        var output = temp.GetPath("dump");
        var initial = await RunAsync("--output", output, "--source", source);
        Assert.True(initial.ExitCode == 0, initial.Output + initial.Errors);
        await File.WriteAllTextAsync(Path.Combine(output, "sentinel.txt"), "preserved");
        var snapshot = Snapshot(output);
        string[][] invalidArguments =
        [
            ["--output", output, "--source", source, "--exclude", "*.dll"],
            ["--output", output, "--source", Path.GetDirectoryName(source)!, "--include", "Absent*.dll"],
            ["--output", output, "--source", source, "--dependencies", "invalid"],
            ["--output", output, "--source", source, "--unknown"],
            ["--output", output, "--source", source, "--include", "nested/*.dll"],
            ["--output", output, "--source", source, "--exclude"],
            ["--output", output, "--output", output, "--source", source],
            [output, source],
        ];
        foreach (var arguments in invalidArguments)
        {
            var result = await RunAsync(arguments);
            Assert.Equal(2, result.ExitCode);
            Assert.False(string.IsNullOrWhiteSpace(result.Errors));
            Assert.Equal(snapshot, Snapshot(output));
        }
    }

    private static Dictionary<string, string> Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(directory, path), path => Convert.ToBase64String(File.ReadAllBytes(path)), StringComparer.Ordinal);

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
