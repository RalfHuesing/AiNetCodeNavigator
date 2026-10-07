using System.Text.Json;
using AiNetCodeNavigator.AssemblyExport;
using AiNetCodeNavigator.Core.Assemblies;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class ExportRunnerTests
{
    [Fact]
    public async Task Runner_NonfatalDecompilerExceptionFailsOnlyItsChild()
    {
        using var temp = TestTempDirectory.Create("export-unsupported-child-");
        var failed = AssemblyTestHelper.EmitAssembly(temp, "AFails", "public class Unsupported { }");
        var successful = AssemblyTestHelper.EmitAssembly(temp, "ZWorks", "public class Independent { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [failed, successful]));
        new ExportDumpOwnership(plan).ResetRoot();
        var failedChild = plan.Assemblies.Single(item => item.SourcePath == failed).ChildPath;
        Directory.CreateDirectory(failedChild);
        await File.WriteAllTextAsync(Path.Combine(failedChild, "stale.cs"), "stale");
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(1, await ExportRunner.RunAsync(plan, output, errors, export: (item, stage, token) =>
            item.SourcePath == failed ? Task.FromException<AssemblyProjectExportResult>(new NotSupportedException("Unsupported decompiler construct."))
                : AssemblyProjectExporter.ExportAsync(item.SourcePath, stage, item.Identity, item.ContentHash, item.DecompilationReferences, token)));
        Assert.False(Directory.Exists(failedChild));
        Assert.True(File.Exists(Path.Combine(plan.OutputDirectory, "_misc", "ZWorks.dll", "export-manifest.json")));
        Assert.False(File.Exists(Path.Combine(plan.OutputDirectory, "last-run.json")));
        Assert.False(Directory.Exists(Path.Combine(plan.OutputDirectory, ".assembly-export-tmp")));
        var log = await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "last-run.log"));
        Assert.Contains("FAILURE", log, StringComparison.Ordinal);
        Assert.Contains("RUN FAILED", log, StringComparison.Ordinal);
        Assert.Contains("Unsupported decompiler construct", errors.ToString(), StringComparison.Ordinal);
        using var catalog = ReadCatalog(plan);
        Assert.Equal("failed", catalog.RootElement.GetProperty("runState").GetString());
        Assert.Equal(1, catalog.RootElement.GetProperty("failed").GetInt32());
        var published = Assert.Single(catalog.RootElement.GetProperty("rows").EnumerateArray());
        Assert.Equal("ZWorks", published[0].GetString());
    }

    [Fact]
    public async Task Runner_BoundsWorkersAndContinuesAfterOneExportFailure()
    {
        using var temp = TestTempDirectory.Create("export-bounded-workers-");
        var first = AssemblyTestHelper.EmitAssembly(temp, "Alpha", "public class Alpha { }");
        var second = AssemblyTestHelper.EmitAssembly(temp, "Beta", "public class Beta { }");
        var third = AssemblyTestHelper.EmitAssembly(temp, "Gamma", "public class Gamma { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [first, second, third]));
        var pairStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var active = 0;
        var maxActive = 0;
        using var output = new StringWriter();
        using var errors = new StringWriter();

        var exitCode = await ExportRunner.RunAsync(plan, output, errors, export: async (item, stage, token) =>
        {
            var current = Interlocked.Increment(ref active);
            while (true)
            {
                var observed = Volatile.Read(ref maxActive);
                if (current <= observed || Interlocked.CompareExchange(ref maxActive, current, observed) == observed) break;
            }
            if (Interlocked.Increment(ref started) >= 2) pairStarted.TrySetResult();
            try
            {
                await pairStarted.Task.WaitAsync(token);
                if (item.Identity.Name == "Beta") throw new NotSupportedException("Unsupported test construct.");
                var name = item.Identity.Name;
                await File.WriteAllTextAsync(Path.Combine(stage, name + ".csproj"), "<Project />", token);
                await File.WriteAllTextAsync(Path.Combine(stage, name + ".cs"), "public class C { }", token);
                return new(true, name + ".csproj", [name + ".cs"], item.ContentHash, "test", []);
            }
            finally { Interlocked.Decrement(ref active); }
        }, maxDegreeOfParallelism: 2);

        Assert.Equal(1, exitCode);
        Assert.Equal(2, maxActive);
        Assert.Equal(3, started);
        Assert.True(File.Exists(Path.Combine(plan.OutputDirectory, "_misc", "Alpha.dll", "export-manifest.json")));
        Assert.False(Directory.Exists(Path.Combine(plan.OutputDirectory, "_misc", "Beta.dll")));
        Assert.True(File.Exists(Path.Combine(plan.OutputDirectory, "_misc", "Gamma.dll", "export-manifest.json")));
        Assert.Contains("Unsupported test construct", errors.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(plan.OutputDirectory, ".assembly-export-tmp")));
    }

    [Fact]
    public async Task Runner_ResetsOwnedOutputAndWritesShortLogWithoutRunReport()
    {
        using var temp = TestTempDirectory.Create("export-short-log-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Entry", "public class Entry { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        using var output = new StringWriter();
        using var errors = new StringWriter();
        async Task<AssemblyProjectExportResult> Export(PlannedAssembly item, string stage, CancellationToken token)
        {
            await File.WriteAllTextAsync(Path.Combine(stage, "Entry.csproj"), "<Project />", token);
            await File.WriteAllTextAsync(Path.Combine(stage, "Entry.cs"), "public class Entry { }", token);
            return new(true, "Entry.csproj", ["Entry.cs"], item.ContentHash, "test", []);
        }

        Assert.Equal(0, await ExportRunner.RunAsync(plan, output, errors, export: Export));
        using var firstCatalog = ReadCatalog(plan);
        var firstRunId = firstCatalog.RootElement.GetProperty("runId").GetString();
        var stale = Path.Combine(plan.OutputDirectory, "stale.txt");
        await File.WriteAllTextAsync(stale, "old content");
        output.GetStringBuilder().Clear();
        errors.GetStringBuilder().Clear();

        Assert.Equal(0, await ExportRunner.RunAsync(plan, output, errors, export: Export));

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(Path.Combine(plan.OutputDirectory, ExportDumpOwnership.MarkerName)));
        Assert.False(File.Exists(Path.Combine(plan.OutputDirectory, "last-run.json")));
        Assert.False(Directory.Exists(Path.Combine(plan.OutputDirectory, ".assembly-export-tmp")));
        var logLines = (await File.ReadAllLinesAsync(Path.Combine(plan.OutputDirectory, "last-run.log")))
            .Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        Assert.Equal(4, logLines.Length);
        Assert.StartsWith("RUN START", logLines[0], StringComparison.Ordinal);
        Assert.EndsWith("RUN COMPLETE exported=1 partial=0 failed=0", logLines[^1], StringComparison.Ordinal);
        Assert.Equal(output.ToString(), string.Join(Environment.NewLine, logLines) + Environment.NewLine);
        Assert.Empty(errors.ToString());
        using var catalog = ReadCatalog(plan);
        Assert.NotEqual(firstRunId, catalog.RootElement.GetProperty("runId").GetString());
        Assert.Equal("complete", catalog.RootElement.GetProperty("runState").GetString());
        Assert.Equal(1, catalog.RootElement.GetProperty("exported").GetInt32());
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(plan.Assemblies[0].ChildPath, "export-manifest.json")));
        Assert.Equal(manifest.RootElement.GetProperty("runId").GetString(), catalog.RootElement.GetProperty("runId").GetString());
        Assert.Equal(2, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, manifest.RootElement.GetProperty("counts").GetProperty("sourceFiles").GetInt32());
        Assert.False(manifest.RootElement.TryGetProperty("sourceFiles", out _));
        Assert.False(manifest.RootElement.TryGetProperty("dependencies", out _));
        Assert.False(manifest.RootElement.TryGetProperty("diagnostics", out _));
        Assert.False(manifest.RootElement.TryGetProperty("filteredEdges", out _));
        using var sources = ReadDetail(plan.Assemblies[0].ChildPath, manifest, "sourceFilesPath");
        Assert.Equal("Entry.cs", Assert.Single(sources.RootElement.EnumerateArray()).GetString());
        Assert.Equal(JsonValueKind.Null, manifest.RootElement.GetProperty("diagnosticsPath").ValueKind);
        Assert.False(File.Exists(Path.Combine(plan.Assemblies[0].ChildPath, "diagnostics.json")));
        var readme = await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "README.md"));
        Assert.Contains("assemblies.json", readme, StringComparison.Ordinal);
        Assert.Contains("confirm catalog membership", readme, StringComparison.Ordinal);
        Assert.Contains("rg --files -g '*.cs'", readme, StringComparison.Ordinal);
        Assert.Contains("INPUT FAILURE", readme, StringComparison.Ordinal);
        Assert.Contains("Select-Object -First", readme, StringComparison.Ordinal);
        Assert.Contains("Type names alone do not prove table names", readme, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_ReportsInputFailureAndReplacesOldChildWhileContinuing()
    {
        using var temp = TestTempDirectory.Create("export-best-effort-preflight-");
        var blocked = AssemblyTestHelper.EmitAssembly(temp, "Blocked", "public class Blocked { }");
        var usable = AssemblyTestHelper.EmitAssembly(temp, "Usable", "public class Usable { }");
        var outputDirectory = temp.GetPath("dump");
        var missingPattern = Path.Combine(temp.GetPath("missing-source"), "missing*.dll");
        var plan = ExportPlanner.Create(new(outputDirectory, [blocked, usable, missingPattern]));
        new ExportDumpOwnership(plan).ResetRoot();
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "Blocked.dll"), "old content");
        plan = ExportPlanner.Create(plan.Arguments);
        using var output = new StringWriter();
        using var errors = new StringWriter();

        var exitCode = await ExportRunner.RunAsync(plan, output, errors, export: async (item, stage, _) =>
        {
            var name = item.Identity.Name;
            await File.WriteAllTextAsync(Path.Combine(stage, name + ".csproj"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(stage, name + ".cs"), "public class Generated { }");
            return new(true, name + ".csproj", [name + ".cs"], item.ContentHash, "test", []);
        });

        Assert.Equal(1, exitCode);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "_misc", "Usable.dll", "export-manifest.json")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "last-run.json")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "_misc", "Blocked.dll", "export-manifest.json")));
        Assert.Contains("missing*.dll", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_ExportsPartialClosureRootsAndTheirProvenDependenciesButReturnsFailure()
    {
        using var temp = TestTempDirectory.Create("export-incomplete-closure-");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "ProvenDependency", "public class Dependency { }");
        var root = AssemblyTestHelper.EmitAssembly(temp, "PartialRoot", "public class Root { }");
        AssemblyExportReferenceClosure Resolve(string path, Func<AssemblyReferenceDto, bool> _)
        {
            var identity = System.Reflection.AssemblyName.GetAssemblyName(path);
            if (path == root)
            {
                var edge = new AssemblyReferenceDto("ProvenDependency", "1.0.0.0", "neutral", true, dependency,
                    ResolutionProvenance: "adjacent");
                return new(new(identity.Name!, identity.Version!.ToString(), "neutral", ""), [edge],
                    [new("reference-limit", "Reference traversal stopped at its configured limit.", true)], false);
            }
            return new(new(identity.Name!, identity.Version!.ToString(), "neutral", ""), [], [], true);
        }
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [root]), Resolve);
        var exported = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var output = new StringWriter();
        using var errors = new StringWriter();

        var exitCode = await ExportRunner.RunAsync(plan, output, errors, export: async (item, stage, _) =>
        {
            exported.Add(item.SourcePath);
            var name = item.Identity.Name;
            await File.WriteAllTextAsync(Path.Combine(stage, name + ".csproj"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(stage, name + ".cs"), "public class Exported { }");
            return new(true, name + ".csproj", [name + ".cs"], item.ContentHash, "test", []);
        });

        Assert.Equal(1, exitCode);
        Assert.Contains(root, exported);
        Assert.Contains(dependency, exported);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "_misc", "PartialRoot.dll", "export-manifest.json")));
        Assert.Equal("partial", manifest.RootElement.GetProperty("completionState").GetString());
        using var diagnosticsDetail = ReadDetail(plan.Assemblies.Single(item => item.SourcePath == root).ChildPath, manifest, "diagnosticsPath");
        Assert.Contains("reference-limit", diagnosticsDetail.RootElement.GetProperty("referenceClosure").EnumerateArray()
            .Select(item => item.GetProperty("code").GetString()));
        Assert.False(File.Exists(Path.Combine(plan.OutputDirectory, "last-run.json")));
        Assert.Contains("RUN FAILED", await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "last-run.log")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_ExportsSharedDependencyOnceAndReportsItsRelativeChildPath()
    {
        using var temp = TestTempDirectory.Create("export-shared-dependency-");
        var first = AssemblyTestHelper.EmitAssembly(temp, "FirstRoot", "public class First { }");
        var second = AssemblyTestHelper.EmitAssembly(temp, "SecondRoot", "public class Second { }");
        var shared = AssemblyTestHelper.EmitAssembly(temp, "SharedVendor", "public class Shared { }");
        AssemblyExportReferenceClosure Resolve(string path, Func<AssemblyReferenceDto, bool> _)
        {
            var identity = System.Reflection.AssemblyName.GetAssemblyName(path);
            var references = path == first || path == second
                ? new[] { new AssemblyReferenceDto("SharedVendor", "1.0.0.0", "neutral", true, shared, ResolutionProvenance: "adjacent") }
                : [];
            return new(new(identity.Name!, identity.Version!.ToString(), "neutral", ""), references, [], true);
        }
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [first, second]), Resolve);
        var exported = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var output = new StringWriter();
        using var errors = new StringWriter();

        Assert.Equal(0, await ExportRunner.RunAsync(plan, output, errors, export: async (item, stage, _) =>
        {
            exported.Add(item.SourcePath);
            var name = item.Identity.Name;
            await File.WriteAllTextAsync(Path.Combine(stage, name + ".csproj"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(stage, name + ".cs"), "public class Exported { }");
            return new(true, name + ".csproj", [name + ".cs"], item.ContentHash, "test", []);
        }));

        Assert.Equal(3, exported.Count);
        Assert.Single(exported.Where(path => path == shared));
        using var rootManifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temp.GetPath("dump"), "_misc", "FirstRoot.dll", "export-manifest.json")));
        using var dependenciesDetail = ReadDetail(Path.Combine(temp.GetPath("dump"), "_misc", "FirstRoot.dll"), rootManifest, "dependenciesPath");
        var edge = Assert.Single(dependenciesDetail.RootElement.GetProperty("dependencies").EnumerateArray());
        Assert.Equal("SharedVendor", edge.GetProperty("name").GetString());
        Assert.Equal(Path.Combine("_misc", "SharedVendor.dll"), edge.GetProperty("childRelativePath").GetString());
    }

    [Fact]
    public async Task Runner_UsesNestedPlannedPathsInCatalogManifestAndDependencyDetails()
    {
        using var temp = TestTempDirectory.Create("export-nested-links-");
        var root = AssemblyTestHelper.EmitAssembly(temp, "Vendor.Core.Alpha.Root", "public class Root { }");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "Vendor.Core.Alpha.Dependency", "public class Dependency { }");
        var selected = new List<string> { root };
        for (var index = 0; index < 63; index++)
            selected.Add(AssemblyTestHelper.EmitAssembly(temp, $"Vendor.Core.Beta.Item{index:000}", "public class Sibling { }"));
        AssemblyExportReferenceClosure Resolve(string path, Func<AssemblyReferenceDto, bool> _)
        {
            var identity = System.Reflection.AssemblyName.GetAssemblyName(path);
            var references = path == root
                ? new[] { new AssemblyReferenceDto("Vendor.Core.Alpha.Dependency", "1.0.0.0", "neutral", true,
                    dependency, ResolutionProvenance: "adjacent") }
                : [];
            return new(new(identity.Name!, identity.Version!.ToString(), "neutral", ""), references, [], true);
        }

        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), selected), Resolve);
        var rootItem = plan.Assemblies.Single(item => item.SourcePath == root);
        var dependencyItem = plan.Assemblies.Single(item => item.SourcePath == dependency);
        Assert.Equal(Path.GetDirectoryName(rootItem.ChildRelativePath), Path.GetDirectoryName(dependencyItem.ChildRelativePath));
        Assert.NotEqual("Vendor", Path.GetDirectoryName(rootItem.ChildRelativePath));
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(0, await ExportRunner.RunAsync(plan, output, errors, export: async (item, stage, token) =>
        {
            var project = item.Identity.Name + ".csproj";
            await File.WriteAllTextAsync(Path.Combine(stage, project), "<Project />", token);
            await File.WriteAllTextAsync(Path.Combine(stage, "Source.cs"), "public class Source { }", token);
            return new(true, project, ["Source.cs"], item.ContentHash, "test", []);
        }));

        using var catalog = ReadCatalog(plan);
        var catalogPaths = catalog.RootElement.GetProperty("rows").EnumerateArray()
            .Select(row => row[4].GetString()).ToArray();
        Assert.Contains(rootItem.ChildRelativePath, catalogPaths);
        Assert.Contains(dependencyItem.ChildRelativePath, catalogPaths);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(rootItem.ChildPath, "export-manifest.json")));
        Assert.Equal(rootItem.ChildRelativePath, manifest.RootElement.GetProperty("childRelativePath").GetString());
        using var details = ReadDetail(rootItem.ChildPath, manifest, "dependenciesPath");
        Assert.Equal(dependencyItem.ChildRelativePath,
            Assert.Single(details.RootElement.GetProperty("dependencies").EnumerateArray())
                .GetProperty("childRelativePath").GetString());
    }

    [Fact]
    public async Task Runner_PublishesUsablePartialOutputWithSyntaxAndEmptySourceDiagnostics()
    {
        using var temp = TestTempDirectory.Create("export-partial-output-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Partial", "public class Partial { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var diagnostics = new[]
        {
            new AssemblyExportReferenceDiagnostic("syntax-warning", "Broken.cs contains CS1525.", true),
            new AssemblyExportReferenceDiagnostic("empty-source-warning", "Empty.cs is empty.", false),
        };

        var exitCode = await ExportRunner.RunAsync(plan, output, errors, export: async (item, stage, _) =>
        {
            await File.WriteAllTextAsync(Path.Combine(stage, "Partial.csproj"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(stage, "Broken.cs"), "public class Broken { void M() { ref } }");
            await File.WriteAllTextAsync(Path.Combine(stage, "Empty.cs"), string.Empty);
            return new(false, "Partial.csproj", ["Broken.cs", "Empty.cs"], item.ContentHash, "test", diagnostics);
        });

        var child = Path.Combine(plan.OutputDirectory, "_misc", "Partial.dll");
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(child, "Broken.cs")));
        Assert.True(File.Exists(Path.Combine(child, "Empty.cs")));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(child, "export-manifest.json")));
        Assert.Equal("partial", manifest.RootElement.GetProperty("completionState").GetString());
        using var diagnosticsDetail = ReadDetail(child, manifest, "diagnosticsPath");
        Assert.Equal(2, manifest.RootElement.GetProperty("counts").GetProperty("decompilationDiagnostics").GetInt32());
        Assert.Equal(2, diagnosticsDetail.RootElement.GetProperty("decompilation").GetArrayLength());
        Assert.False(File.Exists(Path.Combine(plan.OutputDirectory, "last-run.json")));
        Assert.Contains(diagnosticsDetail.RootElement.GetProperty("decompilation")
            .EnumerateArray(), item => item.GetProperty("message").GetString()!.Contains("CS1525", StringComparison.Ordinal));
        Assert.Empty(errors.ToString());
        using var catalog = ReadCatalog(plan);
        Assert.Equal("complete", catalog.RootElement.GetProperty("runState").GetString());
        Assert.Equal(1, catalog.RootElement.GetProperty("partial").GetInt32());
        Assert.Equal("partial", Assert.Single(catalog.RootElement.GetProperty("rows").EnumerateArray())[5].GetString());
    }

    [Theory]
    [InlineData("source-files.json")]
    [InlineData("export-manifest.json")]
    public async Task Runner_RejectsMetadataCollisionBeforePublishing(string reservedName)
    {
        using var temp = TestTempDirectory.Create("export-metadata-collision-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Root", "public class Root { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(1, await ExportRunner.RunAsync(plan, output, errors, export: async (item, stage, token) =>
        {
            await File.WriteAllTextAsync(Path.Combine(stage, "Root.csproj"), "<Project />", token);
            await File.WriteAllTextAsync(Path.Combine(stage, "Root.cs"), "public class Root { }", token);
            await File.WriteAllTextAsync(Path.Combine(stage, reservedName), "untrusted generated resource", token);
            return new(true, "Root.csproj", ["Root.cs"], item.ContentHash, "test", []);
        }));
        Assert.False(Directory.Exists(plan.Assemblies[0].ChildPath));
        using var catalog = ReadCatalog(plan);
        Assert.Empty(catalog.RootElement.GetProperty("rows").EnumerateArray());
    }

    [Fact]
    public async Task Runner_OmitsEmptyDependencyAndDiagnosticDetails()
    {
        using var temp = TestTempDirectory.Create("export-empty-details-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Root", "public class Root { }");
        AssemblyExportReferenceClosure Resolve(string _, Func<AssemblyReferenceDto, bool> __)
            => new(new("Root", "1.0.0.0", "neutral", ""), [], [], true);
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]), Resolve);
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(0, await ExportRunner.RunAsync(plan, output, errors, export: async (item, stage, token) =>
        {
            await File.WriteAllTextAsync(Path.Combine(stage, "Root.csproj"), "<Project />", token);
            await File.WriteAllTextAsync(Path.Combine(stage, "Root.cs"), "public class Root { }", token);
            return new(true, "Root.csproj", ["Root.cs"], item.ContentHash, "test", []);
        }));
        var child = plan.Assemblies[0].ChildPath;
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(child, "export-manifest.json")));
        Assert.Equal(JsonValueKind.Null, manifest.RootElement.GetProperty("dependenciesPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, manifest.RootElement.GetProperty("diagnosticsPath").ValueKind);
        Assert.Equal(0, manifest.RootElement.GetProperty("counts").GetProperty("dependencies").GetInt32());
        Assert.False(File.Exists(Path.Combine(child, "dependencies.json")));
        Assert.False(File.Exists(Path.Combine(child, "diagnostics.json")));
        using var sources = ReadDetail(child, manifest, "sourceFilesPath");
        Assert.Equal("Root.cs", Assert.Single(sources.RootElement.EnumerateArray()).GetString());
    }

    [Theory]
    [InlineData("../outside.cs")]
    [InlineData("missing.cs")]
    [InlineData("folder.cs")]
    [InlineData("absolute")]
    public async Task Runner_RejectsInvalidGeneratedSourceBeforePublishing(string relativeSource)
    {
        using var temp = TestTempDirectory.Create("export-invalid-source-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Root", "public class Root { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(1, await ExportRunner.RunAsync(plan, output, errors, export: async (item, stage, _) =>
        {
            await File.WriteAllTextAsync(Path.Combine(stage, "Root.csproj"), "<Project />");
            await File.WriteAllTextAsync(Path.GetFullPath(Path.Combine(stage, "../outside.cs")), "public class Outside { }");
            if (relativeSource == "absolute") relativeSource = Path.GetFullPath(Path.Combine(stage, "../outside.cs"));
            Directory.CreateDirectory(Path.Combine(stage, "folder.cs"));
            return new(true, "Root.csproj", [relativeSource], item.ContentHash, "test", []);
        }));
        Assert.False(Directory.Exists(plan.Assemblies[0].ChildPath));
        Assert.False(Directory.Exists(Path.Combine(plan.OutputDirectory, ".assembly-export-tmp")));
        using var catalog = ReadCatalog(plan);
        Assert.Equal("failed", catalog.RootElement.GetProperty("runState").GetString());
        Assert.Empty(catalog.RootElement.GetProperty("rows").EnumerateArray());
    }

    [Fact]
    public async Task Runner_ExportsTemporaryGacDependencyWithProvenanceAndInputHashes()
    {
        using var temp = TestTempDirectory.Create("export-gac-project-");
        using var dependencyTemp = TestTempDirectory.Create("export-gac-dependency-");
        var dependency = AssemblyTestHelper.EmitAssembly(dependencyTemp, "VendorGac", "namespace Vendor; public class Api { public int Read() => 42; }");
        var root = AssemblyTestHelper.EmitAssembly(temp, "VendorRoot", "public class Root { public Vendor.Api Value = new(); }", dependency);
        var gac = temp.GetPath("gac");
        var installed = Path.Combine(gac, "GAC_MSIL", "VendorGac", "v4.0_0.0.0.0__", "VendorGac.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
        File.Copy(dependency, installed);
        AssemblyExportReferenceClosure Resolve(string path, Func<AssemblyReferenceDto, bool> traverse)
        {
            var result = new AssemblyReferenceResolver(new AssemblyGacCandidateSource([gac]), exportClosure: true,
                traverseReference: traverse, maxDepth: 128, maxNodes: 4096).Resolve(path);
            return new(result.Identity, result.References, result.Diagnostics.Select(item =>
                new AssemblyExportReferenceDiagnostic(item.Code, item.Message, item.Severity == AssemblyDiagnosticSeverity.Error)).ToArray(), result.IsComplete);
        }
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [root]), Resolve);
        var rootHash = await File.ReadAllBytesAsync(root);
        var dependencyHash = await File.ReadAllBytesAsync(installed);
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(0, await ExportRunner.RunAsync(plan, output, errors));
        var rootChild = Path.Combine(plan.OutputDirectory, "_misc", "VendorRoot.dll");
        var dependencyChild = Path.Combine(plan.OutputDirectory, "_misc", "VendorGac.dll");
        Assert.NotEmpty(Directory.GetFiles(dependencyChild, "*.cs", SearchOption.AllDirectories));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(rootChild, "export-manifest.json")));
        using var dependenciesDetail = ReadDetail(rootChild, manifest, "dependenciesPath");
        Assert.Contains(dependenciesDetail.RootElement.GetProperty("dependencies").EnumerateArray(), edge =>
            edge.GetProperty("name").GetString() == "VendorGac"
            && edge.GetProperty("resolutionProvenance").GetString() == "gac"
            && edge.GetProperty("childRelativePath").GetString() == Path.Combine("_misc", "VendorGac.dll"));
        Assert.Equal(rootHash, await File.ReadAllBytesAsync(root));
        Assert.Equal(dependencyHash, await File.ReadAllBytesAsync(installed));
    }

    [Fact]
    public async Task Runner_SourceDriftFailsChildRemovesOldTreeAndContinuesIndependentSibling()
    {
        using var temp = TestTempDirectory.Create("export-failed-child-");
        var failed = AssemblyTestHelper.EmitAssembly(temp, "AFails", "public class Before { }");
        var successful = AssemblyTestHelper.EmitAssembly(temp, "ZWorks", "public class Independent { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [failed, successful]));
        var owner = new ExportDumpOwnership(plan);
        owner.ResetRoot();
        var oldChild = plan.Assemblies.Single(item => item.SourcePath == failed).ChildPath;
        Directory.CreateDirectory(oldChild);
        await File.WriteAllTextAsync(Path.Combine(oldChild, "stale.cs"), "stale");
        // Same identity, changed bytes: an identity-only preflight-to-use check would miss this.
        AssemblyTestHelper.EmitAssembly(temp, "AFails", "public class After { }");
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(1, await ExportRunner.RunAsync(plan, output, errors));
        Assert.False(Directory.Exists(oldChild));
        Assert.True(File.Exists(Path.Combine(plan.OutputDirectory, "_misc", "ZWorks.dll", "export-manifest.json")));
        Assert.False(Directory.Exists(Path.Combine(plan.OutputDirectory, ".assembly-export-tmp")));
        Assert.False(File.Exists(Path.Combine(plan.OutputDirectory, "last-run.json")));
        Assert.Contains("Source content changed", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_RecordsUnresolvedDependenciesAsPartialPublishedResult()
    {
        using var temp = TestTempDirectory.Create("export-unresolved-");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "MissingVendor", "public class Base { }");
        var root = AssemblyTestHelper.EmitAssembly(temp, "PartialRoot", "public class Root : Base { }", dependency);
        File.Delete(dependency);
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [root]));
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(0, await ExportRunner.RunAsync(plan, output, errors));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "_misc", "PartialRoot.dll", "export-manifest.json")));
        Assert.Equal("partial", manifest.RootElement.GetProperty("completionState").GetString());
        using var dependenciesDetail = ReadDetail(plan.Assemblies.Single(item => item.SourcePath == root).ChildPath, manifest, "dependenciesPath");
        Assert.Contains(dependenciesDetail.RootElement.GetProperty("dependencies").EnumerateArray(), edge =>
            edge.GetProperty("name").GetString() == "MissingVendor"
            && edge.GetProperty("childRelativePath").ValueKind == JsonValueKind.Null
            && edge.GetProperty("diagnostic").GetString()!.Contains("MissingVendor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Runner_RecordsInterruptionAndCleansCurrentStage()
    {
        using var temp = TestTempDirectory.Create("export-interrupted-");
        var root = AssemblyTestHelper.EmitAssembly(temp, "Root", "public class Root { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [root]));
        using var cancellation = new CancellationTokenSource();
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(1, await ExportRunner.RunAsync(plan, output, errors, cancellation.Token, async (_, _, token) =>
        {
            await cancellation.CancelAsync();
            return await Task.FromCanceled<AssemblyProjectExportResult>(token);
        }));
        Assert.False(Directory.Exists(plan.Assemblies[0].ChildPath));
        Assert.False(Directory.Exists(Path.Combine(plan.OutputDirectory, ".assembly-export-tmp")));
        Assert.False(File.Exists(Path.Combine(plan.OutputDirectory, "last-run.json")));
        Assert.Contains("INTERRUPTED", await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "last-run.log")), StringComparison.Ordinal);
        using var catalog = ReadCatalog(plan);
        Assert.Equal("interrupted", catalog.RootElement.GetProperty("runState").GetString());
        Assert.Empty(catalog.RootElement.GetProperty("rows").EnumerateArray());
        Assert.True(File.Exists(Path.Combine(plan.OutputDirectory, "README.md")));
    }

    [Fact]
    public async Task Runner_CatalogIsRunningBeforePublicationAndOrdersRowsByRelativePath()
    {
        using var temp = TestTempDirectory.Create("export-catalog-order-");
        var first = AssemblyTestHelper.EmitAssembly(temp, "Zed.Library", "public class Zed { }");
        var second = AssemblyTestHelper.EmitAssembly(temp, "Alpha.Library", "public class Alpha { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [first, second]));
        // Force a plan order different from catalog order.
        plan = plan with { Assemblies = plan.Assemblies.OrderByDescending(item => item.ChildRelativePath, StringComparer.Ordinal).ToArray() };
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(0, await ExportRunner.RunAsync(plan, output, errors, export: async (item, stage, token) =>
        {
            using var runningCatalog = ReadCatalog(plan);
            Assert.Equal("running", runningCatalog.RootElement.GetProperty("runState").GetString());
            Assert.Empty(runningCatalog.RootElement.GetProperty("rows").EnumerateArray());
            Assert.True(File.Exists(Path.Combine(plan.OutputDirectory, "README.md")));
            await File.WriteAllTextAsync(Path.Combine(stage, "Library.csproj"), "<Project />", token);
            await File.WriteAllTextAsync(Path.Combine(stage, "Library.cs"), "public class Library { }", token);
            return new(true, "Library.csproj", ["Library.cs"], item.ContentHash, "test", []);
        }));
        using var catalog = ReadCatalog(plan);
        Assert.Equal(["name", "version", "culture", "publicKeyToken", "childRelativePath", "completionState"],
            catalog.RootElement.GetProperty("columns").EnumerateArray().Select(value => value.GetString()!).ToArray());
        var rows = catalog.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(["Alpha.Library", "Zed.Library"], rows.Select(row => row[0].GetString()!).ToArray());
        foreach (var row in rows)
        {
            var item = plan.Assemblies.Single(candidate => candidate.Identity.Name == row[0].GetString());
            Assert.Equal(item.Identity.Version, row[1].GetString());
            Assert.Equal(item.Identity.Culture, row[2].GetString());
            Assert.Equal(item.Identity.PublicKeyToken, row[3].GetString());
            Assert.Equal(item.ChildRelativePath, row[4].GetString());
            Assert.Equal("complete", row[5].GetString());
            Assert.Equal(6, row.GetArrayLength());
        }
        Assert.False(File.Exists(Path.Combine(plan.OutputDirectory, ".assemblies.json.tmp")));
    }

    private static JsonDocument ReadCatalog(ExportPlan plan) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(plan.OutputDirectory, "assemblies.json")));

    [Fact]
    public async Task Runner_CatalogFinalizationFailureKeepsRunningSnapshotAndDoesNotReportSuccess()
    {
        using var temp = TestTempDirectory.Create("export-catalog-write-failure-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Library", "public class Library { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var exception = await Record.ExceptionAsync(() => ExportRunner.RunAsync(plan, output, errors, export: async (item, stage, token) =>
        {
            Directory.CreateDirectory(Path.Combine(plan.OutputDirectory, ".assemblies.json.tmp"));
            await File.WriteAllTextAsync(Path.Combine(stage, "Library.csproj"), "<Project />", token);
            await File.WriteAllTextAsync(Path.Combine(stage, "Library.cs"), "public class Library { }", token);
            return new(true, "Library.csproj", ["Library.cs"], item.ContentHash, "test", []);
        }));
        Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
        using var catalog = ReadCatalog(plan);
        Assert.Equal("running", catalog.RootElement.GetProperty("runState").GetString());
        Assert.Empty(catalog.RootElement.GetProperty("rows").EnumerateArray());
        Assert.True(File.Exists(Path.Combine(plan.Assemblies[0].ChildPath, "export-manifest.json")));
        var log = await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "last-run.log"));
        Assert.DoesNotContain("RUN COMPLETE", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_PublishesAssemblyIntoOwnerSubdirectoryBasedOnDottedStem()
    {
        using var temp = TestTempDirectory.Create("export-owner-subfolder-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Vendor.Library.Core", "namespace Vendor.Library; public class Core { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(0, await ExportRunner.RunAsync(plan, output, errors, export: async (item, stage, _) =>
        {
            var name = item.Identity.Name;
            await File.WriteAllTextAsync(Path.Combine(stage, name + ".csproj"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(stage, name + ".cs"), "public class Core { }");
            return new(true, name + ".csproj", [name + ".cs"], item.ContentHash, "test", []);
        }));
        var expectedChild = Path.Combine(plan.OutputDirectory, "Vendor", "Vendor.Library.Core.dll");
        Assert.True(File.Exists(Path.Combine(expectedChild, "export-manifest.json")));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(expectedChild, "export-manifest.json")));
        Assert.Equal(Path.Combine("Vendor", "Vendor.Library.Core.dll"), manifest.RootElement.GetProperty("childRelativePath").GetString());
    }
    private static JsonDocument ReadDetail(string child, JsonDocument manifest, string pathProperty)
    {
        var relativePath = manifest.RootElement.GetProperty(pathProperty).GetString()!;
        Assert.False(Path.IsPathRooted(relativePath));
        Assert.Equal(Path.GetFileName(relativePath), relativePath);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(child, relativePath)));
    }
}
