using AiNetCodeNavigator.Core.Assemblies;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class AssemblyFrameworkReferenceTests
{
    [Theory]
    [InlineData(true, "framework")]
    [InlineData(false, "runtime")]
    public void Resolve_SelectsInstalledFrameworkBeforeRuntimeOnlyForLegacyTarget(bool legacy, string provenance)
    {
        using var temp = TestTempDirectory.Create("framework-reference-precedence-");
        var root = AssemblyTestHelper.EmitMetadataInterface(temp, "Root", "Probe", "IRoot",
            legacy ? [new("mscorlib", "System", "IObject"), new("System.Runtime", "System", "IValue")]
                : [new("System.Runtime", "System", "IValue")]);
        using var runtimeTemp = TestTempDirectory.Create("framework-runtime-candidate-");
        using var frameworkTemp = TestTempDirectory.Create("framework-installed-candidate-");
        var runtimePath = AssemblyTestHelper.EmitMetadataInterface(runtimeTemp, "System.Runtime", "System", "IValue");
        var frameworkPath = AssemblyTestHelper.EmitMetadataInterface(frameworkTemp, "System.Runtime", "System", "IValue");
        var corePath = AssemblyTestHelper.EmitMetadataInterface(frameworkTemp, "mscorlib", "System", "IObject");
        var resolver = new AssemblyReferenceResolver(new AssemblyGacCandidateSource([]), [runtimePath],
            exportClosure: true, traverseReference: _ => false, framework: new AssemblyFrameworkCandidateSource([frameworkTemp.DirectoryPath]));

        var resolution = resolver.Resolve(root);

        var edge = Assert.Single(resolution.References.Where(reference => reference.Name == "System.Runtime"));
        Assert.Equal(legacy ? frameworkPath : runtimePath, edge.ResolvedPath);
        Assert.Equal(provenance, edge.ResolutionProvenance);
        if (legacy) Assert.Equal(corePath, Assert.Single(resolution.References.Where(reference => reference.Name == "mscorlib")).ResolvedPath);
    }

    [Fact]
    public void Resolve_PreservesAdjacentPrecedenceForLegacyTarget()
    {
        using var temp = TestTempDirectory.Create("framework-adjacent-precedence-");
        var root = AssemblyTestHelper.EmitMetadataInterface(temp, "Root", "Probe", "IRoot", [new("mscorlib", "System", "IObject")]);
        var adjacent = AssemblyTestHelper.EmitMetadataInterface(temp, "mscorlib", "System", "IObject");
        using var frameworkTemp = TestTempDirectory.Create("framework-adjacent-candidate-");
        AssemblyTestHelper.EmitMetadataInterface(frameworkTemp, "mscorlib", "System", "IObject");
        var resolver = new AssemblyReferenceResolver(new AssemblyGacCandidateSource([]), [],
            exportClosure: true, traverseReference: _ => false, framework: new AssemblyFrameworkCandidateSource([frameworkTemp.DirectoryPath]));

        var edge = Assert.Single(resolver.Resolve(root).References);

        Assert.Equal(adjacent, edge.ResolvedPath);
        Assert.Equal("adjacent", edge.ResolutionProvenance);
    }

    [Theory]
    [InlineData(".NETFramework,Version=v4.8", false, "framework")]
    [InlineData(".NETCoreApp,Version=v10.0", false, "runtime")]
    [InlineData(".NETStandard,Version=v2.0", false, "runtime")]
    [InlineData(null, true, "framework")]
    public void Resolve_UsesExplicitTargetFrameworkAndToleratesMalformedAttribute(string? targetFramework,
        bool malformedAttribute, string provenance)
    {
        using var temp = TestTempDirectory.Create("framework-target-attribute-");
        using var frameworkTemp = TestTempDirectory.Create("framework-target-candidate-");
        using var runtimeTemp = TestTempDirectory.Create("framework-target-runtime-");
        var root = EmitProbe(temp, "Root", references: ["mscorlib", "System.Runtime"],
            targetFramework: targetFramework, malformedAttribute: malformedAttribute);
        var installed = AssemblyTestHelper.EmitMetadataInterface(frameworkTemp, "System.Runtime", "System", "IValue");
        var runtime = AssemblyTestHelper.EmitMetadataInterface(runtimeTemp, "System.Runtime", "System", "IValue");
        var resolver = new AssemblyReferenceResolver(new AssemblyGacCandidateSource([]), [runtime],
            exportClosure: true, traverseReference: _ => false,
            framework: new AssemblyFrameworkCandidateSource([frameworkTemp.DirectoryPath]));

        var result = resolver.Resolve(root);

        Assert.NotNull(result.Identity);
        var edge = Assert.Single(result.References.Where(reference => reference.Name == "System.Runtime"));
        Assert.Equal(provenance == "framework" ? installed : runtime, edge.ResolvedPath);
        Assert.Equal(provenance, edge.ResolutionProvenance);
    }

    [Theory]
    [InlineData("2.0.0.0", "neutral", "", Machine.I386, false)]
    [InlineData("1.0.0.0", "fr", "", Machine.I386, false)]
    [InlineData("1.0.0.0", "neutral", "0011", Machine.I386, false)]
    [InlineData("1.0.0.0", "neutral", "", Machine.I386, true)]
    public void FrameworkCandidates_RejectIdentityAndDefiniteArchitectureMismatch(string version, string culture,
        string token, Machine machine, bool requires32Bit)
    {
        using var temp = TestTempDirectory.Create("framework-incompatible-");
        using var frameworkTemp = TestTempDirectory.Create("framework-incompatible-candidate-");
        var root = EmitProbe(temp, "Root", machine: Machine.Amd64);
        EmitProbe(frameworkTemp, "System.Probe", machine: machine, requires32Bit: requires32Bit);
        var diagnostics = new List<AssemblySessionDiagnostic>();
        var reference = new AssemblyReferenceDto("System.Probe", version, culture, false, PublicKeyToken: token);

        var match = new AssemblyFrameworkCandidateSource([frameworkTemp.DirectoryPath]).Find(reference, root, diagnostics);

        Assert.Null(match);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void FrameworkCandidates_ReportUnreadableManagedCandidate()
    {
        using var temp = TestTempDirectory.Create("framework-invalid-candidate-");
        var root = EmitProbe(temp, "Root");
        File.WriteAllBytes(temp.GetPath("System.Probe.dll"), [0x4d, 0x5a]);
        var diagnostics = new List<AssemblySessionDiagnostic>();

        var match = new AssemblyFrameworkCandidateSource([temp.DirectoryPath]).Find(
            new("System.Probe", "1.0.0.0", "neutral", false), root, diagnostics);

        Assert.Null(match);
        Assert.Equal("assembly-framework-candidate-failed", Assert.Single(diagnostics).Code);
    }

    [Fact]
    public async Task ExportAsync_LegacyValueTypeCallsPreserveValidSourcesAndRefArguments()
    {
        if (!OperatingSystem.IsWindows()) return;
        var coreLibrary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Microsoft.NET", "Framework", "v4.0.30319", "mscorlib.dll");
        Assert.True(File.Exists(coreLibrary));
        using var temp = TestTempDirectory.Create("export-legacy-value-types-");
        var sourcePath = temp.GetPath("LegacyValueProbe.dll");
        var source = """
            using System;
            public static class ValueProbe
            {
                public static string Format(int value, decimal amount, DateTime date)
                {
                    Increment(ref value);
                    return value.ToString() + amount.ToString() + date.Year.ToString();
                }
                private static void Increment(ref int value) { value++; }
            }
            """;
        var compilation = CSharpCompilation.Create("LegacyValueProbe", [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(coreLibrary)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emitted = compilation.Emit(sourcePath);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var closure = AssemblyExportReferenceResolver.Resolve(sourcePath, _ => false);
        var stage = temp.GetPath("stage");
        Directory.CreateDirectory(stage);

        var result = await AssemblyProjectExporter.ExportAsync(sourcePath, stage, closure.Identity!,
            AssemblyFingerprintCalculator.Create(sourcePath).Sha256, closure.References);

        Assert.True(result.IsComplete, string.Join(Environment.NewLine, result.Diagnostics));
        var generated = Assert.Single(result.SourceRelativePaths.Where(path => Path.GetFileName(path) == "ValueProbe.cs"));
        var tree = CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(Path.Combine(stage, generated)));
        Assert.DoesNotContain(tree.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Increment(ref value)", tree.ToString(), StringComparison.Ordinal);
    }

    private static string EmitProbe(TestTempDirectory temp, string name, Machine machine = Machine.I386,
        bool requires32Bit = false, string[]? references = null, string? targetFramework = null,
        bool malformedAttribute = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        var assembly = metadata.AddAssembly(metadata.GetOrAddString(name), new Version(1, 0, 0, 0),
            default, default, (AssemblyFlags)0, AssemblyHashAlgorithm.Sha256);
        var frameworkScope = default(AssemblyReferenceHandle);
        foreach (var reference in references ?? [])
            frameworkScope = metadata.AddAssemblyReference(metadata.GetOrAddString(reference), new Version(1, 0, 0, 0),
                default, default, (AssemblyFlags)0, default);
        if (targetFramework is not null || malformedAttribute)
        {
            var attribute = metadata.AddTypeReference(frameworkScope,
                metadata.GetOrAddString("System.Runtime.Versioning"), metadata.GetOrAddString("TargetFrameworkAttribute"));
            var constructor = metadata.AddMemberReference(attribute, metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x01, 0x0e }));
            var value = new BlobBuilder();
            if (malformedAttribute) value.WriteByte(1);
            else
            {
                value.WriteUInt16(1);
                value.WriteSerializedString(targetFramework);
                value.WriteUInt16(0);
            }
            metadata.AddCustomAttribute(assembly, constructor, metadata.GetOrAddBlob(value));
        }
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var builder = new ManagedPEBuilder(new PEHeaderBuilder(machine: machine), new MetadataRootBuilder(metadata),
            new BlobBuilder(), flags: CorFlags.ILOnly | (requires32Bit ? CorFlags.Requires32Bit : (CorFlags)0));
        var image = new BlobBuilder();
        builder.Serialize(image);
        var path = temp.GetPath(name + ".dll");
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }
}
