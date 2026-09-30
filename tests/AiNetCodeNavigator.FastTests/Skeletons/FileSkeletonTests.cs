namespace AiNetCodeNavigator.FastTests.Skeletons;

using System.Linq;

[Trait("Category", "Unit")]
public class FileSkeletonTests
{
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
