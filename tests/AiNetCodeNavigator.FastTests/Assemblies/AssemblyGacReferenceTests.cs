#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.TestKit;
using ICSharpCode.Decompiler.Metadata;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class AssemblyGacReferenceTests
{
    [Theory]
    [InlineData("modern", "GAC_MSIL")]
    [InlineData("legacy", "GAC")]
    public void Resolve_ProvesExactGacIdentityAndTransitiveCycle(string location, string bucket)
    {
        using var temp = TestTempDirectory.Create("gac-closure-");
        var root = Emit(temp.GetPath("Root.dll"), "Root", references: ["Dependency"]);
        var gacRoot = temp.GetPath(location);
        var dependency = Emit(Path.Combine(gacRoot, bucket, "Dependency", "v4.0_1.0.0.0__", "Dependency.dll"),
            "Dependency", references: ["Root"]);
        // A cycle must remain provable from the dependency's adjacent directory.
        File.Copy(root, Path.Combine(Path.GetDirectoryName(dependency)!, "Root.dll"));
        var result = Resolver(gacRoot).Resolve(root);
        Assert.True(result.IsComplete);
        var edge = Assert.Single(result.References.Where(edge => edge.Name == "Dependency" && edge.SourceAssemblyPath == root));
        Assert.Equal(dependency, edge.ResolvedPath);
        Assert.Equal("gac", edge.ResolutionProvenance);
        Assert.Equal(root, edge.SourceAssemblyPath);
        Assert.Contains(result.References, edge => edge.ResolutionState == "cycle");
    }

    [Theory]
    [InlineData("adjacent")]
    [InlineData("runtime")]
    public void Resolve_PrefersExistingCandidateBeforeGac(string provenance)
    {
        using var temp = TestTempDirectory.Create("gac-precedence-");
        var root = Emit(temp.GetPath("Root.dll"), "Root", references: ["Dependency"]);
        var local = Emit(temp.GetPath(provenance == "adjacent" ? "Dependency.dll" : "runtime/Dependency.dll"), "Dependency");
        var gacRoot = temp.GetPath("gac");
        Emit(Path.Combine(gacRoot, "GAC_MSIL", "Dependency", "1", "Dependency.dll"), "Dependency");
        var result = Resolver(gacRoot, [local]).Resolve(root);
        var edge = Assert.Single(result.References);
        Assert.Equal(local, edge.ResolvedPath);
        Assert.Equal(provenance, edge.ResolutionProvenance);
    }

    [Theory]
    [InlineData("2.0.0.0", "neutral", false)]
    [InlineData("1.0.0.0", "fr", false)]
    [InlineData("1.0.0.0", "neutral", true)]
    public void Resolve_RejectsGacVersionCultureAndTokenMismatch(string version, string culture, bool signed)
    {
        using var temp = TestTempDirectory.Create("gac-identity-");
        var root = Emit(temp.GetPath("Root.dll"), "Root", references: ["Dependency"]);
        var gacRoot = temp.GetPath("gac");
        Emit(Path.Combine(gacRoot, "GAC_MSIL", "Dependency", "1", "Dependency.dll"), "Dependency", version, culture, signed);
        var edge = Assert.Single(Resolver(gacRoot).Resolve(root).References);
        Assert.False(edge.Resolved);
        Assert.Equal("missing", edge.ResolutionState);
    }

    [Fact]
    public void Resolve_PrefersMsilRejectsArchitectureMismatchAndReportsAmbiguity()
    {
        using var temp = TestTempDirectory.Create("gac-architecture-");
        var root = Emit(temp.GetPath("Root.dll"), "Root", machine: Machine.Amd64, references: ["Dependency"]);
        var gacRoot = temp.GetPath("gac");
        Emit(Path.Combine(gacRoot, "GAC_32", "Dependency", "1", "Dependency.dll"), "Dependency", requires32Bit: true);
        Assert.False(Assert.Single(Resolver(gacRoot).Resolve(root).References).Resolved);
        var x64 = Emit(Path.Combine(gacRoot, "GAC_64", "Dependency", "1", "Dependency.dll"), "Dependency", machine: Machine.Amd64);
        Assert.Equal(x64, Assert.Single(Resolver(gacRoot).Resolve(root).References).ResolvedPath);
        var msil = Emit(Path.Combine(gacRoot, "GAC_MSIL", "Dependency", "1", "Dependency.dll"), "Dependency");
        Assert.Equal(msil, Assert.Single(Resolver(gacRoot).Resolve(root).References).ResolvedPath);
        Emit(Path.Combine(gacRoot, "GAC_MSIL", "Dependency", "2", "Dependency.dll"), "Dependency");
        var ambiguous = Resolver(gacRoot).Resolve(root);
        Assert.False(ambiguous.IsComplete);
        Assert.Equal("ambiguous", Assert.Single(ambiguous.References).ResolutionState);
    }

    [Theory]
    [InlineData(0, 10, "depth_limit")]
    [InlineData(10, 1, "node_limit")]
    public void ExportClosure_ReportsBoundaryInsteadOfSilentlyOmitting(int depth, int nodes, string state)
    {
        using var temp = TestTempDirectory.Create("export-boundary-");
        var root = Emit(temp.GetPath("Root.dll"), "Root", references: ["Dependency"]);
        Emit(temp.GetPath("Dependency.dll"), "Dependency");
        var result = AssemblyExportReferenceResolver.Resolve(root, maxDepth: depth, maxNodes: nodes);
        Assert.False(result.IsComplete);
        Assert.Equal(state, Assert.Single(result.References).ResolutionState);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == AssemblyReferenceResolver.BoundaryDiagnosticCode);
    }

    [Fact]
    public void ExportClosure_RecordsFilteredEdgeWithoutTraversingIt()
    {
        using var temp = TestTempDirectory.Create("export-filter-");
        var root = Emit(temp.GetPath("Root.dll"), "Root", references: ["Dependency"]);
        var dependency = Emit(temp.GetPath("Dependency.dll"), "Dependency", references: ["Unreachable"]);
        var result = AssemblyExportReferenceResolver.Resolve(root, edge => !edge.Resolved, maxDepth: 0);
        Assert.True(result.IsComplete);
        Assert.Equal(dependency, Assert.Single(result.References).ResolvedPath);
    }

    [Fact]
    public void Resolve_AbsentGacRecordsMissingReference()
    {
        using var temp = TestTempDirectory.Create("gac-absent-");
        var root = Emit(temp.GetPath("Root.dll"), "Root", references: ["Dependency"]);
        var result = Resolver(temp.GetPath("absent")).Resolve(root);
        Assert.Equal("missing", Assert.Single(result.References).ResolutionState);
        Assert.True(result.IsComplete); // Complete search with a reported unresolved edge.
    }

    [Fact]
    public void DecompilerResolver_UsesProvenFilePathRatherThanFilenameSearch()
    {
        using var temp = TestTempDirectory.Create("gac-proven-decompiler-");
        var dependency = Emit(temp.GetPath("unusual-file-name.dll"), "Dependency");
        var fallback = new UniversalAssemblyResolver(temp.GetPath("Root.dll"), false, ".NETCoreApp,Version=v10.0");
        using var resolver = new ProvenAssemblyDecompilerResolver(fallback,
            [new AssemblyReferenceDto("Dependency", "1.0.0.0", "neutral", true, dependency)], useOnlyProvenReferences: true);
        Assert.Equal(dependency, resolver.Resolve(AssemblyNameReference.Parse("Dependency, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"))!.FileName);
        Assert.Null(resolver.Resolve(AssemblyNameReference.Parse("Dependency, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null")));
    }

    [Fact]
    public void ExactGacIdentity_RejectsFrameworkVersionTolerance()
    {
        var reference = new AssemblyReferenceDto("System.Probe", "1.0.0.0", "neutral", false, PublicKeyToken: "0011");
        Assert.False(AssemblyGacCandidateSource.ExactIdentityMatches(reference,
            new AssemblyIdentityDto("System.Probe", "2.0.0.0", "neutral", "0011")));
    }

    private static AssemblyReferenceResolver Resolver(string root, string[]? runtime = null) =>
        new(new AssemblyGacCandidateSource([root]), runtime ?? [], exportClosure: true);

    private static string Emit(string path, string name, string version = "1.0.0.0", string culture = "neutral",
        bool signed = false, Machine machine = Machine.I386, bool requires32Bit = false, string[]? references = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), Version.Parse(version),
            culture == "neutral" ? default : metadata.GetOrAddString(culture),
            signed ? metadata.GetOrAddBlob(new byte[32]) : default,
            signed ? AssemblyFlags.PublicKey : (AssemblyFlags)0, AssemblyHashAlgorithm.Sha256);
        foreach (var reference in references ?? [])
            metadata.AddAssemblyReference(metadata.GetOrAddString(reference), new Version(1, 0, 0, 0), default, default, (AssemblyFlags)0, default);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var builder = new ManagedPEBuilder(new PEHeaderBuilder(machine: machine), new MetadataRootBuilder(metadata),
            new BlobBuilder(), flags: CorFlags.ILOnly | (requires32Bit ? CorFlags.Requires32Bit : (CorFlags)0));
        var image = new BlobBuilder();
        builder.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }
}
