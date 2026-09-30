#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class GetSymbolBodyTests
{
    [Fact]
    public async Task Resolve_MethodWithBody_ReturnsSourceAndLineCounts()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);

        var greeterType = compilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);

        var greetMethod = greeterType.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var identity = await AnalysisSymbolIdentity.ForSourceAsync(fixture.Solution);
        var result = SourceSymbolBodyResolver.Resolve(greetMethod, maxBodyLines: 50, handoffIdentity: identity, solution: fixture.Solution);

        Assert.Equal("available", result.Availability);
        Assert.Equal("source", result.ContentMode);
        Assert.Contains("public string Greet(string name)", result.Body);
        Assert.Contains("return $\"{Prefix}, {name}!\";", result.Body);
        Assert.False(result.HasMore);
        Assert.True(result.TotalLines > 0);
        Assert.NotNull(result.HandoffId);
        Assert.StartsWith("h:", result.HandoffId);
    }

    [Fact]
    public async Task Resolve_Pagination_TruncatesAndSetsFlags()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);

        var greeterType = compilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);

        var greetMethod = greeterType.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var result = SourceSymbolBodyResolver.Resolve(greetMethod, maxBodyLines: 2, startLine: 1);

        Assert.Equal(1, result.DisplayedStart);
        Assert.Equal(2, result.DisplayedEnd);
        Assert.True(result.HasMore);
        Assert.Contains("// ... truncated", result.Body);
    }

    [Fact]
    public async Task Resolve_InterfaceMethod_ReturnsUnavailable()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);

        var interfaceType = compilation.GetTypeByMetadataName("SampleNamespace.Hierarchy.IProcessor");
        Assert.NotNull(interfaceType);

        var method = interfaceType.GetMembers("Process").OfType<IMethodSymbol>().First();

        var result = SourceSymbolBodyResolver.Resolve(method, maxBodyLines: 50);

        Assert.Equal("unavailable", result.Availability);
        Assert.NotNull(result.Hint);
        Assert.Contains("Interfaces", result.Hint);
    }

    [Fact]
    public async Task ResolveBatch_ReturnsAllRequestedSymbols()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);

        var greeterType = compilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);

        var methods = greeterType.GetMembers().OfType<IMethodSymbol>().ToList();
        var batch = SourceSymbolBodyResolver.ResolveBatch(methods, maxBodyLines: 50);

        Assert.Equal(methods.Count, batch.Items.Count);
        Assert.All(batch.Items, item => Assert.NotNull(item.Body));
    }
}
