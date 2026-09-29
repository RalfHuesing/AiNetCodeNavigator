#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class ImpactAnalyzerTests
{
    [Fact]
    public async Task AnalyzeSymbolImpactAsync_FindsTransitiveCallersAndAffectedProjects()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);

        var greeterType = coreCompilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);

        var greetMethod = greeterType.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(greetMethod, fixture.Solution, maxDepth: 2);

        Assert.Equal("Greet", impact.TargetSymbol);
        Assert.True(impact.DirectCallersCount >= 2);
        Assert.True(impact.TransitiveImpactCount >= 2);
        Assert.Contains(impact.AffectedProjects, p => p == "Sample.App");
        Assert.Contains(impact.AffectedFiles, f => f.Contains("Caller.cs"));
        Assert.Contains(impact.CallSites, s => s.CallingMember.Contains("ExecuteSingle"));
        Assert.Contains(impact.CallSites, s => s.CallingMember.Contains("ExecuteMultiple"));
    }
}
