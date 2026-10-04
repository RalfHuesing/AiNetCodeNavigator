#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
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

namespace AiNetCodeNavigator.FastTests.Workspace;

// @covers MetadataReferenceImageCapture
// @covers SourceAnalysisIdentityEncoder
[Trait("Category", "Unit")]
public sealed class ResidentSolutionAnalyzerFreshnessTests
{
    [Fact]
    public async Task FileBackedGeneratorWithUncapturedPrivateDependencyIsRejectedForIdentityReuse()
    {
        using var fixture = TestTempDirectory.Create("resident-generator-private-dependency-");
        var solutionPath = fixture.CreateFile("Generator.slnx", string.Empty);
        var dependencyPath = fixture.GetPath("private-dependencies/PrivateGeneratorDependency.dll");
        var generatorPath = fixture.GetPath("analyzers/PrivateGenerator.dll");
        EmitAssembly(
            dependencyPath,
            "PrivateGeneratorDependency",
            "namespace PrivateGeneratorDependency; public static class Marker { public static string GetValue() => \"DependencyWasLoaded\"; }");
        EmitGenerator(generatorPath, dependencyPath);
        Assert.Contains("PrivateGeneratorDependency", ReadAssemblyReferences(generatorPath), StringComparer.Ordinal);

        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("GeneratorConsumer", [
                ("Consumer.cs", "namespace GeneratorConsumer; public sealed class Consumer { }")],
                VirtualProjectDirectory: "src/GeneratorConsumer"))
            .Build();
        var project = Assert.Single(workspace.Solution.Projects);
        using var analyzerLoader = new IsolatedAnalyzerAssemblyLoader();
        analyzerLoader.AddDependencyLocation(dependencyPath);
        var solution = workspace.Solution.AddAnalyzerReference(
            project.Id,
            new AnalyzerFileReference(generatorPath, analyzerLoader));

        var generatedDocuments = await solution.GetProject(project.Id)!.GetSourceGeneratedDocumentsAsync(CancellationToken.None);
        var generatedDocument = Assert.Single(generatedDocuments);
        Assert.Contains("DependencyWasLoaded", (await generatedDocument.GetTextAsync()).ToString(), StringComparison.Ordinal);
        Assert.Equal(CanonicalPath(dependencyPath), analyzerLoader.LoadedDependencyPath);

        var capture = MetadataReferenceImageCapture.Capture(
            solution,
            previousInputs: null,
            cancellationToken: CancellationToken.None,
            provenance: WorkspaceInputProvenance.CreateFromTrustedLoader(solution));
        var generated = Assert.Single(await capture.Solution.GetProject(project.Id)!.GetSourceGeneratedDocumentsAsync(CancellationToken.None));
        Assert.Contains("DependencyWasLoaded", (await generated.GetTextAsync()).ToString(), StringComparison.Ordinal);
        var unsupportedProject = Assert.Single(capture.Inputs.Projects);
        Assert.False(unsupportedProject.IsSupported);
        Assert.Contains("complete creator-supplied immutable input contract", unsupportedProject.UnsupportedReason, StringComparison.Ordinal);

        var identity = await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(capture.Solution, capture.Inputs),
            CancellationToken.None);

        Assert.False(identity.IsSuccess);
        Assert.Equal(NavigationErrorCodes.WorkspaceDiagnostic, identity.Error!.Value.Code);
        Assert.Contains(Path.GetFileName(generatorPath), identity.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GeneratorDefaultLoadContextExternalDependencyIsNotCapturedAndMustFailClosed()
    {
        using var fixture = TestTempDirectory.Create("resident-generator-default-context-dependency-");
        var solutionPath = fixture.CreateFile("Generator.slnx", string.Empty);
        var uniqueName = "DefaultContextPrivate" + Guid.NewGuid().ToString("N");
        var dependencyPath = fixture.GetPath($"external/{uniqueName}.dll");
        var generatorPath = fixture.GetPath("analyzers/DefaultContextGenerator.dll");
        EmitAssembly(
            dependencyPath,
            uniqueName,
            "namespace DefaultContextPrivate; public static class Marker { public static string GetValue() => \"Value17\"; }");
        EmitDefaultContextLoadGenerator(generatorPath, dependencyPath);
        Assert.DoesNotContain(uniqueName, ReadAssemblyReferences(generatorPath), StringComparer.Ordinal);

        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("GeneratorConsumer", [
                ("Consumer.cs", "namespace GeneratorConsumer; public sealed class Consumer { }")],
                VirtualProjectDirectory: "src/GeneratorConsumer"))
            .Build();
        var project = Assert.Single(workspace.Solution.Projects);
        using var analyzerLoader = new IsolatedAnalyzerAssemblyLoader();
        var solution = workspace.Solution.AddAnalyzerReference(project.Id, new AnalyzerFileReference(generatorPath, analyzerLoader));
        var capture = MetadataReferenceImageCapture.Capture(
            solution,
            previousInputs: null,
            cancellationToken: CancellationToken.None,
            provenance: WorkspaceInputProvenance.CreateFromTrustedLoader(solution));

        var generated = Assert.Single(await capture.Solution.GetProject(project.Id)!.GetSourceGeneratedDocumentsAsync(CancellationToken.None));
        Assert.Contains("Value17", (await generated.GetTextAsync()).ToString(), StringComparison.Ordinal);
        var bindingInputs = Assert.Single(capture.Inputs.Projects).BindingInputs;
        var generatorInput = Assert.Single(bindingInputs, input => input.Kind == "analyzer-image");
        Assert.Equal(CanonicalPath(generatorPath), generatorInput.LogicalKey);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(generatorPath))), generatorInput.Sha256);
        Assert.DoesNotContain(bindingInputs, input => input.LogicalKey.Contains(uniqueName, StringComparison.OrdinalIgnoreCase));

        var identity = await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(capture.Solution, capture.Inputs),
            CancellationToken.None);
        Assert.False(identity.IsSuccess);
        Assert.Equal(NavigationErrorCodes.WorkspaceDiagnostic, identity.Error!.Value.Code);
        Assert.Contains(Path.GetFileName(generatorPath), identity.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FileBackedGeneratorWithResolvedPrivateDependencyCapturesAndBindsTheSameImages()
    {
        using var fixture = TestTempDirectory.Create("resident-generator-captured-dependency-");
        var solutionPath = fixture.CreateFile("Generator.slnx", string.Empty);
        var dependencyPath = fixture.GetPath("analyzers/PrivateGeneratorDependency.dll");
        var generatorPath = fixture.GetPath("analyzers/PrivateGenerator.dll");
        var dependencyBytes = EmitAssembly(
            dependencyPath,
            "PrivateGeneratorDependency",
            "namespace PrivateGeneratorDependency; public static class Marker { public static string GetValue() => \"DependencyWasLoaded\"; }");
        var generatorBytes = EmitGenerator(generatorPath, dependencyPath);
        Assert.Contains("PrivateGeneratorDependency", ReadAssemblyReferences(generatorPath), StringComparer.Ordinal);

        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("GeneratorConsumer", [
                ("Consumer.cs", "namespace GeneratorConsumer; public sealed class Consumer { }")],
                VirtualProjectDirectory: "src/GeneratorConsumer"))
            .Build();
        var project = Assert.Single(workspace.Solution.Projects);
        using var analyzerLoader = new IsolatedAnalyzerAssemblyLoader();
        analyzerLoader.AddDependencyLocation(dependencyPath);
        var solution = workspace.Solution.AddAnalyzerReference(
            project.Id,
            new AnalyzerFileReference(generatorPath, analyzerLoader));
        var capture = MetadataReferenceImageCapture.Capture(
            solution,
            previousInputs: null,
            cancellationToken: CancellationToken.None,
            provenance: WorkspaceInputProvenance.CreateForCreatorContracts(solution, [
                GeneratorCreatorInputContract.CreateFromProducedImages(project.FilePath!, generatorPath, [
                    GeneratorCreatorInputImage.FromBytes(generatorPath, generatorBytes.AsSpan()),
                    GeneratorCreatorInputImage.FromBytes(dependencyPath, dependencyBytes.AsSpan()),
                ]),
            ]));

        var projectInputs = Assert.Single(capture.Inputs.Projects).BindingInputs;
        var generatorInput = Assert.Single(projectInputs, input => input.Kind == "analyzer-image");
        Assert.Equal(CanonicalPath(generatorPath), generatorInput.LogicalKey);
        Assert.Contains(projectInputs, input => input.Kind == "analyzer-dependency-image" && input.LogicalKey == CanonicalPath(dependencyPath));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(dependencyPath))),
            Assert.Single(projectInputs, input => input.Kind == "analyzer-dependency-image").Sha256);
        var capturedGeneratorPath = Assert.IsType<AnalyzerFileReference>(
            Assert.Single(capture.Solution.GetProject(project.Id)!.AnalyzerReferences)).FullPath;
        Assert.NotEqual(CanonicalPath(generatorPath), CanonicalPath(capturedGeneratorPath));
        Assert.Equal(
            generatorInput.Sha256,
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(capturedGeneratorPath))));

        var generated = Assert.Single(await capture.Solution.GetProject(project.Id)!.GetSourceGeneratedDocumentsAsync(CancellationToken.None));
        Assert.Contains("DependencyWasLoaded", (await generated.GetTextAsync()).ToString(), StringComparison.Ordinal);
        var identity = await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(capture.Solution, capture.Inputs),
            CancellationToken.None);
        Assert.True(identity.IsSuccess, identity.Error?.Message);
    }

    [Fact]
    public async Task ReplacedGeneratorAtSamePathAndTimestampRebindsCapturedBytesAndPreservesOldSnapshot()
    {
        using var fixture = TestTempDirectory.Create("resident-generator-same-path-");
        var solutionPath = fixture.CreateFile("Generator.slnx", string.Empty);
        var generatorPath = fixture.GetPath("analyzers/VersionedGenerator.dll");
        var alphaBytes = EmitLiteralGenerator(generatorPath, "Alpha");
        var bravoBytes = EmitLiteralGenerator(generatorPath, "Bravo");
        File.WriteAllBytes(generatorPath, alphaBytes.AsSpan());

        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("GeneratorConsumer", [
                ("Consumer.cs", "namespace GeneratorConsumer; public sealed class Consumer { }")],
                VirtualProjectDirectory: "src/GeneratorConsumer"))
            .Build();
        var project = Assert.Single(workspace.Solution.Projects);
        using var analyzerLoader = new IsolatedAnalyzerAssemblyLoader();
        var solution = workspace.Solution.AddAnalyzerReference(project.Id, new AnalyzerFileReference(generatorPath, analyzerLoader));
        var provenance = WorkspaceInputProvenance.CreateForCreatorContracts(solution, [
            GeneratorCreatorInputContract.CreateFromProducedImages(project.FilePath!, generatorPath, [
                GeneratorCreatorInputImage.FromBytes(generatorPath, alphaBytes.AsSpan()),
            ]),
            GeneratorCreatorInputContract.CreateFromProducedImages(project.FilePath!, generatorPath, [
                GeneratorCreatorInputImage.FromBytes(generatorPath, bravoBytes.AsSpan()),
            ]),
        ]);
        var initial = MetadataReferenceImageCapture.Capture(
            solution,
            previousInputs: null,
            cancellationToken: CancellationToken.None,
            provenance: provenance);
        var initialProject = initial.Solution.GetProject(project.Id)!;
        var initialGenerated = Assert.Single(await initialProject.GetSourceGeneratedDocumentsAsync(CancellationToken.None));
        Assert.Contains("Value = \"Alpha\"", (await initialGenerated.GetTextAsync()).ToString(), StringComparison.Ordinal);
        var originalLength = new FileInfo(generatorPath).Length;
        var originalWriteTime = File.GetLastWriteTimeUtc(generatorPath);
        var originalSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(generatorPath)));
        Assert.Equal(
            originalSha256,
            Assert.Single(initial.Inputs.Projects).BindingInputs.Single(input => input.Kind == "analyzer-image").Sha256);

        File.WriteAllBytes(generatorPath, bravoBytes.AsSpan());
        Assert.Equal(originalLength, new FileInfo(generatorPath).Length);
        File.SetLastWriteTimeUtc(generatorPath, originalWriteTime);
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(generatorPath));

        var refreshed = MetadataReferenceImageCapture.Capture(
            initial.Solution,
            initial.Inputs,
            cancellationToken: CancellationToken.None,
            provenance: initial.Provenance);

        Assert.True(refreshed.RequiresWorkspaceReload);
        Assert.NotSame(initial.Solution, refreshed.Solution);
        Assert.NotEqual(
            originalSha256,
            Assert.Single(refreshed.Inputs.Projects).BindingInputs.Single(input => input.Kind == "analyzer-image").Sha256);
        var refreshedGenerated = Assert.Single(await refreshed.Solution.GetProject(project.Id)!.GetSourceGeneratedDocumentsAsync(CancellationToken.None));
        Assert.Contains("Value = \"Bravo\"", (await refreshedGenerated.GetTextAsync()).ToString(), StringComparison.Ordinal);
        var oldGenerated = Assert.Single(await initialProject.GetSourceGeneratedDocumentsAsync(CancellationToken.None));
        Assert.Contains("Value = \"Alpha\"", (await oldGenerated.GetTextAsync()).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DynamicAnalyzerWithoutCreatorContractRejectsIdentityReuse()
    {
        using var fixture = TestTempDirectory.Create("resident-generator-dynamic-dependency-");
        var solutionPath = fixture.CreateFile("Generator.slnx", string.Empty);
        var generatorPath = fixture.GetPath("analyzers/DynamicLoadGenerator.dll");
        EmitDynamicLoadGenerator(generatorPath);

        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("GeneratorConsumer", [
                ("Consumer.cs", "namespace GeneratorConsumer; public sealed class Consumer { }")],
                VirtualProjectDirectory: "src/GeneratorConsumer"))
            .Build();
        var project = Assert.Single(workspace.Solution.Projects);
        using var analyzerLoader = new IsolatedAnalyzerAssemblyLoader();
        var solution = workspace.Solution.AddAnalyzerReference(project.Id, new AnalyzerFileReference(generatorPath, analyzerLoader));
        var capture = MetadataReferenceImageCapture.Capture(
            solution,
            previousInputs: null,
            cancellationToken: CancellationToken.None,
            provenance: WorkspaceInputProvenance.CreateFromTrustedLoader(solution));
        var generated = Assert.Single(await capture.Solution.GetProject(project.Id)!.GetSourceGeneratedDocumentsAsync(CancellationToken.None));
        Assert.Contains("class Generated", (await generated.GetTextAsync()).ToString(), StringComparison.Ordinal);

        var identity = await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(capture.Solution, capture.Inputs),
            CancellationToken.None);

        Assert.False(identity.IsSuccess);
        Assert.Equal(NavigationErrorCodes.WorkspaceDiagnostic, identity.Error!.Value.Code);
        Assert.Contains(project.FilePath!, identity.Error.Value.Message, StringComparison.Ordinal);
        Assert.Contains(generatorPath, identity.Error.Value.Message, StringComparison.Ordinal);
        Assert.Contains("without a complete creator-supplied immutable input contract", identity.Error.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NormalMsBuildMetadataResolverIsCapturedWhenMissingAssemblyResolutionIsDisabled()
    {
        using var fixture = TestTempDirectory.Create("resident-normal-metadata-resolver-");
        var solutionPath = fixture.CreateFile("Normal.slnx",
            "<Solution><Project Path=\"src/Normal/Normal.csproj\" /></Solution>");
        fixture.CreateFile("src/Normal/Normal.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        fixture.CreateFile("src/Normal/Normal.cs", "namespace Normal; public sealed class Probe { }");

        var loaded = await MSBuildSolutionLoader.LoadResidentStateAsync(solutionPath, CancellationToken.None);
        using var loadedWorkspace = loaded.Workspace!;
        var project = Assert.Single(loaded.Solution.Projects);
        var resolver = Assert.IsAssignableFrom<MetadataReferenceResolver>(project.CompilationOptions!.MetadataReferenceResolver);
        Assert.False(resolver.ResolveMissingAssemblies);

        var capture = MetadataReferenceImageCapture.Capture(
            loaded.Solution,
            previousInputs: null,
            cancellationToken: CancellationToken.None,
            provenance: loaded.InputProvenance);
        var identity = await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(capture.Solution, capture.Inputs),
            CancellationToken.None);
        Assert.True(identity.IsSuccess, identity.Error?.Message);
    }

    [Fact]
    public async Task AnalyzerImageReplacementFromAnalyzerOnlyToGeneratorRefreshesCachedClassification()
    {
        using var fixture = TestTempDirectory.Create("resident-analyzer-to-generator-refresh-");
        var solutionPath = fixture.CreateFile("Generator.slnx", string.Empty);
        var generatorPath = fixture.GetPath("analyzers/ChangingAnalyzer.dll");
        var analyzerBytes = EmitAnalyzer(generatorPath);
        var generatorBytes = EmitLiteralGenerator(generatorPath, "FreshGenerator");
        File.WriteAllBytes(generatorPath, analyzerBytes.AsSpan());

        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("GeneratorConsumer", [
                ("Consumer.cs", "namespace GeneratorConsumer; public sealed class Consumer { }")],
                VirtualProjectDirectory: "src/GeneratorConsumer"))
            .Build();
        var project = Assert.Single(workspace.Solution.Projects);
        using var analyzerLoader = new IsolatedAnalyzerAssemblyLoader();
        var solution = workspace.Solution.AddAnalyzerReference(project.Id, new AnalyzerFileReference(generatorPath, analyzerLoader));
        var provenance = WorkspaceInputProvenance.CreateForCreatorContracts(solution, [
            GeneratorCreatorInputContract.CreateFromProducedImages(project.FilePath!, generatorPath, [
                GeneratorCreatorInputImage.FromBytes(generatorPath, generatorBytes.AsSpan()),
            ]),
        ]);
        var initial = MetadataReferenceImageCapture.Capture(
            solution,
            previousInputs: null,
            cancellationToken: CancellationToken.None,
            provenance: provenance);
        var analyzerSha256 = Convert.ToHexString(SHA256.HashData(analyzerBytes.AsSpan()));
        Assert.Equal(
            analyzerSha256,
            Assert.Single(Assert.Single(initial.Inputs.Projects).BindingInputs, input => input.Kind == "analyzer-image").Sha256);
        var capturedAnalyzerPath = Assert.IsType<AnalyzerFileReference>(
            Assert.Single(initial.Solution.GetProject(project.Id)!.AnalyzerReferences)).FullPath;
        Assert.NotEqual(CanonicalPath(generatorPath), CanonicalPath(capturedAnalyzerPath));
        Assert.Equal(analyzerSha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(capturedAnalyzerPath))));
        var initialIdentity = await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(initial.Solution, initial.Inputs),
            CancellationToken.None);
        Assert.True(initialIdentity.IsSuccess, initialIdentity.Error?.Message);

        File.WriteAllBytes(generatorPath, generatorBytes.AsSpan());
        var refreshed = MetadataReferenceImageCapture.Capture(
            initial.Solution,
            initial.Inputs,
            cancellationToken: CancellationToken.None,
            provenance: initial.Provenance);

        Assert.True(refreshed.RequiresWorkspaceReload);
        Assert.NotSame(initial.Solution, refreshed.Solution);
        Assert.Contains(
            Assert.Single(refreshed.Inputs.Projects).BindingInputs,
            input => input.Kind == "analyzer-image"
                && StringComparer.Ordinal.Equals(input.Sha256, Convert.ToHexString(SHA256.HashData(generatorBytes.AsSpan()))));
        var refreshedAnalyzerPath = Assert.IsType<AnalyzerFileReference>(
            Assert.Single(refreshed.Solution.GetProject(project.Id)!.AnalyzerReferences)).FullPath;
        Assert.NotEqual(CanonicalPath(generatorPath), CanonicalPath(refreshedAnalyzerPath));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(generatorBytes.AsSpan())),
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(refreshedAnalyzerPath))));
        var generated = Assert.Single(await refreshed.Solution.GetProject(project.Id)!.GetSourceGeneratedDocumentsAsync(CancellationToken.None));
        Assert.Contains("FreshGenerator", (await generated.GetTextAsync()).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceRejectsMemoHitWhenFreshCaptureLosesGeneratorCreatorContract()
    {
        using var fixture = TestTempDirectory.Create("resident-generator-creator-contract-refresh-");
        var solutionPath = fixture.CreateFile("Generator.slnx", string.Empty);
        var generatorPath = fixture.GetPath("analyzers/StableGenerator.dll");
        var generatorBytes = EmitLiteralGenerator(generatorPath, "StableGenerator");
        File.WriteAllBytes(generatorPath, generatorBytes.AsSpan());

        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("GeneratorConsumer", [
                ("Consumer.cs", "namespace GeneratorConsumer; public sealed class Consumer { }")],
                VirtualProjectDirectory: "src/GeneratorConsumer"))
            .Build();
        var project = Assert.Single(workspace.Solution.Projects);
        using var analyzerLoader = new IsolatedAnalyzerAssemblyLoader();
        var solution = workspace.Solution.AddAnalyzerReference(project.Id, new AnalyzerFileReference(generatorPath, analyzerLoader));
        var creatorContract = GeneratorCreatorInputContract.CreateFromProducedImages(project.FilePath!, generatorPath, [
            GeneratorCreatorInputImage.FromBytes(generatorPath, generatorBytes.AsSpan()),
        ]);
        var initial = MetadataReferenceImageCapture.Capture(
            solution,
            previousInputs: null,
            cancellationToken: CancellationToken.None,
            provenance: WorkspaceInputProvenance.CreateForCreatorContracts(solution, [creatorContract]));
        await using var identityService = new AnalysisSymbolIdentityService();
        var initialIdentity = await identityService.GetForSourceAsync(
            new SourceIdentityValidatedSnapshot(initial.Solution, initial.Inputs),
            CancellationToken.None);
        Assert.True(initialIdentity.IsSuccess, initialIdentity.Error?.Message);

        var freshCaptureWithoutCreator = MetadataReferenceImageCapture.Capture(
            initial.Solution,
            initial.Inputs,
            cancellationToken: CancellationToken.None,
            provenance: WorkspaceInputProvenance.CreateFromTrustedLoader(initial.Solution));

        Assert.Same(initial.Solution, freshCaptureWithoutCreator.Solution);
        var unsupportedProject = Assert.Single(freshCaptureWithoutCreator.Inputs.Projects);
        Assert.False(unsupportedProject.IsSupported);
        Assert.Contains("without a complete creator-supplied immutable input contract", unsupportedProject.UnsupportedReason, StringComparison.Ordinal);
        var identity = await identityService.GetForSourceAsync(
            new SourceIdentityValidatedSnapshot(freshCaptureWithoutCreator.Solution, freshCaptureWithoutCreator.Inputs),
            CancellationToken.None);
        Assert.False(identity.IsSuccess);
        Assert.Equal(NavigationErrorCodes.WorkspaceDiagnostic, identity.Error!.Value.Code);
    }

    [Fact]
    public async Task ChangedConsumedGeneratorCreatorInputRequiresWorkspaceReload()
    {
        using var fixture = TestTempDirectory.Create("resident-generator-consumed-input-refresh-");
        var solutionPath = fixture.CreateFile("Generator.slnx", string.Empty);
        var metadataPath = fixture.GetPath("references/ReloadTriggerApi.dll");
        EmitAssembly(
            metadataPath,
            "ReloadTriggerApi",
            "namespace ReloadTriggerApi; public static class Marker { public const int Value = 17; }");
        var generatorPath = fixture.GetPath("analyzers/FileInputGenerator.dll");
        var inputPath = fixture.CreateFile("generator-input.txt", "CreatorInputValue=17");
        var generatorBytes = EmitFileInputGenerator(generatorPath, inputPath);

        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("MetadataConsumer", [
                ("Metadata.cs", "namespace MetadataConsumer; public sealed class Consumer { public int Value => ReloadTriggerApi.Marker.Value; }")],
                AdditionalReferences: [MetadataReference.CreateFromFile(metadataPath)],
                VirtualProjectDirectory: "src/MetadataConsumer"))
            .WithProject(new ProjectSpec("GeneratorConsumer", [
                ("Consumer.cs", "namespace GeneratorConsumer; public sealed class Consumer { }")],
                VirtualProjectDirectory: "src/GeneratorConsumer"))
            .Build();
        var project = Assert.Single(workspace.Solution.Projects, candidate => candidate.Name == "GeneratorConsumer");
        var metadataProject = Assert.Single(workspace.Solution.Projects, candidate => candidate.Name == "MetadataConsumer");
        using var analyzerLoader = new IsolatedAnalyzerAssemblyLoader();
        var solution = workspace.Solution.AddAnalyzerReference(project.Id, new AnalyzerFileReference(generatorPath, analyzerLoader));
        var initialInputBytes = await File.ReadAllBytesAsync(inputPath);
        var initialContract = GeneratorCreatorInputContract.CreateFromProducedImages(project.FilePath!, generatorPath, [
            GeneratorCreatorInputImage.FromBytes(generatorPath, generatorBytes.AsSpan()),
            GeneratorCreatorInputImage.FromBytes(inputPath, initialInputBytes),
        ]);
        var initial = MetadataReferenceImageCapture.Capture(
            solution,
            previousInputs: null,
            cancellationToken: CancellationToken.None,
            provenance: WorkspaceInputProvenance.CreateForCreatorContracts(solution, [initialContract]));
        var initialProject = Assert.Single(initial.Inputs.Projects, candidate => candidate.OwnerProjectId == project.Id);
        Assert.True(initialProject.IsSupported, initialProject.UnsupportedReason);
        var initialOutput = Assert.Single(await initial.Solution.GetProject(project.Id)!.GetSourceGeneratedDocumentsAsync(CancellationToken.None));
        Assert.Contains("CreatorInputValue=17", (await initialOutput.GetTextAsync()).ToString(), StringComparison.Ordinal);
        var initialCreatorInput = Assert.Single(initialProject.BindingInputs, input =>
            input.Kind == "generator-creator-input" && input.LogicalKey.Contains(CanonicalPath(inputPath), StringComparison.Ordinal));
        var initialMetadataReference = Assert.Single(initial.Solution.GetProject(metadataProject.Id)!.MetadataReferences
            .OfType<PortableExecutableReference>()
            .Where(reference => StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(reference.FilePath!), Path.GetFullPath(metadataPath))));

        var inputTimestamp = File.GetLastWriteTimeUtc(inputPath);
        await File.WriteAllTextAsync(inputPath, "CreatorInputValue=18");
        File.SetLastWriteTimeUtc(inputPath, inputTimestamp);
        var updatedInputBytes = await File.ReadAllBytesAsync(inputPath);
        var metadataTimestamp = File.GetLastWriteTimeUtc(metadataPath);
        EmitAssembly(
            metadataPath,
            "ReloadTriggerApi",
            "namespace ReloadTriggerApi; public static class Marker { public const int Value = 18; }");
        File.SetLastWriteTimeUtc(metadataPath, metadataTimestamp);
        var updatedContract = GeneratorCreatorInputContract.CreateFromProducedImages(project.FilePath!, generatorPath, [
            GeneratorCreatorInputImage.FromBytes(generatorPath, generatorBytes.AsSpan()),
            GeneratorCreatorInputImage.FromBytes(inputPath, updatedInputBytes),
        ]);
        var updatedProvenance = WorkspaceInputProvenance.CreateForCreatorContracts(initial.Solution, [updatedContract]);
        var refreshed = MetadataReferenceImageCapture.Capture(
            initial.Solution,
            initial.Inputs,
            cancellationToken: CancellationToken.None,
            provenance: updatedProvenance);

        Assert.True(refreshed.SolutionChanged);
        Assert.NotSame(initial.Solution, refreshed.Solution);
        Assert.True(refreshed.RequiresWorkspaceReload);
        var refreshedProject = Assert.Single(refreshed.Inputs.Projects, candidate => candidate.OwnerProjectId == project.Id);
        Assert.True(refreshedProject.IsSupported, refreshedProject.UnsupportedReason);
        var refreshedCreatorInput = Assert.Single(refreshedProject.BindingInputs, input =>
            input.Kind == "generator-creator-input" && input.LogicalKey.Contains(CanonicalPath(inputPath), StringComparison.Ordinal));
        Assert.NotEqual(initialCreatorInput.Sha256, refreshedCreatorInput.Sha256);
        var refreshedMetadataReference = Assert.Single(refreshed.Solution.GetProject(metadataProject.Id)!.MetadataReferences
            .OfType<PortableExecutableReference>()
            .Where(reference => StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(reference.FilePath!), Path.GetFullPath(metadataPath))));
        Assert.NotSame(initialMetadataReference, refreshedMetadataReference);
    }

    private static string CanonicalPath(string path)
    {
        var canonical = Path.GetFullPath(path).Replace('\\', '/');
        return OperatingSystem.IsWindows() ? canonical.ToUpperInvariant() : canonical;
    }

    private static ImmutableArray<string> ReadAssemblyReferences(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        return metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .ToImmutableArray();
    }

    private static ImmutableArray<byte> EmitAssembly(string outputPath, string assemblyName, string source, params MetadataReference[] extraReferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var platformPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = platformPaths.Select(path => MetadataReference.CreateFromFile(path)).Concat(extraReferences).ToImmutableArray();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var bytes = ImmutableArray.CreateRange(stream.ToArray());
        File.WriteAllBytes(outputPath, bytes.AsSpan());
        return bytes;
    }

    private static ImmutableArray<byte> EmitGenerator(string outputPath, string dependencyPath)
    {
        var source = """
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Text;
            using System.Text;
            using PrivateGeneratorDependency;
            [Generator]
            public sealed class PrivateDependencyGenerator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }
                public void Execute(GeneratorExecutionContext context) => context.AddSource("Dependency.g.cs", SourceText.From(
                    "namespace GeneratedProbe; public sealed class Generated { public const string Value = \"" + Marker.GetValue() + "\"; }", Encoding.UTF8));
            }
            """;
        return EmitAssembly(
            outputPath,
            "PrivateGenerator",
            source,
            MetadataReference.CreateFromFile(typeof(ISourceGenerator).Assembly.Location),
            MetadataReference.CreateFromFile(dependencyPath));
    }

    private static ImmutableArray<byte> EmitAnalyzer(string outputPath)
    {
        var source = """
            using System.Collections.Immutable;
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Diagnostics;
            [DiagnosticAnalyzer(LanguageNames.CSharp)]
            public sealed class EmptyAnalyzer : DiagnosticAnalyzer
            {
                public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray<DiagnosticDescriptor>.Empty;
                public override void Initialize(AnalysisContext context) { }
            }
            """;
        return EmitAssembly(
            outputPath,
            "ChangingAnalyzer",
            source,
            MetadataReference.CreateFromFile(typeof(DiagnosticAnalyzer).Assembly.Location));
    }

    private static ImmutableArray<byte> EmitLiteralGenerator(string outputPath, string value)
    {
        var source = $$"""
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Text;
            using System.Text;
            [Generator]
            public sealed class VersionedGenerator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }
                public void Execute(GeneratorExecutionContext context) => context.AddSource("Version.g.cs", SourceText.From(
                    "namespace GeneratedProbe; public sealed class Generated { public const string Value = \"{{value}}\"; }", Encoding.UTF8));
            }
            """;
        return EmitAssembly(
            outputPath,
            "VersionedGenerator",
            source,
            MetadataReference.CreateFromFile(typeof(ISourceGenerator).Assembly.Location));
    }

    private static ImmutableArray<byte> EmitFileInputGenerator(string outputPath, string inputPath)
    {
        var pathLiteral = System.Text.Json.JsonSerializer.Serialize(Path.GetFullPath(inputPath));
        var source = $$"""
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Text;
            using System.IO;
            using System.Text;
            using System.Text.Json;
            [Generator]
            public sealed class FileInputGenerator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }
                public void Execute(GeneratorExecutionContext context)
                {
                    var value = File.ReadAllText({{pathLiteral}}).Trim();
                    var generated = "namespace GeneratedProbe; public sealed class Generated { public const string Value = "
                        + JsonSerializer.Serialize(value) + "; }";
                    context.AddSource("Input.g.cs", SourceText.From(generated, Encoding.UTF8));
                }
            }
            """;
        return EmitAssembly(
            outputPath,
            "FileInputGenerator",
            source,
            MetadataReference.CreateFromFile(typeof(ISourceGenerator).Assembly.Location));
    }

    private static void EmitDynamicLoadGenerator(string outputPath)
    {
        var source = """
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Text;
            using System.Reflection;
            using System.Runtime.Loader;
            using System.Text;
            [Generator]
            public sealed class DynamicLoadGenerator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }
                public void Execute(GeneratorExecutionContext context)
                {
                    try { AssemblyLoadContext.GetLoadContext(typeof(DynamicLoadGenerator).Assembly)!.LoadFromAssemblyName(new AssemblyName("DynamicPrivateDependency")); }
                    catch (System.IO.FileNotFoundException) { }
                    context.AddSource("Dynamic.g.cs", SourceText.From("namespace GeneratedProbe; public sealed class Generated { }", Encoding.UTF8));
                }
            }
            """;
        EmitAssembly(
            outputPath,
            "DynamicLoadGenerator",
            source,
            MetadataReference.CreateFromFile(typeof(ISourceGenerator).Assembly.Location));
    }

    private static void EmitDefaultContextLoadGenerator(string outputPath, string dependencyPath)
    {
        var pathLiteral = System.Text.Json.JsonSerializer.Serialize(Path.GetFullPath(dependencyPath));
        var source = $$"""
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Text;
            using System.Reflection;
            using System.Runtime.Loader;
            using System.Text;
            using System.Text.Json;
            [Generator]
            public sealed class DefaultContextLoadGenerator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }
                public void Execute(GeneratorExecutionContext context)
                {
                    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath({{pathLiteral}});
                    var value = assembly.GetType("DefaultContextPrivate.Marker", throwOnError: true)!
                        .GetMethod("GetValue", BindingFlags.Public | BindingFlags.Static)!
                        .Invoke(null, null);
                    var generated = "namespace GeneratedProbe; public sealed class Generated { public const string Value = "
                        + JsonSerializer.Serialize((string)value!) + "; }";
                    context.AddSource("Default.g.cs", SourceText.From(generated, Encoding.UTF8));
                }
            }
            """;
        EmitAssembly(
            outputPath,
            "DefaultContextGenerator",
            source,
            MetadataReference.CreateFromFile(typeof(ISourceGenerator).Assembly.Location));
    }

    private sealed class IsolatedAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader, IDisposable
    {
        private readonly PrivateDependencyLoadContext context = new();

        internal string? LoadedDependencyPath => context.LoadedDependencyPath;

        public Assembly LoadFromPath(string fullPath) => context.LoadFromAssemblyPath(Path.GetFullPath(fullPath));

        public void AddDependencyLocation(string fullPath) => context.AddDependency(fullPath);

        public void Dispose() => context.Unload();

        private sealed class PrivateDependencyLoadContext : AssemblyLoadContext
        {
            private readonly Dictionary<string, string> dependencyPaths = new(StringComparer.OrdinalIgnoreCase);
            internal string? LoadedDependencyPath { get; private set; }

            internal PrivateDependencyLoadContext() : base(isCollectible: true)
            {
            }

            internal void AddDependency(string fullPath)
            {
                var canonicalPath = Path.GetFullPath(fullPath);
                var name = AssemblyName.GetAssemblyName(canonicalPath).Name!;
                dependencyPaths[name] = canonicalPath;
            }

            protected override Assembly? Load(AssemblyName assemblyName)
            {
                var codeAnalysisName = typeof(ISourceGenerator).Assembly.GetName().Name;
                if (StringComparer.Ordinal.Equals(assemblyName.Name, codeAnalysisName))
                {
                    return Default.LoadFromAssemblyName(assemblyName);
                }

                if (assemblyName.Name is null || !dependencyPaths.TryGetValue(assemblyName.Name, out var dependencyPath))
                {
                    return null;
                }

                var loaded = LoadFromAssemblyPath(dependencyPath);
                LoadedDependencyPath = CanonicalPath(dependencyPath);
                return loaded;
            }
        }
    }
}
