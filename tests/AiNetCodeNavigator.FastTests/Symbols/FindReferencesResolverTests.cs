#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
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

        var caller = Assert.Single(result.References.Where(reference => reference.ProjectName == "Sample.App" && reference.EnclosingSymbolName.Contains("ExecuteSingle")));
        Assert.StartsWith("h:", caller.EnclosingSymbolHandoffId);
        var resolvedCaller = await SourceSymbolResolver.ResolveAsync(fixture.Solution, caller.EnclosingSymbolHandoffId!);
        Assert.True(resolvedCaller.IsSuccess);
        Assert.Equal("ExecuteSingle", resolvedCaller.Symbol!.Name);
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
    public async Task FindImplementationsAsync_Class_FindsIndirectDerivedClasses()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var baseProcessor = compilation.GetTypeByMetadataName("SampleNamespace.Hierarchy.BaseProcessor");

        var result = await FindReferencesResolver.FindImplementationsAsync(baseProcessor!, fixture.Solution);

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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task FindReferencesAsync_NonPositiveLimitStillReturnsOneAndReportsTruncation(int maxResults)
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var core = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(core);
        var type = core.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(type);
        var method = type.GetMembers("Greet").OfType<IMethodSymbol>().Single();

        var result = await FindReferencesResolver.FindReferencesAsync(method, fixture.Solution, maxResults);

        Assert.Single(result.References);
        Assert.True(result.IsTruncated);
        Assert.True(result.TotalCount > result.References.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task FindImplementationsAsync_NonPositiveLimitStillReturnsOneAndReportsTruncation(int maxResults)
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var core = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(core);
        var type = core.GetTypeByMetadataName("SampleNamespace.Hierarchy.IProcessor");
        Assert.NotNull(type);

        var result = await FindReferencesResolver.FindImplementationsAsync(type, fixture.Solution, maxResults);

        Assert.Single(result.Implementations);
        Assert.True(result.IsTruncated);
        Assert.True(result.TotalCount > result.Implementations.Count);
    }

    [Fact]
    public async Task PublicResolvers_RejectNullSymbolsAndSolutions()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var core = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(core);
        var type = core.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(type);
        var method = type.GetMembers("Greet").OfType<IMethodSymbol>().Single();

        await Assert.ThrowsAsync<System.ArgumentNullException>(() => FindReferencesResolver.FindReferencesAsync(null!, fixture.Solution));
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => FindReferencesResolver.FindReferencesAsync(method, null!));
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => FindReferencesResolver.FindImplementationsAsync(null!, fixture.Solution));
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => FindReferencesResolver.FindImplementationsAsync(type, null!));
    }

    [Fact]
    public async Task FindImplementationsAsync_Interface_FollowsEqualNameImplementationsAcrossProjectsByHandoff()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Implementations.slnx",
            new ProjectSpec("Contracts", [("IService.cs", "namespace Contracts; public interface IService { void Run(); }")]),
            new ProjectSpec("App.One", [("SharedWorker.cs", "namespace App; public sealed class SharedWorker : Contracts.IService { public void Run() { } }")], ProjectReferences: ["Contracts"]),
            new ProjectSpec("App.Two", [("SharedWorker.cs", "namespace App; public sealed class SharedWorker : Contracts.IService { public void Run() { } }")], ProjectReferences: ["Contracts"]));
        var contracts = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(contracts);
        var service = contracts.GetTypeByMetadataName("Contracts.IService");
        Assert.NotNull(service);

        var result = await FindReferencesResolver.FindImplementationsAsync(service, fixture.Solution);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Implementations.Count);
        Assert.All(result.Implementations, implementation => Assert.StartsWith("h:", implementation.HandoffId));
        Assert.Equal("App.One", result.Implementations[0].ProjectName);
        Assert.Equal("App.Two", result.Implementations[1].ProjectName);

        foreach (var implementation in result.Implementations)
        {
            var resolved = await SourceSymbolResolver.ResolveAsync(fixture.Solution, implementation.HandoffId!);
            Assert.True(resolved.IsSuccess);
            Assert.Equal("SharedWorker", resolved.Symbol!.Name);
            Assert.Equal(implementation.ProjectName, resolved.Symbol.ContainingAssembly?.Name);
        }
    }

    [Fact]
    public async Task FindImplementationsAsync_InterfaceAndAbstractMembers_FindsCrossProjectMethods()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\MemberImplementations.slnx",
            new ProjectSpec("Contracts", [("Contracts.cs", "namespace Contracts; public interface IService { void Run(); } public abstract class BaseService { public abstract void Stop(); }")]),
            new ProjectSpec("App", [("Service.cs", "namespace App; public sealed class Service : Contracts.IService { public void Run() { } } public sealed class ManagedService : Contracts.BaseService { public override void Stop() { } }")], ProjectReferences: ["Contracts"]));
        var contracts = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(contracts);
        var interfaceType = contracts.GetTypeByMetadataName("Contracts.IService");
        var baseType = contracts.GetTypeByMetadataName("Contracts.BaseService");
        Assert.NotNull(interfaceType);
        Assert.NotNull(baseType);
        var interfaceMethod = interfaceType.GetMembers("Run").OfType<IMethodSymbol>().Single();
        var abstractMethod = baseType.GetMembers("Stop").OfType<IMethodSymbol>().Single();

        var interfaceResult = await FindReferencesResolver.FindImplementationsAsync(interfaceMethod, fixture.Solution);
        var abstractResult = await FindReferencesResolver.FindImplementationsAsync(abstractMethod, fixture.Solution);

        var interfaceImplementation = Assert.Single(interfaceResult.Implementations);
        var abstractImplementation = Assert.Single(abstractResult.Implementations);
        Assert.Equal("App", interfaceImplementation.ProjectName);
        Assert.Equal("App", abstractImplementation.ProjectName);
        Assert.StartsWith("h:", interfaceImplementation.HandoffId);
        Assert.StartsWith("h:", abstractImplementation.HandoffId);
        Assert.Equal("Run", (await SourceSymbolResolver.ResolveAsync(fixture.Solution, interfaceImplementation.HandoffId!)).Symbol!.Name);
        Assert.Equal("Stop", (await SourceSymbolResolver.ResolveAsync(fixture.Solution, abstractImplementation.HandoffId!)).Symbol!.Name);
    }
}
