#nullable enable

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Hierarchy;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Hierarchy;

[Trait("Category", "Unit")]
public sealed class TypeHierarchyTests
{
    [Fact]
    public async Task ScanAsync_ClassHierarchy_ExtractsBaseTypesAndInterfaces()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);

        var safeProcessor = coreCompilation.GetTypeByMetadataName("SampleNamespace.Hierarchy.SafeProcessor");
        Assert.NotNull(safeProcessor);

        var payload = await TypeHierarchyScanner.ScanAsync(safeProcessor, fixture.Solution);

        Assert.Equal("SampleNamespace.Hierarchy.SafeProcessor", payload.TypeName);

        // Base types: FastProcessor, BaseProcessor, object
        Assert.Contains(payload.BaseTypes, b => b.Name.Contains("FastProcessor", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(payload.BaseTypes, b => b.Name.Contains("BaseProcessor", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(payload.BaseTypes, b => b.Name.Contains("object", System.StringComparison.OrdinalIgnoreCase));

        // Interfaces: IDisposable, IAdvancedProcessor, IProcessor
        Assert.Contains(payload.Interfaces, i => i.Name.Contains("IDisposable"));
        Assert.Contains(payload.Interfaces, i => i.Name.Contains("IAdvancedProcessor"));
        Assert.Contains(payload.Interfaces, i => i.Name.Contains("IProcessor"));
    }

    [Fact]
    public async Task ScanAsync_BaseClass_FindsDerivedClasses()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);

        var baseProcessor = coreCompilation.GetTypeByMetadataName("SampleNamespace.Hierarchy.BaseProcessor");
        Assert.NotNull(baseProcessor);

        var payload = await TypeHierarchyScanner.ScanAsync(baseProcessor, fixture.Solution);

        Assert.Equal("Derived classes:", payload.SubtypesHeading);
        Assert.True(payload.TotalSubtypes >= 2);
        Assert.Contains(payload.Subtypes, s => s.Name.Contains("FastProcessor"));
        Assert.Contains(payload.Subtypes, s => s.Name.Contains("SafeProcessor"));
    }

    [Fact]
    public async Task FormatText_RendersValidMarkdown()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);

        var safeProcessor = coreCompilation.GetTypeByMetadataName("SampleNamespace.Hierarchy.SafeProcessor");
        Assert.NotNull(safeProcessor);

        var text = await TypeHierarchyService.GetFormattedHierarchyAsync(safeProcessor, fixture.Solution);

        Assert.Contains("# Type hierarchy for SampleNamespace.Hierarchy.SafeProcessor", text);
        Assert.Contains("## Base classes:", text);
        Assert.Contains("FastProcessor", text);
        Assert.Contains("## Implemented interfaces:", text);
        Assert.Contains("IDisposable", text);
    }

    [Fact]
    public async Task ScanAsync_HierarchyAcrossProjects_PreservesSourceHandoffs()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\HierarchyProjects.slnx",
            new ProjectSpec("Contracts", [
                ("Contracts.cs", "namespace Contracts; public interface IRoot { } public class Root { }")],
                VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("Application", [
                ("Middle.cs", "namespace Application; public class Shared : Contracts.Root, Contracts.IRoot { }")],
                ProjectReferences: ["Contracts"],
                VirtualProjectDirectory: "src/Application"),
            new ProjectSpec("Extension", [
                ("Leaf.cs", "namespace Extension; public class Shared : Application.Shared { }")],
                ProjectReferences: ["Application", "Contracts"],
                VirtualProjectDirectory: "src/Extension"));
        var contracts = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        var application = await fixture.Solution.Projects.Single(project => project.Name == "Application").GetCompilationAsync();
        Assert.NotNull(contracts);
        Assert.NotNull(application);
        var root = contracts.GetTypeByMetadataName("Contracts.Root");
        var rootInterface = contracts.GetTypeByMetadataName("Contracts.IRoot");
        var middle = application.GetTypeByMetadataName("Application.Shared");
        Assert.NotNull(root);
        Assert.NotNull(rootInterface);
        Assert.NotNull(middle);

        var baseHierarchy = await TypeHierarchyScanner.ScanAsync(root, fixture.Solution);
        var interfaceHierarchy = await TypeHierarchyScanner.ScanAsync(rootInterface, fixture.Solution);
        var limitedInterfaceHierarchy = await TypeHierarchyScanner.ScanAsync(rootInterface, fixture.Solution, maxResults: 1);
        var middleHierarchy = await TypeHierarchyScanner.ScanAsync(middle, fixture.Solution);

        Assert.Contains(baseHierarchy.Subtypes, entry => entry.Name == "Application.Shared");
        Assert.Contains(baseHierarchy.Subtypes, entry => entry.Name == "Extension.Shared");
        Assert.Contains(interfaceHierarchy.Subtypes, entry => entry.Name == "Application.Shared");
        Assert.Contains(interfaceHierarchy.Subtypes, entry => entry.Name == "Extension.Shared");
        Assert.Single(limitedInterfaceHierarchy.Subtypes);
        Assert.Equal(2, limitedInterfaceHierarchy.TotalSubtypes);
        Assert.True(limitedInterfaceHierarchy.IsTruncated);
        var externalBase = Assert.Single(middleHierarchy.BaseTypes.Where(entry => entry.Name == "Contracts.Root"));
        var inheritedInterface = Assert.Single(middleHierarchy.Interfaces.Where(entry => entry.Name == "Contracts.IRoot"));
        Assert.StartsWith("src:", externalBase.HandoffId);
        Assert.StartsWith("src:", inheritedInterface.HandoffId);
        Assert.Equal("Root", (await SourceSymbolResolver.ResolveAsync(fixture.Solution, externalBase.HandoffId!)).Symbol!.Name);
        Assert.Equal("IRoot", (await SourceSymbolResolver.ResolveAsync(fixture.Solution, inheritedInterface.HandoffId!)).Symbol!.Name);

        var sameNamedSubtypes = baseHierarchy.Subtypes.Where(entry => entry.Name.EndsWith("Shared", System.StringComparison.Ordinal)).ToList();
        Assert.Equal(2, sameNamedSubtypes.Count);
        Assert.All(sameNamedSubtypes, entry => Assert.StartsWith("src:", entry.HandoffId));
        var resolvedProjects = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var entry in sameNamedSubtypes)
        {
            var resolved = await SourceSymbolResolver.ResolveAsync(fixture.Solution, entry.HandoffId!);
            Assert.True(resolved.IsSuccess);
            resolvedProjects.Add(resolved.Symbol!.ContainingAssembly!.Name);
        }

        Assert.Contains("Application", resolvedProjects);
        Assert.Contains("Extension", resolvedProjects);
    }

    [Fact]
    public async Task ScanAsync_PartialBaseAndInterfaceReturnEverySourceLocationAcrossProjects()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\PartialHierarchy.slnx",
            new ProjectSpec("Contracts", [
                ("Base.First.cs", "namespace Contracts; public partial class BaseType { }"),
                ("Base.Second.cs", "namespace Contracts; public partial class BaseType { }"),
                ("IContract.First.cs", "namespace Contracts; public partial interface IContract { }"),
                ("IContract.Second.cs", "namespace Contracts; public partial interface IContract { }")],
                VirtualProjectDirectory: "src/Contracts"),
            new ProjectSpec("App", [
                ("Derived.First.cs", "namespace App; public partial class Derived : Contracts.BaseType, Contracts.IContract { }"),
                ("Derived.Second.cs", "namespace App; public partial class Derived { }")],
                ProjectReferences: ["Contracts"],
                VirtualProjectDirectory: "src/App"));
        var contracts = await fixture.Solution.Projects.Single(project => project.Name == "Contracts").GetCompilationAsync();
        var app = await fixture.Solution.Projects.Single(project => project.Name == "App").GetCompilationAsync();
        Assert.NotNull(contracts);
        Assert.NotNull(app);
        var baseType = contracts.GetTypeByMetadataName("Contracts.BaseType");
        var derived = app.GetTypeByMetadataName("App.Derived");
        Assert.NotNull(baseType);
        Assert.NotNull(derived);

        var baseHierarchy = await TypeHierarchyScanner.ScanAsync(baseType, fixture.Solution);
        var derivedHierarchy = await TypeHierarchyScanner.ScanAsync(derived, fixture.Solution);

        Assert.Equal(1, baseHierarchy.TotalSubtypes);
        Assert.Single(baseHierarchy.Subtypes);
        var baseLocations = derivedHierarchy.BaseTypes.Where(entry => entry.Name == "Contracts.BaseType").ToList();
        var interfaceLocations = derivedHierarchy.Interfaces.Where(entry => entry.Name == "Contracts.IContract").ToList();
        Assert.Equal(2, baseLocations.Count);
        Assert.Equal(2, interfaceLocations.Count);
        Assert.Equal(new[] { "Base.First.cs", "Base.Second.cs" }, baseLocations.Select(entry => Path.GetFileName(entry.FilePath)).OrderBy(name => name, System.StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "IContract.First.cs", "IContract.Second.cs" }, interfaceLocations.Select(entry => Path.GetFileName(entry.FilePath)).OrderBy(name => name, System.StringComparer.Ordinal).ToArray());

        foreach (var entry in baseLocations.Concat(interfaceLocations))
        {
            Assert.StartsWith("src:", entry.HandoffId);
            var resolved = await SourceSymbolResolver.ResolveAsync(fixture.Solution, entry.HandoffId!);
            Assert.True(resolved.IsSuccess);
            Assert.Equal("Contracts", resolved.Symbol!.ContainingAssembly!.Name);
            Assert.Contains(resolved.Symbol.Name, new[] { "BaseType", "IContract" });
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ScanAsync_NonPositiveLimitsStillReturnOneSubtype(int maxResults)
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);
        var baseProcessor = coreCompilation.GetTypeByMetadataName("SampleNamespace.Hierarchy.BaseProcessor");
        Assert.NotNull(baseProcessor);

        var payload = await TypeHierarchyScanner.ScanAsync(baseProcessor, fixture.Solution, maxResults);

        Assert.Single(payload.Subtypes);
        Assert.True(payload.IsTruncated);
        Assert.True(payload.TotalSubtypes > payload.Subtypes.Count);
        Assert.Contains("1 more truncated", GetTypeHierarchyFormatter.FormatText(payload), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScannerAndService_RejectNullTypeAndSolution()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);
        var type = coreCompilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(type);

        await Assert.ThrowsAsync<System.ArgumentNullException>(() => TypeHierarchyScanner.ScanAsync(null!, fixture.Solution));
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => TypeHierarchyScanner.ScanAsync(type, null!));
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => TypeHierarchyService.GetFormattedHierarchyAsync(null!, fixture.Solution));
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => TypeHierarchyService.GetFormattedHierarchyAsync(type, null!));
        Assert.Throws<System.ArgumentNullException>(() => GetTypeHierarchyFormatter.FormatText(null!));
    }

    [Fact]
    public async Task ScanAsync_HandlesStructsAndRejectsUnsupportedNamedTypes()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\HierarchyTypeKinds.slnx",
            new ProjectSpec("Types", [("Types.cs", "namespace Types; public struct Point { } public enum State { Ready } ")]));
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var point = compilation.GetTypeByMetadataName("Types.Point");
        var state = compilation.GetTypeByMetadataName("Types.State");
        Assert.NotNull(point);
        Assert.NotNull(state);

        var structPayload = await TypeHierarchyScanner.ScanAsync(point, fixture.Solution);
        var unsupportedPayload = await TypeHierarchyScanner.ScanAsync(state, fixture.Solution);

        Assert.True(structPayload.IsSuccess);
        Assert.Contains(structPayload.BaseTypes, entry => entry.Name.Contains("ValueType", System.StringComparison.Ordinal));
        Assert.Empty(structPayload.Subtypes);
        Assert.False(unsupportedPayload.IsSuccess);
        Assert.Contains("enum", unsupportedPayload.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal(unsupportedPayload.ErrorMessage, GetTypeHierarchyFormatter.FormatText(unsupportedPayload));
    }

    [Fact]
    public async Task FormatText_ReportsExternalBaseAndInterfaceWithoutFakeSourceLocations()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var coreCompilation = await fixture.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(coreCompilation);
        var safeProcessor = coreCompilation.GetTypeByMetadataName("SampleNamespace.Hierarchy.SafeProcessor");
        Assert.NotNull(safeProcessor);

        var payload = await TypeHierarchyScanner.ScanAsync(safeProcessor, fixture.Solution);
        var text = GetTypeHierarchyFormatter.FormatText(payload);

        var objectEntry = Assert.Single(payload.BaseTypes.Where(entry => entry.Name == "object"));
        var disposable = Assert.Single(payload.Interfaces.Where(entry => entry.Name.Contains("IDisposable", System.StringComparison.Ordinal)));
        Assert.Equal(string.Empty, objectEntry.FilePath);
        Assert.Equal(0, objectEntry.Line);
        Assert.Null(objectEntry.HandoffId);
        Assert.Equal(string.Empty, disposable.FilePath);
        Assert.Equal(0, disposable.Line);
        Assert.Null(disposable.HandoffId);
        Assert.Contains("object", text, System.StringComparison.Ordinal);
        Assert.Contains("IDisposable", text, System.StringComparison.Ordinal);
        Assert.DoesNotContain(".cs:0", text, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScanAsync_InvalidCircularBaseDeclarationsTerminateWithFiniteChain()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\CircularHierarchy.slnx",
            new ProjectSpec("Cycle", [("Cycle.cs", "namespace Cycle; public class First : Second { } public class Second : First { }")]));
        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var first = compilation.GetTypeByMetadataName("Cycle.First");
        Assert.NotNull(first);

        var payload = await TypeHierarchyScanner.ScanAsync(first, fixture.Solution);

        Assert.True(payload.BaseTypes.Count < 10);
        Assert.Equal(payload.BaseTypes.Count, payload.BaseTypes.Select(entry => entry.Name).Distinct(System.StringComparer.Ordinal).Count());
    }
}
