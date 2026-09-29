#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class FindReferencesResolverTests
{
    [Fact]
    public async Task FindReferencesAsync_FindsCrossProjectReferences()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);

        var greeterType = coreCompilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);

        var greetMethod = greeterType.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var result = await FindReferencesResolver.FindReferencesAsync(greetMethod, fixture.Solution);

        Assert.Equal("Greet", result.TargetSymbolName);
        Assert.True(result.TotalCount >= 2); // In ExecuteSingle, ExecuteMultiple, and GreetLoud
        Assert.Contains(result.References, r => r.ProjectName == "Sample.App");
        Assert.Contains(result.References, r => r.EnclosingSymbolName.Contains("ExecuteSingle"));
        Assert.All(result.References, r => Assert.NotEmpty(r.Snippet));
    }

    [Fact]
    public async Task FindImplementationsAsync_Interface_FindsAllImplementations()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);

        var interfaceType = coreCompilation.GetTypeByMetadataName("SampleNamespace.Hierarchy.IProcessor");
        Assert.NotNull(interfaceType);

        var result = await FindReferencesResolver.FindImplementationsAsync(interfaceType, fixture.Solution);

        Assert.Equal("IProcessor", result.TargetSymbolName);
        Assert.True(result.TotalCount >= 3); // BaseProcessor, FastProcessor, SafeProcessor
        Assert.Contains(result.Implementations, i => i.SymbolName == "BaseProcessor");
        Assert.Contains(result.Implementations, i => i.SymbolName == "FastProcessor");
        Assert.Contains(result.Implementations, i => i.SymbolName == "SafeProcessor");
    }

    [Fact]
    public async Task FindImplementationsAsync_AbstractMethod_FindsOverrides()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);

        var baseProcessor = coreCompilation.GetTypeByMetadataName("SampleNamespace.Hierarchy.BaseProcessor");
        Assert.NotNull(baseProcessor);

        var processMethod = baseProcessor.GetMembers("Process").OfType<IMethodSymbol>().First();

        var result = await FindReferencesResolver.FindImplementationsAsync(processMethod, fixture.Solution);

        Assert.Equal("Process", result.TargetSymbolName);
        Assert.NotEmpty(result.Implementations);
        Assert.Contains(result.Implementations, i => i.Signature.Contains("Process"));
    }
}
