#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class FindSymbolScannerTests
{
    [Fact]
    public async Task FindMatchesWithDetailsAsync_FindsExactClassMatch()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new FindSymbolScanRequest(fixture.Solution, "Greeter");

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(request);

        Assert.NotEmpty(result.Entries);
        var entry = result.Entries.First();
        Assert.Equal("Greeter", entry.Name);
        Assert.Equal("class", entry.Kind);
        Assert.NotNull(entry.HandoffId);
        Assert.StartsWith("h:", entry.HandoffId);
        var internalId = HandoffHandleRegistry.Default.RestoreInternalHandoffForInput(entry.HandoffId!);
        Assert.True(internalId.IsSuccess);
        Assert.True(SymbolHandoffIdentifier.TryParse(internalId.Value!, out var parsed));
        Assert.Equal(SymbolHandoffOrigin.Source, parsed.Origin);
        Assert.Contains("~p:", parsed.DocumentationCommentId, System.StringComparison.Ordinal);
        Assert.True(SymbolHandoffToken.TryCreateTarget(fixture.Solution.FilePath!, out var targetToken));
        Assert.Equal(targetToken, parsed.TargetToken);

        var context = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(fixture.Solution, entry.HandoffId!));
        Assert.NotNull(context);
        Assert.Null(context.Error);
        Assert.Equal("Greeter", context.Declaration.SymbolName);

        var structure = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(fixture.Solution, entry.HandoffId!));
        Assert.NotNull(structure);
        Assert.Contains(structure.Members, member => member.Name == "Greet" && member.HandoffId?.StartsWith("h:", System.StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task SourceHandoffResolver_ReturnsTypedUnknownForeignAndStaleErrors()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var identity = await AnalysisSymbolIdentity.ForSourceAsync(fixture.Solution);
        Assert.NotNull(identity);

        var unknown = await SourceHandoffResolver.ResolveAsync(fixture.Solution, "h:unknown99", identity!);
        Assert.False(unknown.IsSuccess);
        Assert.Equal(NavigationErrorCodes.HandoffUnknown, unknown.Error!.Value.Code);
        var unknownContext = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(fixture.Solution, "h:unknown99"));
        Assert.NotNull(unknownContext?.Error);
        Assert.Equal(NavigationErrorCodes.HandoffUnknown, unknownContext!.Error!.Value.Code);
        var unknownStructure = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(fixture.Solution, "h:unknown99"));
        Assert.NotNull(unknownStructure?.Error);
        Assert.Equal(NavigationErrorCodes.HandoffUnknown, unknownStructure!.Error!.Value.Code);

        var project = fixture.Solution.Projects.First();
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var symbol = compilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(symbol);

        var foreignIdentity = AnalysisSymbolIdentity.ForSource(
            @"C:\\OtherRepo\\Sample.sln",
            identity!.ContentHash,
            fixture.Solution);
        var foreignInternal = foreignIdentity.FormatHandoff(symbol!, fixture.Solution);
        Assert.NotNull(foreignInternal);
        var foreignHandle = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(foreignInternal!);
        var foreign = await SourceHandoffResolver.ResolveAsync(fixture.Solution, foreignHandle, identity);
        Assert.False(foreign.IsSuccess);
        Assert.Equal(NavigationErrorCodes.TargetMismatch, foreign.Error!.Value.Code);

        var staleIdentity = AnalysisSymbolIdentity.ForSource(
            fixture.Solution.FilePath!,
            new string('f', 64),
            fixture.Solution);
        var staleInternal = staleIdentity.FormatHandoff(symbol!, fixture.Solution);
        Assert.NotNull(staleInternal);
        var staleHandle = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(staleInternal!);
        var stale = await SourceHandoffResolver.ResolveAsync(fixture.Solution, staleHandle, identity);
        Assert.False(stale.IsSuccess);
        Assert.Equal(NavigationErrorCodes.StaleSnapshot, stale.Error!.Value.Code);
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_FiltersByKind()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var classRequest = new FindSymbolScanRequest(fixture.Solution, "Greeter", Kind: SymbolKindFilter.Class);
        var classResult = await FindSymbolScanner.FindMatchesWithDetailsAsync(classRequest);
        Assert.Single(classResult.Entries);

        var methodRequest = new FindSymbolScanRequest(fixture.Solution, "Greeter", Kind: SymbolKindFilter.Method);
        var methodResult = await FindSymbolScanner.FindMatchesWithDetailsAsync(methodRequest);
        Assert.Empty(methodResult.Entries);
        Assert.Contains("Vorhandene Symbole mit diesem Namen haben den Typ: class", methodResult.Text);
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_WildcardSearch_ReturnsMatchingSymbols()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new FindSymbolScanRequest(fixture.Solution, "*Greet*");

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(request);

        Assert.True(result.Entries.Count >= 3); // Greeter, Greet, GreetLoud
        Assert.Contains(result.Entries, e => e.Name == "Greet");
        Assert.Contains(result.Entries, e => e.Name == "GreetLoud");
        Assert.Contains(result.Entries, e => e.Name == "Greeter");
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_MaxResults_TruncatesAndFlags()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new FindSymbolScanRequest(fixture.Solution, "*", MaxResults: 2);

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(request);

        Assert.Equal(2, result.Entries.Count);
        Assert.True(result.IsTruncated);
        Assert.True(result.TotalMatches > 2);
        Assert.Contains("maxResults", result.TruncatedBy);
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_SuggestsSimilarNamesOnMiss()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new FindSymbolScanRequest(fixture.Solution, "GreeterTypo");

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(request);

        Assert.Empty(result.Entries);
        Assert.Contains("Meintest du eventuell", result.Text);
        Assert.Contains("Greeter", result.Text);
    }
}
