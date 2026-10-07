namespace AiNetCodeNavigator.FastTests.Skeletons;

using System.Linq;

[Trait("Category", "Unit")]
public class FileSkeletonTests
{
    [Theory]
    [InlineData("")]
    [InlineData("namespace Outline; public class Outer { public int Value = 12345; public int Read() => 54321; public class HiddenNested { public void HiddenMethod() {} } }")]
    public async Task BuildMarkdownForDocumentAsync_DeclaresScopeForEmptyAndNestedFiles(string source)
    {
        using var fixture = AiNetCodeNavigator.TestKit.Fixtures.SampleCodeFixtures.CreateStandardTestSolution();
        var document = fixture.Solution.Projects.First().AddDocument("Outline.cs", source);
        var markdown = await AiNetCodeNavigator.Core.Skeletons.FileSkeletonBuilder.BuildMarkdownForDocumentAsync(document, fixture.Solution.FilePath!);

        Assert.Contains("> Scope: top-level types and their direct members; nested types, implementation bodies and initializers are omitted.", markdown);
        if (source.Length == 0)
        {
            Assert.Contains("Types: 0 | Members: 0", markdown);
            Assert.DoesNotContain("handoffId:", markdown);
            return;
        }

        Assert.Contains("### Outer", markdown);
        Assert.Contains("public int Value;", markdown);
        Assert.Contains("public int Read()", markdown);
        Assert.DoesNotContain("HiddenNested", markdown);
        Assert.DoesNotContain("HiddenMethod", markdown);
        Assert.DoesNotContain("12345", markdown);
        Assert.DoesNotContain("54321", markdown);
        var references = System.Text.RegularExpressions.Regex.Matches(markdown, @"handoffId: `(?<id>src:[^`]+)`",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, System.TimeSpan.FromSeconds(1));
        Assert.Equal(3, references.Count);
        foreach (System.Text.RegularExpressions.Match reference in references)
        {
            var resolved = await AiNetCodeNavigator.Core.Symbols.SourceSymbolResolver.ResolveAsync(document.Project.Solution, reference.Groups["id"].Value);
            Assert.True(resolved.IsSuccess);
        }
    }

    [Fact]
    public async Task BuildMarkdownForDocumentAsync_RejectsNullDocument()
    {
        await Assert.ThrowsAsync<System.ArgumentNullException>(
            () => AiNetCodeNavigator.Core.Skeletons.FileSkeletonBuilder.BuildMarkdownForDocumentAsync(null!, "solution.slnx"));
    }

    [Fact]
    public async Task BuildMarkdownForDocumentAsync_RejectsNullSolutionPath()
    {
        using var fixture = AiNetCodeNavigator.TestKit.Fixtures.SampleCodeFixtures.CreateStandardTestSolution();
        var document = fixture.Solution.Projects.First().Documents.First();

        await Assert.ThrowsAsync<System.ArgumentNullException>(
            () => AiNetCodeNavigator.Core.Skeletons.FileSkeletonBuilder.BuildMarkdownForDocumentAsync(document, null!));
    }
}
