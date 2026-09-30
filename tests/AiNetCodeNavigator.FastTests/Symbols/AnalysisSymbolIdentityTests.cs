#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
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
}
