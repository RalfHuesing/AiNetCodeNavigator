#nullable enable

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Skeletons;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Skeletons;

[Trait("Category", "Unit")]
public sealed class SkeletonMapTests
{
    [Fact]
    public async Task BuildForDocumentAsync_RestrictsDeclarationsToTopLevelTypesAndDirectMembers()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var document = fixture.Solution.Projects.First().AddDocument("Nested.cs", """
            namespace Outline;
            public class Outer
            {
                public int Value = 12345;
                public int Read() => 54321;
                public class HiddenNested { public void HiddenMethod() {} }
            }
            """);
        var type = Assert.Single(await SkeletonMapBuilder.BuildForDocumentAsync(document, "."));
        Assert.Equal("Outer", type.Name);
        Assert.Equal(2, type.Members.Count);
        Assert.Contains(type.Members, member => member.Signature == "public int Value;");
        Assert.Contains(type.Members, member => member.Signature.Contains("Read()"));
        Assert.DoesNotContain(type.Members, member => member.Signature.Contains("Hidden") || member.Signature.Contains("12345") || member.Signature.Contains("54321"));
    }

    [Fact]
    public async Task BuildForDocumentAsync_ExtractsTypesAndMembersWithoutBodies()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var doc = project.Documents.Single(d => d.Name == "Greeter.cs");
        var solutionDir = Path.GetDirectoryName(fixture.Solution.FilePath) ?? "";

        var types = await SkeletonMapBuilder.BuildForDocumentAsync(doc, solutionDir);

        Assert.Single(types);
        var type = types[0];
        Assert.Equal("Greeter", type.Name);
        Assert.Equal("SampleNamespace", type.Namespace);
        Assert.Equal("class", type.Kind);

        // Members: Prefix property, Greet method, GreetLoud method
        Assert.Contains(type.Members, m => m.Signature.Contains("Prefix"));
        Assert.Contains(type.Members, m => m.Signature.Contains("Greet(string name)"));
        Assert.Contains(type.Members, m => m.Signature.Contains("GreetLoud(string name)"));

        // No method bodies in signature
        Assert.DoesNotContain("{ return", type.Members.First(m => m.Signature.Contains("Greet(")).Signature);
    }

    [Fact]
    public async Task BuildForDocumentAsync_HandlesRecordsAndStructs()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var doc = project.Documents.Single(d => d.Name == "Types.cs");
        var solutionDir = Path.GetDirectoryName(fixture.Solution.FilePath) ?? "";

        var types = await SkeletonMapBuilder.BuildForDocumentAsync(doc, solutionDir);

        Assert.Equal(3, types.Count);
        Assert.Contains(types, t => t.Name == "Person" && t.Kind == "record");
        Assert.Contains(types, t => t.Name == "Coordinate" && t.Kind == "record struct");
        Assert.Contains(types, t => t.Name == "ProcessingStatus" && t.Kind == "enum");
    }

    [Fact]
    public async Task BuildForDocumentAsync_ComposesNestedNamespaceNames()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var document = project.AddDocument(
            "NestedNamespace.cs",
            "namespace Outer { namespace Inner { public class NestedType { } } }");

        var types = await SkeletonMapBuilder.BuildForDocumentAsync(document, Path.GetDirectoryName(fixture.Solution.FilePath) ?? "");

        var type = Assert.Single(types);
        Assert.Equal("Outer.Inner", type.Namespace);
        Assert.Equal("NestedType", type.Name);
    }

    [Fact]
    public async Task BuildForDocumentAsync_OmitsMethodAndConstructorBodies()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var document = project.AddDocument("Bodies.cs", """
            public class BodySample
            {
                public BodySample() { System.Console.WriteLine("BODY_MARKER"); }
                public string Compute(string value) { return "BODY_MARKER"; }
                public string ComputeExpression(string value) => "BODY_MARKER";
                public string ComputedProperty => "BODY_MARKER";
            }
            """);

        var types = await SkeletonMapBuilder.BuildForDocumentAsync(document, Path.GetDirectoryName(fixture.Solution.FilePath) ?? "");
        var type = Assert.Single(types);

        Assert.Contains(type.Members, member => member.Kind == SkeletonMemberKind.Constructor);
        Assert.Contains(type.Members, member => member.Signature.Contains("Compute(string value)"));
        Assert.DoesNotContain(type.Members, member => member.Signature.Contains("BODY_MARKER", System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildForDocumentAsync_OmitsFieldAndEventInitializerBodiesFromStructuredResults()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var document = project.AddDocument("InitializerBodies.cs", """
            public class InitializerSample
            {
                private System.Action _callback = () => { System.Console.WriteLine("FIELD_BODY_MARKER"); };
                public event System.Action Changed = () => { System.Console.WriteLine("EVENT_BODY_MARKER"); };
            }
            """);

        var types = await SkeletonMapBuilder.BuildForDocumentAsync(document, Path.GetDirectoryName(fixture.Solution.FilePath) ?? "");
        var type = Assert.Single(types);

        Assert.DoesNotContain(type.Members, member => member.Signature.Contains("BODY_MARKER", System.StringComparison.Ordinal));
        Assert.Equal("private System.Action _callback;", Assert.Single(type.Members, member => member.Kind == SkeletonMemberKind.Field).Signature);
        Assert.Equal("public event System.Action Changed;", Assert.Single(type.Members, member => member.Kind == SkeletonMemberKind.Event).Signature);
    }

    [Fact]
    public async Task BuildMarkdownForDocumentAsync_EmitsPerVariableHandoffsForMultiVariableDeclarations()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var document = project.AddDocument("MultiVariables.cs", """
            public class MultiVariableSample
            {
                private System.Action _first = () => { System.Console.WriteLine("FIELD_BODY_MARKER"); }, _second = () => { System.Console.WriteLine("FIELD_BODY_MARKER"); };
                public event System.Action Changed = () => { System.Console.WriteLine("EVENT_BODY_MARKER"); }, Closed = () => { System.Console.WriteLine("EVENT_BODY_MARKER"); };
            }
            """);

        var markdown = await FileSkeletonBuilder.BuildMarkdownForDocumentAsync(document, fixture.Solution.FilePath ?? "");
        var lines = markdown.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        var memberLines = lines.Where(line => line.Contains("_first", System.StringComparison.Ordinal)
            || line.Contains("_second", System.StringComparison.Ordinal)
            || line.Contains("Changed", System.StringComparison.Ordinal)
            || line.Contains("Closed", System.StringComparison.Ordinal)).ToList();

        Assert.Equal(4, memberLines.Count);
        Assert.DoesNotContain("BODY_MARKER", markdown, System.StringComparison.Ordinal);

        var handoffIds = memberLines
            .Select(line => Regex.Match(line, @"handoffId: `(?<id>src:[^`]+)`", RegexOptions.CultureInvariant, System.TimeSpan.FromSeconds(1)))
            .ToList();
        Assert.All(handoffIds, match => Assert.True(match.Success));
        Assert.Equal(4, handoffIds.Select(match => match.Groups["id"].Value).Distinct().Count());

        foreach (var (line, match) in memberLines.Zip(handoffIds))
        {
            var expectedName = line.Contains("_first", System.StringComparison.Ordinal) ? "_first"
                : line.Contains("_second", System.StringComparison.Ordinal) ? "_second"
                : line.Contains("Changed", System.StringComparison.Ordinal) ? "Changed"
                : "Closed";
            var resolved = await SourceSymbolResolver.ResolveAsync(document.Project.Solution, match.Groups["id"].Value);
            Assert.True(resolved.IsSuccess);
            Assert.Equal(expectedName, resolved.Symbol!.Name);
        }
    }

    [Fact]
    public async Task BuildForDocumentAsync_FormatsDistinctHandoffsForEachFieldAndEventVariable()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var document = project.AddDocument("FormattedMultiVariables.cs", """
            namespace SkeletonTests;
            public class FormattedMultiVariableSample
            {
                private System.Action _first, _second;
                public event System.Action Changed, Closed;
            }
            """);
        var solutionDir = Path.GetDirectoryName(fixture.Solution.FilePath) ?? string.Empty;
        var types = await SkeletonMapBuilder.BuildForDocumentAsync(
            document,
            solutionDir,
            formatSymbolId: id => id is null ? null : $"formatted:{id}");
        var type = Assert.Single(types);
        var syntaxRoot = await document.GetSyntaxRootAsync();
        var semanticModel = await document.GetSemanticModelAsync();
        Assert.NotNull(syntaxRoot);
        Assert.NotNull(semanticModel);

        var expectedSymbolIds = syntaxRoot.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Select(variable => semanticModel.GetDeclaredSymbol(variable))
            .Where(symbol => symbol is not null)
            .ToDictionary(symbol => symbol!.Name, symbol => symbol!.GetDocumentationCommentId());

        Assert.Equal(4, type.Members.Count);
        foreach (var (name, documentationId) in expectedSymbolIds)
        {
            Assert.NotNull(documentationId);
            var member = Assert.Single(type.Members, candidate => candidate.Signature.Contains(name, System.StringComparison.Ordinal));
            Assert.Equal($"formatted:{documentationId}", member.Id);
        }

        Assert.Equal(4, type.Members.Select(member => member.Id).Distinct().Count());
    }

    [Fact]
    public async Task BuildForDocumentAsync_RejectsNullDocument()
    {
        await Assert.ThrowsAsync<System.ArgumentNullException>(
            () => SkeletonMapBuilder.BuildForDocumentAsync(null!, "."));
    }

    [Fact]
    public async Task BuildForProjectAsync_RejectsNullProject()
    {
        await Assert.ThrowsAsync<System.ArgumentNullException>(
            () => SkeletonMapBuilder.BuildForProjectAsync(null!, "."));
    }

    [Fact]
    public void SkeletonSyntaxWalker_RejectsNullSemanticModel()
    {
        Assert.Throws<System.ArgumentNullException>(() => new SkeletonSyntaxWalker(null!, "file.cs"));
    }

    [Fact]
    public async Task MarkdownRenderer_RendersValidMarkdown()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var doc = project.Documents.Single(d => d.Name == "Greeter.cs");

        var markdown = await FileSkeletonBuilder.BuildMarkdownForDocumentAsync(doc, fixture.Solution.FilePath ?? "");

        Assert.Contains("# AiNetCodeNavigator — Skeleton Map", markdown);
        Assert.Contains("### Greeter", markdown);
        Assert.Contains("```csharp", markdown);
        Assert.Contains("public string Greet(string name)", markdown);
        Assert.Contains("// handoffId:", markdown);
    }

    [Fact]
    public async Task MarkdownRenderer_HandoffRoundTripsToSourceSymbol()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var doc = project.Documents.Single(d => d.Name == "Greeter.cs");
        var markdown = await FileSkeletonBuilder.BuildMarkdownForDocumentAsync(doc, fixture.Solution.FilePath ?? "");
        var match = new Regex(
            @"handoffId: `(?<id>src:[^`]+)`",
            RegexOptions.CultureInvariant,
            System.TimeSpan.FromSeconds(1)).Match(markdown);
        Assert.True(match.Success, markdown);

        var resolved = await SourceSymbolResolver.ResolveAsync(fixture.Solution, match.Groups["id"].Value);
        Assert.True(resolved.IsSuccess);
        Assert.Equal("Greeter", resolved.Symbol!.Name);
    }

    [Fact]
    public async Task MarkdownRenderer_MemberHandoffRoundTripsToSourceSymbol()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var document = project.Documents.Single(d => d.Name == "Greeter.cs");
        var markdown = await FileSkeletonBuilder.BuildMarkdownForDocumentAsync(document, fixture.Solution.FilePath ?? "");
        var match = Regex.Match(
            markdown,
            @"public string Greet\(string name\).*handoffId: `(?<id>src:[^`]+)`",
            RegexOptions.CultureInvariant,
            System.TimeSpan.FromSeconds(1));
        Assert.True(match.Success, markdown);

        var resolved = await SourceSymbolResolver.ResolveAsync(fixture.Solution, match.Groups["id"].Value);
        Assert.True(resolved.IsSuccess);
        Assert.Equal("Greet", resolved.Symbol!.Name);
    }

    [Fact]
    public void MarkdownRenderer_RejectsNullTypes()
    {
        Assert.Throws<System.ArgumentNullException>(() => SkeletonMarkdownRenderer.Render(null!, "solution.slnx"));
    }

    [Fact]
    public void MarkdownRenderer_RejectsNullSolutionPath()
    {
        Assert.Throws<System.ArgumentNullException>(() => SkeletonMarkdownRenderer.Render([], null!));
    }
}
