#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.FileStructure;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.FileStructure;

[Trait("Category", "Unit")]
public sealed class IndexScopeScannerTests
{
    [Fact]
    public async Task ScanAsync_ReturnsProjectAndDocumentBreakdown()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var payload = await IndexScopeScanner.ScanAsync(fixture.Solution);

        Assert.Equal(2, payload.ProjectCount);
        Assert.True(payload.TotalDocumentCount >= 5);
        Assert.True(payload.CSharpFileCount >= 5);
        Assert.Contains(payload.Projects, p => p.Name == "Sample.Core");
        Assert.Contains(payload.Projects, p => p.Name == "Sample.App");

        // Breakdown has .cs extension covered
        var csEntry = payload.FileTypes.FirstOrDefault(f => f.Extension == ".cs");
        Assert.NotNull(csEntry);
        Assert.True(csEntry.SymbolGraphCovered);

        // Formatted report
        Assert.Contains("# Index Scope:", payload.FormattedText);
        Assert.Contains("## Projekte:", payload.FormattedText);
        Assert.Contains("## Dateitypen im Roslyn-Index:", payload.FormattedText);
    }
}
