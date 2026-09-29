#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class ClassStructureScannerTests
{
    [Fact]
    public async Task ScanAsync_ExtractsAllMembersAndVisibilities()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new ClassStructureScanRequest(fixture.Solution, "Greeter");

        var payload = await ClassStructureScanner.ScanAsync(request);

        Assert.NotNull(payload);
        Assert.Equal("SampleNamespace.Greeter", payload.TypeName);
        Assert.Equal("Class", payload.Kind);
        Assert.True(payload.TotalLines > 0);
        Assert.Single(payload.Files);

        // Members
        Assert.Contains(payload.Members, m => m.Name == "Prefix" && m.Kind == "Property" && m.Visibility == "public");
        Assert.Contains(payload.Members, m => m.Name == "Greet" && m.Kind == "Method" && m.Visibility == "public");
        Assert.Contains(payload.Members, m => m.Name == "GreetLoud" && m.Kind == "Method" && m.Visibility == "public");
    }

    [Fact]
    public async Task ScanAsync_FilterByKind_FiltersMembers()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new ClassStructureScanRequest(fixture.Solution, "Greeter", KindFilter: "Method");

        var payload = await ClassStructureScanner.ScanAsync(request);

        Assert.NotNull(payload);
        Assert.All(payload.Members, m => Assert.Equal("Method", m.Kind));
        Assert.Equal(2, payload.Members.Count); // Greet, GreetLoud
    }

    [Fact]
    public async Task ScanAsync_SortByName_OrdersAlphabetically()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new ClassStructureScanRequest(fixture.Solution, "Greeter", SortBy: "name");

        var payload = await ClassStructureScanner.ScanAsync(request);

        Assert.NotNull(payload);
        var names = payload.Members.Select(m => m.Name).ToList();
        var sorted = names.OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, names);
    }

    [Fact]
    public async Task RenderMarkdown_FormatsMarkdownTable()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new ClassStructureScanRequest(fixture.Solution, "Greeter");

        var payload = await ClassStructureScanner.ScanAsync(request);
        Assert.NotNull(payload);

        var markdown = ClassStructureScanner.RenderMarkdown(payload);

        Assert.Contains("# Typ: SampleNamespace.Greeter", markdown);
        Assert.Contains("| Kind | Name | Visibility | Lines | Signature | Handoff |", markdown);
        Assert.Contains("| Method | Greet | public |", markdown);
    }
}
