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
        new ExportDumpOwnership(plan).CreateOrValidateRoot();
        var failedChild = plan.Assemblies.Single(item => item.SourcePath == failed).ChildPath;
        Directory.CreateDirectory(failedChild);
        await File.WriteAllTextAsync(Path.Combine(failedChild, "stale.cs"), "stale");
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(1, await ExportRunner.RunAsync(plan, output, errors, export: (item, stage, token) =>
            item.SourcePath == failed ? Task.FromException<AssemblyProjectExportResult>(new NotSupportedException("Unsupported decompiler construct."))
                : AssemblyProjectExporter.ExportAsync(item.SourcePath, stage, item.Identity, item.ContentHash, item.DecompilationReferences, token)));
        Assert.False(Directory.Exists(failedChild));
        Assert.True(File.Exists(Path.Combine(plan.OutputDirectory, "ZWorks.dll", "export-manifest.json")));
        using var run = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "last-run.json")));
        Assert.Equal("failed", run.RootElement.GetProperty("completionState").GetString());
        Assert.Equal("AFails.dll", Assert.Single(run.RootElement.GetProperty("failures").EnumerateArray()).GetProperty("childName").GetString());
        Assert.Empty(Directory.GetDirectories(plan.OutputDirectory, ".assembly-export-stage-*"));
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
        var exported = new List<string>();
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
        using var rootManifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temp.GetPath("dump"), "FirstRoot.dll", "export-manifest.json")));
        var edge = Assert.Single(rootManifest.RootElement.GetProperty("dependencies").EnumerateArray());
        Assert.Equal("SharedVendor", edge.GetProperty("name").GetString());
        var link = Assert.Single(rootManifest.RootElement.GetProperty("dependencyChildren").EnumerateArray());
        Assert.Equal(0, link.GetProperty("referenceIndex").GetInt32());
        Assert.Equal("SharedVendor.dll", link.GetProperty("childRelativePath").GetString());
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

        var child = Path.Combine(plan.OutputDirectory, "Partial.dll");
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(child, "Broken.cs")));
        Assert.True(File.Exists(Path.Combine(child, "Empty.cs")));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(child, "export-manifest.json")));
        Assert.Equal("partial", manifest.RootElement.GetProperty("completionState").GetString());
        Assert.Equal(2, manifest.RootElement.GetProperty("diagnostics").GetArrayLength());
        using var run = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "last-run.json")));
        Assert.Equal("partial", run.RootElement.GetProperty("completionState").GetString());
        Assert.Equal("partial", Assert.Single(run.RootElement.GetProperty("selectedChildren").EnumerateArray())
            .GetProperty("state").GetString());
        Assert.Contains("CS1525", output.ToString(), StringComparison.Ordinal);
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
        Assert.Empty(Directory.GetDirectories(plan.OutputDirectory, ".assembly-export-stage-*"));
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
        var rootChild = Path.Combine(plan.OutputDirectory, "VendorRoot.dll");
        var dependencyChild = Path.Combine(plan.OutputDirectory, "VendorGac.dll");
        Assert.NotEmpty(Directory.GetFiles(dependencyChild, "*.cs", SearchOption.AllDirectories));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(rootChild, "export-manifest.json")));
        Assert.Contains(manifest.RootElement.GetProperty("dependencies").EnumerateArray(), edge =>
            edge.GetProperty("name").GetString() == "VendorGac" && edge.GetProperty("resolutionProvenance").GetString() == "gac");
        Assert.Contains("VendorGac.dll", manifest.RootElement.GetProperty("automaticallyExportedChildren").EnumerateArray().Select(item => item.GetString()));
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
        owner.CreateOrValidateRoot();
        var oldChild = plan.Assemblies.Single(item => item.SourcePath == failed).ChildPath;
        Directory.CreateDirectory(oldChild);
        await File.WriteAllTextAsync(Path.Combine(oldChild, "stale.cs"), "stale");
        // Same identity, changed bytes: an identity-only preflight-to-use check would miss this.
        AssemblyTestHelper.EmitAssembly(temp, "AFails", "public class After { }");
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(1, await ExportRunner.RunAsync(plan, output, errors));
        Assert.False(Directory.Exists(oldChild));
        Assert.True(File.Exists(Path.Combine(plan.OutputDirectory, "ZWorks.dll", "export-manifest.json")));
        Assert.Empty(Directory.GetDirectories(plan.OutputDirectory, ".assembly-export-stage-*"));
        using var run = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "last-run.json")));
        Assert.Equal("failed", run.RootElement.GetProperty("completionState").GetString());
        Assert.Equal(2, run.RootElement.GetProperty("selectedChildren").GetArrayLength());
        Assert.Single(run.RootElement.GetProperty("failures").EnumerateArray());
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
        using var run = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "last-run.json")));
        Assert.Equal("partial", run.RootElement.GetProperty("completionState").GetString());
        Assert.False(run.RootElement.GetProperty("isComplete").GetBoolean());
        Assert.Contains("MissingVendor", errors.ToString(), StringComparison.Ordinal);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "PartialRoot.dll", "export-manifest.json")));
        Assert.Contains(manifest.RootElement.GetProperty("unresolvedDependencies").EnumerateArray(), edge => edge.GetProperty("name").GetString() == "MissingVendor");
    }

    [Fact]
    public void Planner_RejectsRunReportDirectoryBeforeDeletingSelectedChild()
    {
        using var temp = TestTempDirectory.Create("export-report-path-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Root", "public class Root { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        new ExportDumpOwnership(plan).CreateOrValidateRoot();
        Directory.CreateDirectory(Path.Combine(plan.OutputDirectory, "last-run.json"));
        Assert.Throws<InvalidOperationException>(() => ExportPlanner.Create(plan.Arguments));
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
        Assert.Empty(Directory.GetDirectories(plan.OutputDirectory, ".assembly-export-stage-*"));
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "last-run.json")));
        Assert.Equal("interrupted", report.RootElement.GetProperty("completionState").GetString());
    }
}
