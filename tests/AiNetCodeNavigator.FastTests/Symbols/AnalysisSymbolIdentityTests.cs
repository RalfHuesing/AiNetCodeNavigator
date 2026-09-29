#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class AnalysisSymbolIdentityTests
{
    [Fact]
    public async Task FormatHandoff_SourceSymbol_FormatsCorrectIdentifier()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);

        var greeterType = compilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);

        var identity = AnalysisSymbolIdentity.ForSource(
            @"C:\app\sample.sln",
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            fixture.Solution);

        var handoffId = identity.FormatHandoff(greeterType, fixture.Solution);
        Assert.NotNull(handoffId);
        Assert.StartsWith("i:0:", handoffId, System.StringComparison.Ordinal);
        Assert.Contains("T:SampleNamespace.Greeter", handoffId, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Matches_ComparesPathAndHash()
    {
        var id1 = AnalysisSymbolIdentity.ForAssembly(@"C:\bin\app.dll", "hash1", 1);
        var id2 = AnalysisSymbolIdentity.ForAssembly(@"C:\bin\app.dll", "hash1", 2);
        var id3 = AnalysisSymbolIdentity.ForAssembly(@"C:\bin\other.dll", "hash2", 1);

        Assert.True(id1.Matches(id2));
        Assert.False(id1.Matches(id3));
    }
}
