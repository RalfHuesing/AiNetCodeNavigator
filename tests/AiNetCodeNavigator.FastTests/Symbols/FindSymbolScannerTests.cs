#nullable enable

using System;
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
        Assert.NotNull(featureContext);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, featureContext.Error?.Code);

        var classStructure = await ClassStructureScanner.ScanAsync(
            new ClassStructureScanRequest(fixture.Solution, @"H:\repo\file.cs"));
        Assert.NotNull(classStructure);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, classStructure.Error?.Code);
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
    public async Task FindMatchesWithDetailsAsync_StructFilterExcludesRecordStructs()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Kinds.slnx",
            new ProjectSpec(
                "App",
                [("Kinds.cs", "public struct PlainStruct { } public record struct RecordStruct(int Value);")],
                VirtualProjectDirectory: "src/App"));

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "*", Kind: SymbolKindFilter.Struct));

        Assert.Equal("PlainStruct", Assert.Single(result.Entries).Name);
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_FiltersDelegateAndSpecificRecordKinds()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\SpecificKinds.slnx",
            new ProjectSpec(
                "App",
                [("Kinds.cs", "public delegate void WorkHandler(); public record class RecordClass(int Value); public record struct RecordStruct(int Value);")],
                VirtualProjectDirectory: "src/App"));

        var delegateResult = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "WorkHandler", Kind: SymbolKindFilter.Delegate));
        var recordResult = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "Record*", Kind: SymbolKindFilter.Record));
        var recordClassResult = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "RecordClass", Kind: SymbolKindFilter.RecordClass));
        var recordStructResult = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "RecordStruct", Kind: SymbolKindFilter.RecordStruct));
        var delegateMismatch = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "RecordClass", Kind: SymbolKindFilter.Delegate));
        var recordClassMismatch = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "RecordStruct", Kind: SymbolKindFilter.RecordClass));
        var recordStructMismatch = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "RecordClass", Kind: SymbolKindFilter.RecordStruct));

        Assert.Equal("delegate", Assert.Single(delegateResult.Entries).Kind);
        Assert.Equal(2, recordResult.Entries.Count);
        Assert.Contains(recordResult.Entries, entry => entry.Name == "RecordClass" && entry.Kind == "record class");
        Assert.Contains(recordResult.Entries, entry => entry.Name == "RecordStruct" && entry.Kind == "record struct");
        Assert.Equal("RecordClass", Assert.Single(recordClassResult.Entries).Name);
        Assert.Equal("RecordStruct", Assert.Single(recordStructResult.Entries).Name);
        Assert.Empty(delegateMismatch.Entries);
        Assert.Empty(recordClassMismatch.Entries);
        Assert.Empty(recordStructMismatch.Entries);
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_ExcludesGeneratedLocationsUnlessRequested()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\Generated.slnx",
            new ProjectSpec(
                "App",
                [
                    ("GeneratedPath.g.cs", "public class ProductionPathGenerated { }"),
                    ("GeneratedPath.g.i.cs", "public class ProductionPathGiGenerated { }"),
                    ("GeneratedPath.generated.cs", "public class ProductionPathSuffixGenerated { }"),
                    ("GeneratedPath.designer.cs", "public class ProductionPathDesignerGenerated { }"),
                    ("obj/GeneratedPath.cs", "public class ProductionObjPathGenerated { }"),
                    ("GeneratedHeader.cs", "// <auto-generated />\npublic class ProductionHeaderGenerated { }"),
                    ("GeneratedAttribute.cs", "namespace Custom; public sealed class GeneratedCodeAttribute : System.Attribute { public GeneratedCodeAttribute(string tool, string version) { } } [GeneratedCode(\"tool\", \"1\")] public class ProductionAttributeGenerated { }"),
                    ("Mixed.cs", "public partial class MixedGenerated { }"),
                    ("Mixed.g.cs", "public partial class MixedGenerated { }")
                ],
                VirtualProjectDirectory: "src/App"),
            new ProjectSpec(
                "App.Tests",
                [("TestGenerated.g.cs", "public class TestPathGenerated { }")],
                VirtualProjectDirectory: "src/TestHost"));

        var defaultProduction = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "ProductionPathGenerated", ScopeType: SymbolScopeType.Production));
        var includedProduction = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "ProductionPathGenerated", ScopeType: SymbolScopeType.Production, IncludeGenerated: true));
        var defaultTests = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "TestPathGenerated", ScopeType: SymbolScopeType.Tests));
        var includedTests = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "TestPathGenerated", ScopeType: SymbolScopeType.Tests, IncludeGenerated: true));
        var defaultHeader = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "ProductionHeaderGenerated"));
        var includedHeader = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "ProductionHeaderGenerated", IncludeGenerated: true));
        var defaultAttribute = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "ProductionAttributeGenerated"));
        var includedAttribute = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "ProductionAttributeGenerated", IncludeGenerated: true));
        var defaultMixed = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "MixedGenerated"));
        var includedMixed = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, "MixedGenerated", IncludeGenerated: true));

        Assert.Empty(defaultProduction.Entries);
        Assert.Equal("App", Assert.Single(includedProduction.Entries).ProjectName);
        foreach (var generatedName in new[]
                 {
                     "ProductionPathGiGenerated",
                     "ProductionPathSuffixGenerated",
                     "ProductionPathDesignerGenerated",
                     "ProductionObjPathGenerated"
                 })
        {
            var excluded = await FindSymbolScanner.FindMatchesWithDetailsAsync(
                new FindSymbolScanRequest(fixture.Solution, generatedName));
            var included = await FindSymbolScanner.FindMatchesWithDetailsAsync(
                new FindSymbolScanRequest(fixture.Solution, generatedName, IncludeGenerated: true));

            Assert.Empty(excluded.Entries);
            Assert.Single(included.Entries);
        }

        Assert.Empty(defaultTests.Entries);
        Assert.Equal("App.Tests", Assert.Single(includedTests.Entries).ProjectName);
        Assert.Empty(defaultHeader.Entries);
        Assert.Single(includedHeader.Entries);
        Assert.Empty(defaultAttribute.Entries);
        Assert.Single(includedAttribute.Entries);
        var defaultMixedEntry = Assert.Single(defaultMixed.Entries);
        Assert.Single(defaultMixedEntry.Locations!);
        Assert.Contains(defaultMixedEntry.Locations!, location => location.FilePath.EndsWith("Mixed.cs", StringComparison.Ordinal));
        var includedMixedEntry = Assert.Single(includedMixed.Entries);
        Assert.Equal(2, includedMixedEntry.Locations!.Count);
        Assert.Contains(includedMixedEntry.Locations, location => location.FilePath.EndsWith("Mixed.g.cs", StringComparison.Ordinal));
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
    public async Task FindMatchesWithDetailsAsync_ClassifiesRootRelativeTestDirectoriesByScope()
    {
        using var handle = TestWorkspaceBuilder.CreateSolution(
            new ProjectSpec(
                "App",
                [
                    ("TestsProbe.cs", "namespace ScopeChecks; public class RootTestsDirectoryProbe { }"),
                    ("TestProbe.cs", "namespace ScopeChecks; public class RootTestDirectoryProbe { }"),
                    ("Contest.cs", "namespace ScopeChecks; public class Contest { }")
                ]));
        var project = handle.Solution.Projects.Single();
        var documents = project.Documents.ToDictionary(document => document.Name, document => document.Id);
        var solution = handle.Solution
            .WithDocumentFilePath(documents["TestsProbe.cs"], "tests/TestsProbe.cs")
            .WithDocumentFilePath(documents["TestProbe.cs"], "test/TestProbe.cs")
            .WithDocumentFilePath(documents["Contest.cs"], "Contest.cs");

        var rootTestsDirectory = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "RootTestsDirectoryProbe", ScopeType: SymbolScopeType.Tests));
        var rootTestDirectory = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "RootTestDirectoryProbe", ScopeType: SymbolScopeType.Tests));
        var ordinaryNameInProductionScope = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "Contest", ScopeType: SymbolScopeType.Production));
        var ordinaryNameInTestScope = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "Contest", ScopeType: SymbolScopeType.Tests));

        Assert.Single(rootTestsDirectory.Entries);
        Assert.Single(rootTestDirectory.Entries);
        Assert.Single(ordinaryNameInProductionScope.Entries);
        Assert.Empty(ordinaryNameInTestScope.Entries);
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
