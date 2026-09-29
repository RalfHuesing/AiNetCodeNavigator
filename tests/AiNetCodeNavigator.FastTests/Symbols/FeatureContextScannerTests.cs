#nullable enable

using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class FeatureContextScannerTests
{
    [Fact]
    public async Task ScanAsync_ReturnsDeclarationAndCallers()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new FeatureContextRequest(
            Solution: fixture.Solution,
            SymbolIdentifier: "Greet",
            MaxCallers: 10,
            MaxTests: 10);

        var payload = await FeatureContextScanner.ScanAsync(request);

        Assert.NotNull(payload);
        Assert.Equal("Greet", payload.Declaration.SymbolName);
        Assert.Equal("method", payload.Declaration.Kind);
        Assert.Equal("public", payload.Declaration.Modifiers);
        Assert.NotNull(payload.Declaration.HandoffId);

        // ServiceCaller calls Greet
        Assert.NotEmpty(payload.Callers);
        Assert.Contains(payload.Callers, c => c.CallerName.Contains("ServiceCaller.ExecuteSingle"));
        Assert.Contains(payload.Callers, c => c.CallerName.Contains("ServiceCaller.ExecuteMultiple"));
    }

    [Fact]
    public async Task RenderMarkdown_FormatsCleanMarkdownWithoutViolationsOrMetrics()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new FeatureContextRequest(
            Solution: fixture.Solution,
            SymbolIdentifier: "Greeter");

        var payload = await FeatureContextScanner.ScanAsync(request);
        Assert.NotNull(payload);

        var markdown = FeatureContextScanner.RenderMarkdown(payload);

        Assert.Contains("# Feature Context: Greeter", markdown);
        Assert.Contains("## Incoming Callers", markdown);
        Assert.Contains("## Associated Tests", markdown);
        Assert.DoesNotContain("Violations", markdown);
        Assert.DoesNotContain("Metrics", markdown);
        Assert.DoesNotContain("QualityGate", markdown);
    }
}
