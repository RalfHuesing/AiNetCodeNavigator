#nullable enable

using System.Collections.Generic;
using System.IO;
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

        var result = await FindReferencesResolver.FindReferencesAsync(greetMethod, fixture.Solution, maxResults: 50, depth: 1);

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
    public async Task FindReferencesAsync_FormatsHandoffsOnlyForVisibleLocations()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\ReferencePage.slnx",
            new ProjectSpec("Contracts", [("A.cs", "namespace Page; public class Target { public void Run() { } }")]),
            new ProjectSpec("Callers", [("B.cs", "namespace Page; public class Caller { public void One(Target t) => t.Run(); public void Two(Target t) => t.Run(); public void Three(Target t) => t.Run(); }")], ProjectReferences: ["Contracts"]));
        var compilation = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Page.Target")!.GetMembers("Run").OfType<IMethodSymbol>().Single();
        var formattedSymbols = new List<ISymbol>();

        var result = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, maxResults: 1, depth: 1,
            handoffFormatter: symbol =>
            {
                formattedSymbols.Add(symbol);
                return $"h:page-{formattedSymbols.Count}";
            });

        Assert.Single(result.References);
        Assert.Equal(3, result.TotalCount);
        Assert.True(result.IsTruncated);
        Assert.Equal(2, formattedSymbols.Count);
        Assert.NotNull(result.References[0].EnclosingSymbolHandoffId);
        Assert.NotNull(result.References[0].ReachedFromSymbolHandoffId);
    }

    [Fact]
    public async Task FindReferencesAsync_PreservesLinkedFileOwnersWithDuplicateProjectNames()
    {
        const string linkedPath = @"C:\VirtualRepo\Shared\Caller.cs";
        const string linkedContent = "namespace Linked; public class Caller { public void Invoke(Contracts.Target target) => target.Run(); }";
        var workspace = new AdhocWorkspace();
        try
        {
            var solutionPath = @"C:\VirtualRepo\LinkedOwners.slnx";
            var solution = workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), filePath: solutionPath));
            var contractsId = ProjectId.CreateNewId("Contracts");
            var contracts = ProjectInfo.Create(contractsId, VersionStamp.Create(), "Contracts", "Contracts", LanguageNames.CSharp,
                    filePath: @"C:\VirtualRepo\Contracts\Contracts.csproj")
                .WithMetadataReferences(TestWorkspaceBuilder.CoreReferences)
                .WithCompilationOptions(new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
            solution = solution.AddProject(contracts).AddDocument(DocumentId.CreateNewId(contractsId), "Target.cs",
                "namespace Contracts; public class Target { public void Run() { } }", filePath: @"C:\VirtualRepo\Contracts\Target.cs");

            var callerProjects = new List<ProjectId>();
            foreach (var projectPath in new[] { @"C:\VirtualRepo\First\Shared.csproj", @"C:\VirtualRepo\Second\Shared.csproj" })
            {
                var projectId = ProjectId.CreateNewId("Shared");
                callerProjects.Add(projectId);
                var project = ProjectInfo.Create(projectId, VersionStamp.Create(), "Shared", "Shared", LanguageNames.CSharp,
                        filePath: projectPath)
                    .WithMetadataReferences(TestWorkspaceBuilder.CoreReferences)
                    .WithProjectReferences([new ProjectReference(contractsId)])
                    .WithCompilationOptions(new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
                solution = solution.AddProject(project).AddDocument(DocumentId.CreateNewId(projectId), "Caller.cs", linkedContent, filePath: linkedPath);
            }

            Assert.True(workspace.TryApplyChanges(solution));
            solution = workspace.CurrentSolution;
            var compilation = await solution.GetProject(contractsId)!.GetCompilationAsync();
            var target = compilation!.GetTypeByMetadataName("Contracts.Target")!.GetMembers("Run").OfType<IMethodSymbol>().Single();
            var result = await FindReferencesResolver.FindReferencesAsync(target, solution, maxResults: 10, depth: 1);
            var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(target, solution, maxResults: 10);
            var referencesWithoutHandoffs = await FindReferencesResolver.FindReferencesAsync(
                target, solution, maxResults: 10, depth: 1, handoffFormatter: _ => null);
            var impactWithoutHandoffs = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(
                target, solution, maxResults: 10, handoffFormatter: _ => null);

            Assert.Equal(2, result.TotalCount);
            Assert.Equal(2, result.References.Count);
            Assert.Equal(2, impact.TransitiveImpactCount);
            Assert.Equal(2, impact.CallSites.Count);
            Assert.Equal(2, impact.CallSites.Select(site => site.CallingMemberHandoffId).Distinct().Count());
            foreach (var site in impact.CallSites)
            {
                var resolved = await SourceSymbolResolver.ResolveAsync(solution, site.CallingMemberHandoffId!);
                Assert.True(resolved.IsSuccess);
                var sourceTree = Assert.Single(resolved.Symbol!.Locations.Where(location => location.IsInSource)).SourceTree;
                Assert.Equal(linkedPath, solution.GetDocument(sourceTree!)!.FilePath);
            }
            Assert.Equal(2, referencesWithoutHandoffs.TotalCount);
            Assert.Equal(2, impactWithoutHandoffs.TransitiveImpactCount);
            Assert.Equal(2, impactWithoutHandoffs.DirectCallersCount);
            Assert.All(result.References, reference => Assert.Equal("Shared", reference.ProjectName));
            var ownerPaths = new List<string?>();
            foreach (var reference in result.References)
            {
                var resolved = await SourceSymbolResolver.ResolveAsync(solution, reference.EnclosingSymbolHandoffId!);
                Assert.True(resolved.IsSuccess);
                var sourceTree = Assert.Single(resolved.Symbol!.Locations.Where(location => location.IsInSource)).SourceTree;
                ownerPaths.Add(solution.GetDocument(sourceTree!)!.Project.FilePath);
            }
            Assert.Equal(2, ownerPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(2, ownerPaths.Count(path => path == @"C:\VirtualRepo\First\Shared.csproj" || path == @"C:\VirtualRepo\Second\Shared.csproj"));
            Assert.Equal(@"C:\VirtualRepo\First\Shared.csproj", ownerPaths[0]);
            Assert.Equal(@"C:\VirtualRepo\Second\Shared.csproj", ownerPaths[1]);
            Assert.All(result.References, reference => Assert.Equal("Shared/Caller.cs", reference.FilePath));
        }
        finally
        {
            workspace.Dispose();
        }
    }

    [Fact]
    public async Task FindReferencesAsync_DepthTwo_ReturnsCallerChainAcrossProjectsWithOriginAndDepth()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\ReferenceChain.slnx",
            new ProjectSpec("Contracts", [("A.cs", "namespace Chain; public class Target { public void A() { } }")]),
            new ProjectSpec("Middle", [("B.cs", "namespace Chain; public class CallerB { public void B(Target target) { target.A(); } }")], ProjectReferences: ["Contracts"]),
            new ProjectSpec("App", [("C.cs", "namespace Chain; public class CallerC { public void C(CallerB caller, Target target) { caller.B(target); } }")], ProjectReferences: ["Middle", "Contracts"]));
        var contracts = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(contracts);
        var target = contracts.GetTypeByMetadataName("Chain.Target")!.GetMembers("A").OfType<IMethodSymbol>().Single();

        var depthOne = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, maxResults: 50, depth: 1);
        var depthTwo = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, maxResults: 50, depth: 2);

        var direct = Assert.Single(depthOne.References);
        Assert.Equal("CallerB.B", direct.EnclosingSymbolName);
        Assert.Equal(1, direct.Depth);
        Assert.Equal("Target.A", direct.ReachedFromSymbolName);
        Assert.StartsWith("h:", direct.ReachedFromSymbolHandoffId);
        Assert.Equal("A", (await SourceSymbolResolver.ResolveAsync(fixture.Solution, direct.ReachedFromSymbolHandoffId!)).Symbol!.Name);
        Assert.Equal(2, depthTwo.References.Count);
        var indirect = Assert.Single(depthTwo.References.Where(reference => reference.Depth == 2));
        Assert.Equal("CallerC.C", indirect.EnclosingSymbolName);
        Assert.Equal("CallerB.B", indirect.ReachedFromSymbolName);
        Assert.Equal(2, indirect.Depth);
        Assert.Equal("B", (await SourceSymbolResolver.ResolveAsync(fixture.Solution, indirect.ReachedFromSymbolHandoffId!)).Symbol!.Name);
        Assert.True(depthTwo.IsComplete);
        Assert.Equal(2, depthTwo.VisitedSymbolCount);

        var clamped = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, maxResults: 50, depth: 99);
        Assert.Equal(99, clamped.RequestedDepth);
        Assert.Equal(FindReferencesResolver.MaxReferenceDepth, clamped.EffectiveDepth);
        Assert.True(clamped.IsDepthClamped);
        Assert.False(clamped.IsComplete);

        var nodeLimitClamped = await FindReferencesResolver.FindReferencesAsync(
            target,
            fixture.Solution,
            maxResults: 50,
            depth: 2,
            maxNodes: FindReferencesResolver.DefaultMaxVisitedSymbols + 1);
        Assert.Equal(FindReferencesResolver.DefaultMaxVisitedSymbols, nodeLimitClamped.EffectiveNodeLimit);

        var limited = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, maxResults: 1, depth: 2);
        Assert.Single(limited.References);
        Assert.Equal(2, limited.TotalCount);
        Assert.True(limited.IsTruncated);
        Assert.False(limited.IsTruncatedByNodeLimit);
        Assert.False(limited.IsComplete);
    }

    [Fact]
    public async Task FindReferencesAsync_NodeLimitReportsIncompleteTraversal()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\ReferenceNodeLimit.slnx",
            new ProjectSpec("Contracts", [("A.cs", "namespace Limit; public class Target { public void A() { } }")]),
            new ProjectSpec("Middle", [("B.cs", "namespace Limit; public class CallerB { public void B(Target target) { target.A(); } }")], ProjectReferences: ["Contracts"]),
            new ProjectSpec("App", [("C.cs", "namespace Limit; public class CallerC { public void C(CallerB caller, Target target) { caller.B(target); } }")], ProjectReferences: ["Middle", "Contracts"]));
        var contracts = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(contracts);
        var target = contracts.GetTypeByMetadataName("Limit.Target")!.GetMembers("A").OfType<IMethodSymbol>().Single();

        var result = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, maxResults: 50, depth: 3, maxNodes: 1);

        Assert.Single(result.References);
        Assert.Equal("CallerB.B", result.References[0].EnclosingSymbolName);
        Assert.False(result.IsComplete);
        Assert.True(result.IsTruncatedByNodeLimit);
        Assert.Equal(1, result.VisitedSymbolCount);
        await Assert.ThrowsAsync<System.ArgumentOutOfRangeException>(() => FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, maxResults: 50, depth: 3, maxNodes: 0));
    }

    [Fact]
    public async Task FindReferencesAsync_CycleDoesNotRepeatVisitedSymbols()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\ReferenceCycle.slnx",
            new ProjectSpec("Cycle", [("Cycle.cs", "namespace Cycle; public class Calls { public void A() { B(); } public void B() { A(); } }")]));
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Cycle.Calls")!.GetMembers("A").OfType<IMethodSymbol>().Single();

        var result = await FindReferencesResolver.FindReferencesAsync(target, fixture.Solution, maxResults: 50, depth: 3);

        Assert.Equal(2, result.References.Count);
        Assert.Equal(2, result.VisitedSymbolCount);
        Assert.All(result.References, reference => Assert.InRange(reference.Depth, 1, 2));
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

        var result = await FindReferencesResolver.FindReferencesAsync(method, fixture.Solution, maxResults, depth: 1);

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

        await Assert.ThrowsAsync<System.ArgumentNullException>(() => FindReferencesResolver.FindReferencesAsync(null!, fixture.Solution, maxResults: 50, depth: 1));
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => FindReferencesResolver.FindReferencesAsync(method, null!, maxResults: 50, depth: 1));
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

    [Fact]
    public async Task FindImplementationsAsync_InterfaceAndAbstractProperties_FindsCrossProjectImplementationsAndOverrides()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\PropertyImplementations.slnx",
            new ProjectSpec("Contracts", [("Contracts.cs", "namespace Contracts; public interface IHasName { string Name { get; set; } } public abstract class BaseEntity { public abstract string Label { get; set; } }")]),
            new ProjectSpec("App", [("Entity.cs", "namespace App; public sealed class Entity : Contracts.IHasName { public string Name { get; set; } = string.Empty; } public sealed class NamedEntity : Contracts.BaseEntity { public override string Label { get; set; } = string.Empty; }")], ProjectReferences: ["Contracts"]));
        var contracts = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        Assert.NotNull(contracts);
        var interfaceProperty = contracts.GetTypeByMetadataName("Contracts.IHasName")!.GetMembers("Name").OfType<IPropertySymbol>().Single();
        var abstractProperty = contracts.GetTypeByMetadataName("Contracts.BaseEntity")!.GetMembers("Label").OfType<IPropertySymbol>().Single();

        var interfaceResult = await FindReferencesResolver.FindImplementationsAsync(interfaceProperty, fixture.Solution);
        var abstractResult = await FindReferencesResolver.FindImplementationsAsync(abstractProperty, fixture.Solution);

        var interfaceImplementation = Assert.Single(interfaceResult.Implementations);
        var propertyOverride = Assert.Single(abstractResult.Implementations);
        Assert.Equal("Name", interfaceImplementation.SymbolName);
        Assert.Equal("Label", propertyOverride.SymbolName);
        Assert.Equal("App", interfaceImplementation.ProjectName);
        Assert.Equal("App", propertyOverride.ProjectName);
        Assert.StartsWith("h:", interfaceImplementation.HandoffId);
        Assert.StartsWith("h:", propertyOverride.HandoffId);
        Assert.Equal("Name", (await SourceSymbolResolver.ResolveAsync(fixture.Solution, interfaceImplementation.HandoffId!)).Symbol!.Name);
        Assert.Equal("Label", (await SourceSymbolResolver.ResolveAsync(fixture.Solution, propertyOverride.HandoffId!)).Symbol!.Name);
    }

    [Fact]
    public async Task FindImplementationsAsync_UnsupportedTargetsReturnRecoverableTypedErrors()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\UnsupportedImplementationTargets.slnx",
            new ProjectSpec("App", [("Types.cs", "namespace Unsupported; public struct Point { public void Move() { } } public class Concrete { public void Run() { } } public interface INoMatches { void Missing(); }")]));
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var point = compilation.GetTypeByMetadataName("Unsupported.Point")!;
        var concreteMethod = compilation.GetTypeByMetadataName("Unsupported.Concrete")!.GetMembers("Run").OfType<IMethodSymbol>().Single();
        var emptyInterface = compilation.GetTypeByMetadataName("Unsupported.INoMatches")!;

        var structResult = await FindReferencesResolver.FindImplementationsAsync(point, fixture.Solution);
        var methodResult = await FindReferencesResolver.FindImplementationsAsync(concreteMethod, fixture.Solution);
        var validEmptyResult = await FindReferencesResolver.FindImplementationsAsync(emptyInterface, fixture.Solution);

        Assert.False(structResult.IsSuccess);
        Assert.Contains("struct", structResult.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
        Assert.Empty(structResult.Implementations);
        Assert.False(methodResult.IsSuccess);
        Assert.Contains("interface", methodResult.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
        Assert.Empty(methodResult.Implementations);
        Assert.True(validEmptyResult.IsSuccess);
        Assert.Null(validEmptyResult.ErrorMessage);
        Assert.Empty(validEmptyResult.Implementations);
    }
}
