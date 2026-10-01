#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.TestKit.Builders;
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
    public async Task Resolve_Pagination_StartLineSelectsOneBasedWindowAndClampsLimits()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var greeterType = compilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);
        var greetMethod = greeterType.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var complete = SourceSymbolBodyResolver.Resolve(greetMethod, maxBodyLines: 50);
        var window = SourceSymbolBodyResolver.Resolve(greetMethod, maxBodyLines: 2, startLine: 2);
        var normalized = SourceSymbolBodyResolver.Resolve(greetMethod, maxBodyLines: 0, startLine: 0);
        var expected = string.Join("\n", complete.Body.Split('\n').Skip(1).Take(2));

        Assert.Equal(2, window.DisplayedStart);
        Assert.Equal(3, window.DisplayedEnd);
        Assert.Equal(expected, string.Join("\n", window.Body.Split('\n').Take(2)));
        Assert.Equal(1, normalized.DisplayedStart);
        Assert.Equal(1, normalized.DisplayedEnd);
    }

    [Fact]
    public async Task Resolve_PaginationBeyondDeclarationReturnsRecoverableRangeMessage()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var greeterType = compilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);
        var greetMethod = greeterType.GetMembers("Greet").OfType<IMethodSymbol>().First();

        var result = SourceSymbolBodyResolver.Resolve(greetMethod, maxBodyLines: 10, startLine: 9999);

        Assert.Contains("is outside the symbol", result.Body, System.StringComparison.OrdinalIgnoreCase);
        Assert.False(result.HasMore);
        Assert.True(result.TotalLines > 0);
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

    [Fact]
    public async Task ResolveBatch_PreservesInputOrderAndAcceptsEmptySequence()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var greeterType = compilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(greeterType);
        var greet = greeterType.GetMembers("Greet").OfType<IMethodSymbol>().First();
        var prefix = greeterType.GetMembers("Prefix").OfType<IPropertySymbol>().Single();
        var metadataType = compilation.GetTypeByMetadataName("System.String");
        Assert.NotNull(metadataType);

        var batch = SourceSymbolBodyResolver.ResolveBatch(new ISymbol[] { greet, prefix, metadataType }, maxBodyLines: 50);
        var empty = SourceSymbolBodyResolver.ResolveBatch(System.Array.Empty<ISymbol>(), maxBodyLines: 50);

        Assert.Equal(3, batch.Items.Count);
        Assert.Contains("Greet", batch.Items[0].Body);
        Assert.Contains("Prefix", batch.Items[1].Body);
        Assert.Equal("unavailable", batch.Items[2].Availability);
        Assert.Equal(0, batch.Items[2].TotalLines);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task Resolve_PartialMethodDefinition_ReturnsImplementationBody()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\PartialMethods.slnx",
            new ProjectSpec(
                "App",
                [("PartialWorker.cs", "public partial class PartialWorker { public int Runs; partial void Execute(); partial void Execute() { Runs++; } partial void Missing(); }")],
                VirtualProjectDirectory: "src/App"));
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);

        var type = compilation.GetTypeByMetadataName("PartialWorker");
        Assert.NotNull(type);
        var method = type.GetMembers("Execute").OfType<IMethodSymbol>().Single();

        var result = SourceSymbolBodyResolver.Resolve(method, maxBodyLines: 50);

        Assert.Equal("available", result.Availability);
        Assert.Contains("Runs++;", result.Body);

        var unimplemented = type.GetMembers("Missing").OfType<IMethodSymbol>().Single();
        var unavailable = SourceSymbolBodyResolver.Resolve(unimplemented, maxBodyLines: 50);
        Assert.Equal("unavailable", unavailable.Availability);
        Assert.Contains("partial void Missing();", unavailable.Body);
        Assert.Contains("implementation", unavailable.Hint, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolve_DefaultInterfaceMethod_ReturnsExecutableBody()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DefaultInterface.slnx",
            new ProjectSpec(
                "App",
                [("Worker.cs", "public interface IWorker { string Run() { return \"done\"; } }")],
                VirtualProjectDirectory: "src/App"));
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var interfaceType = compilation.GetTypeByMetadataName("IWorker");
        Assert.NotNull(interfaceType);
        var method = interfaceType.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var result = SourceSymbolBodyResolver.Resolve(method, maxBodyLines: 50);
        var interfaceDeclaration = SourceSymbolBodyResolver.Resolve(interfaceType, maxBodyLines: 50);

        Assert.Equal("available", result.Availability);
        Assert.Contains("return \"done\";", result.Body);
        Assert.Null(result.Hint);
        Assert.Equal("unavailable", interfaceDeclaration.Availability);
        Assert.NotNull(interfaceDeclaration.Hint);
    }

    [Fact]
    public async Task Resolve_AbstractMethodAndProperty_ReturnUnavailable()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\AbstractMembers.slnx",
            new ProjectSpec(
                "App",
                [("Worker.cs", "public abstract class Worker { public abstract void Run(); public abstract int Value { get; } }")],
                VirtualProjectDirectory: "src/App"));
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var type = compilation.GetTypeByMetadataName("Worker");
        Assert.NotNull(type);
        var method = type.GetMembers("Run").OfType<IMethodSymbol>().Single();
        var property = type.GetMembers("Value").OfType<IPropertySymbol>().Single();

        var methodResult = SourceSymbolBodyResolver.Resolve(method, maxBodyLines: 50);
        var propertyResult = SourceSymbolBodyResolver.Resolve(property, maxBodyLines: 50);

        Assert.Equal("unavailable", methodResult.Availability);
        Assert.Equal("unavailable", propertyResult.Availability);
        Assert.NotNull(methodResult.Hint);
        Assert.NotNull(propertyResult.Hint);
    }

    [Fact]
    public async Task Resolve_MetadataSymbol_ReturnsUnavailableWithoutSourceLines()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var metadataType = compilation.GetTypeByMetadataName("System.String");
        Assert.NotNull(metadataType);

        var result = SourceSymbolBodyResolver.Resolve(metadataType, maxBodyLines: 50);

        Assert.Equal("unavailable", result.Availability);
        Assert.Equal(0, result.TotalLines);
        Assert.Equal(1, result.DisplayedStart);
        Assert.Equal(0, result.DisplayedEnd);
        Assert.Contains("Quell-Syntax", result.Hint, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_NullSymbol_ThrowsArgumentNullException()
    {
        Assert.Throws<System.ArgumentNullException>(() => SourceSymbolBodyResolver.Resolve(null!, maxBodyLines: 20));
    }

    [Fact]
    public void ResolveBatch_NullSymbols_ThrowsArgumentNullException()
    {
        Assert.Throws<System.ArgumentNullException>(() => SourceSymbolBodyResolver.ResolveBatch(null!, maxBodyLines: 20));
    }
}
