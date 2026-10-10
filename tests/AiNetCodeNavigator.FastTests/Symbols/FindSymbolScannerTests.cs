#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using AiNetCodeNavigator.Core.FileStructure;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit.Builders;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class FindSymbolScannerTests
{
    [Theory]
    [InlineData("*", "method", "Greet1,Greet1,Greet1,Greet12,PrefixGreet1,Welcome,Welcome")]
    [InlineData("**", "method", "Greet1,Greet1,Greet1,Greet12,PrefixGreet1,Welcome,Welcome")]
    [InlineData("?*", "method", "Greet1,Greet1,Greet1,Greet12,PrefixGreet1,Welcome,Welcome")]
    [InlineData("gReEt", "method", "Greet1,Greet1,Greet1,Greet12,PrefixGreet1")]
    [InlineData("*gReEt*", "method", "Greet1,Greet1,Greet1,Greet12,PrefixGreet1")]
    [InlineData("Greet*", "method", "Greet1,Greet1,Greet1,Greet12")]
    [InlineData("Greet?", "method", "Greet1,Greet1,Greet1")]
    [InlineData("^greet[0-9]+$", "method", "Greet1,Greet1,Greet1,Greet12")]
    [InlineData("Greet\\d+", "method", "Greet1,Greet1,Greet1,Greet12,PrefixGreet1")]
    [InlineData("^[", "method", "")]
    [InlineData("  `Greet1()`  ", "method", "Greet1,Greet1,Greet1,Greet12,PrefixGreet1")]
    [InlineData("'Greeter<T>'", "class", "Greeter,Greeter,GreeterDecoy")]
    [InlineData("^Gr.*$", "class", "")]
    [InlineData("Matching.Greeter", "class", "Greeter,GreeterDecoy")]
    [InlineData("Matching.Greeter.Greet?", "method", "Greet1")]
    [InlineData("Matching.Greeter.^greet[0-9]+$", "method", "Greet1,Greet12")]
    [InlineData("`Matching.Greeter.Greet1()`", "method", "Greet1,Greet12,PrefixGreet1")]
    public async Task FindMatchesWithDetailsAsync_CharacterizesSimpleAndQualifiedMatching(
        string pattern, string kind, string expectedNames)
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("Matching",
            [("Matching.cs", """
                namespace Matching
                {
                    public class Greeter<T>
                    {
                        public void Greet1() { }
                        public void Greet12() { }
                        public void PrefixGreet1() { }
                        public void Welcome() { }
                    }
                    public class GreeterDecoy { public void Welcome() { } }
                    public class Other { public void Greet1() { } }
                }
                namespace Elsewhere { public class Greeter { public void Greet1() { } } }
                """)]));

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(fixture.Solution, pattern,
                Kind: kind == "class" ? SymbolKindFilter.Class : SymbolKindFilter.Method));

        var expected = expectedNames.Length == 0 ? Array.Empty<string>() : expectedNames.Split(',');
        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal),
            result.Entries.Select(entry => entry.Name).OrderBy(name => name, StringComparer.Ordinal));

        var compilation = await fixture.Solution.Projects.Single().GetCompilationAsync();
        var greeter = compilation!.GetTypeByMetadataName("Matching.Greeter`1")!;
        var candidate = kind == "class" ? (ISymbol)greeter : greeter.GetMembers("Greet1").Single();
        if (expected.Length > 0)
        {
            Assert.True(SymbolNameMatcher.MatchesSymbol(candidate, pattern));
            Assert.True(SymbolNameMatcher.CreateSymbolFilter(pattern)(candidate));
            Assert.True(SymbolNameMatcher.CreateDeclarationNameFilter(pattern)(candidate.Name));
        }
        if (pattern.Contains('.', StringComparison.Ordinal) && kind == "method")
        {
            Assert.True(SymbolNameMatcher.CreateDeclarationNameFilter(pattern)("Greeter"));
            var decoy = compilation.GetTypeByMetadataName("Matching.Other")!.GetMembers("Greet1").Single();
            Assert.True(SymbolNameMatcher.CreateDeclarationNameFilter(pattern)(decoy.Name));
            Assert.False(SymbolNameMatcher.MatchesSymbol(decoy, pattern));
            Assert.False(SymbolNameMatcher.CreateSymbolFilter(pattern)(decoy));
        }
    }

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
        Assert.StartsWith("src:src/Sample.Core/", entry.HandoffId, System.StringComparison.Ordinal);
        Assert.True(StableSymbolReferenceCodec.TryParse(entry.HandoffId!, out var parsed, out var parseError), parseError?.Message);
        Assert.IsType<StableSymbolReference.Source>(parsed);
        Assert.Equal("T:SampleNamespace.Greeter", parsed!.DeclarationId);

        var resolved = await SourceSymbolResolver.ResolveAsync(fixture.Solution, entry.HandoffId!);
        Assert.True(resolved.IsSuccess);
        Assert.Equal("Greeter", resolved.Symbol!.Name);

        var structure = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(fixture.Solution, entry.HandoffId!));
        Assert.NotNull(structure);
        Assert.Contains(structure.Members, member => member.Name == "Greet" && member.HandoffId?.StartsWith("src:", System.StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task SourceSymbolConsumers_RejectLegacyAndMalformedReferencesWithoutNameFallback()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        foreach (var input in new[] { "h:unknown99", "i:0:identifier", "SRC:src/Sample.Core/Sample.Core.csproj|T:SampleNamespace.Greeter" })
        {
            var symbol = await SourceSymbolResolver.ResolveAsync(fixture.Solution, input);
            Assert.False(symbol.IsSuccess);
            Assert.Equal(NavigationErrorCodes.InvalidSymbolReference, symbol.Error!.Value.Code);

            var structure = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(fixture.Solution, input));
            Assert.NotNull(structure?.Error);
            Assert.Equal(NavigationErrorCodes.InvalidSymbolReference, structure!.Error!.Value.Code);
        }
    }

    [Fact]
    public async Task FeatureAndClassScanners_RejectUppercaseHandoffPrefixAsMalformed()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var sourceSymbol = await SourceSymbolResolver.ResolveAsync(fixture.Solution, "H:unknown99");
        Assert.False(sourceSymbol.IsSuccess);
        Assert.Equal(NavigationErrorCodes.InvalidSymbolReference, sourceSymbol.Error!.Value.Code);

        var classStructure = await ClassStructureScanner.ScanAsync(
            new ClassStructureScanRequest(fixture.Solution, "H:unknown99"));
        Assert.NotNull(classStructure);
        Assert.NotNull(classStructure!.Error);
        Assert.Equal(NavigationErrorCodes.InvalidSymbolReference, classStructure.Error!.Value.Code);
    }

    [Fact]
    public async Task FeatureAndClassScanners_DoNotTreatWindowsDrivePathsAsHandoffHandles()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var sourceSymbol = await SourceSymbolResolver.ResolveAsync(fixture.Solution, @"H:\repo\file.cs");
        Assert.False(sourceSymbol.IsSuccess);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, sourceSymbol.Error?.Code);

        var classStructure = await ClassStructureScanner.ScanAsync(
            new ClassStructureScanRequest(fixture.Solution, @"H:\repo\file.cs"));
        Assert.NotNull(classStructure);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, classStructure.Error?.Code);
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_RejectsPublicSourceIdentityForAnotherTarget()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var (solution, currentRequest) = await CreateCurrentIdentityRequestAsync(fixture.Solution);
        var canonicalIdentity = currentRequest.Identity;
        var suppliedIdentity = canonicalIdentity with { CanonicalPath = @"C:\ForeignRepo\Other.slnx" };

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "Greeter", SourceIdentity: suppliedIdentity)
            { CurrentIdentityRequest = currentRequest });
        Assert.Empty(result.Entries);
        Assert.Equal(NavigationErrorCodes.TargetMismatch, result.Error!.Value.Code);

        var staleIdentity = canonicalIdentity with { ContentHash = new string('f', 64) };
        var staleResult = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "Greeter", SourceIdentity: staleIdentity)
            { CurrentIdentityRequest = currentRequest });
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

        var (_, original) = await CreateCurrentIdentityRequestAsync(upper.Solution);
        var (_, variant) = await CreateCurrentIdentityRequestAsync(lower.Solution);
        Assert.True(original.Identity.Matches(variant.Identity));
        Assert.Equal(original.Identity.ContentHash, variant.Identity.ContentHash);
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_RejectsSourceIdentityWithForgedProjectMarker()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var (solution, currentRequest) = await CreateCurrentIdentityRequestAsync(fixture.Solution);
        var identity = currentRequest.Identity;
        var project = solution.Projects.First();
        var forgedIdentity = identity! with
        {
            SourceProjectMarkers = new Dictionary<Microsoft.CodeAnalysis.ProjectId, string>
            {
                [project.Id] = "forged-project-marker",
            },
        };

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "Greeter", Kind: SymbolKindFilter.Class, SourceIdentity: forgedIdentity)
            { CurrentIdentityRequest = currentRequest });

        Assert.Empty(result.Entries);
        Assert.Equal(NavigationErrorCodes.TargetMismatch, result.Error!.Value.Code);

    }

    [Fact]
    public async Task FeatureAndClassScanners_RejectSourceIdentityWithForgedProjectMarker()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var (solution, currentRequest) = await CreateCurrentIdentityRequestAsync(fixture.Solution);
        var identity = currentRequest.Identity;
        var project = solution.Projects.First();
        var forgedIdentity = identity! with
        {
            SourceProjectMarkers = new Dictionary<Microsoft.CodeAnalysis.ProjectId, string>
            {
                [project.Id] = "forged-project-marker",
            },
        };

        var sourceSymbol = await SourceSymbolResolver.ResolveAsync(solution, "Greeter", forgedIdentity, currentRequest);
        Assert.False(sourceSymbol.IsSuccess);
        Assert.Equal(NavigationErrorCodes.TargetMismatch, sourceSymbol.Error!.Value.Code);

        var classStructure = await ClassStructureScanner.ScanAsync(
            new ClassStructureScanRequest(solution, "Greeter", HandoffIdentity: forgedIdentity)
            { CurrentIdentityRequest = currentRequest });
        Assert.Equal(NavigationErrorCodes.TargetMismatch, classStructure!.Error!.Value.Code);
    }

    private static async Task<(Solution Solution, SourceIdentityRequest Request)> CreateCurrentIdentityRequestAsync(Solution solution)
    {
        var provenance = Assert.IsType<WorkspaceInputProvenance>(WorkspaceInputProvenance.FindTestWorkspaceBuilderOutput(solution));
        var captured = MetadataReferenceImageCapture.Capture(solution, previousInputs: null,
            cancellationToken: default, provenance: provenance);
        await using var service = new AnalysisSymbolIdentityService();
        var result = await service.GetForSourceAsync(new SourceIdentityValidatedSnapshot(captured.Solution, captured.Inputs));
        Assert.True(result.IsSuccess, result.Error?.Message);
        return (captured.Solution, result.Value!);
    }

    [Fact]
    public async Task StableSourceReferenceRoundTripsAcrossCaseVariantSolutionPaths()
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

        var payload = await SourceSymbolResolver.ResolveAsync(lower.Solution, entry.HandoffId!);
        Assert.True(payload.IsSuccess);
        Assert.Equal("SampleType", payload.Symbol!.Name);
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
        Assert.Contains("Existing symbols with this name have kind: class", methodResult.Text);
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
    public async Task FindMatchesWithDetailsAsync_RestrictsMixedLanguageSolutionsToCSharp()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\MixedLanguages.slnx",
            new ProjectSpec("App", [
                ("CSharpSource.cs", "namespace CSharpOnly; public class CSharpType {}"),
                ("GeneratedSource.g.cs", "namespace GeneratedOnly; public class GeneratedType {}") ]));
        var legacyProjectId = ProjectId.CreateNewId("Legacy");
        var legacyProject = fixture.Solution.AddProject(ProjectInfo.Create(
            legacyProjectId,
            VersionStamp.Create(),
            "Legacy",
            "Legacy",
            LanguageNames.VisualBasic));
        var solution = legacyProject.AddDocument(
            DocumentId.CreateNewId(legacyProjectId),
            "Legacy.vb",
            SourceText.From("Namespace LegacyOnly\n Public Class LegacyType\n End Class\nEnd Namespace"));

        var legacy = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "LegacyType"));
        var legacyTypo = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "LegacyTypo"));
        var csharp = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "CSharpType"));
        var generatedDefault = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "GeneratedType"));
        var generatedIncluded = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "GeneratedType", IncludeGenerated: true));
        var indexScope = await IndexScopeScanner.ScanAsync(solution);
        var namespaceTree = await NamespaceTreeScanner.ScanSolutionAsync(solution);

        Assert.Empty(legacy.Entries);
        Assert.DoesNotContain("LegacyType", legacyTypo.Text);
        Assert.Equal("App", Assert.Single(csharp.Entries).ProjectName);
        Assert.Empty(generatedDefault.Entries);
        Assert.Equal("App", Assert.Single(generatedIncluded.Entries).ProjectName);
        Assert.False(Assert.Single(indexScope.Projects, project => project.Name == "Legacy").IsCSharpProject);
        Assert.DoesNotContain(namespaceTree.RootNamespaces, node => node.Name == "LegacyOnly");
        Assert.Contains(namespaceTree.RootNamespaces, node => node.Name == "CSharpOnly");
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
        var (solution, identityRequest) = await CreateCurrentIdentityRequestAsync(fixture.Solution);
        var request = new FindSymbolScanRequest(solution, "*", MaxResults: 2)
        {
            CurrentIdentityRequest = identityRequest,
        };

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(request);

        Assert.Equal(2, result.Entries.Count);
        Assert.True(result.IsTruncated);
        Assert.True(result.TotalMatches > 2);
        Assert.Contains("maxResults", result.TruncatedBy);
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_RetainsKnownMatchesBeyondPageSize()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\PagedSymbols.slnx",
            new ProjectSpec("Sample", [
                ("One.cs", "public class MarkerOne { }"),
                ("Two.cs", "public class MarkerTwo { }"),
                ("Three.cs", "public class MarkerThree { }"),
            ], VirtualProjectDirectory: "src/Sample"));

        var (solution, identityRequest) = await CreateCurrentIdentityRequestAsync(fixture.Solution);
        var first = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "Marker*", MaxResults: 2)
            {
                CurrentIdentityRequest = identityRequest,
            });
        Assert.Equal(2, first.Entries.Count);
        Assert.NotNull(first.ResultCursor);
        var second = await FindSymbolScanner.FindMatchesWithDetailsAsync(
            new FindSymbolScanRequest(solution, "Marker*", MaxResults: 2, ResultCursor: first.ResultCursor)
            {
                CurrentIdentityRequest = identityRequest,
            });

        Assert.Null(second.Error);
        Assert.Null(second.ResultCursor);
        Assert.Equal(3, first.Entries.Concat(second.Entries).Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task FindMatchesWithDetailsAsync_SuggestsSimilarNamesOnMiss()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new FindSymbolScanRequest(fixture.Solution, "GreeterTypo");

        var result = await FindSymbolScanner.FindMatchesWithDetailsAsync(request);

        Assert.Empty(result.Entries);
        Assert.Contains("Did you mean", result.Text);
        Assert.Contains("Greeter", result.Text);
    }
}
