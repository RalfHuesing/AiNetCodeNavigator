#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class StableSymbolReferenceTests
{
    [Theory]
    [InlineData("src:src/App%20Lib/App.csproj|M:Acme.Type.Run(System.String)", "src/App Lib/App.csproj", "M:Acme.Type.Run(System.String)")]
    [InlineData("asm:Acme%25Lib|T:Acme.Type", "Acme%Lib", "T:Acme.Type")]
    public void Codec_ParsesCanonicalReferences(string value, string owner, string declarationId)
    {
        Assert.True(StableSymbolReferenceCodec.TryParse(value, out var parsed, out var error));
        Assert.Null(error);
        Assert.NotNull(parsed);
        Assert.Equal(value, StableSymbolReferenceCodec.Format(parsed!));
        Assert.Equal(owner, parsed switch
        {
            StableSymbolReference.Source source => source.ProjectPath,
            StableSymbolReference.Assembly assembly => assembly.SimpleName,
            _ => null,
        });
        Assert.Equal(declarationId, parsed!.DeclarationId);
    }

    [Theory]
    [InlineData("SRC:src/App.csproj|T:Acme.Type")]
    [InlineData("src:src//App.csproj|T:Acme.Type")]
    [InlineData("src:src/./App.csproj|T:Acme.Type")]
    [InlineData("src:C:relative.csproj|T:Acme.Type")]
    [InlineData("src:src/App.csproj|T:Acme.Type%7cOther")]
    [InlineData("src:src/App.csproj|T:Acme.Type%41")]
    [InlineData("src:src/App.csproj|T:Acme.Type|extra")]
    [InlineData("asm:Acme Lib|T:Acme.Type")]
    [InlineData("src:src/App.csproj|X:Acme.Type")]
    public void Codec_RejectsMalformedOrNoncanonicalReferences(string value)
    {
        Assert.False(StableSymbolReferenceCodec.TryParse(value, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.Equal("INVALID_SYMBOL_REFERENCE", error?.Code);
    }

    [Fact]
    public void Codec_RejectsUnicodeControlsAndInvalidSurrogateSequences()
    {
        Assert.False(StableSymbolReferenceCodec.TryParse("src:src/App.csproj|T:Acme.Type\u0001", out _, out var controlError));
        Assert.Equal("INVALID_SYMBOL_REFERENCE", controlError?.Code);
        var invalidUnicode = "src:src/App.csproj|T:Acme.Type" + '\uD800';
        Assert.False(StableSymbolReferenceCodec.TryParse(invalidUnicode, out _, out var unicodeError));
        Assert.Equal("INVALID_SYMBOL_REFERENCE", unicodeError?.Code);
    }

    [Fact]
    public void Codec_DecodesPercentEscapesExactlyOnce()
    {
        const string value = "asm:Name%257CPart|T:Acme.Type";
        Assert.True(StableSymbolReferenceCodec.TryParse(value, out var parsed, out _));
        Assert.Equal("Name%7CPart", Assert.IsType<StableSymbolReference.Assembly>(parsed).SimpleName);
        Assert.Equal(value, StableSymbolReferenceCodec.Format(parsed!));
    }

    [Fact]
    public void Codec_RoundTripsUnicodeAndReservedPipeInOwnerAndDeclarationId()
    {
        StableSymbolReference[] references =
        [
            new StableSymbolReference.Source("src/Über|Lib/外部.csproj", "T:Acme.Über|Nested"),
            new StableSymbolReference.Assembly("Libraire|東京", "M:Acme.Δelta.Run(System.String|System.Int32)"),
        ];

        foreach (var reference in references)
        {
            var formatted = StableSymbolReferenceCodec.Format(reference);
            Assert.Contains("%7C", formatted, System.StringComparison.Ordinal);
            Assert.True(StableSymbolReferenceCodec.TryParse(formatted, out var parsed, out var error), error?.Message);
            Assert.Equal(reference, parsed);
            Assert.Equal(formatted, StableSymbolReferenceCodec.Format(parsed!));
        }
    }

    [Theory]
    [InlineData(" \" src:src/App.csproj|T:Acme.Type \" ")]
    [InlineData("`src:src/App.csproj|T:Acme.Type`")]
    public void InputPreprocessing_TrimsAsciiSpacesAndOneWrapper(string input)
    {
        Assert.True(StableSymbolReferenceCodec.TryPrepareReferenceInput(input, out var payload, out var error));
        Assert.Null(error);
        Assert.Equal("src:src/App.csproj|T:Acme.Type", payload);
    }

    [Theory]
    [InlineData("H:/repo/File.cs")]
    [InlineData("I:\\repo\\File.cs")]
    public void InputPreprocessing_DoesNotRouteDrivePathsAsLegacyHandles(string input)
    {
        Assert.False(StableSymbolReferenceCodec.TryPrepareReferenceInput(input, out _, out _));
    }

    [Theory]
    [InlineData("h:/")]
    [InlineData("h:\\")]
    [InlineData("i:/")]
    [InlineData("i:\\")]
    [InlineData("H:/")]
    [InlineData("H:\\")]
    [InlineData("I:/")]
    [InlineData("I:\\")]
    [InlineData("\"h:/\"")]
    [InlineData("`H:\\`")]
    [InlineData("\"i:/\"")]
    [InlineData("`I:\\`")]
    public void InputPreprocessing_RejectsLegacyDriveSelectorsWithEmptyRemainder(string input)
    {
        Assert.True(StableSymbolReferenceCodec.TryParseReferenceInput(input, null, out var reference, out var error));
        Assert.Null(reference);
        Assert.Equal("INVALID_SYMBOL_REFERENCE", error?.Code);
        AssertRecoveryHint(error?.Hint, "rediscover");
    }

    [Fact]
    public void InputPreprocessing_UsesOriginalWhenDiscoveryCleanupExposedPrefix()
    {
        Assert.True(StableSymbolReferenceCodec.TryParseReferenceInput(
            "\"\"src:src/App.csproj|T:Acme.Type\"\"",
            "src:src/App.csproj|T:Acme.Type",
            out var reference,
            out var error));
        Assert.Null(reference);
        Assert.Equal("INVALID_SYMBOL_REFERENCE", error?.Code);
    }

    [Fact]
    public void Codec_InvalidReferenceIncludesActionableRecoveryHint()
    {
        Assert.True(StableSymbolReferenceCodec.TryParseReferenceInput(
            "SRC:src/App.csproj|T:Sample.Widget", null, out var reference, out var error));
        Assert.Null(reference);
        Assert.Equal("INVALID_SYMBOL_REFERENCE", error?.Code);
        AssertRecoveryHint(error?.Hint, "rediscover");
    }

    [Theory]
    [InlineData("`src:src/App.csproj|T:Acme.Type", "src:src/App.csproj|T:Acme.Type")]
    [InlineData("\u00A0src:src/App.csproj|T:Acme.Type", "src:src/App.csproj|T:Acme.Type")]
    [InlineData("SRC:src/App.csproj|T:Acme.Type", null)]
    [InlineData("h:gdu0", null)]
    [InlineData("i:0:identifier", null)]
    public void Codec_ReferenceInputNeverFallsThroughAfterRouting(string original, string? discoveryCleaned)
    {
        Assert.True(StableSymbolReferenceCodec.TryParseReferenceInput(original, discoveryCleaned, out var reference, out var error));
        Assert.Null(reference);
        Assert.Equal("INVALID_SYMBOL_REFERENCE", error?.Code);
    }

    [Fact]
    public async Task SourceReference_ResolvesAfterBodyEditInCurrentSolution()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("App", [("App.cs", "namespace Sample; public class Widget { public int Run(string value) => 1; }")], VirtualProjectDirectory: "src/App"));
        var solution = workspace.Solution;
        var project = solution.Projects.Single();
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var symbol = Assert.IsAssignableFrom<ISymbol>(compilation!.GetTypeByMetadataName("Sample.Widget")!.GetMembers("Run").Single());

        var created = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, symbol);
        Assert.True(created.IsSuccess, created.Error?.Message);
        var reference = created.Value!;
        Assert.Equal("src/App/App.csproj", reference.ProjectPath);

        var document = project.Documents.Single();
        var edited = solution.WithDocumentText(document.Id, SourceText.From("namespace Sample; public class Widget { public int Run(string value) => 2; }"));
        var resolved = await ExactSourceSymbolResolver.ResolveAsync(edited, reference);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        Assert.Equal(reference.DeclarationId, DocumentationCommentId.CreateDeclarationId(resolved.Value!));
    }

    [Fact]
    public async Task SourceReference_RoundTripsAcrossIndependentReloadAndCompilationOptionChange()
    {
        using var initial = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("App", [("App.cs", "namespace Sample; public class Widget { public string? Name { get; set; } }")],
                Nullable: NullableContextOptions.Enable, VirtualProjectDirectory: "src/App"));
        var initialProject = initial.Solution.Projects.Single();
        var initialCompilation = await initialProject.GetCompilationAsync();
        Assert.NotNull(initialCompilation);
        var initialSymbol = Assert.IsAssignableFrom<ISymbol>(initialCompilation!.GetTypeByMetadataName("Sample.Widget"));
        var created = await ExactSourceSymbolResolver.CreateReferenceAsync(initial.Solution, initialProject, initialSymbol);
        Assert.True(created.IsSuccess, created.Error?.Message);

        using var reloaded = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("App", [("App.cs", "namespace Sample; public class Widget { public string? Name { get; set; } }")],
                Nullable: NullableContextOptions.Disable, VirtualProjectDirectory: "src/App"));
        var reloadedProject = reloaded.Solution.Projects.Single();
        Assert.Equal(initialProject.FilePath, reloadedProject.FilePath);
        var reloadedCompilation = await reloadedProject.GetCompilationAsync();
        Assert.NotNull(reloadedCompilation);
        var reloadedSymbol = Assert.IsAssignableFrom<ISymbol>(reloadedCompilation!.GetTypeByMetadataName("Sample.Widget"));
        var reloadedReference = await ExactSourceSymbolResolver.CreateReferenceAsync(reloaded.Solution, reloadedProject, reloadedSymbol);
        Assert.True(reloadedReference.IsSuccess, reloadedReference.Error?.Message);
        Assert.Equal(created.Value, reloadedReference.Value);

        var resolved = await ExactSourceSymbolResolver.ResolveAsync(reloaded.Solution, created.Value!);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        Assert.Equal(created.Value!.DeclarationId, DocumentationCommentId.CreateDeclarationId(resolved.Value!));
    }

    [Theory]
    [InlineData("namespace Sample; public class Widget { public void Renamed(string value) { } }")]
    [InlineData("namespace Sample; public class Widget { public void Run(int value) { } }")]
    [InlineData("namespace Sample; public class Widget { }")]
    public async Task SourceReference_RenameDeleteOrSignatureChangeDoesNotRedirect(string changedSource)
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("App", [("App.cs", "namespace Sample; public class Widget { public void Run(string value) { } }")], VirtualProjectDirectory: "src/App"));
        var project = workspace.Solution.Projects.Single();
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var type = Assert.IsAssignableFrom<INamedTypeSymbol>(compilation!.GetTypeByMetadataName("Sample.Widget"));
        var method = Assert.IsAssignableFrom<IMethodSymbol>(type.GetMembers("Run").Single());
        var created = await ExactSourceSymbolResolver.CreateReferenceAsync(workspace.Solution, project, method);
        Assert.True(created.IsSuccess, created.Error?.Message);

        var document = project.Documents.Single();
        var changed = workspace.Solution.WithDocumentText(document.Id, SourceText.From(changedSource));
        var result = await ExactSourceSymbolResolver.ResolveAsync(changed, created.Value!);

        Assert.False(result.IsSuccess);
        Assert.Equal("SYMBOL_NOT_FOUND", result.Error?.Code);
        AssertRecoveryHint(result.Error?.Hint, "rediscover");
    }

    [Fact]
    public async Task SourceReference_ProducerRequiresSymbolFromCurrentSolutionCompilation()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("App", [("App.cs", "namespace Sample; public class Widget { public int Run() => 1; }")], VirtualProjectDirectory: "src/App"));
        var oldSolution = workspace.Solution;
        var oldProject = oldSolution.Projects.Single();
        var oldCompilation = await oldProject.GetCompilationAsync();
        Assert.NotNull(oldCompilation);
        var oldSymbol = Assert.IsAssignableFrom<ISymbol>(oldCompilation!.GetTypeByMetadataName("Sample.Widget"));
        var document = oldProject.Documents.Single();
        var currentSolution = oldSolution.WithDocumentText(document.Id, SourceText.From("namespace Sample; public class Widget { public int Run() => 2; }"));

        var result = await ExactSourceSymbolResolver.CreateReferenceAsync(currentSolution, oldProject, oldSymbol);

        Assert.False(result.IsSuccess);
        Assert.Equal("TARGET_MISMATCH", result.Error?.Code);
    }

    [Fact]
    public async Task SourceReference_DuplicateLoadedOwnerContextsAreAmbiguous()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("AppOne", [("App.cs", "namespace Sample; public class Widget { }")], VirtualProjectDirectory: "src/App"),
            new ProjectSpec("AppTwo", [("App.cs", "namespace Sample; public class Widget { }")], VirtualProjectDirectory: "src/App"));
        var originalProjects = workspace.Solution.Projects.ToArray();
        var sharedProjectPath = @"C:\VirtualRepo\src\App\App.csproj";
        var solution = workspace.Solution
            .WithProjectFilePath(originalProjects[0].Id, sharedProjectPath)
            .WithProjectFilePath(originalProjects[1].Id, sharedProjectPath)
            .WithProjectName(originalProjects[1].Id, originalProjects[0].Name)
            .WithProjectAssemblyName(originalProjects[1].Id, originalProjects[0].AssemblyName!);
        var project = solution.GetProject(originalProjects[0].Id)!;
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var symbol = Assert.IsAssignableFrom<ISymbol>(compilation!.GetTypeByMetadataName("Sample.Widget"));

        var created = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, symbol);

        Assert.False(created.IsSuccess);
        Assert.Equal("AMBIGUOUS_SYMBOL", created.Error?.Code);
        Assert.Contains("TargetFramework=unknown", created.Error?.Message, System.StringComparison.Ordinal);
        Assert.Contains("multiplicity=2", created.Error?.Message, System.StringComparison.Ordinal);

        var raw = await SourceSymbolResolver.ResolveAsync(solution, "Widget");
        Assert.False(raw.IsSuccess);
        Assert.Equal("AMBIGUOUS_SYMBOL", raw.Error?.Code);
        Assert.Equal(2, raw.Candidates.Count);
        Assert.All(raw.Candidates, candidate => Assert.Null(candidate.HandoffId));
        Assert.All(raw.Candidates, candidate => Assert.Equal("Widget", candidate.Name));
    }

    [Fact]
    public async Task SourceReference_SameDeclarationInDifferentProjectsUsesExactProjectOwner()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("First", [("Worker.cs", "namespace Sample; public class Worker { }")], VirtualProjectDirectory: "src/First"),
            new ProjectSpec("Second", [("Worker.cs", "namespace Sample; public class Worker { }")], VirtualProjectDirectory: "src/Second"));
        var references = new System.Collections.Generic.List<StableSymbolReference.Source>();
        foreach (var project in workspace.Solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            var symbol = Assert.IsAssignableFrom<ISymbol>(compilation!.GetTypeByMetadataName("Sample.Worker"));
            var created = await ExactSourceSymbolResolver.CreateReferenceAsync(workspace.Solution, project, symbol);
            Assert.True(created.IsSuccess, created.Error?.Message);
            references.Add(created.Value!);
        }

        Assert.Equal(references[0].DeclarationId, references[1].DeclarationId);
        Assert.NotEqual(references[0].ProjectPath, references[1].ProjectPath);
        foreach (var reference in references)
        {
            var resolved = await ExactSourceSymbolResolver.ResolveAsync(workspace.Solution, reference);
            Assert.True(resolved.IsSuccess, resolved.Error?.Message);
            Assert.Equal(reference.DeclarationId, DocumentationCommentId.CreateDeclarationId(resolved.Value!));
        }
    }

    [Fact]
    public async Task SourceReference_WindowsPathCaseVariantsResolveTheSameLoadedOwner()
    {
        if (!System.OperatingSystem.IsWindows()) return;
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("App", [("App.cs", "namespace Sample; public class Widget { }")], VirtualProjectDirectory: "src/App"));
        var reference = new StableSymbolReference.Source("SRC/app/APP.csproj", "T:Sample.Widget");

        var resolved = await ExactSourceSymbolResolver.ResolveAsync(workspace.Solution, reference);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        Assert.Equal(reference.DeclarationId, DocumentationCommentId.CreateDeclarationId(resolved.Value!));
    }

    [Fact]
    public async Task SourceReference_PartialDeclarationsShareOneReference()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("App", [
                ("First.cs", "namespace Sample; public partial class Worker { }"),
                ("Second.cs", "namespace Sample; public partial class Worker { public void Run() { } }")], VirtualProjectDirectory: "src/App"));
        var project = workspace.Solution.Projects.Single();
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var symbol = Assert.IsAssignableFrom<ISymbol>(compilation!.GetTypeByMetadataName("Sample.Worker"));

        var created = await ExactSourceSymbolResolver.CreateReferenceAsync(workspace.Solution, project, symbol);
        Assert.True(created.IsSuccess, created.Error?.Message);
        var resolved = await ExactSourceSymbolResolver.ResolveAsync(workspace.Solution, created.Value!);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        Assert.Equal(created.Value!.DeclarationId, DocumentationCommentId.CreateDeclarationId(resolved.Value!));
    }

    [Fact]
    public async Task SourceReference_LinkedDocumentsRemainOwnedByTheirProjects()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("First", [("Shared.cs", "namespace Sample; public class Worker { }")], VirtualProjectDirectory: "src/Shared"),
            new ProjectSpec("Second", [("Shared.cs", "namespace Sample; public class Worker { }")], VirtualProjectDirectory: "src/Shared"));
        var references = new System.Collections.Generic.List<StableSymbolReference.Source>();
        var filePaths = new System.Collections.Generic.List<string?>();
        foreach (var project in workspace.Solution.Projects)
        {
            var document = project.Documents.Single();
            filePaths.Add(document.FilePath);
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            var symbol = Assert.IsAssignableFrom<ISymbol>(compilation!.GetTypeByMetadataName("Sample.Worker"));
            var created = await ExactSourceSymbolResolver.CreateReferenceAsync(workspace.Solution, project, symbol);
            Assert.True(created.IsSuccess, created.Error?.Message);
            references.Add(created.Value!);
        }

        Assert.Equal(filePaths[0], filePaths[1]);
        Assert.Equal(references[0].DeclarationId, references[1].DeclarationId);
        Assert.NotEqual(references[0].ProjectPath, references[1].ProjectPath);
    }

    [Fact]
    public async Task SourceReference_ExternalSameVolumeOwnerMustBeLoadedForResolution()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\src\Navigator.slnx",
            new ProjectSpec("External", [("External.cs", "namespace External; public class LibraryType { }")], VirtualProjectDirectory: "placeholder"));
        var initialProject = workspace.Solution.Projects.Single();
        var externalPath = @"C:\VirtualRepo\external\External.csproj";
        var solution = workspace.Solution.WithProjectFilePath(initialProject.Id, externalPath);
        var project = solution.GetProject(initialProject.Id)!;
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var symbol = Assert.IsAssignableFrom<ISymbol>(compilation!.GetTypeByMetadataName("External.LibraryType"));

        var created = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, symbol);
        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.Equal("../external/External.csproj", created.Value!.ProjectPath);
        var loaded = await ExactSourceSymbolResolver.ResolveAsync(solution, created.Value);
        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        Assert.Equal(created.Value.DeclarationId, DocumentationCommentId.CreateDeclarationId(loaded.Value!));

        var unloaded = await ExactSourceSymbolResolver.ResolveAsync(solution.RemoveProject(project.Id), created.Value);
        Assert.False(unloaded.IsSuccess);
        Assert.Equal("TARGET_MISMATCH", unloaded.Error?.Code);
        AssertRecoveryHint(unloaded.Error?.Hint, "intended solution");
    }

    [Fact]
    public async Task SourceReference_ExactDuplicateDeclarationIdIsAmbiguous()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("App", [
                ("First.cs", "namespace Sample; public class Container { public void Duplicate() { } }"),
                ("Second.cs", "namespace Sample; public class Container { public void Duplicate() { } }")], VirtualProjectDirectory: "src/App"));
        var project = workspace.Solution.Projects.Single();
        var symbols = new System.Collections.Generic.List<ISymbol>();
        foreach (var document in project.Documents)
        {
            var tree = await document.GetSyntaxTreeAsync();
            Assert.NotNull(tree);
            var root = await tree!.GetRootAsync();
            var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single());
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            var symbol = compilation!.GetSemanticModel(tree).GetDeclaredSymbol(declaration);
            symbols.Add(Assert.IsAssignableFrom<ISymbol>(symbol));
        }

        Assert.False(SymbolEqualityComparer.Default.Equals(symbols[0], symbols[1]));
        Assert.Equal(DocumentationCommentId.CreateDeclarationId(symbols[0]), DocumentationCommentId.CreateDeclarationId(symbols[1]));
        var direct = await ExactSourceSymbolResolver.ResolveAsync(
            workspace.Solution,
            new StableSymbolReference.Source("src/App/App.csproj", DocumentationCommentId.CreateDeclarationId(symbols[0])!));
        Assert.False(direct.IsSuccess);
        Assert.Equal("AMBIGUOUS_SYMBOL", direct.Error?.Code);
        AssertAmbiguousDeclarationRecoveryHint(direct.Error?.Hint);

        var produced = await ExactSourceSymbolResolver.CreateReferenceAsync(workspace.Solution, project, symbols[0]);
        Assert.False(produced.IsSuccess);
        Assert.Equal("AMBIGUOUS_SYMBOL", produced.Error?.Code);
        AssertAmbiguousDeclarationRecoveryHint(produced.Error?.Hint);
    }

    [Fact]
    public async Task SourceReference_RejectsLocalAndImplicitDeclarationsWithoutStableIds()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("App", [("App.cs", "namespace Sample; public class Worker { public void Use() { void Local() { } Local(); } }")], VirtualProjectDirectory: "src/App"));
        var solution = workspace.Solution;
        var project = solution.Projects.Single();
        var document = project.Documents.Single();
        var tree = await document.GetSyntaxTreeAsync();
        Assert.NotNull(tree);
        var root = await tree!.GetRootAsync();
        var localDeclaration = Assert.Single(root.DescendantNodes().OfType<LocalFunctionStatementSyntax>());
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var local = Assert.IsAssignableFrom<IMethodSymbol>(compilation!.GetSemanticModel(tree).GetDeclaredSymbol(localDeclaration));
        var localResult = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, local);
        Assert.False(localResult.IsSuccess);
        Assert.Equal("UNSUPPORTED_IDENTIFIER", localResult.Error?.Code);

        var worker = Assert.IsAssignableFrom<INamedTypeSymbol>(compilation.GetTypeByMetadataName("Sample.Worker"));
        var implicitConstructor = Assert.Single(worker.InstanceConstructors.Where(constructor => constructor.IsImplicitlyDeclared));
        var constructorResult = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, implicitConstructor);
        Assert.False(constructorResult.IsSuccess);
        Assert.Equal("UNSUPPORTED_IDENTIFIER", constructorResult.Error?.Code);
    }

    [Fact]
    public async Task SourceReference_NormalizesConstructedAndReducedExtensionMethods()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("App", [("Extensions.cs", "namespace Sample; public static class Extensions { public static T Echo<T>(this T value) => value; }")], VirtualProjectDirectory: "src/App"));
        var solution = workspace.Solution;
        var project = solution.Projects.Single();
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var extensions = Assert.IsAssignableFrom<INamedTypeSymbol>(compilation!.GetTypeByMetadataName("Sample.Extensions"));
        var method = Assert.IsAssignableFrom<IMethodSymbol>(extensions.GetMembers("Echo").Single());
        var constructed = method.Construct(compilation.GetSpecialType(SpecialType.System_Int32));
        var reduced = method.ReduceExtensionMethod(compilation.GetSpecialType(SpecialType.System_Int32));
        Assert.NotNull(reduced);

        var originalReference = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, method);
        var constructedReference = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, constructed);
        var reducedReference = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, reduced!);
        Assert.True(originalReference.IsSuccess, originalReference.Error?.Message);
        Assert.True(constructedReference.IsSuccess, constructedReference.Error?.Message);
        Assert.True(reducedReference.IsSuccess, reducedReference.Error?.Message);
        Assert.Equal(originalReference.Value, constructedReference.Value);
        Assert.Equal(originalReference.Value, reducedReference.Value);
    }

    [Fact]
    public async Task SourceReference_RoundTripsSupportedDeclarationKindsAndOverloads()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("App", [("Members.cs", """
                using System;
                namespace Sample;
                public interface IContract { void Execute(); }
                public class Container : IContract
                {
                    public Container() { }
                    public Container(int value) { }
                    public static Container operator +(Container left, Container right) => left;
                    void IContract.Execute() { }
                    public void Overload(int value) { }
                    public void Overload(string value) { }
                    public void Generic<T>(T value) { }
                    public class Nested { }
                    public int Property { get; set; }
                    public event Action? Changed;
                    public int Field;
                }
                """)], VirtualProjectDirectory: "src/App"));
        var solution = workspace.Solution;
        var project = solution.Projects.Single();
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var container = Assert.IsAssignableFrom<INamedTypeSymbol>(compilation!.GetTypeByMetadataName("Sample.Container"));
        var declarations = new System.Collections.Generic.List<ISymbol> { container };
        declarations.AddRange(container.InstanceConstructors.Where(symbol => !symbol.IsImplicitlyDeclared));
        declarations.AddRange(container.GetMembers("Overload"));
        declarations.Add(Assert.Single(container.GetMembers("Generic")));
        declarations.Add(Assert.Single(container.GetMembers().OfType<IMethodSymbol>().Where(symbol => symbol.MethodKind == MethodKind.UserDefinedOperator)));
        declarations.Add(Assert.Single(container.GetMembers().OfType<IMethodSymbol>().Where(symbol => symbol.ExplicitInterfaceImplementations.Length > 0)));
        declarations.Add(Assert.Single(container.GetTypeMembers("Nested")));
        declarations.Add(Assert.Single(container.GetMembers("Property")));
        declarations.Add(Assert.Single(container.GetMembers("Changed")));
        declarations.Add(Assert.Single(container.GetMembers("Field")));

        foreach (var declaration in declarations)
        {
            var created = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, declaration);
            Assert.True(created.IsSuccess, $"{declaration.Kind} {declaration.ToDisplayString()}: {created.Error?.Message}");
            var resolved = await ExactSourceSymbolResolver.ResolveAsync(solution, created.Value!);
            Assert.True(resolved.IsSuccess, resolved.Error?.Message);
            Assert.True(SymbolEqualityComparer.Default.Equals(declaration.OriginalDefinition, resolved.Value), declaration.ToDisplayString());
            Assert.Equal(created.Value!.DeclarationId, DocumentationCommentId.CreateDeclarationId(resolved.Value!));
        }
    }

    [Fact]
    public async Task SourceReference_FileLocalTypesFollowRoslynDeclarationIdRoundTripOrRejectUnsupportedIds()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("App", [
                ("First.cs", "file class Local { public void First() { } }"),
                ("Second.cs", "file class Local { public void Second() { } }")], VirtualProjectDirectory: "src/App"));
        var solution = workspace.Solution;
        var project = solution.Projects.Single();
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var declarations = new System.Collections.Generic.List<ISymbol>();
        foreach (var document in project.Documents)
        {
            var tree = await document.GetSyntaxTreeAsync();
            Assert.NotNull(tree);
            var root = await tree!.GetRootAsync();
            var declaration = Assert.IsAssignableFrom<ClassDeclarationSyntax>(root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single());
            declarations.Add(Assert.IsAssignableFrom<ISymbol>(compilation!.GetSemanticModel(tree).GetDeclaredSymbol(declaration)));
        }
        Assert.False(SymbolEqualityComparer.Default.Equals(declarations[0], declarations[1]));

        var declarationIds = declarations.Select(DocumentationCommentId.CreateDeclarationId).ToArray();
        if (declarationIds.Any(id => !StableSymbolReferenceCodec.IsCanonicalDeclarationId(id ?? string.Empty)))
        {
            foreach (var declaration in declarations)
            {
                var unsupported = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, declaration);
                Assert.False(unsupported.IsSuccess);
                Assert.Equal("UNSUPPORTED_IDENTIFIER", unsupported.Error?.Code);
            }
            return;
        }

        if (string.Equals(declarationIds[0], declarationIds[1], System.StringComparison.Ordinal))
        {
            var direct = await ExactSourceSymbolResolver.ResolveAsync(solution,
                new StableSymbolReference.Source("src/App/App.csproj", declarationIds[0]!));
            Assert.False(direct.IsSuccess);
            Assert.Equal("AMBIGUOUS_SYMBOL", direct.Error?.Code);
            foreach (var declaration in declarations)
            {
                var ambiguous = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, declaration);
                Assert.False(ambiguous.IsSuccess);
                Assert.Equal("AMBIGUOUS_SYMBOL", ambiguous.Error?.Code);
            }
            return;
        }

        foreach (var declaration in declarations)
        {
            var created = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, declaration);
            Assert.True(created.IsSuccess, created.Error?.Message);
            var resolved = await ExactSourceSymbolResolver.ResolveAsync(solution, created.Value!);
            Assert.True(resolved.IsSuccess, resolved.Error?.Message);
            Assert.True(SymbolEqualityComparer.Default.Equals(declaration, resolved.Value));
        }
    }

    [Fact]
    public async Task SourceReference_ProducerAndResolverIncludeMaterializedGeneratedDocumentsInOwner()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("App", [("App.cs", "namespace Sample; public class Ordinary { }")], VirtualProjectDirectory: "src/App"));
        var initialProject = workspace.Solution.Projects.Single();
        var generatorReference = new TestGeneratorReference(new R01IncrementalTestSourceGenerator().AsSourceGenerator());
        var projectWithGenerator = initialProject.WithAnalyzerReferences(initialProject.AnalyzerReferences.Append(generatorReference));
        Assert.True(workspace.Workspace.TryApplyChanges(projectWithGenerator.Solution));
        var solution = workspace.Workspace.CurrentSolution;
        var project = solution.GetProject(initialProject.Id)!;

        var generatedDocuments = await project.GetSourceGeneratedDocumentsAsync();
        var generatedDocument = Assert.Single(generatedDocuments);
        Assert.Contains("R01Generated.g.cs", generatedDocument.FilePath ?? generatedDocument.Name, System.StringComparison.Ordinal);
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var generatedSymbol = Assert.IsAssignableFrom<ISymbol>(compilation!.GetTypeByMetadataName("Generated.NamespaceType"));

        var created = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, generatedSymbol);
        Assert.True(created.IsSuccess, created.Error?.Message);
        var resolved = await ExactSourceSymbolResolver.ResolveAsync(solution, created.Value!);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        Assert.True(SymbolEqualityComparer.Default.Equals(generatedSymbol, resolved.Value));
    }

    [Theory]
    [InlineData("Generated.*")]
    [InlineData("Generated.?*")]
    [InlineData("Generated.^NamespaceType$")]
    [InlineData("*")]
    [InlineData("**")]
    [InlineData("?*")]
    public async Task FindSymbolScanner_KindAllIncludesGeneratedTypesButNotNamespaceDeclarations(string pattern)
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\GeneratedFind.slnx",
            new ProjectSpec("App", [("App.cs", "namespace Sample; public class Ordinary { }")], VirtualProjectDirectory: "src/App"));
        var initialProject = workspace.Solution.Projects.Single();
        var generatorReference = new TestGeneratorReference(new R01IncrementalTestSourceGenerator().AsSourceGenerator());
        var projectWithGenerator = initialProject.WithAnalyzerReferences(initialProject.AnalyzerReferences.Append(generatorReference));
        Assert.True(workspace.Workspace.TryApplyChanges(projectWithGenerator.Solution));
        var solution = workspace.Workspace.CurrentSolution;
        var project = solution.GetProject(initialProject.Id)!;
        var generatedDocument = Assert.Single(await project.GetSourceGeneratedDocumentsAsync());
        Assert.Contains("namespace Generated;", (await generatedDocument.GetTextAsync()).ToString(), StringComparison.Ordinal);

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, pattern, Kind: SymbolKindFilter.All, IncludeGenerated: true)
            { NamespaceFilter = "Generated" });

        Assert.Null(result.Error);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("NamespaceType", entry.Name);
        Assert.Equal("class", entry.Kind);
        Assert.DoesNotContain(result.Entries, candidate => candidate.Kind == "namespace");
    }

    [Fact]
    public async Task SourceReference_ExcludesProjectReferenceAndMetadataDeclarations()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("Dependency", [("External.cs", "namespace External; public class SharedType { }")], VirtualProjectDirectory: "src/Dependency"),
            new ProjectSpec("App", [("App.cs", "namespace Sample; public class Worker { }")], ProjectReferences: ["Dependency"], VirtualProjectDirectory: "src/App"));
        var solution = workspace.Solution;
        var project = solution.Projects.Single(candidate => candidate.Name == "App");
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);

        var external = Assert.IsAssignableFrom<ISymbol>(compilation!.GetTypeByMetadataName("External.SharedType"));
        var producerResult = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, project, external);
        Assert.False(producerResult.IsSuccess);
        Assert.Equal("TARGET_MISMATCH", producerResult.Error?.Code);
        AssertRecoveryHint(producerResult.Error?.Hint, "select");

        var projectReference = await ExactSourceSymbolResolver.ResolveAsync(
            solution, new StableSymbolReference.Source("src/App/App.csproj", "T:External.SharedType"));
        var metadataReference = await ExactSourceSymbolResolver.ResolveAsync(
            solution, new StableSymbolReference.Source("src/App/App.csproj", "T:System.String"));
        Assert.False(projectReference.IsSuccess);
        Assert.Equal("SYMBOL_NOT_FOUND", projectReference.Error?.Code);
        AssertRecoveryHint(projectReference.Error?.Hint, "rediscover");
        Assert.False(metadataReference.IsSuccess);
        Assert.Equal("SYMBOL_NOT_FOUND", metadataReference.Error?.Code);
        AssertRecoveryHint(metadataReference.Error?.Hint, "rediscover");
    }

    [Fact]
    public async Task SourceReference_UnsupportedLoadedLanguageIsWorkspaceDiagnostic()
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.AddSolution(SolutionInfo.Create(
            SolutionId.CreateNewId(), VersionStamp.Create(), filePath: @"C:\VirtualRepo\Unsupported.slnx"));
        var projectId = ProjectId.CreateNewId("NoCompiler");
        solution = solution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "NoCompiler", "NoCompiler",
            language: LanguageNames.VisualBasic, filePath: @"C:\VirtualRepo\src\NoCompiler\NoCompiler.vbproj"));
        var reference = new StableSymbolReference.Source("src/NoCompiler/NoCompiler.vbproj", "T:NoCompiler.Type");

        var result = await ExactSourceSymbolResolver.ResolveAsync(solution, reference);

        Assert.False(result.IsSuccess);
        Assert.Equal("WORKSPACE_DIAGNOSTIC", result.Error?.Code);
        Assert.Contains("supported C# compilation context", result.Error?.Message, System.StringComparison.Ordinal);
        AssertRecoveryHint(result.Error?.Hint, "configuration");

        using var symbolWorkspace = TestWorkspaceBuilder.CreateSolution("namespace Other; public class Type { }");
        var symbolProject = symbolWorkspace.Solution.Projects.Single();
        var symbolCompilation = await symbolProject.GetCompilationAsync();
        Assert.NotNull(symbolCompilation);
        var symbol = Assert.IsAssignableFrom<ISymbol>(symbolCompilation!.GetTypeByMetadataName("Other.Type"));
        var producerResult = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, solution.GetProject(projectId)!, symbol);
        Assert.False(producerResult.IsSuccess);
        Assert.Equal("WORKSPACE_DIAGNOSTIC", producerResult.Error?.Code);
        AssertRecoveryHint(producerResult.Error?.Hint, "configuration");
    }

    [Fact]
    public async Task SourceReference_AmbiguityListsLoadedBuildPropertiesInStableOrder()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Stable.slnx",
            new ProjectSpec("Zed", [("App.cs", "namespace Sample; public class Widget { }")], VirtualProjectDirectory: "src/App"),
            new ProjectSpec("Alpha", [("App.cs", "namespace Sample; public class Widget { }")], VirtualProjectDirectory: "src/App"));
        var originalProjects = workspace.Solution.Projects.ToArray();
        var sharedProjectPath = @"C:\VirtualRepo\src\App\App.csproj";
        var solution = workspace.Solution
            .WithProjectFilePath(originalProjects[0].Id, sharedProjectPath)
            .WithProjectFilePath(originalProjects[1].Id, sharedProjectPath);
        foreach (var originalProject in solution.Projects.ToArray())
        {
            var project = solution.GetProject(originalProject.Id)!;
            var framework = project.Name == "Zed" ? "net8.0" : "net9.0";
            var configuration = project.Name == "Zed" ? "Debug" : "Release";
            var platform = project.Name == "Zed" ? "AnyCPU" : "x64";
            solution = project.AddAnalyzerConfigDocument("build.globalconfig",
                SourceText.From($"is_global = true{System.Environment.NewLine}build_property.TargetFramework = {framework}{System.Environment.NewLine}build_property.Configuration = {configuration}{System.Environment.NewLine}build_property.Platform = {platform}"),
                filePath: System.IO.Path.Combine(System.IO.Path.GetDirectoryName(project.FilePath)!, project.Name + ".globalconfig"))
                .Project.Solution;
        }
        var selected = solution.GetProject(originalProjects[0].Id)!;
        var compilation = await selected.GetCompilationAsync();
        Assert.NotNull(compilation);
        var symbol = Assert.IsAssignableFrom<ISymbol>(compilation!.GetTypeByMetadataName("Sample.Widget"));

        var result = await ExactSourceSymbolResolver.CreateReferenceAsync(solution, selected, symbol);

        Assert.False(result.IsSuccess);
        Assert.Equal("AMBIGUOUS_SYMBOL", result.Error?.Code);
        var message = result.Error?.Message ?? string.Empty;
        Assert.True(message.IndexOf("project=Alpha", System.StringComparison.Ordinal) < message.IndexOf("project=Zed", System.StringComparison.Ordinal));
        Assert.Contains("TargetFramework=net9.0 | Configuration=Release | Platform=x64", message, System.StringComparison.Ordinal);
        Assert.Contains("TargetFramework=net8.0 | Configuration=Debug | Platform=AnyCPU", message, System.StringComparison.Ordinal);
    }

    private static void AssertRecoveryHint(string? hint, string action)
    {
        Assert.False(string.IsNullOrWhiteSpace(hint));
        Assert.Contains(action, hint, System.StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertAmbiguousDeclarationRecoveryHint(string? hint)
    {
        Assert.False(string.IsNullOrWhiteSpace(hint));
        Assert.Contains("unique", hint, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("raw source location", hint, System.StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestGeneratorReference(ISourceGenerator generator) : AnalyzerReference
    {
        public override string? FullPath => null;
        public override string Display => "R01 source generator fixture";
        public override object Id => "R01 source generator fixture";
        public override System.Collections.Immutable.ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => [];
        public override System.Collections.Immutable.ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [];
        public override System.Collections.Immutable.ImmutableArray<ISourceGenerator> GetGenerators(string language) =>
            language == LanguageNames.CSharp ? [generator] : [];
        public override System.Collections.Immutable.ImmutableArray<ISourceGenerator> GetGeneratorsForAllLanguages() => [generator];
    }

    private sealed class R01IncrementalTestSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterPostInitializationOutput(static postInitialization =>
                postInitialization.AddSource("R01Generated.g.cs", "namespace Generated; public class NamespaceType { }"));
    }
}
