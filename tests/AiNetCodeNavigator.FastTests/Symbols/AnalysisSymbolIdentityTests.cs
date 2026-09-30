#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
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
    public async Task FormatHandoff_SourceSymbol_FormatsCorrectIdentifier()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);

        var greeterType = compilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);

        var identity = AnalysisSymbolIdentity.ForSource(
            @"C:\app\sample.sln",
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            fixture.Solution);

        var handoffId = identity.FormatHandoff(greeterType, fixture.Solution);
        Assert.NotNull(handoffId);
        Assert.StartsWith("i:0:", handoffId, System.StringComparison.Ordinal);
        Assert.Contains("T:SampleNamespace.Greeter", handoffId, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatHandoff_SameDocumentationIdInDifferentProjects_UsesDifferentProjectIdentity()
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
        var identity = AnalysisSymbolIdentity.ForSource(
            @"C:\VirtualRepo\Identity.slnx",
            new string('a', 64),
            solution);

        Assert.Equal(
            DocumentationCommentId.CreateDeclarationId(first),
            DocumentationCommentId.CreateDeclarationId(second));
        var firstId = identity.FormatHandoff(first, solution);
        var secondId = identity.FormatHandoff(second, solution);

        Assert.NotNull(firstId);
        Assert.NotNull(secondId);
        Assert.NotEqual(firstId, secondId);
        Assert.Contains("~p:", firstId, System.StringComparison.Ordinal);
        Assert.Contains("~p:", secondId, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatHandoff_SameProjectPathAndDocumentationId_DistinguishesTargetFrameworksStably()
    {
        using var firstSnapshot = CreateMultiTargetSolution();
        using var reloadedSnapshot = CreateMultiTargetSolution();
        var firstIdentity = AnalysisSymbolIdentity.ForSource(
            @"C:\VirtualRepo\Multi.slnx",
            new string('e', 64),
            firstSnapshot.Solution);
        var reloadedIdentity = AnalysisSymbolIdentity.ForSource(
            @"C:\VirtualRepo\Multi.slnx",
            new string('e', 64),
            reloadedSnapshot.Solution);

        var firstIds = await GetMultiTargetHandoffsAsync(firstSnapshot.Solution, firstIdentity);
        var reloadedIds = await GetMultiTargetHandoffsAsync(reloadedSnapshot.Solution, reloadedIdentity);

        Assert.Equal(firstIds[0].DocumentationId, firstIds[1].DocumentationId);
        Assert.NotEqual(firstIds[0].Handoff, firstIds[1].Handoff);
        Assert.Equal(firstIds, reloadedIds);
    }

    [Fact]
    public async Task FormatHandoff_SameProjectPathAndOptions_DistinguishesMetadataReferencesStably()
    {
        using var firstSnapshot = CreateReferenceContextSolution(varyMetadataReferences: true, varyProjectReferences: false);
        using var reloadedSnapshot = CreateReferenceContextSolution(varyMetadataReferences: true, varyProjectReferences: false);
        var firstIds = await GetRootHandoffsAsync(firstSnapshot.Solution);
        var reloadedIds = await GetRootHandoffsAsync(reloadedSnapshot.Solution);

        Assert.Equal(firstIds[0].DocumentationId, firstIds[1].DocumentationId);
        Assert.NotEqual(firstIds[0].Handoff, firstIds[1].Handoff);
        Assert.Equal(firstIds, reloadedIds);
    }

    [Fact]
    public async Task FormatHandoff_SameProjectPathAndOptions_DistinguishesProjectReferencesStably()
    {
        using var firstSnapshot = CreateReferenceContextSolution(varyMetadataReferences: false, varyProjectReferences: true);
        using var reloadedSnapshot = CreateReferenceContextSolution(varyMetadataReferences: false, varyProjectReferences: true);
        var firstIds = await GetRootHandoffsAsync(firstSnapshot.Solution);
        var reloadedIds = await GetRootHandoffsAsync(reloadedSnapshot.Solution);

        Assert.Equal(firstIds[0].DocumentationId, firstIds[1].DocumentationId);
        Assert.NotEqual(firstIds[0].Handoff, firstIds[1].Handoff);
        Assert.Equal(firstIds, reloadedIds);
    }

    [Fact]
    public async Task FormatHandoff_IndistinguishableProjectContexts_SuppressesAmbiguousHandoffs()
    {
        using var snapshot = CreateReferenceContextSolution(varyMetadataReferences: false, varyProjectReferences: false);

        var handoffs = await GetRootHandoffsAsync(snapshot.Solution);

        Assert.All(handoffs, result => Assert.Null(result.Handoff));
    }

    [Fact]
    public Task FormatHandoff_CaseVariantSourceTargetPaths_UseTheSameHandoffId() =>
        AssertCaseVariantTargetPathsAsync(SymbolHandoffOrigin.Source);

    [Fact]
    public Task FormatHandoff_CaseVariantAssemblyTargetPaths_UseTheSameHandoffId() =>
        AssertCaseVariantTargetPathsAsync(SymbolHandoffOrigin.Assembly);

    private static async Task AssertCaseVariantTargetPathsAsync(SymbolHandoffOrigin origin)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var symbol = Assert.IsAssignableFrom<ISymbol>(compilation.GetTypeByMetadataName("SampleNamespace.Greeter"));
        const string contentHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        var mixedCasePath = origin == SymbolHandoffOrigin.Source
            ? @"C:\VirtualRepo\SampleSolution.slnx"
            : @"C:\VirtualRepo\Sample.dll";
        var lowerCasePath = origin == SymbolHandoffOrigin.Source
            ? @"c:\virtualrepo\samplesolution.slnx"
            : @"c:\virtualrepo\sample.dll";
        var mixedCaseIdentity = origin == SymbolHandoffOrigin.Source
            ? AnalysisSymbolIdentity.ForSource(mixedCasePath, contentHash, fixture.Solution)
            : AnalysisSymbolIdentity.ForAssembly(mixedCasePath, contentHash);
        var lowerCaseIdentity = origin == SymbolHandoffOrigin.Source
            ? AnalysisSymbolIdentity.ForSource(lowerCasePath, contentHash, fixture.Solution)
            : AnalysisSymbolIdentity.ForAssembly(lowerCasePath, contentHash);
        var mixedCaseHandoff = origin == SymbolHandoffOrigin.Source
            ? mixedCaseIdentity.FormatHandoff(symbol, fixture.Solution)
            : mixedCaseIdentity.FormatHandoff(symbol);
        var lowerCaseHandoff = origin == SymbolHandoffOrigin.Source
            ? lowerCaseIdentity.FormatHandoff(symbol, fixture.Solution)
            : lowerCaseIdentity.FormatHandoff(symbol);

        Assert.NotNull(mixedCaseHandoff);
        Assert.NotNull(lowerCaseHandoff);
        Assert.True(SymbolHandoffIdentifier.TryParse(mixedCaseHandoff, out var parsedMixedCase));
        Assert.True(SymbolHandoffIdentifier.TryParse(lowerCaseHandoff, out var parsedLowerCase));
        Assert.Equal(origin, parsedMixedCase.Origin);
        Assert.Equal(origin, parsedLowerCase.Origin);
        Assert.Equal(mixedCaseHandoff, lowerCaseHandoff);
    }

    [Fact]
    public void CreateSourceSnapshotHash_CaseVariantWindowsTargetPaths_HaveTheSameHash()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var fileStates = new System.Collections.Generic.Dictionary<string, AiNetCodeNavigator.Core.Workspace.DocumentFileState>();

        var upperCaseHash = AnalysisSymbolIdentity.CreateSourceSnapshotHash(
            @"C:\VirtualRepo\Sample.slnx",
            fileStates);
        var lowerCaseHash = AnalysisSymbolIdentity.CreateSourceSnapshotHash(
            @"c:\virtualrepo\sample.slnx",
            fileStates);

        Assert.Equal(upperCaseHash, lowerCaseHash);
    }

    [Fact]
    public async Task FormatHandoff_ForeignSolutionSymbol_DoesNotCreateAnUnboundIdentity()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var symbol = Assert.IsAssignableFrom<ISymbol>(compilation.GetTypeByMetadataName("SampleNamespace.Greeter"));
        var identity = AnalysisSymbolIdentity.ForSource(
            @"C:\VirtualRepo\SampleSolution.slnx",
            new string('b', 64),
            fixture.Solution);

        using var foreignSolution = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Foreign.slnx",
            new ProjectSpec("Foreign", [("Greeter.cs", SampleCodeFixtures.GreeterSource)]));
        var foreignProject = Assert.Single(foreignSolution.Solution.Projects);
        var foreignCompilation = await foreignProject.GetCompilationAsync();
        Assert.NotNull(foreignCompilation);
        var foreignSymbol = Assert.IsAssignableFrom<ISymbol>(foreignCompilation.GetTypeByMetadataName("SampleNamespace.Greeter"));
        Assert.Null(identity.FormatHandoff(foreignSymbol, fixture.Solution));

        var withoutProjectMap = AnalysisSymbolIdentity.ForSource(
            @"C:\VirtualRepo\SampleSolution.slnx",
            new string('b', 64));
        Assert.Null(withoutProjectMap.FormatHandoff(symbol));
    }

    [Fact]
    public async Task FormatHandoff_ProjectWithoutStablePath_DoesNotUseTransientProjectId()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            "namespace Sample; public class Worker { public void Run() {} }");
        var project = Assert.Single(workspace.Solution.Projects);
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var symbol = Assert.IsAssignableFrom<ISymbol>(compilation.GetTypeByMetadataName("Sample.Worker"));
        var identity = AnalysisSymbolIdentity.ForSource(
            @"C:\VirtualRepo\Snapshot.slnx",
            new string('c', 64),
            workspace.Solution);

        Assert.Null(identity.FormatHandoff(symbol, workspace.Solution));
    }

    [Fact]
    public async Task CreateCanonicalSymbolIdentifier_UsesDocumentationIdsForOverloads()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            "namespace Names; public class Worker { public void Run(int value) {} public void Run(string value) {} }");
        var project = Assert.Single(workspace.Solution.Projects);
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var worker = compilation.GetTypeByMetadataName("Names.Worker");
        Assert.NotNull(worker);
        var overloads = worker.GetMembers("Run").OfType<IMethodSymbol>().ToArray();

        var identifiers = overloads.Select(AnalysisSymbolIdentity.CreateCanonicalSymbolIdentifier).ToArray();

        Assert.Equal(2, identifiers.Length);
        Assert.All(identifiers, identifier => Assert.StartsWith("M:Names.Worker.Run(", identifier, System.StringComparison.Ordinal));
        Assert.NotEqual(identifiers[0], identifiers[1]);
    }

    [Fact]
    public async Task CreateCanonicalSymbolIdentifier_UsesFileAndLineForLocalFunctions()
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
            .Select(node => AnalysisSymbolIdentity.CreateCanonicalSymbolIdentifier(model.GetDeclaredSymbol(node)!))
            .ToArray();

        Assert.Equal(2, identifiers.Length);
        Assert.All(identifiers, identifier => Assert.StartsWith("L:C:\\VIRTUALREPO\\LINES\\WORKER.CS:", identifier, System.StringComparison.Ordinal));
        Assert.NotEqual(identifiers[0], identifiers[1]);
        Assert.EndsWith(":5:13", identifiers[0], System.StringComparison.Ordinal);
        Assert.EndsWith(":6:13", identifiers[1], System.StringComparison.Ordinal);
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

    private static async Task<(string? DocumentationId, string? Handoff)[]> GetMultiTargetHandoffsAsync(
        Solution solution,
        AnalysisSymbolIdentity identity)
    {
        var results = new System.Collections.Generic.List<(string? DocumentationId, string? Handoff)>();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            var symbol = Assert.IsAssignableFrom<ISymbol>(compilation.GetTypeByMetadataName("Shared.Worker"));
            results.Add((DocumentationCommentId.CreateDeclarationId(symbol), identity.FormatHandoff(symbol, solution)));
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

    private static async Task<(string? DocumentationId, string? Handoff)[]> GetRootHandoffsAsync(Solution solution)
    {
        var identity = AnalysisSymbolIdentity.ForSource(
            @"C:\VirtualRepo\References.slnx",
            new string('f', 64),
            solution);
        var results = new System.Collections.Generic.List<(string? DocumentationId, string? Handoff)>();
        foreach (var project in solution.Projects.Where(project => project.Name == "Root"))
        {
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            var symbol = Assert.IsAssignableFrom<ISymbol>(compilation.GetTypeByMetadataName("Shared.Worker"));
            results.Add((DocumentationCommentId.CreateDeclarationId(symbol), identity.FormatHandoff(symbol, solution)));
        }

        return results.ToArray();
    }

}
