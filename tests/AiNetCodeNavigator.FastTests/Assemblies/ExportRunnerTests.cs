using System.Text.Json;
using AiNetCodeNavigator.AssemblyExport;
using AiNetCodeNavigator.Core.Assemblies;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class ExportRunnerTests
{
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
