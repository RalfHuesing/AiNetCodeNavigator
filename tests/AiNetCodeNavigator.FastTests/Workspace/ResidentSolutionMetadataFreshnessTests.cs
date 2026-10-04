#nullable enable

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Security;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AiNetCodeNavigator.FastTests.Workspace;

// @covers ResidentSolution
// @covers SolutionStructureFingerprint
// @covers MetadataReferenceImageCapture
[Trait("Category", "Unit")]
public sealed class ResidentSolutionMetadataFreshnessTests
{
    [Fact]
    public async Task GetCurrentSnapshot_WhenReferencedImageChangesWithSamePathSizeMtimeAndMvid_RebindsCompilation()
    {
        using var fixture = TestTempDirectory.Create("metadata-reference-freshness-");
        using var replacementFixture = TestTempDirectory.Create("metadata-reference-replacement-");
        const string originalApi = "namespace MetadataFreshnessProbe; public static class Api { public const int Version = 17; }";
        const string changedApi = "namespace MetadataFreshnessProbe; public static class Api { public const int Version = 18; }";
        const string consumer = "namespace MetadataFreshnessProbe; public sealed class Consumer { public int Read() => Api.Version; }";
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "MetadataFreshnessApi", originalApi);
        var solutionPath = fixture.CreateFile("MetadataFreshness.slnx", string.Empty);
        var sourcePath = fixture.CreateFile("src/Consumer/Consumer.cs", consumer);
        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("Consumer", [(sourcePath, consumer)],
                AdditionalReferences: [MetadataReference.CreateFromFile(assemblyPath)],
                VirtualProjectDirectory: "src/Consumer"))
            .Build();
        await using var resident = new ResidentSolution(workspace.Solution, solutionPath: solutionPath);
        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        var initialCompilation = await Assert.Single(initial.Solution!.Projects).GetCompilationAsync();
        Assert.Equal(17, Assert.IsAssignableFrom<IFieldSymbol>(initialCompilation!
            .GetTypeByMetadataName("MetadataFreshnessProbe.Api")!.GetMembers("Version")[0]).ConstantValue);

        var originalMvid = ReadMvid(assemblyPath);
        var originalMtime = File.GetLastWriteTimeUtc(assemblyPath);
        var originalSize = new FileInfo(assemblyPath).Length;
        var originalHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assemblyPath)));
        var replacementPath = AssemblyTestHelper.EmitAssembly(replacementFixture, "MetadataFreshnessApi", changedApi);
        PreserveMvid(replacementPath, originalMvid);
        var changedBytes = await File.ReadAllBytesAsync(replacementPath);
        Assert.Equal(originalSize, changedBytes.LongLength);
        await File.WriteAllBytesAsync(assemblyPath, changedBytes);
        File.SetLastWriteTimeUtc(assemblyPath, originalMtime);
        Assert.Equal(originalMtime, File.GetLastWriteTimeUtc(assemblyPath));
        Assert.Equal(originalMvid, ReadMvid(assemblyPath));
        Assert.NotEqual(originalHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assemblyPath))));

        var refreshed = await resident.GetCurrentSnapshotAsync();

        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        Assert.NotSame(initial.Solution, refreshed.Solution);
        var refreshedCompilation = await Assert.Single(refreshed.Solution!.Projects).GetCompilationAsync();
        Assert.Equal(18, Assert.IsAssignableFrom<IFieldSymbol>(refreshedCompilation!
            .GetTypeByMetadataName("MetadataFreshnessProbe.Api")!.GetMembers("Version")[0]).ConstantValue);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.Equal(17, Assert.IsAssignableFrom<IFieldSymbol>(initialCompilation!
            .GetTypeByMetadataName("MetadataFreshnessProbe.Api")!.GetMembers("Version")[0]).ConstantValue);
    }

    [Fact]
    public async Task GetCurrentSnapshot_WhenSourceReadFailsAfterMetadataCapture_KeepsOldSolutionAndInputEvidenceTogether()
    {
        using var fixture = TestTempDirectory.Create("metadata-source-transaction-");
        using var replacementFixture = TestTempDirectory.Create("metadata-source-transaction-replacement-");
        const string originalApi = "namespace MetadataTransactionProbe; public static class Api { public const int Version = 17; }";
        const string changedApi = "namespace MetadataTransactionProbe; public static class Api { public const int Version = 18; }";
        const string consumer = "namespace MetadataTransactionProbe; public sealed class Consumer { public int Read() => Api.Version; }";
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "MetadataTransactionApi", originalApi);
        var solutionPath = fixture.CreateFile("MetadataTransaction.slnx", string.Empty);
        var sourcePath = fixture.CreateFile("src/Consumer/Consumer.cs", consumer);
        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("Consumer", [(sourcePath, consumer)],
                AdditionalReferences: [MetadataReference.CreateFromFile(assemblyPath)],
                VirtualProjectDirectory: "src/Consumer"))
            .Build();
        await using var resident = new ResidentSolution(workspace.Solution);
        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        var initialProject = Assert.Single(initial.Solution!.Projects);
        var initialImageHash = GetReferenceEvidence(initial, initialProject, assemblyPath).Images[0].Sha256;

        var replacementPath = AssemblyTestHelper.EmitAssembly(replacementFixture, "MetadataTransactionApi", changedApi);
        await File.WriteAllBytesAsync(assemblyPath, await File.ReadAllBytesAsync(replacementPath));

        ResidentSolutionSnapshot failed;
        await using (var sourceLock = new FileStream(sourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            failed = await resident.GetCurrentSnapshotAsync();
        }

        Assert.False(failed.Succeeded);
        Assert.NotNull(failed.Error);
        var stillCurrent = resident.GetCurrentSolution();
        Assert.Same(initial.Solution, stillCurrent);
        Assert.Equal(initialImageHash, GetReferenceEvidence(initial, initialProject, assemblyPath).Images[0].Sha256);

        var refreshed = await resident.GetCurrentSnapshotAsync();

        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        Assert.NotSame(initial.Solution, refreshed.Solution);
        var refreshedProject = Assert.Single(refreshed.Solution!.Projects);
        var refreshedImageHash = GetReferenceEvidence(refreshed, refreshedProject, assemblyPath).Images[0].Sha256;
        Assert.NotEqual(initialImageHash, refreshedImageHash);
        var compilation = await refreshedProject.GetCompilationAsync();
        Assert.Equal(18, Assert.IsAssignableFrom<IFieldSymbol>(compilation!
            .GetTypeByMetadataName("MetadataTransactionProbe.Api")!.GetMembers("Version")[0]).ConstantValue);
    }

    [Fact]
    public async Task GetCurrentSnapshot_WhenAdditionalAndAnalyzerConfigFilesChangeWithSameTimestamp_RefreshesLoadedText()
    {
        using var fixture = TestTempDirectory.Create("workspace-text-input-freshness-");
        var solutionPath = fixture.CreateFile("WorkspaceInputs.slnx", string.Empty);
        var projectDirectory = fixture.GetPath(Path.Combine("src", "Inputs"));
        Directory.CreateDirectory(projectDirectory);
        var additionalPath = Path.Combine(projectDirectory, "data.txt");
        var analyzerConfigPath = Path.Combine(projectDirectory, ".editorconfig");
        const string originalAdditional = "payload=old";
        const string changedAdditional = "payload=new";
        const string originalEditorConfig = "root=true\nbuild_property.Configuration = Debug\n";
        const string changedEditorConfig = "root=true\nbuild_property.Configuration = Other\n";
        await File.WriteAllTextAsync(additionalPath, originalAdditional);
        await File.WriteAllTextAsync(analyzerConfigPath, originalEditorConfig);
        Assert.Equal(Encoding.UTF8.GetByteCount(originalAdditional), Encoding.UTF8.GetByteCount(changedAdditional));
        Assert.Equal(Encoding.UTF8.GetByteCount(originalEditorConfig), Encoding.UTF8.GetByteCount(changedEditorConfig));
        var additionalLength = new FileInfo(additionalPath).Length;
        var editorConfigLength = new FileInfo(analyzerConfigPath).Length;
        var additionalMtime = File.GetLastWriteTimeUtc(additionalPath);
        var editorConfigMtime = File.GetLastWriteTimeUtc(analyzerConfigPath);
        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec(
                "Inputs",
                [("Input.cs", "public sealed class Input { }")],
                VirtualProjectDirectory: "src/Inputs",
                AdditionalDocuments: [(additionalPath, originalAdditional)],
                AnalyzerConfigDocuments: [(analyzerConfigPath, originalEditorConfig)]))
            .Build();
        await using var resident = new ResidentSolution(workspace.Solution);
        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        var initialProject = Assert.Single(initial.Solution!.Projects);
        Assert.Equal(originalAdditional, (await Assert.Single(initialProject.AdditionalDocuments).GetTextAsync()).ToString());
        Assert.Equal(originalEditorConfig, (await Assert.Single(initialProject.AnalyzerConfigDocuments).GetTextAsync()).ToString());

        await File.WriteAllTextAsync(additionalPath, changedAdditional);
        await File.WriteAllTextAsync(analyzerConfigPath, changedEditorConfig);
        File.SetLastWriteTimeUtc(additionalPath, additionalMtime);
        File.SetLastWriteTimeUtc(analyzerConfigPath, editorConfigMtime);
        Assert.Equal(additionalLength, new FileInfo(additionalPath).Length);
        Assert.Equal(editorConfigLength, new FileInfo(analyzerConfigPath).Length);
        Assert.Equal(additionalMtime, File.GetLastWriteTimeUtc(additionalPath));
        Assert.Equal(editorConfigMtime, File.GetLastWriteTimeUtc(analyzerConfigPath));

        var refreshed = await resident.GetCurrentSnapshotAsync();

        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        Assert.NotSame(initial.Solution, refreshed.Solution);
        var refreshedProject = Assert.Single(refreshed.Solution!.Projects);
        Assert.Equal(changedAdditional, (await Assert.Single(refreshedProject.AdditionalDocuments).GetTextAsync()).ToString());
        Assert.Equal(changedEditorConfig, (await Assert.Single(refreshedProject.AnalyzerConfigDocuments).GetTextAsync()).ToString());
    }

    [Fact]
    public async Task GetCurrentSnapshot_WhenReferenceImageWasSuppliedByItsCreator_UsesRetainedBytes()
    {
        using var fixture = TestTempDirectory.Create("captured-reference-documentation-");
        var apiPath = AssemblyTestHelper.EmitAssembly(fixture, "CapturedDocumentationApi",
            "namespace CapturedDocs; public sealed class Api { }");
        var apiImage = ImmutableArray.CreateRange(await File.ReadAllBytesAsync(apiPath));
        var xmlDocumentation = ImmutableArray.CreateRange(Encoding.UTF8.GetBytes(
            "<doc><members><member name=\"T:CapturedDocs.Api\"><summary>Captured API documentation.</summary></member></members></doc>"));
        var reference = CapturedMetadataReference.CreateFromImage(apiImage, filePath: "Moq.dll", documentationXmlBytes: xmlDocumentation);
        using var workspace = TestWorkspaceBuilder.Create()
            .WithProject(new ProjectSpec("InMemoryReference", [("Consumer.cs", "public sealed class Consumer { public global::CapturedDocs.Api? Value { get; } }")],
                AdditionalReferences: [reference]))
            .Build();
        await using var resident = new ResidentSolution(workspace.Solution);

        var snapshot = await resident.GetCurrentSnapshotAsync();

        Assert.True(snapshot.Succeeded, snapshot.Error?.Message);
        Assert.Contains(snapshot.IdentityInputs!.MetadataReferences,
            item => item.Images.Any(image => image.CanonicalImageKey.StartsWith("in-memory:", StringComparison.Ordinal)));
        Assert.Contains(snapshot.IdentityInputs!.Projects.SelectMany(project => project.BindingInputs),
            input => input.Kind == "xml-documentation-image" && input.LogicalKey.StartsWith("in-memory-xml:", StringComparison.Ordinal));
        var compilation = await Assert.Single(snapshot.Solution!.Projects).GetCompilationAsync();
        Assert.Contains("Captured API documentation.", compilation!.GetTypeByMetadataName("CapturedDocs.Api")!
            .GetDocumentationCommentXml(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentSnapshot_WhenCreatorProviderHasNoCapturedXmlBytes_PreservesProviderAndRejectsIdentityReuse()
    {
        using var fixture = TestTempDirectory.Create("opaque-reference-documentation-");
        var apiPath = AssemblyTestHelper.EmitAssembly(fixture, "OpaqueDocumentationApi",
            "namespace OpaqueDocs; public sealed class Api { }");
        var apiImage = ImmutableArray.CreateRange(await File.ReadAllBytesAsync(apiPath));
        var providerBytes = ImmutableArray.CreateRange(Encoding.UTF8.GetBytes(
            "<doc><members><member name=\"T:OpaqueDocs.Api\"><summary>Opaque API documentation.</summary></member></members></doc>"));
        var provider = XmlDocumentationProvider.CreateFromBytes(providerBytes.ToArray());
        var reference = CapturedMetadataReference.CreateFromImage(apiImage, documentationProvider: provider, filePath: "Opaque.dll");
        using var workspace = TestWorkspaceBuilder.Create()
            .WithProject(new ProjectSpec("OpaqueDocumentation",
                [("Consumer.cs", "public sealed class Consumer { public global::OpaqueDocs.Api? Value { get; } }")],
                AdditionalReferences: [reference]))
            .Build();
        await using var resident = new ResidentSolution(workspace.Solution);

        var snapshot = await resident.GetCurrentSnapshotAsync();

        Assert.True(snapshot.Succeeded, snapshot.Error?.Message);
        var projectEvidence = Assert.Single(snapshot.IdentityInputs!.Projects);
        Assert.False(projectEvidence.IsSupported);
        Assert.Contains("documentation provider", projectEvidence.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
        var compilation = await Assert.Single(snapshot.Solution!.Projects).GetCompilationAsync();
        Assert.Contains("Opaque API documentation.", compilation!.GetTypeByMetadataName("OpaqueDocs.Api")!
            .GetDocumentationCommentXml(), StringComparison.Ordinal);
        var identity = await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(snapshot.Solution!, snapshot.IdentityInputs!), default);
        Assert.False(identity.IsSuccess);
        Assert.Equal(NavigationErrorCodes.WorkspaceDiagnostic, identity.Error!.Value.Code);
    }

    [Fact]
    public void Capture_WhenReferencesSharePhysicalImage_ReadsDistinctImageOnce()
    {
        using var fixture = TestTempDirectory.Create("shared-metadata-capture-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "SharedCapture", "namespace SharedCapture; public class Api { }");
        using var workspace = TestWorkspaceBuilder.Create()
            .WithProject(new ProjectSpec("SharedReferences", [("Consumer.cs", "public sealed class Consumer { }")],
                AdditionalReferences: [MetadataReference.CreateFromFile(assemblyPath), MetadataReference.CreateFromFile(assemblyPath)]))
            .Build();
        var capturedPaths = new System.Collections.Generic.List<string>();

        var result = MetadataReferenceImageCapture.Capture(
            workspace.Solution,
            previousInputs: null,
            observer: new MetadataReferenceImageCapture.MetadataImageCaptureObserver
            {
                OnImageCaptured = path => capturedPaths.Add(path),
            });

        Assert.True(result.SolutionChanged);
        var expectedKey = Path.GetFullPath(assemblyPath).Replace('\\', '/');
        if (OperatingSystem.IsWindows()) expectedKey = expectedKey.ToUpperInvariant();
        Assert.Equal(1, capturedPaths.Count(path => StringComparer.Ordinal.Equals(path, expectedKey)));
    }

    [Fact]
    public void Capture_WhenPhysicalImageChangesDuringCapture_RetriesWholeBoundaryThreeTimesWithoutReturningPartialState()
    {
        using var fixture = TestTempDirectory.Create("metadata-capture-race-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "ChangingCapture", "namespace ChangingCapture; public class Api { }");
        using var workspace = TestWorkspaceBuilder.Create()
            .WithProject(new ProjectSpec("ChangingReferences", [("Consumer.cs", "public sealed class Consumer { }")],
                AdditionalReferences: [MetadataReference.CreateFromFile(assemblyPath)]))
            .Build();
        var originalReference = Assert.Single(Assert.Single(workspace.Solution.Projects).MetadataReferences.OfType<PortableExecutableReference>(),
            reference => StringComparer.OrdinalIgnoreCase.Equals(reference.FilePath, assemblyPath));
        var attempts = 0;

        var captureAttempts = new System.Collections.Generic.List<int>();
        var exception = Assert.Throws<IOException>(() => MetadataReferenceImageCapture.Capture(
            workspace.Solution,
            previousInputs: null,
            observer: new MetadataReferenceImageCapture.MetadataImageCaptureObserver
            {
                BeforeImageRead = (path, attempt) =>
                {
                    if (StringComparer.OrdinalIgnoreCase.Equals(path, assemblyPath))
                    {
                        attempts++;
                        captureAttempts.Add(attempt);
                        File.AppendAllText(path, "x");
                    }
                },
            }));

        Assert.Contains("after 3 attempt(s)", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(assemblyPath), exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, attempts);
        Assert.Equal(new[] { 0, 1, 2 }, captureAttempts);
        Assert.Same(originalReference, Assert.Single(Assert.Single(workspace.Solution.Projects).MetadataReferences.OfType<PortableExecutableReference>(),
            reference => StringComparer.OrdinalIgnoreCase.Equals(reference.FilePath, assemblyPath)));
    }

    [Fact]
    public async Task GetCurrentSnapshot_WhenMetadataImageChangesDuringCapture_UsesAtMostThreeCapturesForTheBoundary()
    {
        using var fixture = TestTempDirectory.Create("resident-metadata-capture-boundary-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "ResidentBoundary", "namespace ResidentBoundary; public class Api { }");
        var solutionPath = fixture.CreateFile("ResidentBoundary.slnx", string.Empty);
        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("Boundary", [("Consumer.cs", "public sealed class Consumer { }")],
                AdditionalReferences: [MetadataReference.CreateFromFile(assemblyPath)]))
            .Build();
        await using var resident = new ResidentSolution(workspace.Solution, solutionPath: solutionPath);
        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        var initialSolution = initial.Solution;
        var attempts = 0;
        resident.ImageCaptureObserver = new MetadataReferenceImageCapture.MetadataImageCaptureObserver
        {
            BeforeImageRead = (path, _) =>
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(path, assemblyPath))
                {
                    attempts++;
                    File.AppendAllText(path, "x");
                }
            },
        };

        var failed = await resident.GetCurrentSnapshotAsync();

        Assert.False(failed.Succeeded);
        var failure = failed.Error ?? throw new InvalidOperationException("The failed refresh did not return its structured error.");
        Assert.Equal(ProjectErrorCodes.ProjectLoadFailed, failure.ErrorCode);
        Assert.True(failure.Retryable);
        Assert.Contains(Path.GetFullPath(assemblyPath), failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, attempts);
        Assert.Same(initialSolution, resident.GetCurrentSolution());
    }

    [Fact]
    public async Task GetCurrentSnapshot_WhenPhysicalXmlDocumentationChanges_PreservesUpdatedSymbolDocumentation()
    {
        using var fixture = TestTempDirectory.Create("metadata-documentation-freshness-");
        using var replacementFixture = TestTempDirectory.Create("metadata-documentation-replacement-");
        const string api = "namespace DocumentationFreshness; public sealed class Api { }";
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "DocumentationFreshness", api);
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        const string originalXml = "<doc><members><member name=\"T:DocumentationFreshness.Api\"><summary>First documentation.</summary></member></members></doc>";
        const string changedXml = "<doc><members><member name=\"T:DocumentationFreshness.Api\"><summary>Other documentation.</summary></member></members></doc>";
        await File.WriteAllTextAsync(xmlPath, originalXml);
        Assert.Equal(Encoding.UTF8.GetByteCount(originalXml), Encoding.UTF8.GetByteCount(changedXml));
        var xmlLength = new FileInfo(xmlPath).Length;
        var originalXmlHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(xmlPath)));
        var solutionPath = fixture.CreateFile("DocumentationFreshness.slnx", string.Empty);
        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("Documentation", [("Consumer.cs", "public sealed class Consumer { }")],
                AdditionalReferences: [MetadataReference.CreateFromFile(assemblyPath)]))
            .Build();
        await using var resident = new ResidentSolution(workspace.Solution, solutionPath: solutionPath);
        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        var initialCompilation = await Assert.Single(initial.Solution!.Projects).GetCompilationAsync();
        Assert.Contains("First documentation.", initialCompilation!.GetTypeByMetadataName("DocumentationFreshness.Api")!
            .GetDocumentationCommentXml(), StringComparison.Ordinal);

        var xmlTime = File.GetLastWriteTimeUtc(xmlPath);
        await File.WriteAllTextAsync(xmlPath, changedXml);
        File.SetLastWriteTimeUtc(xmlPath, xmlTime);
        Assert.Equal(xmlLength, new FileInfo(xmlPath).Length);
        Assert.Equal(xmlTime, File.GetLastWriteTimeUtc(xmlPath));
        Assert.NotEqual(originalXmlHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(xmlPath))));

        var refreshed = await resident.GetCurrentSnapshotAsync();

        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        Assert.NotSame(initial.Solution, refreshed.Solution);
        var refreshedCompilation = await Assert.Single(refreshed.Solution!.Projects).GetCompilationAsync();
        Assert.Contains("Other documentation.", refreshedCompilation!.GetTypeByMetadataName("DocumentationFreshness.Api")!
            .GetDocumentationCommentXml(), StringComparison.Ordinal);
        Assert.Contains(refreshed.IdentityInputs!.Projects,
            project => project.BindingInputs.Any(input => input.Kind == "xml-documentation-image" && input.LogicalKey.EndsWith("documentationfreshness.xml", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GetCurrentSnapshot_WhenPhysicalMetadataModuleChanges_RebindsAllModulesAndKeepsOldCompilationAlive()
    {
        using var fixture = TestTempDirectory.Create("multimodule-freshness-");
        using var replacementFixture = TestTempDirectory.Create("multimodule-freshness-replacement-");
        var assemblyPath = WritePhysicalMultiModuleAssembly(fixture, 1);
        var replacementAssemblyPath = WritePhysicalMultiModuleAssembly(replacementFixture, 2);
        var modulePath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, "Secondary.netmodule");
        var replacementModulePath = Path.Combine(Path.GetDirectoryName(replacementAssemblyPath)!, "Secondary.netmodule");
        var solutionPath = fixture.CreateFile("MultiModule.slnx", string.Empty);
        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("MultiModuleConsumer",
                [("Consumer.cs", "public sealed class Consumer { public int Read() => MultiModuleProbe.SecondaryApi.Version; }")],
                AdditionalReferences: [MetadataReference.CreateFromFile(assemblyPath)]))
            .Build();
        await using var resident = new ResidentSolution(workspace.Solution, solutionPath: solutionPath);
        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        var initialProject = Assert.Single(initial.Solution!.Projects);
        var initialCompilation = await initialProject.GetCompilationAsync();
        Assert.Equal(1, GetConstant(initialCompilation!, "MultiModuleProbe.SecondaryApi", "Version"));
        var primaryReferenceOrdinal = initialProject.MetadataReferences
            .Select((reference, ordinal) => (reference, ordinal))
            .Single(item => item.reference is PortableExecutableReference portable
                && StringComparer.OrdinalIgnoreCase.Equals(portable.FilePath, assemblyPath))
            .ordinal;
        var initialReferenceEvidence = Assert.Single(initial.IdentityInputs!.MetadataReferences,
            item => item.OwnerProjectId == initialProject.Id && item.ReferenceOrdinal == primaryReferenceOrdinal);
        Assert.Contains(initialReferenceEvidence.Images,
            image => image.CanonicalImageKey.EndsWith("SECONDARY.NETMODULE", StringComparison.OrdinalIgnoreCase));

        var assemblyMtime = File.GetLastWriteTimeUtc(assemblyPath);
        var moduleMtime = File.GetLastWriteTimeUtc(modulePath);
        await File.WriteAllBytesAsync(assemblyPath, await File.ReadAllBytesAsync(replacementAssemblyPath));
        await File.WriteAllBytesAsync(modulePath, await File.ReadAllBytesAsync(replacementModulePath));
        File.SetLastWriteTimeUtc(assemblyPath, assemblyMtime);
        File.SetLastWriteTimeUtc(modulePath, moduleMtime);

        var refreshed = await resident.GetCurrentSnapshotAsync();

        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        Assert.NotSame(initial.Solution, refreshed.Solution);
        var refreshedProject = Assert.Single(refreshed.Solution!.Projects);
        var refreshedReferenceOrdinal = refreshedProject.MetadataReferences
            .Select((reference, ordinal) => (reference, ordinal))
            .Single(item => item.reference is PortableExecutableReference portable
                && StringComparer.OrdinalIgnoreCase.Equals(portable.FilePath, assemblyPath))
            .ordinal;
        var refreshedReferenceEvidence = Assert.Single(refreshed.IdentityInputs!.MetadataReferences,
            item => item.OwnerProjectId == refreshedProject.Id && item.ReferenceOrdinal == refreshedReferenceOrdinal);
        Assert.Contains(refreshedReferenceEvidence.Images,
            image => image.CanonicalImageKey.EndsWith("SECONDARY.NETMODULE", StringComparison.OrdinalIgnoreCase));
        var refreshedCompilation = await refreshedProject.GetCompilationAsync();
        Assert.Equal(2, GetConstant(refreshedCompilation!, "MultiModuleProbe.SecondaryApi", "Version"));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.Equal(1, GetConstant(initialCompilation!, "MultiModuleProbe.SecondaryApi", "Version"));
    }

    [Fact]
    public async Task GetCurrentSnapshot_WhenStructureReloads_CapturesEachSharedPhysicalImageOnce()
    {
        using var fixture = TestTempDirectory.Create("resident-structure-reload-capture-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "StructureReloadShared", "namespace StructureReload; public sealed class Api { }");
        var solutionPath = fixture.CreateFile("StructureReload.slnx",
            "<Solution><Project Path=\"src/Consumer/Consumer.csproj\" /></Solution>");
        fixture.CreateFile("src/Consumer/Consumer.csproj",
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include=\"StructureReloadShared\"><HintPath>{SecurityElement.Escape(assemblyPath)}</HintPath></Reference></ItemGroup></Project>");
        fixture.CreateFile("src/Consumer/Consumer.cs", "public sealed class Consumer { public StructureReload.Api? Value { get; } }");
        await using var resident = new ResidentSolution(
            async cancellationToken => (ResidentLoadedState?)await MSBuildSolutionLoader.LoadResidentStateAsync(solutionPath, cancellationToken).ConfigureAwait(false),
            solutionPath);
        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        var capturedPaths = new System.Collections.Generic.List<string>();
        resident.ImageCaptureObserver = new MetadataReferenceImageCapture.MetadataImageCaptureObserver
        {
            OnImageCaptured = path => capturedPaths.Add(path),
        };

        fixture.CreateFile("src/Consumer/Added.cs", "public sealed class Added { }");

        var refreshed = await resident.GetCurrentSnapshotAsync();

        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        Assert.NotSame(initial.Solution, refreshed.Solution);
        Assert.Contains(refreshed.Solution!.Projects.SelectMany(project => project.Documents),
            document => StringComparer.OrdinalIgnoreCase.Equals(document.FilePath, fixture.GetPath("src/Consumer/Added.cs")));
        var expectedKey = Path.GetFullPath(assemblyPath).Replace('\\', '/');
        if (OperatingSystem.IsWindows()) expectedKey = expectedKey.ToUpperInvariant();
        Assert.Equal(1, capturedPaths.Count(path => StringComparer.Ordinal.Equals(path, expectedKey)));
    }

    [Fact]
    public void TestWorkspaceBuilderProvenance_DoesNotKeepItsSolutionAlive()
    {
        var solutionReference = CreateWeaklyAssociatedTestWorkspace();
        for (var attempt = 0; attempt < 5 && IsAlive(solutionReference); attempt++)
        {
            ForceCollection();
        }

        Assert.False(IsAlive(solutionReference));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Solution> CreateWeaklyAssociatedTestWorkspace()
    {
        using var fixture = TestTempDirectory.Create("workspace-provenance-lifetime-");
        using var workspace = TestWorkspaceBuilder.Create()
            .WithProject(new ProjectSpec("Provenance", [("Main.cs", "public sealed class Main { }")]))
            .Build();
        var solution = workspace.Solution;
        Assert.IsType<WorkspaceInputProvenance>(WorkspaceInputProvenance.FindTestWorkspaceBuilderOutput(solution));
        return new WeakReference<Solution>(solution);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive(WeakReference<Solution> reference) => reference.TryGetTarget(out _);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static SourceIdentityMetadataReferenceEvidence GetReferenceEvidence(
        ResidentSolutionSnapshot snapshot,
        Project project,
        string referencePath)
    {
        var ordinal = project.MetadataReferences
            .Select((reference, index) => (reference, index))
            .Single(item => item.reference is PortableExecutableReference portable
                && StringComparer.OrdinalIgnoreCase.Equals(portable.FilePath, referencePath))
            .index;
        return Assert.Single(snapshot.IdentityInputs!.MetadataReferences,
            item => item.OwnerProjectId == project.Id && item.ReferenceOrdinal == ordinal);
    }

    private static Guid ReadMvid(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
    }

    private static void PreserveMvid(string replacementPath, Guid mvid)
    {
        var bytes = File.ReadAllBytes(replacementPath);
        var guidBytes = ReadMvid(replacementPath).ToByteArray();
        var mvidBytes = mvid.ToByteArray();
        var start = FindSequence(bytes, guidBytes);
        Assert.True(start >= 0, "Could not locate the emitted module MVID in the replacement PE image.");
        Assert.Equal(-1, FindSequence(bytes.AsSpan(start + 1), guidBytes));
        mvidBytes.CopyTo(bytes.AsSpan(start, mvidBytes.Length));
        File.WriteAllBytes(replacementPath, bytes);
        Assert.Equal(mvid, ReadMvid(replacementPath));
    }

    private static int FindSequence(ReadOnlySpan<byte> content, ReadOnlySpan<byte> sequence)
    {
        for (var start = 0; start <= content.Length - sequence.Length; start++)
        {
            if (content.Slice(start, sequence.Length).SequenceEqual(sequence)) return start;
        }
        return -1;
    }

    private static string WritePhysicalMultiModuleAssembly(TestTempDirectory fixture, int version)
    {
        var moduleName = "Secondary.netmodule";
        var modulePath = fixture.GetPath(moduleName);
        var moduleCompilation = CSharpCompilation.Create(
            "Secondary",
            [CSharpSyntaxTree.ParseText($"namespace MultiModuleProbe; public sealed class SecondaryApi {{ public const int Version = {version}; }}")],
            TestWorkspaceBuilder.CoreReferences,
            new CSharpCompilationOptions(OutputKind.NetModule));
        using (var moduleStream = new MemoryStream())
        {
            var moduleResult = moduleCompilation.Emit(moduleStream);
            Assert.True(moduleResult.Success, string.Join(Environment.NewLine, moduleResult.Diagnostics));
            File.WriteAllBytes(modulePath, moduleStream.ToArray());
        }

        var moduleBytes = File.ReadAllBytes(modulePath);
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("MultiModule.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            @namespace: default,
            name: metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddAssembly(
            name: metadata.GetOrAddString("MultiModule"),
            version: new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: AssemblyHashAlgorithm.Sha256);
        metadata.AddAssemblyFile(
            name: metadata.GetOrAddString(moduleName),
            hashValue: metadata.GetOrAddBlob(SHA256.HashData(moduleBytes)),
            containsMetadata: true);

        var root = new MetadataRootBuilder(metadata, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            root,
            new BlobBuilder(),
            mappedFieldData: null,
            managedResources: null,
            nativeResources: null,
            debugDirectoryBuilder: null,
            strongNameSignatureSize: 0,
            entryPoint: default,
            flags: CorFlags.ILOnly,
            deterministicIdProvider: null);
        var peImage = new BlobBuilder();
        peBuilder.Serialize(peImage);
        var assemblyPath = fixture.GetPath("MultiModule.dll");
        File.WriteAllBytes(assemblyPath, peImage.ToArray());
        return assemblyPath;
    }

    private static int? GetConstant(Compilation compilation, string typeName, string fieldName) =>
        (int)Assert.IsAssignableFrom<IFieldSymbol>(compilation.GetTypeByMetadataName(typeName)!.GetMembers(fieldName)[0]).ConstantValue!;
}
