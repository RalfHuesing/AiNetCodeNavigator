#nullable enable

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit.Builders;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class AnalysisSymbolIdentityTests
{
    [Fact]
    public async Task ExactProducer_FormatsSourceReferenceForExactOwner()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);

        var greeterType = compilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);

        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var reference = await ExactSourceSymbolResolver.CreateReferenceAsync(fixture.Solution, project, greeterType);

        Assert.True(reference.IsSuccess, reference.Error?.Message);
        Assert.StartsWith("src:src/Sample.Core/", StableSymbolReferenceCodec.Format(reference.Value!), System.StringComparison.Ordinal);
        Assert.EndsWith("|T:SampleNamespace.Greeter", StableSymbolReferenceCodec.Format(reference.Value!), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactProducer_UsesProjectPathWhenDocumentationIdsRepeatAcrossProjects()
    {
        using var solutionHandle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Identity.slnx",
            new ProjectSpec("First", [("First.cs", "namespace Shared; public class Worker { public void Run() {} }")], VirtualProjectDirectory: "src/First"),
            new ProjectSpec("Second", [("Second.cs", "namespace Shared; public class Worker { public void Run() {} }")], VirtualProjectDirectory: "src/Second"));
        var solution = solutionHandle.Solution;
        var symbols = await Task.WhenAll(solution.Projects.Select(async project =>
        {
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            return (project.Id, Symbol: compilation.GetTypeByMetadataName("Shared.Worker"));
        }));
        var first = Assert.IsAssignableFrom<ISymbol>(symbols[0].Symbol);
        var second = Assert.IsAssignableFrom<ISymbol>(symbols[1].Symbol);
        Assert.Equal(
            DocumentationCommentId.CreateDeclarationId(first),
            DocumentationCommentId.CreateDeclarationId(second));
        var firstReference = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, solution.GetProject(symbols[0].Id)!, first);
        var secondReference = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, solution.GetProject(symbols[1].Id)!, second);

        Assert.True(firstReference.IsSuccess, firstReference.Error?.Message);
        Assert.True(secondReference.IsSuccess, secondReference.Error?.Message);
        Assert.NotEqual(StableSymbolReferenceCodec.Format(firstReference.Value!), StableSymbolReferenceCodec.Format(secondReference.Value!));
    }

    [Fact]
    public async Task SnapshotContextMarkers_DistinguishTargetFrameworksStably()
    {
        using var firstSnapshot = CreateMultiTargetSolution();
        using var reloadedSnapshot = CreateMultiTargetSolution();
        var firstIds = await GetMultiTargetProjectMarkersAsync(firstSnapshot.Solution);
        var reloadedIds = await GetMultiTargetProjectMarkersAsync(reloadedSnapshot.Solution);

        Assert.Equal(firstIds[0].DocumentationId, firstIds[1].DocumentationId);
        Assert.NotEqual(firstIds[0].ProjectMarker, firstIds[1].ProjectMarker);
        Assert.Equal(firstIds, reloadedIds);
    }

    [Fact]
    public async Task SnapshotContextMarkers_DistinguishMetadataReferencesStably()
    {
        using var firstSnapshot = CreateReferenceContextSolution(varyMetadataReferences: true, varyProjectReferences: false);
        using var reloadedSnapshot = CreateReferenceContextSolution(varyMetadataReferences: true, varyProjectReferences: false);
        var firstIds = await GetRootProjectMarkersAsync(firstSnapshot.Solution);
        var reloadedIds = await GetRootProjectMarkersAsync(reloadedSnapshot.Solution);

        Assert.Equal(firstIds[0].DocumentationId, firstIds[1].DocumentationId);
        Assert.NotEqual(firstIds[0].ProjectMarker, firstIds[1].ProjectMarker);
        Assert.Equal(firstIds, reloadedIds);
    }

    [Fact]
    public async Task SnapshotContextMarkers_DistinguishProjectReferencesStably()
    {
        using var firstSnapshot = CreateReferenceContextSolution(varyMetadataReferences: false, varyProjectReferences: true);
        using var reloadedSnapshot = CreateReferenceContextSolution(varyMetadataReferences: false, varyProjectReferences: true);
        var firstIds = await GetRootProjectMarkersAsync(firstSnapshot.Solution);
        var reloadedIds = await GetRootProjectMarkersAsync(reloadedSnapshot.Solution);

        Assert.Equal(firstIds[0].DocumentationId, firstIds[1].DocumentationId);
        Assert.NotEqual(firstIds[0].ProjectMarker, firstIds[1].ProjectMarker);
        Assert.Equal(firstIds, reloadedIds);
    }

    [Fact]
    public async Task SnapshotContext_IndistinguishableProjectContextsRemainInSnapshotEvidence()
    {
        using var snapshot = CreateReferenceContextSolution(varyMetadataReferences: false, varyProjectReferences: false);

        var markers = await GetRootProjectMarkersAsync(snapshot.Solution);
        Assert.Equal(markers[0].ProjectMarker, markers[1].ProjectMarker);
        var formatter = await SourceReferenceFormattingContext.CreateFormatterAsync(snapshot.Solution, default);
        foreach (var project in snapshot.Solution.Projects.Where(project => project.Name == "Root"))
        {
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            var symbol = Assert.IsAssignableFrom<ISymbol>(compilation.GetTypeByMetadataName("Shared.Worker"));
            Assert.Null(formatter(symbol));
        }
    }

    [Fact]
    public async Task RelationshipSymbolIdentity_UsesContainerAndLocationForDistinctLocalFunctions()
    {
        const string source = "public class Worker\n{\n    public void Run()\n    {\n        int First() => 1;\n        int Second() => 2;\n    }\n}";
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Lines.slnx",
            new ProjectSpec("Lines", [("Worker.cs", source)]));
        var project = Assert.Single(workspace.Solution.Projects);
        var document = Assert.Single(project.Documents);
        var root = await document.GetSyntaxRootAsync();
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(root);
        Assert.NotNull(compilation);
        var model = compilation.GetSemanticModel(Assert.IsAssignableFrom<SyntaxTree>(await document.GetSyntaxTreeAsync()));
        var localFunctions = root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().ToArray();

        var identifiers = localFunctions
            .Select(node => RelationshipSymbolIdentity.GetStableId(model.GetDeclaredSymbol(node)!))
            .ToArray();

        Assert.Equal(2, identifiers.Length);
        Assert.All(identifiers, identifier => Assert.Contains("#lf:", identifier, System.StringComparison.Ordinal));
        Assert.NotEqual(identifiers[0], identifiers[1]);
        Assert.StartsWith("M:Worker.Run#lf:First@", identifiers[0], System.StringComparison.Ordinal);
        Assert.EndsWith("@5:13", identifiers[0], System.StringComparison.Ordinal);
        Assert.StartsWith("M:Worker.Run#lf:Second@", identifiers[1], System.StringComparison.Ordinal);
        Assert.EndsWith("@6:13", identifiers[1], System.StringComparison.Ordinal);
    }

    [Fact]
    public void Matches_ComparesPathAndHash()
    {
        var id1 = AnalysisSymbolIdentity.ForAssembly(@"C:\bin\app.dll", "hash1", 1);
        var id2 = AnalysisSymbolIdentity.ForAssembly(@"C:\bin\app.dll", "hash1", 2);
        var id3 = AnalysisSymbolIdentity.ForAssembly(@"C:\bin\other.dll", "hash2", 1);

        Assert.True(id1.Matches(id2));
        Assert.False(id1.Matches(id3));
    }

    [Fact]
    public void Matches_RequiresSameAbsoluteTargetPath()
    {
        var emptyPath = AnalysisSymbolIdentity.ForAssembly(string.Empty, new string('d', 64));
        var validPath = AnalysisSymbolIdentity.ForAssembly(@"C:\bin\app.dll", new string('d', 64));
        var equivalentPath = AnalysisSymbolIdentity.ForAssembly(@"C:\bin\..\bin\app.dll", new string('d', 64));

        Assert.False(emptyPath.Matches(validPath));
        Assert.True(validPath.Matches(equivalentPath));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The workspace lifetime is transferred to the returned TestSolutionHandle.")]
    private static TestSolutionHandle CreateMultiTargetSolution()
    {
        const string solutionPath = @"C:\VirtualRepo\Multi.slnx";
        const string projectPath = @"C:\VirtualRepo\src\Multi\Multi.csproj";
        const string sourcePath = @"C:\VirtualRepo\src\Multi\Worker.cs";
        const string source = "namespace Shared; public class Worker { public void Run() {} }";
        var workspace = new AdhocWorkspace();
        try
        {
            var solution = workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                filePath: solutionPath));
            var targetFrameworks = new[] { "NET8_0", "NET9_0" };
            foreach (var targetFramework in targetFrameworks)
            {
                var projectId = ProjectId.CreateNewId("Multi");
                var projectInfo = ProjectInfo.Create(
                        projectId,
                        VersionStamp.Create(),
                        name: "Multi",
                        assemblyName: "Multi",
                        language: LanguageNames.CSharp,
                        filePath: projectPath)
                    .WithMetadataReferences(TestWorkspaceBuilder.CoreReferences)
                    .WithParseOptions(new CSharpParseOptions(preprocessorSymbols: [targetFramework]));
                solution = solution.AddProject(projectInfo);
                solution = solution.AddDocument(
                    DocumentId.CreateNewId(projectId),
                    "Worker.cs",
                    SourceText.From(source),
                    filePath: sourcePath);
            }

            if (!workspace.TryApplyChanges(solution))
            {
                throw new System.InvalidOperationException("The test workspace rejected the multi-target solution.");
            }

            return new TestSolutionHandle(workspace.CurrentSolution, workspace);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static async Task<(string? DocumentationId, string ProjectMarker)[]> GetMultiTargetProjectMarkersAsync(Solution solution)
    {
        var fingerprint = await ComputeControlledFingerprintAsync(solution);
        var projects = solution.Projects.ToArray();
        var results = new System.Collections.Generic.List<(string? DocumentationId, string ProjectMarker)>();
        for (var index = 0; index < projects.Length; index++)
        {
            var project = projects[index];
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            var symbol = Assert.IsAssignableFrom<ISymbol>(compilation.GetTypeByMetadataName("Shared.Worker"));
            results.Add((DocumentationCommentId.CreateDeclarationId(symbol), fingerprint.ProjectMarkers[index].ContextFingerprint));
        }

        return results.ToArray();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The workspace lifetime is transferred to the returned TestSolutionHandle.")]
    private static TestSolutionHandle CreateReferenceContextSolution(bool varyMetadataReferences, bool varyProjectReferences)
    {
        const string solutionPath = @"C:\VirtualRepo\References.slnx";
        const string rootProjectPath = @"C:\VirtualRepo\Root\Root.csproj";
        const string rootSourcePath = @"C:\VirtualRepo\Root\Worker.cs";
        const string rootSource = "namespace Shared; public class Worker { public void Run() {} }";
        var workspace = new AdhocWorkspace();
        try
        {
            var solution = workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                filePath: solutionPath));
            var libraryOneId = ProjectId.CreateNewId("LibraryOne");
            var libraryTwoId = ProjectId.CreateNewId("LibraryTwo");
            solution = AddReferenceTestProject(
                solution,
                libraryOneId,
                "LibraryOne",
                @"C:\VirtualRepo\LibraryOne\LibraryOne.csproj",
                "namespace References; public class One {}",
                TestWorkspaceBuilder.CoreReferences);
            solution = AddReferenceTestProject(
                solution,
                libraryTwoId,
                "LibraryTwo",
                @"C:\VirtualRepo\LibraryTwo\LibraryTwo.csproj",
                "namespace References; public class Two {}",
                TestWorkspaceBuilder.CoreReferences);

            var uriReference = MetadataReference.CreateFromFile(typeof(System.Uri).Assembly.Location);
            var consoleReference = MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location);
            for (var index = 0; index < 2; index++)
            {
                var projectId = ProjectId.CreateNewId("Root");
                var metadataReferences = TestWorkspaceBuilder.CoreReferences
                    .Concat([varyMetadataReferences && index == 1 ? consoleReference : uriReference]);
                solution = AddReferenceTestProject(
                    solution,
                    projectId,
                    "Root",
                    rootProjectPath,
                    rootSource,
                    metadataReferences,
                    rootSourcePath);
                if (varyProjectReferences)
                {
                    solution = solution.AddProjectReference(
                        projectId,
                        new ProjectReference(index == 0 ? libraryOneId : libraryTwoId));
                }
            }

            if (!workspace.TryApplyChanges(solution))
            {
                throw new System.InvalidOperationException("The test workspace rejected the reference-context solution.");
            }

            return new TestSolutionHandle(workspace.CurrentSolution, workspace);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static Solution AddReferenceTestProject(
        Solution solution,
        ProjectId projectId,
        string name,
        string projectPath,
        string source,
        System.Collections.Generic.IEnumerable<MetadataReference> metadataReferences,
        string? sourcePath = null)
    {
        solution = solution.AddProject(
            ProjectInfo.Create(
                    projectId,
                    VersionStamp.Create(),
                    name,
                    name,
                    LanguageNames.CSharp,
                    filePath: projectPath)
                .WithMetadataReferences(metadataReferences)
                .WithParseOptions(new CSharpParseOptions()));
        return solution.AddDocument(
            DocumentId.CreateNewId(projectId),
            System.IO.Path.GetFileNameWithoutExtension(projectPath) + ".cs",
            SourceText.From(source),
            filePath: sourcePath ?? System.IO.Path.ChangeExtension(projectPath, ".cs"));
    }

    private static async Task<(string? DocumentationId, string ProjectMarker)[]> GetRootProjectMarkersAsync(Solution solution)
    {
        var fingerprint = await ComputeControlledFingerprintAsync(solution);
        var results = new System.Collections.Generic.List<(string? DocumentationId, string ProjectMarker)>();
        var projects = solution.Projects.ToArray();
        for (var index = 0; index < projects.Length; index++)
        {
            var project = projects[index];
            if (project.Name != "Root") continue;
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            var symbol = Assert.IsAssignableFrom<ISymbol>(compilation.GetTypeByMetadataName("Shared.Worker"));
            results.Add((DocumentationCommentId.CreateDeclarationId(symbol), fingerprint.ProjectMarkers[index].ContextFingerprint));
        }

        return results.ToArray();
    }

    private static async Task<SourceIdentityFingerprintData> ComputeControlledFingerprintAsync(Solution solution)
    {
        // These synthetic context fixtures exercise encoder graph semantics directly, not resident or loader admission.
        var captured = MetadataReferenceImageCapture.Capture(solution, previousInputs: null, cancellationToken: default);
        var projects = captured.Inputs.Projects.Select(item =>
        {
            var project = captured.Solution.GetProject(item.OwnerProjectId)!;
            var options = project.CompilationOptions as CSharpCompilationOptions;
            return item with
            {
                IsSupported = true,
                UnsupportedReason = null,
                OwnerCreatedAnalyzerConfigProvider = true,
                OwnerCreatedSyntaxTreeOptionsProvider = options?.SyntaxTreeOptionsProvider is not null,
                OwnerCreatedStrongNameProvider = options?.StrongNameProvider is not null,
            };
        }).ToImmutableArray();
        var validated = new SourceIdentityValidatedSnapshot(captured.Solution, captured.Inputs with { Projects = projects });
        var result = await SourceAnalysisIdentityEncoder.ComputeAsync(validated, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value!;
    }

}
