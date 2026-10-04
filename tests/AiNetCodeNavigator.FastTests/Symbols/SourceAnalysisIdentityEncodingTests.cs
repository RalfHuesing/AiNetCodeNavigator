#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace AiNetCodeNavigator.FastTests.Symbols;

// @covers SourceAnalysisIdentityEncoder
[Trait("Category", "Unit")]
public sealed class SourceAnalysisIdentityEncodingTests
{
    [Fact]
    public void StringAndListFramesUseBigEndianLengthsAndStrictUtf8()
    {
        Assert.Equal(new byte[] { 0, 0, 0, 1, 0x41 }, SourceAnalysisIdentityEncoder.EncodeStringFrameForTesting("A"));
        Assert.Equal(new byte[] { 0 }, SourceAnalysisIdentityEncoder.EncodeNullableStringFrameForTesting(null));
        Assert.Equal(new byte[] { 1, 0, 0, 0, 0 }, SourceAnalysisIdentityEncoder.EncodeNullableStringFrameForTesting(string.Empty));
        Assert.Equal(new byte[] { 1, 0, 0, 0, 1, 0x41 }, SourceAnalysisIdentityEncoder.EncodeNullableStringFrameForTesting("A"));
        Assert.Equal(new byte[]
        {
            0, 0, 0, 2,
            0, 0, 0, 5, 0, 0, 0, 1, 0x41,
            0, 0, 0, 6, 0, 0, 0, 2, 0x42, 0x43,
        }, SourceAnalysisIdentityEncoder.EncodeStringListFrameForTesting(["A", "BC"]));
        Assert.Throws<EncoderFallbackException>(() => SourceAnalysisIdentityEncoder.EncodeStringFrameForTesting("\uD800"));
    }

    [Fact]
    public async Task EquivalentReloadAndProjectDocumentReferencePermutationsHaveTheSameHash()
    {
        using var fixture = TestTempDirectory.Create("source-analysis-identity-permutation-");
        var solutionPath = fixture.GetPath("Identity.slnx");
        var metadataA = AssemblyTestHelper.EmitAssembly(fixture, "IdentityReferenceA", "namespace IdentityRefs; public sealed class A { }");
        var metadataB = AssemblyTestHelper.EmitAssembly(fixture, "IdentityReferenceB", "namespace IdentityRefs; public sealed class B { }");
        var first = BuildWorkspace(solutionPath,
            new ProjectSpec("Root", [("AA.cs", "namespace Identity; public sealed class RootA { }"), ("B.cs", "namespace Identity; public sealed class RootB { }")],
                ProjectReferences: ["Library"], VirtualProjectDirectory: "src/AA/Root",
                AdditionalReferences: [MetadataReference.CreateFromFile(metadataA), MetadataReference.CreateFromFile(metadataB)]),
            new ProjectSpec("Library", [("C.cs", "namespace Identity; public sealed class LibraryC { }"), ("D.cs", "namespace Identity; public sealed class LibraryD { }")],
                VirtualProjectDirectory: "src/B/Library"));
        var second = BuildWorkspace(solutionPath,
            new ProjectSpec("Library", [("D.cs", "namespace Identity; public sealed class LibraryD { }"), ("C.cs", "namespace Identity; public sealed class LibraryC { }")],
                VirtualProjectDirectory: "src/B/Library"),
            new ProjectSpec("Root", [("B.cs", "namespace Identity; public sealed class RootB { }"), ("AA.cs", "namespace Identity; public sealed class RootA { }")],
                ProjectReferences: ["Library"], VirtualProjectDirectory: "src/AA/Root",
                AdditionalReferences: [MetadataReference.CreateFromFile(metadataB), MetadataReference.CreateFromFile(metadataA)]));
        using (first)
        using (second)
        {
            var firstHash = await ComputeCapturedHashAsync(first.Solution);
            var secondHash = await ComputeCapturedHashAsync(second.Solution);
            Assert.Equal(firstHash, secondHash);

            var reloaded = BuildWorkspace(solutionPath,
                new ProjectSpec("Root", [("B.cs", "namespace Identity; public sealed class RootB { }"), ("AA.cs", "namespace Identity; public sealed class RootA { }")],
                    ProjectReferences: ["Library"], VirtualProjectDirectory: "src/AA/Root",
                    AdditionalReferences: [MetadataReference.CreateFromFile(metadataB), MetadataReference.CreateFromFile(metadataA)]),
                new ProjectSpec("Library", [("D.cs", "namespace Identity; public sealed class LibraryD { }"), ("C.cs", "namespace Identity; public sealed class LibraryC { }")],
                    VirtualProjectDirectory: "src/B/Library"));
            using (reloaded)
            {
                Assert.Equal(firstHash, await ComputeCapturedHashAsync(reloaded.Solution));
            }

            var changed = BuildWorkspace(solutionPath,
                new ProjectSpec("Root", [("AA.cs", "namespace Identity; public sealed class RootChanged { }"), ("B.cs", "namespace Identity; public sealed class RootB { }")],
                    ProjectReferences: ["Library"], VirtualProjectDirectory: "src/AA/Root",
                    AdditionalReferences: [MetadataReference.CreateFromFile(metadataB), MetadataReference.CreateFromFile(metadataA)]),
                new ProjectSpec("Library", [("C.cs", "namespace Identity; public sealed class LibraryC { }"), ("D.cs", "namespace Identity; public sealed class LibraryD { }")],
                    VirtualProjectDirectory: "src/B/Library"));
            using (changed)
                Assert.NotEqual(firstHash, await ComputeCapturedHashAsync(changed.Solution));
        }
    }

    [Fact]
    public async Task TypedParseAndCompilationOptionsContributeToTheFullHash()
    {
        using var fixture = TestTempDirectory.Create("source-analysis-identity-options-");
        using var workspace = BuildWorkspace(fixture.GetPath("Options.slnx"),
            new ProjectSpec("Options", [("Options.cs", "namespace Identity; public sealed class Probe { }")], VirtualProjectDirectory: "src/Options"));
        var solution = workspace.Solution;
        var project = Assert.Single(solution.Projects);
        var baseline = await ComputeHashAsync(solution);

        var parseOptions = Assert.IsType<CSharpParseOptions>(project.ParseOptions);
        var parseMutations = new Func<CSharpParseOptions, CSharpParseOptions>[]
        {
            options => options.WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp10),
            options => options.WithKind(SourceCodeKind.Script),
            options => options.WithDocumentationMode(DocumentationMode.Diagnose),
            options => options.WithPreprocessorSymbols(ImmutableArray.Create("IDENTITY_SYMBOL")),
            options => options.WithFeatures([new KeyValuePair<string, string>("identity-feature", "enabled")]),
        };
        foreach (var mutate in parseMutations)
            Assert.NotEqual(baseline, await ComputeFromExactOwnerCreatedSolutionAsync(
                solution.WithProjectParseOptions(project.Id, mutate(parseOptions))));

        var options = Assert.IsType<CSharpCompilationOptions>(project.CompilationOptions);
        var compilationMutations = new Func<CSharpCompilationOptions, CSharpCompilationOptions>[]
        {
            value => value.WithOutputKind(OutputKind.ConsoleApplication),
            value => value.WithModuleName("IdentityModule"),
            value => value.WithMainTypeName("Identity.EntryPoint"),
            value => value.WithScriptClassName("IdentityScript"),
            value => value.WithOptimizationLevel(OptimizationLevel.Release),
            value => value.WithOverflowChecks(true),
            value => value.WithAllowUnsafe(true),
            value => value.WithNullableContextOptions(NullableContextOptions.Disable),
            value => value.WithPlatform(Platform.X86),
            value => value.WithMetadataImportOptions(MetadataImportOptions.All),
            value => value.WithUsings(ImmutableArray.Create("Identity.GlobalUsing")),
            value => value.WithWarningLevel(3),
            value => value.WithGeneralDiagnosticOption(ReportDiagnostic.Error),
            value => value.WithSpecificDiagnosticOptions([new KeyValuePair<string, ReportDiagnostic>("ID0001", ReportDiagnostic.Suppress)]),
            value => value.WithReportSuppressedDiagnostics(true),
            value => value.WithDeterministic(true),
            value => value.WithCryptoPublicKey(ImmutableArray.Create<byte>(1, 2, 3)),
            value => value.WithDelaySign(true),
            value => value.WithPublicSign(true),
        };
        foreach (var mutate in compilationMutations)
            Assert.NotEqual(baseline, await ComputeFromExactOwnerCreatedSolutionAsync(
                solution.WithProjectCompilationOptions(project.Id, mutate(options))));

        Assert.Equal(baseline, await ComputeFromExactOwnerCreatedSolutionAsync(
            solution.WithProjectCompilationOptions(project.Id, options.WithConcurrentBuild(!options.ConcurrentBuild))));
        Assert.NotEqual(
            await ComputeFromExactOwnerCreatedSolutionAsync(
                solution.WithProjectCompilationOptions(project.Id, options.WithUsings(ImmutableArray.Create("A", "BC")))),
            await ComputeFromExactOwnerCreatedSolutionAsync(
                solution.WithProjectCompilationOptions(project.Id, options.WithUsings(ImmutableArray.Create("AB", "C")))));
        Assert.NotEqual(
            await ComputeFromExactOwnerCreatedSolutionAsync(
                solution.WithProjectCompilationOptions(project.Id, options.WithModuleName(null))),
            await ComputeFromExactOwnerCreatedSolutionAsync(
                solution.WithProjectCompilationOptions(project.Id, options.WithModuleName(string.Empty))));
    }

    [Fact]
    public async Task KnownResolverValuesAndDesktopComparerAreEncodedInControlledFixtures()
    {
        using var fixture = TestTempDirectory.Create("source-analysis-identity-known-resolvers-");
        using var workspace = BuildWorkspace(fixture.GetPath("Resolvers.slnx"),
            new ProjectSpec("Resolvers", [("Main.cs", "namespace Identity; public sealed class Probe { }")],
                VirtualProjectDirectory: "src/Resolvers"));
        var solution = workspace.Solution;
        var project = Assert.Single(solution.Projects);
        var options = Assert.IsType<CSharpCompilationOptions>(project.CompilationOptions);
        var baseline = await ComputeControlledHashAsync(solution);
        var basePath = fixture.GetPath("resolver-base");

        var sourceResolverA = solution.WithProjectCompilationOptions(project.Id,
            options.WithSourceReferenceResolver(new SourceFileResolver(ImmutableArray.Create(fixture.GetPath("search-a")), basePath)));
        var sourceResolverB = solution.WithProjectCompilationOptions(project.Id,
            options.WithSourceReferenceResolver(new SourceFileResolver(ImmutableArray.Create(fixture.GetPath("search-b")), basePath)));
        var sourceHashA = await ComputeControlledHashAsync(sourceResolverA);
        var sourceHashB = await ComputeControlledHashAsync(sourceResolverB);
        Assert.NotEqual(baseline, sourceHashA);
        Assert.NotEqual(sourceHashA, sourceHashB);

        var xmlResolverA = solution.WithProjectCompilationOptions(project.Id,
            options.WithXmlReferenceResolver(new XmlFileResolver(fixture.GetPath("xml-a"))));
        var xmlResolverB = solution.WithProjectCompilationOptions(project.Id,
            options.WithXmlReferenceResolver(new XmlFileResolver(fixture.GetPath("xml-b"))));
        var xmlHashA = await ComputeControlledHashAsync(xmlResolverA);
        var xmlHashB = await ComputeControlledHashAsync(xmlResolverB);
        Assert.NotEqual(baseline, xmlHashA);
        Assert.NotEqual(xmlHashA, xmlHashB);

        var desktopComparer = solution.WithProjectCompilationOptions(project.Id,
            options.WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default));
        Assert.NotEqual(baseline, await ComputeControlledHashAsync(desktopComparer));
    }

    [Fact]
    public async Task TrustedDesktopStrongNameProviderWithoutExternalSigningInputsIsSupported()
    {
        using var fixture = TestTempDirectory.Create("source-analysis-identity-desktop-signer-");
        using var workspace = BuildWorkspace(fixture.GetPath("DesktopSigner.slnx"),
            new ProjectSpec("DesktopSigner", [("Main.cs", "namespace Identity; public sealed class Probe { }")],
                VirtualProjectDirectory: "src/DesktopSigner"));
        var solution = workspace.Solution;
        var project = Assert.Single(solution.Projects);
        var options = Assert.IsType<CSharpCompilationOptions>(project.CompilationOptions);
        var desktopSigner = options.WithStrongNameProvider(
            new DesktopStrongNameProvider(ImmutableArray<string>.Empty, tempPath: null));
        using var trustedWorkspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId("DesktopSigner");
        var projectInfo = ProjectInfo.Create(projectId, VersionStamp.Create(), "DesktopSigner", "DesktopSigner",
                LanguageNames.CSharp, filePath: fixture.GetPath("src/DesktopSigner/DesktopSigner.csproj"))
            .WithParseOptions(new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview))
            .WithCompilationOptions(desktopSigner)
            .WithMetadataReferences(TestWorkspaceBuilder.CoreReferences);
        var initialSolution = trustedWorkspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(),
            filePath: fixture.GetPath("DesktopSigner.slnx")));
        var trustedSolution = initialSolution.AddProject(projectInfo)
            .AddDocument(DocumentId.CreateNewId(projectId), "Main.cs",
                SourceText.From("namespace Identity; public sealed class Probe { }"), filePath: fixture.GetPath("src/DesktopSigner/Main.cs"));
        Assert.True(trustedWorkspace.TryApplyChanges(trustedSolution));
        trustedSolution = trustedWorkspace.CurrentSolution;

        var unproven = await CaptureAndComputeAsync(trustedSolution);
        Assert.False(unproven.Result.IsSuccess);
        Assert.Equal(NavigationErrorCodes.WorkspaceDiagnostic, unproven.Result.Error!.Value.Code);
        Assert.Contains("no matching workspace-owner proof", unproven.Result.Error.Value.Message,
            StringComparison.OrdinalIgnoreCase);

        var trusted = await ComputeFromExactOwnerCreatedSolutionAsync(trustedSolution);
        Assert.Matches("^[A-F0-9]{64}$", trusted);
    }

    [Fact]
    public async Task UncapturedActiveLoadDirectiveFailsClosedButInactiveDirectiveDoesNot()
    {
        using var fixture = TestTempDirectory.Create("source-analysis-identity-load-directive-");
        using var workspace = BuildWorkspace(fixture.GetPath("LoadDirective.slnx"),
            new ProjectSpec("LoadDirective", [("Main.csx", "public sealed class Probe { }")],
                VirtualProjectDirectory: "src/LoadDirective"));
        var original = workspace.Solution;
        var project = Assert.Single(original.Projects);
        var parse = Assert.IsType<CSharpParseOptions>(project.ParseOptions).WithKind(SourceCodeKind.Script);
        var document = Assert.Single(project.Documents);

        var active = original.WithProjectParseOptions(project.Id, parse).WithDocumentText(document.Id,
            SourceText.From("#load \"external.csx\"\npublic sealed class Probe { }"));
        var activeResult = await ComputeResultFromExactOwnerCreatedSolutionAsync(active);
        Assert.False(activeResult.IsSuccess);
        Assert.Equal(NavigationErrorCodes.WorkspaceDiagnostic, activeResult.Error!.Value.Code);
        Assert.Contains("active #load directive", activeResult.Error.Value.Message, StringComparison.OrdinalIgnoreCase);

        var inactive = original.WithProjectParseOptions(project.Id, parse).WithDocumentText(document.Id,
            SourceText.From("#if NEVER_DEFINED\n#load \"external.csx\"\n#endif\npublic sealed class Probe { }"));
        var inactiveResult = await ComputeResultFromExactOwnerCreatedSolutionAsync(inactive);
        Assert.True(inactiveResult.IsSuccess, inactiveResult.Error?.Message);
    }

    [Fact]
    public async Task SourceAdditionalAndAnalyzerConfigTextChangesWithTrustedBuilderProofAffectIdentity()
    {
        using var fixture = TestTempDirectory.Create("source-analysis-identity-documents-");
        const string source = "namespace Identity; public sealed class Main { }";
        const string additional = "asset=one";
        const string analyzerConfig = "is_global = true\nbuild_property.IdentityProbe = one\n";
        var solutionPath = fixture.GetPath("Documents.slnx");

        using var baselineWorkspace = BuildWorkspace(solutionPath,
            new ProjectSpec("Documents", [("Main.cs", source)], VirtualProjectDirectory: "src/Documents",
                AdditionalDocuments: [("assets/asset.txt", additional)],
                AnalyzerConfigDocuments: [("config/.globalconfig", analyzerConfig)]));
        var baseline = await ComputeCapturedHashAsync(baselineWorkspace.Solution);

        using var changedSourceWorkspace = BuildWorkspace(solutionPath,
            new ProjectSpec("Documents", [("Main.cs", source + " public sealed class Added { }")], VirtualProjectDirectory: "src/Documents",
                AdditionalDocuments: [("assets/asset.txt", additional)],
                AnalyzerConfigDocuments: [("config/.globalconfig", analyzerConfig)]));
        Assert.NotEqual(baseline, await ComputeCapturedHashAsync(changedSourceWorkspace.Solution));

        using var changedAdditionalWorkspace = BuildWorkspace(solutionPath,
            new ProjectSpec("Documents", [("Main.cs", source)], VirtualProjectDirectory: "src/Documents",
                AdditionalDocuments: [("assets/asset.txt", "asset=two")],
                AnalyzerConfigDocuments: [("config/.globalconfig", analyzerConfig)]));
        Assert.NotEqual(baseline, await ComputeCapturedHashAsync(changedAdditionalWorkspace.Solution));

        using var changedConfigWorkspace = BuildWorkspace(solutionPath,
            new ProjectSpec("Documents", [("Main.cs", source)], VirtualProjectDirectory: "src/Documents",
                AdditionalDocuments: [("assets/asset.txt", additional)],
                AnalyzerConfigDocuments: [("config/.globalconfig", analyzerConfig.Replace("one", "two", StringComparison.Ordinal))]));
        Assert.NotEqual(baseline, await ComputeCapturedHashAsync(changedConfigWorkspace.Solution));

        var project = Assert.Single(baselineWorkspace.Solution.Projects);
        var untrackedGenerator = baselineWorkspace.Solution.AddAnalyzerReference(project.Id,
            new TestGeneratorReference(new IdentityGeneratedSourceGenerator("untracked-v1").AsSourceGenerator()));
        var unsupported = await CaptureAndComputeAsync(untrackedGenerator);
        Assert.False(unsupported.Result.IsSuccess);
        Assert.Equal(NavigationErrorCodes.WorkspaceDiagnostic, unsupported.Result.Error!.Value.Code);
        Assert.Contains("analyzer reference whose source image path cannot be established", unsupported.Result.Error.Value.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Documents.csproj", unsupported.Result.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FileBackedGeneratorImageAndMaterializedOutputContributeToIdentity()
    {
        using var fixture = TestTempDirectory.Create("source-analysis-identity-file-generator-");
        using var workspace = BuildWorkspace(fixture.GetPath("Generator.slnx"),
            new ProjectSpec("Generator", [("Main.cs", "namespace Identity; public sealed class Main { }")],
                VirtualProjectDirectory: "src/Generator"));
        var project = Assert.Single(workspace.Solution.Projects);

        var generatorPath = fixture.GetPath("generators/IdentityGenerator.dll");
        EmitIdentityGenerator(generatorPath, "captured-v1");
        var replacementGeneratorPath = fixture.GetPath("generators/IdentityGeneratorV2.dll");
        EmitIdentityGenerator(replacementGeneratorPath, "captured-v2");
        using var generatorLoader = new IsolatedAnalyzerAssemblyLoader();
        var generatedSolution = workspace.Solution.AddAnalyzerReference(project.Id,
            new AnalyzerFileReference(generatorPath, generatorLoader));
        var generatedOwnerProof = WorkspaceInputProvenance.CreateFromTrustedLoader(generatedSolution);
        var generatedCapture = MetadataReferenceImageCapture.Capture(generatedSolution, previousInputs: null,
            CancellationToken.None, provenance: generatedOwnerProof);
        var generatedResult = await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(generatedCapture.Solution, generatedCapture.Inputs), CancellationToken.None);
        Assert.True(generatedResult.IsSuccess, generatedResult.Error?.Message);
        var generatedHash = generatedResult.Value!.ContentHash;
        var generatedDocuments = await generatedCapture.Solution.GetProject(project.Id)!.GetSourceGeneratedDocumentsAsync(CancellationToken.None);
        var generatedDocument = Assert.Single(generatedDocuments);
        Assert.Contains("captured-v1", (await generatedDocument.GetTextAsync()).ToString(), StringComparison.Ordinal);

        var generatorInput = Assert.Single(generatedCapture.Inputs.Projects.Single().BindingInputs,
            input => input.Kind == "analyzer-image");
        var expectedGeneratorKey = Path.GetFullPath(generatorPath).Replace('\\', '/');
        if (OperatingSystem.IsWindows())
            expectedGeneratorKey = expectedGeneratorKey.ToUpperInvariant();
        Assert.Equal(expectedGeneratorKey, generatorInput.LogicalKey);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(generatorPath))),
            generatorInput.Sha256);

        using var replacementGeneratorLoader = new IsolatedAnalyzerAssemblyLoader();
        var replacementSolution = workspace.Solution.AddAnalyzerReference(project.Id,
            new AnalyzerFileReference(replacementGeneratorPath, replacementGeneratorLoader));
        var replacementOwnerProof = WorkspaceInputProvenance.CreateFromTrustedLoader(replacementSolution);
        var replacementCapture = MetadataReferenceImageCapture.Capture(replacementSolution, previousInputs: null,
            CancellationToken.None, provenance: replacementOwnerProof);
        var replacementResult = await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(replacementCapture.Solution, replacementCapture.Inputs), CancellationToken.None);
        Assert.True(replacementResult.IsSuccess, replacementResult.Error?.Message);
        Assert.NotEqual(generatedHash, replacementResult.Value!.ContentHash);
        var replacementDocuments = await replacementCapture.Solution.GetProject(project.Id)!.GetSourceGeneratedDocumentsAsync(CancellationToken.None);
        var replacementDocument = Assert.Single(replacementDocuments);
        Assert.Contains("captured-v2", (await replacementDocument.GetTextAsync()).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PathlessDocumentIdentityFramesNameAndOrderedFolderComponents()
    {
        using var fixture = TestTempDirectory.Create("source-analysis-identity-pathless-");
        using var workspace = BuildWorkspace(fixture.GetPath("Pathless.slnx"),
            new ProjectSpec("Pathless", [("Main.cs", "namespace Identity; public sealed class Main { }")], VirtualProjectDirectory: "src/Pathless"));
        var baseSolution = workspace.Solution;
        var project = Assert.Single(baseSolution.Projects);

        var first = AddPathlessAdditionalDocument(baseSolution, project.Id, "asset", ["A|B", "C"]);
        var second = AddPathlessAdditionalDocument(baseSolution, project.Id, "asset", ["A", "B|C"]);
        Assert.NotEqual(await ComputeControlledHashAsync(first), await ComputeControlledHashAsync(second));

        var sourceDocumentId = Assert.Single(project.Documents).Id;
        var regular = baseSolution;
        var script = baseSolution.WithDocumentSourceCodeKind(sourceDocumentId, SourceCodeKind.Script);
        Assert.NotEqual(await ComputeControlledHashAsync(regular), await ComputeControlledHashAsync(script));
    }

    [Fact]
    public async Task CapturedMetadataAliasesMultiplicityAndEveryModuleImageContributeToIdentity()
    {
        using var fixture = TestTempDirectory.Create("source-analysis-identity-metadata-");
        using var moduleFixture = TestTempDirectory.Create("source-analysis-identity-module-");
        var solutionPath = fixture.GetPath("Metadata.slnx");
        ProjectSpec WithReferences(params MetadataReference[] references) => new("Metadata",
            [("Main.cs", "namespace Identity; public sealed class Main { }")],
            AdditionalReferences: references, VirtualProjectDirectory: "src/Metadata");
        var firstPath = AssemblyTestHelper.EmitAssembly(fixture, "MetadataA", "namespace Metadata; public sealed class A { }");
        var secondPath = AssemblyTestHelper.EmitAssembly(fixture, "MetadataB", "namespace Metadata; public sealed class B { }");
        var firstReference = MetadataReference.CreateFromFile(firstPath);
        var secondReference = MetadataReference.CreateFromFile(secondPath);
        using var two = BuildWorkspace(solutionPath, WithReferences(firstReference, secondReference));
        var twoHash = await ComputeCapturedHashAsync(two.Solution);
        using var permuted = BuildWorkspace(solutionPath, WithReferences(secondReference, firstReference));
        Assert.Equal(twoHash, await ComputeCapturedHashAsync(permuted.Solution));
        using var one = BuildWorkspace(solutionPath, WithReferences(firstReference));
        Assert.NotEqual(twoHash, await ComputeCapturedHashAsync(one.Solution));

        var aliasedReference = MetadataReference.CreateFromFile(firstPath,
            MetadataReferenceProperties.Assembly.WithAliases(["metadataAlias"]));
        using var aliased = BuildWorkspace(solutionPath, WithReferences(aliasedReference, secondReference));
        Assert.NotEqual(twoHash, await ComputeCapturedHashAsync(aliased.Solution));

        var primaryPath = AssemblyTestHelper.EmitAssembly(moduleFixture, "MultiModulePrimary", "namespace Metadata; public sealed class Primary { }");
        var firstModule = EmitNetModule("Secondary", "namespace Metadata; public sealed class Secondary { public int Value => 1; }");
        var secondModule = EmitNetModule("Secondary", "namespace Metadata; public sealed class Secondary { public int Value => 2; }");
        var primaryBytes = ImmutableArray.CreateRange(await File.ReadAllBytesAsync(primaryPath));
        var moduleReferenceA = CapturedMetadataReference.CreateFromImages([primaryBytes, firstModule], filePath: "C:\\memory\\primary.dll");
        var moduleReferenceB = CapturedMetadataReference.CreateFromImages([primaryBytes, secondModule], filePath: "C:\\memory\\primary.dll");
        using var multiA = BuildWorkspace(solutionPath, WithReferences(moduleReferenceA));
        using var multiB = BuildWorkspace(solutionPath, WithReferences(moduleReferenceB));
        Assert.NotEqual(await ComputeCapturedHashAsync(multiA.Solution), await ComputeCapturedHashAsync(multiB.Solution));
    }

    [Fact]
    public async Task DuplicatePhysicalProjectContextsKeepMultiplicityAndBindTextToFramework()
    {
        using var fixture = TestTempDirectory.Create("source-analysis-identity-contexts-");
        var solutionPath = fixture.GetPath("Contexts.slnx");
        var projectPath = fixture.GetPath("src/Shared/Shared.csproj");
        var sourcePath = fixture.GetPath("src/Shared/Shared.cs");

        using var first = CreateDuplicateContexts(solutionPath, projectPath, sourcePath,
            ("net10.0", "namespace Identity; public class ForNet10 { }"),
            ("net9.0", "namespace Identity; public class ForNet9 { }"));
        using var reordered = CreateDuplicateContexts(solutionPath, projectPath, sourcePath,
            ("net9.0", "namespace Identity; public class ForNet9 { }"),
            ("net10.0", "namespace Identity; public class ForNet10 { }"));
        using var swapped = CreateDuplicateContexts(solutionPath, projectPath, sourcePath,
            ("net10.0", "namespace Identity; public class ForNet9 { }"),
            ("net9.0", "namespace Identity; public class ForNet10 { }"));
        Assert.Equal(await ComputeControlledHashAsync(first.Solution), await ComputeControlledHashAsync(reordered.Solution));
        Assert.NotEqual(await ComputeControlledHashAsync(first.Solution), await ComputeControlledHashAsync(swapped.Solution));
        using var one = CreateDuplicateContexts(solutionPath, projectPath, sourcePath,
            ("net10.0", "namespace Identity; public class ForNet10 { }"));
        Assert.NotEqual(await ComputeControlledHashAsync(one.Solution), await ComputeControlledHashAsync(first.Solution));
    }

    [Fact]
    public async Task DuplicatePhysicalChildrenBindAliasedImagesToRootOwnerContext()
    {
        using var fixture = TestTempDirectory.Create("source-analysis-identity-owner-context-");
        var imageLeft = AssemblyTestHelper.EmitAssembly(fixture, "OwnerImageLeft",
            "namespace IdentityImages; public sealed class LeftImage { }");
        var imageRight = AssemblyTestHelper.EmitAssembly(fixture, "OwnerImageRight",
            "namespace IdentityImages; public sealed class RightImage { }");
        var solutionPath = fixture.GetPath("OwnerContexts.slnx");
        var rootPath = fixture.GetPath("src/Root/Root.csproj");
        var duplicateChildPath = fixture.GetPath("src/Shared/Shared.csproj");
        var sharedSourcePath = fixture.GetPath("src/Shared/Shared.cs");

        using var first = CreateBoundDuplicateContexts(solutionPath, rootPath, duplicateChildPath, sharedSourcePath,
            imageLeft, imageRight, reverseProjectOrder: false);
        using var reordered = CreateBoundDuplicateContexts(solutionPath, rootPath, duplicateChildPath, sharedSourcePath,
            imageLeft, imageRight, reverseProjectOrder: true);
        using var swapped = CreateBoundDuplicateContexts(solutionPath, rootPath, duplicateChildPath, sharedSourcePath,
            imageRight, imageLeft, reverseProjectOrder: false);

        var firstIdentity = await ComputeControlledIdentityAsync(first);
        var reorderedIdentity = await ComputeControlledIdentityAsync(reordered);
        var swappedIdentity = await ComputeControlledIdentityAsync(swapped);

        Assert.Equal(firstIdentity.Hash, reorderedIdentity.Hash);
        Assert.Equal(firstIdentity.RootContextFingerprint, reorderedIdentity.RootContextFingerprint);
        Assert.NotEqual(firstIdentity.Hash, swappedIdentity.Hash);
        Assert.NotEqual(firstIdentity.RootContextFingerprint, swappedIdentity.RootContextFingerprint);
        Assert.NotEqual(firstIdentity.LeftChildContextFingerprint, firstIdentity.RightChildContextFingerprint);
        Assert.NotEqual(swappedIdentity.LeftChildContextFingerprint, swappedIdentity.RightChildContextFingerprint);
        Assert.Equal(firstIdentity.LeftChildContextFingerprint, swappedIdentity.RightChildContextFingerprint);
        Assert.Equal(firstIdentity.RightChildContextFingerprint, swappedIdentity.LeftChildContextFingerprint);
    }

    [Fact]
    public async Task UnsupportedContextsProvidersAndUnprovenSigningStateReturnWorkspaceDiagnostic()
    {
        using var fixture = TestTempDirectory.Create("source-analysis-identity-unsupported-");
        using var workspace = BuildWorkspace(fixture.GetPath("Unsupported.slnx"),
            new ProjectSpec("Unsupported", [("Main.cs", "namespace Identity; public sealed class Main { }")], VirtualProjectDirectory: "src/Unsupported"));
        var solution = workspace.Solution;
        var project = Assert.Single(solution.Projects);

        var vbId = ProjectId.CreateNewId("VisualBasic");
        var withVisualBasic = solution.AddProject(ProjectInfo.Create(vbId, VersionStamp.Create(), "VisualBasic", "VisualBasic",
                LanguageNames.VisualBasic, filePath: Path.Combine(Path.GetDirectoryName(project.FilePath)!, "VisualBasic.vbproj"))
            .WithParseOptions(new VisualBasicParseOptions())
            .WithCompilationOptions(new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
        await AssertWorkspaceDiagnosticAsync(withVisualBasic, "does not use supported C# option types");

        // Roslyn's public Solution API fills omitted options with defaults when ProjectInfo is added. The missing-option
        // guard is defensive and cannot be reached through this public fixture API.
        var missingOptionsId = ProjectId.CreateNewId("MissingOptions");
        var missingOptions = solution.AddProject(ProjectInfo.Create(missingOptionsId, VersionStamp.Create(), "MissingOptions", "MissingOptions",
            LanguageNames.CSharp, filePath: Path.Combine(Path.GetDirectoryName(project.FilePath)!, "MissingOptions.csproj")));
        var normalizedProject = missingOptions.GetProject(missingOptionsId)!;
        Assert.IsType<CSharpParseOptions>(normalizedProject.ParseOptions);
        Assert.IsType<CSharpCompilationOptions>(normalizedProject.CompilationOptions);

        await AssertWorkspaceDiagnosticAsync(solution.WithProjectCompilationOptions(project.Id,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                sourceReferenceResolver: new UnsupportedSourceReferenceResolver())), "unsupported source reference resolver");
        await AssertWorkspaceDiagnosticAsync(solution.WithProjectCompilationOptions(project.Id,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                metadataReferenceResolver: new UnsupportedMetadataReferenceResolver())),
            "metadata reference resolver without matching creator provenance");
        var requestedSyntaxProvider = new UnsupportedSyntaxTreeOptionsProvider();
        var withRequestedSyntaxProvider = solution.WithProjectCompilationOptions(project.Id,
            ((CSharpCompilationOptions)project.CompilationOptions!).WithSyntaxTreeOptionsProvider(requestedSyntaxProvider));
        var loadedSyntaxProvider = withRequestedSyntaxProvider.GetProject(project.Id)!.CompilationOptions!.SyntaxTreeOptionsProvider;
        Assert.NotNull(loadedSyntaxProvider);
        Assert.NotSame(requestedSyntaxProvider, loadedSyntaxProvider);
        await AssertWorkspaceDiagnosticAsync(withRequestedSyntaxProvider,
            "syntax-tree options provider without matching creator provenance", syntaxTreeProviderHasCreatorProof: false);

        var keyFile = fixture.CreateFile("unproven.snk", "captured only by a source owner");
        await AssertWorkspaceDiagnosticAsync(solution.WithProjectCompilationOptions(project.Id,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, cryptoKeyFile: keyFile)),
            "signing key path has no complete loader-captured hash");
        await AssertWorkspaceDiagnosticAsync(solution.WithProjectCompilationOptions(project.Id,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, cryptoKeyContainer: "unproven-container")),
            "signing key container lacks complete loader-captured provenance");
    }

    private static Solution AddPathlessAdditionalDocument(Solution solution, ProjectId projectId, string name, IReadOnlyList<string> folders) =>
        solution.AddAdditionalDocument(DocumentId.CreateNewId(projectId), name, SourceText.From("pathless"), folders: folders);

    private static TestSolutionHandle BuildWorkspace(string solutionPath, params ProjectSpec[] projects)
    {
        var builder = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath);
        foreach (var project in projects) builder.WithProject(project);
        return builder.Build();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the AdhocWorkspace transfers to the returned TestSolutionHandle.")]
    private static TestSolutionHandle CreateDuplicateContexts(string solutionPath, string projectPath, string sourcePath,
        params (string Framework, string Source)[] contexts)
    {
        var workspace = new AdhocWorkspace();
        try
        {
            var solution = workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), filePath: solutionPath));
            foreach (var (framework, source) in contexts)
            {
                var id = ProjectId.CreateNewId("Shared");
                solution = solution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), "Shared", "Shared", LanguageNames.CSharp,
                        filePath: projectPath)
                    .WithParseOptions(new CSharpParseOptions())
                    .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
                solution = solution.AddDocument(DocumentId.CreateNewId(id), "Shared.cs", SourceText.From(source), filePath: sourcePath);
                solution = solution.AddAnalyzerConfigDocument(DocumentId.CreateNewId(id), ".globalconfig",
                    SourceText.From($"is_global = true\nbuild_property.TargetFramework = {framework}\n"), filePath: Path.ChangeExtension(projectPath, ".globalconfig"));
            }
            return new TestSolutionHandle(solution, workspace);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The returned fixture owns the TestSolutionHandle and disposes its AdhocWorkspace.")]
    private static BoundDuplicateContextFixture CreateBoundDuplicateContexts(string solutionPath, string rootPath,
        string duplicateChildPath, string sharedSourcePath, string leftImagePath, string rightImagePath,
        bool reverseProjectOrder)
    {
        var workspace = new AdhocWorkspace();
        try
        {
            var solution = workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), filePath: solutionPath));
            var rootId = ProjectId.CreateNewId("Root");
            var leftId = ProjectId.CreateNewId("Shared");
            var rightId = ProjectId.CreateNewId("Shared");
            var rootInfo = CreateCSharpProject(rootId, "Root", rootPath);
            var leftInfo = CreateCSharpProject(leftId, "Shared", duplicateChildPath);
            var rightInfo = CreateCSharpProject(rightId, "Shared", duplicateChildPath);

            foreach (var info in reverseProjectOrder
                         ? new[] { rootInfo, rightInfo, leftInfo }
                         : new[] { rootInfo, leftInfo, rightInfo })
                solution = solution.AddProject(info);

            solution = solution.AddDocument(DocumentId.CreateNewId(rootId), "Root.cs",
                SourceText.From("namespace Identity; public sealed class Root { }"), filePath: rootPath.Replace(".csproj", ".cs", StringComparison.OrdinalIgnoreCase));
            solution = solution.AddDocument(DocumentId.CreateNewId(leftId), "Shared.cs",
                SourceText.From("namespace Identity; public sealed class Shared { }"), filePath: sharedSourcePath);
            solution = solution.AddDocument(DocumentId.CreateNewId(rightId), "Shared.cs",
                SourceText.From("namespace Identity; public sealed class Shared { }"), filePath: sharedSourcePath);

            var leftChildImage = MetadataReference.CreateFromFile(leftImagePath);
            var rightChildImage = MetadataReference.CreateFromFile(rightImagePath);
            solution = solution.WithProjectMetadataReferences(leftId, [leftChildImage]);
            solution = solution.WithProjectMetadataReferences(rightId, [rightChildImage]);
            solution = solution.AddProjectReference(rootId, new ProjectReference(leftId, aliases: ["left"]));
            solution = solution.AddProjectReference(rootId, new ProjectReference(rightId, aliases: ["right"]));
            return new BoundDuplicateContextFixture(new TestSolutionHandle(solution, workspace), rootId, leftId, rightId);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static ProjectInfo CreateCSharpProject(ProjectId id, string name, string projectPath) =>
        ProjectInfo.Create(id, VersionStamp.Create(), name, name, LanguageNames.CSharp, filePath: projectPath)
            .WithParseOptions(new CSharpParseOptions())
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static Task<string> ComputeHashAsync(Solution solution) => ComputeCapturedHashAsync(solution);

    private static async Task<string> ComputeCapturedHashAsync(Solution solution)
    {
        var captured = await CaptureAndComputeAsync(solution);
        var result = captured.Result;
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value!.ContentHash;
    }

    private static async Task<(MetadataReferenceImageCapture.CapturedMetadataReferences Captured,
        Result<SourceIdentityFingerprintData> Result)> CaptureAndComputeAsync(Solution solution)
    {
        var captured = MetadataReferenceImageCapture.Capture(solution, previousInputs: null, CancellationToken.None);
        var result = await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(captured.Solution, captured.Inputs), CancellationToken.None);
        return (captured, result);
    }

    private static async Task<string> ComputeFromExactOwnerCreatedSolutionAsync(Solution solution)
    {
        // This is the trusted-owner boundary for a known public provider; no signing file or key container is configured.
        var ownerProof = WorkspaceInputProvenance.CreateFromTrustedLoader(solution);
        var captured = MetadataReferenceImageCapture.Capture(solution, previousInputs: null, CancellationToken.None,
            provenance: ownerProof);
        var result = await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(captured.Solution, captured.Inputs), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value!.ContentHash;
    }

    private static async Task<Result<SourceIdentityFingerprintData>> ComputeResultFromExactOwnerCreatedSolutionAsync(Solution solution)
    {
        var ownerProof = WorkspaceInputProvenance.CreateFromTrustedLoader(solution);
        var captured = MetadataReferenceImageCapture.Capture(solution, previousInputs: null, CancellationToken.None,
            provenance: ownerProof);
        return await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(captured.Solution, captured.Inputs), CancellationToken.None);
    }

    private static ImmutableArray<byte> EmitIdentityGenerator(string outputPath, string version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var platformPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = platformPaths.Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(ISourceGenerator).Assembly.Location)).ToImmutableArray();
        var source = $$"""
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Text;
            using System.Text;
            [Generator]
            public sealed class IdentityGenerator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }
                public void Execute(GeneratorExecutionContext context) => context.AddSource("Identity.g.cs", SourceText.From(
                    "namespace Identity.Generated; public sealed class GeneratedType { public const string Version = \"{{version}}\"; }", Encoding.UTF8));
            }
            """;
        var compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(outputPath), [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var imageBytes = ImmutableArray.CreateRange(stream.ToArray());
        File.WriteAllBytes(outputPath, imageBytes.ToArray());
        return imageBytes;
    }

    private sealed class IsolatedAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader, IDisposable
    {
        private readonly GeneratorLoadContext loadContext = new();

        public Assembly LoadFromPath(string fullPath) => loadContext.LoadFromAssemblyPath(Path.GetFullPath(fullPath));

        public void AddDependencyLocation(string fullPath) => _ = Path.GetFullPath(fullPath);

        public void Dispose() => loadContext.Unload();

        private sealed class GeneratorLoadContext : AssemblyLoadContext
        {
            internal GeneratorLoadContext() : base(isCollectible: true) { }

            protected override Assembly? Load(AssemblyName assemblyName)
            {
                var codeAnalysis = typeof(ISourceGenerator).Assembly.GetName();
                return StringComparer.Ordinal.Equals(assemblyName.Name, codeAnalysis.Name)
                    ? Default.LoadFromAssemblyName(assemblyName)
                    : null;
            }
        }
    }

    private static async Task<string> ComputeControlledHashAsync(Solution solution)
    {
        // Controlled encoder fixture for synthetic pathless documents and duplicate owner coordinates; no loader-acceptance claim.
        var captured = MetadataReferenceImageCapture.Capture(solution, previousInputs: null, CancellationToken.None);
        var projects = solution.Projects.Select(project => new SourceIdentityProjectProvenance(
            project.Id, true, null, ImmutableArray<SourceIdentityCapturedInput>.Empty,
            OwnerCreatedAnalyzerConfigProvider: true,
            OwnerCreatedSyntaxTreeOptionsProvider: project.CompilationOptions?.SyntaxTreeOptionsProvider is not null)).ToImmutableArray();
        var inputs = captured.Inputs with { Projects = projects };
        var result = await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(captured.Solution, inputs), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value!.ContentHash;
    }

    private static async Task<(string Hash, string RootContextFingerprint, string LeftChildContextFingerprint,
        string RightChildContextFingerprint)> ComputeControlledIdentityAsync(BoundDuplicateContextFixture fixture)
    {
        // Controlled duplicate-owner fixture: captured PE images are real, while empty project provenance isolates encoder association.
        var captured = MetadataReferenceImageCapture.Capture(fixture.Handle.Solution, previousInputs: null, CancellationToken.None);
        var projects = fixture.Handle.Solution.Projects.Select(project => new SourceIdentityProjectProvenance(
            project.Id, true, null, ImmutableArray<SourceIdentityCapturedInput>.Empty,
            OwnerCreatedAnalyzerConfigProvider: true,
            OwnerCreatedSyntaxTreeOptionsProvider: project.CompilationOptions?.SyntaxTreeOptionsProvider is not null)).ToImmutableArray();
        var inputs = captured.Inputs with { Projects = projects };
        var result = await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(captured.Solution, inputs), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);

        var orderedProjects = fixture.Handle.Solution.Projects.Select(project => project.Id).ToArray();
        string Context(ProjectId id) => result.Value!.ProjectMarkers[Array.IndexOf(orderedProjects, id)].ContextFingerprint;
        return (result.Value!.ContentHash, Context(fixture.RootProjectId), Context(fixture.LeftChildProjectId),
            Context(fixture.RightChildProjectId));
    }

    private sealed record BoundDuplicateContextFixture(TestSolutionHandle Handle, ProjectId RootProjectId,
        ProjectId LeftChildProjectId, ProjectId RightChildProjectId) : IDisposable
    {
        public void Dispose() => Handle.Dispose();
    }

    private static async Task AssertWorkspaceDiagnosticAsync(Solution solution, string expectedReason,
        bool? syntaxTreeProviderHasCreatorProof = null)
    {
        // This direct encoder fixture captures real PE evidence and only marks the builder's unchanged syntax provider as owner-created.
        var captured = MetadataReferenceImageCapture.Capture(solution, previousInputs: null, CancellationToken.None);
        var provenance = solution.Projects.Select(project => new SourceIdentityProjectProvenance(
            project.Id, true, null, ImmutableArray<SourceIdentityCapturedInput>.Empty,
            OwnerCreatedAnalyzerConfigProvider: true,
            OwnerCreatedSyntaxTreeOptionsProvider: syntaxTreeProviderHasCreatorProof
                ?? (project.CompilationOptions?.SyntaxTreeOptionsProvider is not null
                    and not UnsupportedSyntaxTreeOptionsProvider))).ToImmutableArray();
        var inputs = captured.Inputs with { Projects = provenance };
        var result = await SourceAnalysisIdentityEncoder.ComputeAsync(new SourceIdentityValidatedSnapshot(captured.Solution, inputs), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(NavigationErrorCodes.WorkspaceDiagnostic, result.Error!.Value.Code);
        Assert.Contains(expectedReason, result.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ImmutableArray<byte> EmitNetModule(string name, string source)
    {
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)],
            TestWorkspaceBuilder.CoreReferences, new CSharpCompilationOptions(OutputKind.NetModule));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return ImmutableArray.CreateRange(stream.ToArray());
    }

    private sealed class TestGeneratorReference(ISourceGenerator generator) : AnalyzerReference
    {
        public override string? FullPath => null;
        public override string Display => "Source identity generator fixture";
        public override object Id => "Source identity generator fixture";
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => [];
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [];
        public override ImmutableArray<ISourceGenerator> GetGenerators(string language) => language == LanguageNames.CSharp ? [generator] : [];
        public override ImmutableArray<ISourceGenerator> GetGeneratorsForAllLanguages() => [generator];
    }

    private sealed class IdentityGeneratedSourceGenerator(string version) : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterPostInitializationOutput(output => output.AddSource("GeneratedIdentity.g.cs",
                $"namespace Identity.Generated; public sealed class GeneratedType {{ public const string Version = \"{version}\"; }}"));
    }

    private sealed class UnsupportedSourceReferenceResolver : SourceReferenceResolver
    {
        public override string? NormalizePath(string path, string? baseFilePath) => path;
        public override string? ResolveReference(string path, string? baseFilePath) => path;
        public override Stream OpenRead(string resolvedPath) => Stream.Null;
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
    }

    private sealed class UnsupportedMetadataReferenceResolver : MetadataReferenceResolver
    {
        public override bool ResolveMissingAssemblies => false;
        public override PortableExecutableReference? ResolveMissingAssembly(MetadataReference definition, AssemblyIdentity referenceIdentity) => null;
        public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath,
            MetadataReferenceProperties properties) => [];
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
    }

    private sealed class UnsupportedSyntaxTreeOptionsProvider : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) => default;
        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken,
            out ReportDiagnostic severity) { severity = default; return false; }
        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken,
            out ReportDiagnostic severity) { severity = default; return false; }
    }
}
