#nullable enable

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Skeletons;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Skeletons;

[Trait("Category", "Unit")]
public sealed class SkeletonMapTests
{
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
    public async Task MarkdownRenderer_HandoffRoundTripsToFeatureContext()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var doc = project.Documents.Single(d => d.Name == "Greeter.cs");
        var markdown = await FileSkeletonBuilder.BuildMarkdownForDocumentAsync(doc, fixture.Solution.FilePath ?? "");
        var match = new Regex(
            @"handoffId: `(?<id>h:[A-Za-z0-9_-]+)`",
            RegexOptions.CultureInvariant,
            System.TimeSpan.FromSeconds(1)).Match(markdown);
        Assert.True(match.Success, markdown);

        var context = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(fixture.Solution, match.Groups["id"].Value));
        Assert.NotNull(context);
        Assert.Null(context.Error);
        Assert.Equal("Greeter", context.Declaration.SymbolName);
    }

    [Fact]
    public async Task MarkdownRenderer_MemberHandoffRoundTripsToFeatureContext()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var document = project.Documents.Single(d => d.Name == "Greeter.cs");
        var markdown = await FileSkeletonBuilder.BuildMarkdownForDocumentAsync(document, fixture.Solution.FilePath ?? "");
        var match = Regex.Match(
            markdown,
            @"public string Greet\(string name\).*handoffId: `(?<id>h:[A-Za-z0-9_-]+)`",
            RegexOptions.CultureInvariant,
            System.TimeSpan.FromSeconds(1));
        Assert.True(match.Success, markdown);

        var context = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(fixture.Solution, match.Groups["id"].Value));
        Assert.NotNull(context);
        Assert.Null(context.Error);
        Assert.Equal("Greet", context.Declaration.SymbolName);
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
