#nullable enable

using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit.Builders;
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
    public async Task FeatureAndClassScanners_RejectUppercaseHandoffPrefixAsMalformed()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var featureContext = await FeatureContextScanner.ScanAsync(
            new FeatureContextRequest(fixture.Solution, "H:unknown99"));
        Assert.NotNull(featureContext);
        Assert.NotNull(featureContext!.Error);
        Assert.Equal(NavigationErrorCodes.InvalidHandoff, featureContext.Error!.Value.Code);

        var classStructure = await ClassStructureScanner.ScanAsync(
            new ClassStructureScanRequest(fixture.Solution, "H:unknown99"));
        Assert.NotNull(classStructure);
        Assert.NotNull(classStructure!.Error);
        Assert.Equal(NavigationErrorCodes.InvalidHandoff, classStructure.Error!.Value.Code);
    }

    [Fact]
    public async Task FeatureAndClassScanners_DoNotTreatWindowsDrivePathsAsHandoffHandles()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var featureContext = await FeatureContextScanner.ScanAsync(
            new FeatureContextRequest(fixture.Solution, @"H:\repo\file.cs"));
        Assert.Null(featureContext);

        var classStructure = await ClassStructureScanner.ScanAsync(
            new ClassStructureScanRequest(fixture.Solution, @"H:\repo\file.cs"));
        Assert.Null(classStructure);
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_RejectsPublicSourceIdentityForAnotherTarget()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var canonicalIdentity = await AnalysisSymbolIdentity.ForSourceAsync(fixture.Solution);
        Assert.NotNull(canonicalIdentity);
        var suppliedIdentity = AnalysisSymbolIdentity.ForSource(
            @"C:\ForeignRepo\Other.slnx",
            canonicalIdentity!.ContentHash,
            fixture.Solution);

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "Greeter", SourceIdentity: suppliedIdentity));
        Assert.Empty(result.Entries);
        Assert.Equal(NavigationErrorCodes.TargetMismatch, result.Error!.Value.Code);

        var staleIdentity = AnalysisSymbolIdentity.ForSource(
            fixture.Solution.FilePath!,
            new string('f', 64),
            fixture.Solution);
        var staleResult = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "Greeter", SourceIdentity: staleIdentity));
        Assert.Empty(staleResult.Entries);
        Assert.Equal(NavigationErrorCodes.StaleSnapshot, staleResult.Error!.Value.Code);
    }

    [Fact]
    public async Task SourceSnapshotIdentity_CaseVariantSolutionPathsHaveSameHash()
    {
        using var upper = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Sample.slnx",
            new ProjectSpec("Sample", [("Sample.cs", "public class SampleType { }")], VirtualProjectDirectory: "src/Sample"));
        using var lower = TestWorkspaceBuilder.CreateSolution(
            @"c:\virtualrepo\sample.slnx",
            new ProjectSpec("Sample", [("Sample.cs", "public class SampleType { }")], VirtualProjectDirectory: "src/Sample"));

        var original = await AnalysisSymbolIdentity.ForSourceAsync(upper.Solution);
        var variant = await AnalysisSymbolIdentity.ForSourceAsync(lower.Solution);

        Assert.NotNull(original);
        Assert.NotNull(variant);
        Assert.Equal(original!.ContentHash, variant!.ContentHash);
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_RejectsSourceIdentityWithForgedProjectMarker()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var identity = await AnalysisSymbolIdentity.ForSourceAsync(fixture.Solution);
        Assert.NotNull(identity);
        var project = fixture.Solution.Projects.First();
        var forgedIdentity = identity! with
        {
            SourceProjectMarkers = new Dictionary<Microsoft.CodeAnalysis.ProjectId, string>
            {
                [project.Id] = "forged-project-marker",
            },
        };

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "Greeter", Kind: SymbolKindFilter.Class, SourceIdentity: forgedIdentity));

        Assert.Empty(result.Entries);
        Assert.Equal(NavigationErrorCodes.TargetMismatch, result.Error!.Value.Code);

    }

    [Fact]
    public async Task FeatureAndClassScanners_RejectSourceIdentityWithForgedProjectMarker()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var identity = await AnalysisSymbolIdentity.ForSourceAsync(fixture.Solution);
        Assert.NotNull(identity);
        var project = fixture.Solution.Projects.First();
        var forgedIdentity = identity! with
        {
            SourceProjectMarkers = new Dictionary<Microsoft.CodeAnalysis.ProjectId, string>
            {
                [project.Id] = "forged-project-marker",
            },
        };

        var featureContext = await FeatureContextScanner.ScanAsync(
            new FeatureContextRequest(fixture.Solution, "Greeter", HandoffIdentity: forgedIdentity));
        Assert.Equal(NavigationErrorCodes.TargetMismatch, featureContext!.Error!.Value.Code);

        var classStructure = await ClassStructureScanner.ScanAsync(
            new ClassStructureScanRequest(fixture.Solution, "Greeter", HandoffIdentity: forgedIdentity));
        Assert.Equal(NavigationErrorCodes.TargetMismatch, classStructure!.Error!.Value.Code);
    }

    [Fact]
    public async Task SourceHandoffRoundTripsAcrossCaseVariantSolutionPaths()
    {
        using var upper = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Roundtrip.slnx",
            new ProjectSpec("Sample", [("Sample.cs", "namespace Shared; public class SampleType { public void Run() { } }")], VirtualProjectDirectory: "src/Sample"));
        using var lower = TestWorkspaceBuilder.CreateSolution(
            @"c:\virtualrepo\roundtrip.slnx",
            new ProjectSpec("Sample", [("Sample.cs", "namespace Shared; public class SampleType { public void Run() { } }")], VirtualProjectDirectory: "src/Sample"));

        var found = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(upper.Solution, "SampleType", Kind: SymbolKindFilter.Class));
        var entry = Assert.Single(found.Entries);
        Assert.NotNull(entry.HandoffId);

        var payload = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(lower.Solution, entry.HandoffId!));
        Assert.NotNull(payload);
        Assert.Null(payload.Error);
        Assert.Equal("SampleType", payload.Declaration.SymbolName);
    }

    [Fact]
    public async Task SameDocumentationIdInTwoProjects_RoundTripsToExactProject()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Duplicates.slnx",
            new ProjectSpec("First", [("Worker.cs", "namespace Shared; public class Worker { public void OnlyFirst() { } }")], VirtualProjectDirectory: "src/First"),
            new ProjectSpec("Second", [("Worker.cs", "namespace Shared; public class Worker { public void OnlySecond() { } }")], VirtualProjectDirectory: "src/Second"));

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "Worker", Kind: SymbolKindFilter.Class));
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(result.Entries[0].DocCommentId, result.Entries[1].DocCommentId);
        Assert.NotEqual(result.Entries[0].HandoffId, result.Entries[1].HandoffId);

        foreach (var entry in result.Entries)
        {
            var payload = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(fixture.Solution, entry.HandoffId!));
            Assert.NotNull(payload);
            Assert.Null(payload.Error);
            Assert.Contains(payload.Members, member => member.Name == (entry.ProjectName == "First" ? "OnlyFirst" : "OnlySecond"));
        }
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
    public async Task FindMatchesWithDetailsAsync_FiltersByProjectScope()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Scope.slnx",
            new ProjectSpec(
                "App",
                [("Shared.cs", "namespace Shared; public class SharedType { }")],
                VirtualProjectDirectory: "src/App"),
            new ProjectSpec(
                "App.Tests",
                [("Shared.cs", "namespace Shared; public class SharedType { }")],
                VirtualProjectDirectory: "src/TestHost"));

        var production = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "SharedType", ScopeType: SymbolScopeType.Production));
        var tests = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "SharedType", ScopeType: SymbolScopeType.Tests));
        var all = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "SharedType", ScopeType: SymbolScopeType.All));

        Assert.Equal("App", Assert.Single(production.Entries).ProjectName);
        Assert.Equal("App.Tests", Assert.Single(tests.Entries).ProjectName);
        Assert.Equal(2, all.Entries.Count);
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
