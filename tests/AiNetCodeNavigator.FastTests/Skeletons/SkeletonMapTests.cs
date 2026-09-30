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
}
