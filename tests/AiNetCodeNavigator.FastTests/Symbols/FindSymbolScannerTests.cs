#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class FindSymbolScannerTests
{
    [Fact]
    public async Task FindMatchesWithDetailsAsync_FindsExactClassMatch()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new FindSymbolScanRequest(fixture.Solution, "Greeter");

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(request);

        Assert.NotEmpty(result.Entries);
        var entry = result.Entries.First();
        Assert.Equal("Greeter", entry.Name);
        Assert.Equal("class", entry.Kind);
        Assert.NotNull(entry.HandoffId);
        Assert.StartsWith("h:", entry.HandoffId);
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_FiltersByKind()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var classRequest = new FindSymbolScanRequest(fixture.Solution, "Greeter", Kind: SymbolKindFilter.Class);
        var classResult = await FindSymbolScanner.FindMatchesWithDetailsAsync(classRequest);
        Assert.Single(classResult.Entries);

        var methodRequest = new FindSymbolScanRequest(fixture.Solution, "Greeter", Kind: SymbolKindFilter.Method);
        var methodResult = await FindSymbolScanner.FindMatchesWithDetailsAsync(methodRequest);
        Assert.Empty(methodResult.Entries);
        Assert.Contains("Vorhandene Symbole mit diesem Namen haben den Typ: class", methodResult.Text);
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_WildcardSearch_ReturnsMatchingSymbols()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new FindSymbolScanRequest(fixture.Solution, "*Greet*");

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(request);

        Assert.True(result.Entries.Count >= 3); // Greeter, Greet, GreetLoud
        Assert.Contains(result.Entries, e => e.Name == "Greet");
        Assert.Contains(result.Entries, e => e.Name == "GreetLoud");
        Assert.Contains(result.Entries, e => e.Name == "Greeter");
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_MaxResults_TruncatesAndFlags()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new FindSymbolScanRequest(fixture.Solution, "*", MaxResults: 2);

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(request);

        Assert.Equal(2, result.Entries.Count);
        Assert.True(result.IsTruncated);
        Assert.True(result.TotalMatches > 2);
        Assert.Contains("maxResults", result.TruncatedBy);
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_SuggestsSimilarNamesOnMiss()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new FindSymbolScanRequest(fixture.Solution, "GreeterTypo");

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(request);

        Assert.Empty(result.Entries);
        Assert.Contains("Meintest du eventuell", result.Text);
        Assert.Contains("Greeter", result.Text);
    }
}
