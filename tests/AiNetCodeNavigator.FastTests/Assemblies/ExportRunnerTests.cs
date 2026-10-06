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
        Assert.Contains("reference-limit", manifest.RootElement.GetProperty("diagnostics").GetProperty("referenceClosure").EnumerateArray()
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
        var edge = Assert.Single(rootManifest.RootElement.GetProperty("dependencies").EnumerateArray());
        Assert.Equal("SharedVendor", edge.GetProperty("name").GetString());
        Assert.Equal(Path.Combine("_misc", "SharedVendor.dll"), edge.GetProperty("childRelativePath").GetString());
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
        Assert.Equal(2, manifest.RootElement.GetProperty("diagnostics").GetProperty("decompilation").GetArrayLength());
        Assert.False(File.Exists(Path.Combine(plan.OutputDirectory, "last-run.json")));
        Assert.Contains(manifest.RootElement.GetProperty("diagnostics").GetProperty("decompilation")
            .EnumerateArray(), item => item.GetProperty("message").GetString()!.Contains("CS1525", StringComparison.Ordinal));
        Assert.Empty(errors.ToString());
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
        Assert.Contains(manifest.RootElement.GetProperty("dependencies").EnumerateArray(), edge =>
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
        Assert.Contains(manifest.RootElement.GetProperty("dependencies").EnumerateArray(), edge =>
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
}
