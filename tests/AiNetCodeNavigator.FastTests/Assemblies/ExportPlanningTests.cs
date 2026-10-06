using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using AiNetCodeNavigator.AssemblyExport;
using AiNetCodeNavigator.Core.Assemblies;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class ExportPlanningTests
{
    [Fact]
    public void Expand_SortsDeduplicatesAndExpandsMultipleFinalSegmentPatterns()
    {
        using var temp = TestTempDirectory.Create("export-input-");
        var second = Emit(temp.GetPath("B.dll"), "B");
        var first = Emit(temp.GetPath("A.dll"), "A");
        var other = Emit(temp.GetPath("nested/C.dll"), "C");
        Assert.Equal([first, second, other], ExportPlanner.Expand([temp.GetPath("?.dll"), temp.GetPath("nested/*.dll"), first]));
        Assert.Throws<ArgumentException>(() => ExportPlanner.Expand([temp.GetPath("*/A.dll")]));
        Assert.Throws<ArgumentException>(() => ExportPlanner.Expand([temp.GetPath("**.dll")]));
        Assert.Throws<ArgumentException>(() => ExportPlanner.Expand([temp.GetPath("Absent*.dll")]));
        File.WriteAllText(temp.GetPath("native.dll"), "not managed");
        Assert.Throws<BadImageFormatException>(() => ExportPlanner.Expand([temp.GetPath("native.dll")]));
        File.Copy(first, temp.GetPath("A.exe"));
        Assert.Equal([temp.GetPath("A.exe")], ExportPlanner.Expand([temp.GetPath("A.exe")]));
    }

    [Fact]
    public void Expand_RecursesDirectoriesAndFilenamePatternsAcrossManagedDllsAndExecutables()
    {
        using var temp = TestTempDirectory.Create("export-recursive-input-");
        var sourceDirectory = temp.GetPath("sources");
        var rootDll = Emit(Path.Combine(sourceDirectory, "Root.dll"), "Root");
        var nestedDll = Emit(Path.Combine(sourceDirectory, "nested", "More.dll"), "More");
        var deepExe = Emit(Path.Combine(sourceDirectory, "nested", "deep", "Tool.exe"), "Tool");
        File.WriteAllText(Path.Combine(sourceDirectory, "native.dll"), "not managed");
        File.WriteAllText(Path.Combine(sourceDirectory, "nested", "notes.txt"), "ignored");

        Assert.Equal(new[] { rootDll, nestedDll, deepExe }.Order(StringComparer.OrdinalIgnoreCase),
            ExportPlanner.Expand([sourceDirectory]));
        Assert.Equal(new[] { rootDll, nestedDll }.Order(StringComparer.OrdinalIgnoreCase),
            ExportPlanner.Expand([Path.Combine(sourceDirectory, "*.dll")]));
        Assert.Equal([deepExe], ExportPlanner.Expand([Path.Combine(sourceDirectory, "*.exe")]));
        Assert.Throws<ArgumentException>(() => ExportPlanner.Expand([Path.Combine(sourceDirectory, "native*.dll")]));
    }

    [Fact]
    public void Expand_AppliesMultipleBareFilenamePatternsToPrecedingDirectoryRecursively()
    {
        using var temp = TestTempDirectory.Create("export-filtered-directory-");
        var sources = temp.GetPath("sources");
        var fooExe = Emit(Path.Combine(sources, "nested", "fooTool.exe"), "FooTool");
        var barDll = Emit(Path.Combine(sources, "deep", "nested", "mybarPlugin.dll"), "BarPlugin");
        Emit(Path.Combine(sources, "unmatched.exe"), "UnmatchedExe");
        Emit(Path.Combine(sources, "nested", "unmatched.dll"), "UnmatchedDll");
        File.WriteAllText(Path.Combine(sources, "nativebar.dll"), "native");

        Assert.Equal(new[] { fooExe, barDll }.Order(StringComparer.OrdinalIgnoreCase),
            ExportPlanner.Expand([sources, "foo*.exe", "*bar*.dll"]));
        Assert.Throws<ArgumentException>(() => ExportPlanner.Expand([sources, "absent*.exe"]));
    }

    [Fact]
    public void Expand_RejectsReparseDirectoryInsteadOfFollowingItOutsideSourceRoot()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = TestTempDirectory.Create("export-recursive-junction-");
        var sources = temp.GetPath("sources");
        var outside = temp.GetPath("outside");
        Directory.CreateDirectory(sources);
        Emit(Path.Combine(outside, "External.dll"), "External");
        var local = Emit(Path.Combine(sources, "Local.dll"), "Local");
        var link = Path.Combine(sources, "redirect");
        CreateJunction(link, outside);
        try
        {
            Assert.Throws<InvalidOperationException>(() => ExportPlanner.Expand([sources]));
            var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [sources]));
            Assert.Contains(plan.Assemblies, item => item.SourcePath == local);
            Assert.Contains(plan.Issues, issue => issue.Error.Contains("Reparse-point source directory", StringComparison.Ordinal));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void Plan_FollowsThirdPartyGacAndRuntimeEdgesFiltersSystemAndKeepsExplicitOverride()
    {
        using var temp = TestTempDirectory.Create("export-closure-plan-");
        var root = Emit(temp.GetPath("Root.dll"), "Root", ["Vendor.Local", "Vendor.Gac", "Vendor.Runtime", "System.Hidden"]);
        var local = Emit(temp.GetPath("Vendor.Local.dll"), "Vendor.Local");
        var gacRoot = temp.GetPath("gac");
        var gac = Emit(Path.Combine(gacRoot, "GAC_MSIL/Vendor.Gac/1/Vendor.Gac.dll"), "Vendor.Gac");
        var runtime = Emit(temp.GetPath("runtime/Vendor.Runtime.dll"), "Vendor.Runtime");
        var system = Emit(temp.GetPath("System.Hidden.dll"), "System.Hidden", ["Explicit.Dependency"]);
        var explicitDependency = Emit(temp.GetPath("Explicit.Dependency.dll"), "Explicit.Dependency");
        AssemblyExportReferenceClosure Resolve(string path, Func<AssemblyReferenceDto, bool> traverse)
        {
            var result = new AssemblyReferenceResolver(new AssemblyGacCandidateSource([gacRoot]), [runtime],
                exportClosure: true, traverseReference: traverse).Resolve(path);
            return new(result.Identity, result.References, [], result.IsComplete);
        }
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [root]), Resolve);
        Assert.Equal(4, plan.Assemblies.Count);
        Assert.Equal(new[] { root, local, gac, runtime }.Order(StringComparer.OrdinalIgnoreCase),
            plan.Assemblies.Select(item => item.SourcePath).Order(StringComparer.OrdinalIgnoreCase));
        var filtered = Assert.Single(plan.Assemblies.Single(item => item.SourcePath == root).FilteredReferences);
        Assert.Equal("prefix:System.", filtered.Rule);
        Assert.Equal(system, filtered.Reference.ResolvedPath);
        Assert.DoesNotContain(plan.Assemblies.SelectMany(item => item.Closure.References), edge => edge.Name == "Explicit.Dependency");
        var overridden = ExportPlanner.Create(new(temp.GetPath("dump"), [root, system]), Resolve);
        Assert.Contains(overridden.Assemblies, item => item.SourcePath == system && item.IsExplicit);
        Assert.Contains(overridden.Assemblies, item => item.SourcePath == explicitDependency);
        Assert.False(Directory.Exists(plan.OutputDirectory));
    }

    [Theory]
    [InlineData("MICROSOFT", "simple-name:Microsoft")]
    [InlineData("Windows.Native", "prefix:Windows.")]
    [InlineData("UIAutomationProvider", "simple-name:UIAutomationProvider")]
    [InlineData("Systematic", null)]
    [InlineData("MicrosoftVendor", null)]
    public void Filter_UsesOnlyDocumentedNameRules(string name, string? rule) => Assert.Equal(rule, AutomaticExportFilter.Match(name));

    [Fact]
    public void Plan_DeduplicatesProvenIdentityAndKeepsByteDistinctVariants()
    {
        using var temp = TestTempDirectory.Create("export-preflight-");
        var source = Emit(temp.GetPath("one/Shared.dll"), "Shared");
        var alias = temp.GetPath("Alias.dll");
        File.Copy(source, alias);
        var sameNameAlias = temp.GetPath("two/Shared.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(sameNameAlias)!);
        File.Copy(source, sameNameAlias);
        var output = temp.GetPath("dump");
        Assert.Single(ExportPlanner.Create(new(output, [source, alias, sameNameAlias])).Assemblies);
        var collision = Emit(temp.GetPath("two/Shared.dll"), "Different", version: new Version(2, 0, 0, 0));
        var collisionPlan = ExportPlanner.Create(new(output, [source, collision]));
        Assert.Equal(2, collisionPlan.Assemblies.Count);
        Assert.All(collisionPlan.Assemblies, item => Assert.Equal(Path.Combine(output, "Shared.dll"), Path.GetDirectoryName(item.ChildPath)));
        Assert.Equal(collisionPlan.Assemblies.Select(item => item.ChildPath).Distinct(StringComparer.OrdinalIgnoreCase).Count(), 2);
        Assert.All(collisionPlan.Assemblies, item => Assert.StartsWith(Path.Combine("Shared.dll", "local-"), item.ChildRelativePath, StringComparison.OrdinalIgnoreCase));
        var conflictingIdentity = Emit(temp.GetPath("DifferentBytes.dll"), "Shared");
        Directory.CreateDirectory(temp.GetPath("two"));
        File.Copy(conflictingIdentity, temp.GetPath("two/Shared.dll"), overwrite: true);
        var equalVersionDifferentBytes = ExportPlanner.Create(new(output, [source, temp.GetPath("two/Shared.dll")]));
        Assert.Equal(2, equalVersionDifferentBytes.Assemblies.Count);
        var incomplete = ExportPlanner.Create(new(output, [source]),
            (_, _) => new(new("Shared", "1.0.0.0", "neutral", ""), [], [], false));
        Assert.Contains(incomplete.Issues, issue => issue.SourcePath == source && issue.Error.Contains("Incomplete reference closure", StringComparison.Ordinal));
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void Plan_PatternSelectionKeepsHighestVersionAndClosureKeepsReferencedOlderVersion()
    {
        using var temp = TestTempDirectory.Create("export-pattern-versions-");
        var sources = temp.GetPath("sources");
        var older = Emit(Path.Combine(sources, "old", "Shared.dll"), "Shared", version: new Version(1, 0, 0, 0));
        var newer = Emit(Path.Combine(sources, "new", "Shared.dll"), "Shared", version: new Version(2, 0, 0, 0));
        var root = Emit(Path.Combine(sources, "Root.dll"), "Root", ["Shared"], new Version(1, 0, 0, 0));
        AssemblyExportReferenceClosure Resolve(string path, Func<AssemblyReferenceDto, bool> _)
        {
            var identity = AssemblyName.GetAssemblyName(path);
            var references = path == root
                ? new[] { new AssemblyReferenceDto("Shared", "1.0.0.0", "neutral", true, older, ResolutionProvenance: "adjacent") }
                : [];
            return new(new(identity.Name!, identity.Version!.ToString(), "neutral", ""), references, [], true);
        }

        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [sources, "*.dll"]), Resolve);

        Assert.Contains(plan.Assemblies, item => item.SourcePath == root && item.IsExplicit);
        Assert.Contains(plan.Assemblies, item => item.SourcePath == newer && item.IsExplicit);
        Assert.Contains(plan.Assemblies, item => item.SourcePath == older && !item.IsExplicit);
    }

    [Fact]
    public void Plan_ResolvesEachReachablePathOnceAndBuildsSharedDiamondClosure()
    {
        using var temp = TestTempDirectory.Create("export-diamond-");
        var root = Emit(temp.GetPath("Root.dll"), "Root");
        var left = Emit(temp.GetPath("Left.dll"), "Left");
        var right = Emit(temp.GetPath("Right.dll"), "Right");
        var shared = Emit(temp.GetPath("Shared.dll"), "Shared");
        var edges = new Dictionary<string, AssemblyReferenceDto[]>(StringComparer.OrdinalIgnoreCase)
        {
            [root] = [Edge("Left", left), Edge("Right", right)],
            [left] = [Edge("Shared", shared)],
            [right] = [Edge("Shared", shared)],
            [shared] = [],
        };
        var calls = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        AssemblyExportReferenceClosure Resolve(string path, Func<AssemblyReferenceDto, bool> traverse)
        {
            calls[path] = calls.GetValueOrDefault(path) + 1;
            Assert.False(traverse(new("DirectOnlyProbe", "1.0.0.0", "neutral", false)));
            var assembly = AssemblyName.GetAssemblyName(path);
            return new(new(assembly.Name!, assembly.Version!.ToString(), "neutral", ""), edges[path], [], true);
        }

        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [root]), Resolve);

        Assert.Equal(new[] { root, left, right, shared }.Order(StringComparer.OrdinalIgnoreCase),
            calls.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(calls.Values, count => Assert.Equal(1, count));
        var rootPlan = plan.Assemblies.Single(item => item.SourcePath == root);
        Assert.Equal(2, rootPlan.Closure.References.Count);
        Assert.Equal(new[] { left, right }, rootPlan.Closure.References.Select(edge => edge.ResolvedPath));
        Assert.Equal(new[] { "Left", "Right" }, rootPlan.Closure.References.Select(edge => edge.Name));
        Assert.Single(plan.Assemblies.Single(item => item.SourcePath == left).Closure.References);
        Assert.Single(plan.Assemblies.Single(item => item.SourcePath == right).Closure.References);

        static AssemblyReferenceDto Edge(string name, string path) =>
            new(name, "1.0.0.0", "neutral", true, path, ResolutionProvenance: "adjacent");
    }

    [Fact]
    public void Plan_RejectsOutputInsideRecursiveSourceBeforeEnumerating()
    {
        using var temp = TestTempDirectory.Create("export-overlap-");
        var sources = temp.GetPath("sources");
        Directory.CreateDirectory(sources);
        Emit(Path.Combine(sources, "Root.dll"), "Root");
        var output = Path.Combine(sources, "dump");
        Directory.CreateDirectory(output);

        var error = Assert.Throws<InvalidOperationException>(() => ExportPlanner.Create(new(output, [sources])));

        Assert.Contains("overlaps", error.Message, StringComparison.OrdinalIgnoreCase);

        var secondOutput = temp.GetPath("dump2");
        var nestedSource = Emit(Path.Combine(secondOutput, "input", "Nested.dll"), "Nested");
        var reverseError = Assert.Throws<InvalidOperationException>(() => ExportPlanner.Create(new(secondOutput, [nestedSource])));
        Assert.Contains("inside the output dump", reverseError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("AiNetCodeNavigator.AssemblyExport:2\n")]
    [InlineData("AiNetCodeNavigator.AssemblyExport:1")]
    public void Plan_RejectsUnownedRootWithoutChangingContent(string? marker)
    {
        using var temp = TestTempDirectory.Create("export-unowned-");
        var source = Emit(temp.GetPath("Root.dll"), "Root");
        var output = temp.GetPath("dump");
        Directory.CreateDirectory(output);
        var sentinel = Path.Combine(output, "keep.txt");
        File.WriteAllText(sentinel, "keep");
        if (marker is not null) File.WriteAllText(Path.Combine(output, ExportDumpOwnership.MarkerName), marker);
        Assert.Throws<InvalidOperationException>(() => ExportPlanner.Create(new(output, [source])));
        Assert.Equal("keep", File.ReadAllText(sentinel));
        Assert.Equal(marker, File.Exists(Path.Combine(output, ExportDumpOwnership.MarkerName))
            ? File.ReadAllText(Path.Combine(output, ExportDumpOwnership.MarkerName)) : null);
    }

    [Fact]
    public void Plan_NormalizesIdenticalDependencyAliasesAndRetainsOriginalProvenance()
    {
        using var temp = TestTempDirectory.Create("export-alias-");
        var source = Emit(temp.GetPath("Root.dll"), "Root");
        var dependency = Emit(temp.GetPath("Dependency.dll"), "Dependency");
        var alias = temp.GetPath("DependencyAlias.dll");
        File.Copy(dependency, alias);
        var original = new[]
        {
            new AssemblyReferenceDto("Dependency", "1.0.0.0", "neutral", true, dependency, ResolutionProvenance: "adjacent"),
            new AssemblyReferenceDto("Dependency", "1.0.0.0", "neutral", true, alias, ResolutionProvenance: "gac"),
        };
        AssemblyExportReferenceClosure Resolve(string path, Func<AssemblyReferenceDto, bool> _) =>
            new(new(path == source ? "Root" : "Dependency", "1.0.0.0", "neutral", ""), path == source ? original : [], [], true);
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]), Resolve);
        Assert.Equal(2, plan.Assemblies.Count);
        var root = plan.Assemblies.Single(item => item.SourcePath == source);
        Assert.Equal(new[] { dependency, alias }, root.Closure.References.Select(edge => edge.ResolvedPath));
        Assert.Single(root.DecompilationReferences.Select(edge => edge.ResolvedPath).Distinct());
        Assert.Equal(new[] { "adjacent", "gac" }, root.DecompilationReferences.Select(edge => edge.ResolutionProvenance));
    }

    private static void CreateJunction(string path, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", path, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static string Emit(string path, string name, string[]? references = null, Version? version = null)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), version ?? new Version(1, 0, 0, 0), default, default, (AssemblyFlags)0, AssemblyHashAlgorithm.Sha256);
        foreach (var reference in references ?? [])
            metadata.AddAssemblyReference(metadata.GetOrAddString(reference), new Version(1, 0, 0, 0), default, default, (AssemblyFlags)0, default);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var builder = new ManagedPEBuilder(new PEHeaderBuilder(), new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }
}
