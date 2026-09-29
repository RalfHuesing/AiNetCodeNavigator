#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Hierarchy;
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

        Assert.Equal("Abgeleitete Klassen:", payload.SubtypesHeading);
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

        Assert.Contains("# Typ-Hierarchie für SampleNamespace.Hierarchy.SafeProcessor", text);
        Assert.Contains("## Basisklassen:", text);
        Assert.Contains("FastProcessor", text);
        Assert.Contains("## Implementierte Interfaces:", text);
        Assert.Contains("IDisposable", text);
    }
}
