#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit.Builders;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class FeatureContextScannerTests
{
    [Fact]
    public async Task ScanAsync_ReturnsDeclarationAndCallers()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new FeatureContextRequest(
            Solution: fixture.Solution,
            SymbolIdentifier: "Greet",
            MaxCallers: 10,
            MaxTests: 10);

        var payload = await FeatureContextScanner.ScanAsync(request);

        Assert.NotNull(payload);
        Assert.Equal("Greet", payload.Declaration.SymbolName);
        Assert.Equal("method", payload.Declaration.Kind);
        Assert.Equal("public", payload.Declaration.Modifiers);
        Assert.NotNull(payload.Declaration.HandoffId);

        // ServiceCaller calls Greet
        Assert.NotEmpty(payload.Callers);
        Assert.Contains(payload.Callers, c => c.CallerName.Contains("ServiceCaller.ExecuteSingle"));
        Assert.Contains(payload.Callers, c => c.CallerName.Contains("ServiceCaller.ExecuteMultiple"));
    }

    [Fact]
    public async Task ScanAsync_DeclarationAndCallerHandoffsResolveToTheirSymbols()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var payload = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(fixture.Solution, "Greet"));
        Assert.NotNull(payload);

        var caller = Assert.Single(payload.Callers, item => item.CallerName.EndsWith("ExecuteSingle", StringComparison.Ordinal));
        Assert.StartsWith("h:", payload.Declaration.HandoffId);
        Assert.StartsWith("h:", caller.CallerHandoffId);

        var targetRoundtrip = await FeatureContextScanner.ScanAsync(
            new FeatureContextRequest(fixture.Solution, payload.Declaration.HandoffId!));
        var callerRoundtrip = await FeatureContextScanner.ScanAsync(
            new FeatureContextRequest(fixture.Solution, caller.CallerHandoffId!));

        Assert.NotNull(targetRoundtrip);
        Assert.Equal("Greet", targetRoundtrip.Declaration.SymbolName);
        Assert.NotNull(callerRoundtrip);
        Assert.Equal("ExecuteSingle", callerRoundtrip.Declaration.SymbolName);
    }

    [Fact]
    public async Task FindSymbolHandoff_RoundtripsThroughBodyFeatureAndClassStructure()
    {
        using var handle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\FollowUpResolution.slnx",
            new ProjectSpec("Sample.Core", [("Greeter.cs", "namespace Sample.Core;\npublic class Greeter\n{\n public string Greet() => \"hello\";\n}")]));
        var search = await FindSymbolScanner.FindMatchesWithDetailsAsync(new FindSymbolScanRequest(handle.Solution, "Greet"));
        var entry = Assert.Single(search.Entries, candidate => candidate.Name == "Greet");
        Assert.StartsWith("h:", entry.HandoffId);
        var document = handle.Solution.Projects.Single().Documents.Single();
        var sourceText = await document.GetTextAsync();
        var position = $"{document.FilePath}:4:{sourceText.ToString().Split('\n')[3].IndexOf("Greet", StringComparison.Ordinal) + 1}";

        foreach (var identifier in new[] { entry.HandoffId!, entry.DocCommentId!, position, $"{document.FilePath}:4" })
        {
            var body = await SourceSymbolBodyResolver.ResolveAsync(handle.Solution, identifier, maxBodyLines: 20);
            var feature = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(handle.Solution, identifier));
            var structure = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(handle.Solution, identifier));

            Assert.Null(body.Error);
            Assert.Contains("Greet", body.Body?.Body);
            Assert.NotNull(feature);
            Assert.Equal("Greet", feature.Declaration.SymbolName);
            Assert.NotNull(structure);
            Assert.Equal("Sample.Core.Greeter", structure.TypeName);
            Assert.Contains(structure.Members, member => member.Name == "Greet");
        }
    }

    [Fact]
    public async Task ScanAsync_ReturnsTestCandidateWithMethodHandoff()
    {
        const string testSource = """
            using System;
            namespace Xunit { public sealed class FactAttribute : Attribute { } }
            namespace Sample.Tests
            {
                public class TargetServiceTests
                {
                    [Xunit.Fact]
                    public void DoesWork() { }
                }
            }
            """;
        using var handle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\FeatureTests.slnx",
            new ProjectSpec("Sample.Core", [("TargetService.cs", "namespace Sample.Core; public class TargetService { }")]),
            new ProjectSpec("Sample.Tests", [("TargetServiceTests.cs", testSource)], ProjectReferences: ["Sample.Core"]));

        var payload = await FeatureContextScanner.ScanAsync(
            new FeatureContextRequest(handle.Solution, "Sample.Core.TargetService"));
        Assert.NotNull(payload);

        var test = Assert.Single(payload.Tests);
        Assert.Equal("TargetServiceTests", test.FixtureName);
        Assert.Equal("DoesWork", test.TestMethod);
        Assert.Equal("xUnit", test.Framework);
        Assert.StartsWith("h:", test.HandoffId);

        var roundtrip = await FeatureContextScanner.ScanAsync(
            new FeatureContextRequest(handle.Solution, test.HandoffId!));
        Assert.NotNull(roundtrip);
        Assert.Equal("DoesWork", roundtrip.Declaration.SymbolName);
    }

    [Fact]
    public async Task ScanAsync_AppliesProductionAndTestScopeToCallerEntries()
    {
        const string testSource = "using System; namespace Xunit { public sealed class FactAttribute : Attribute { } } namespace Sample.Tests { public class TargetServiceTests { [Xunit.Fact] public void ExercisesTarget(Sample.Core.TargetService target) => target.Run(); } }";
        using var handle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\FeatureScopes.slnx",
            new ProjectSpec("Sample.Core", [("TargetService.cs", "namespace Sample.Core; public class TargetService { public void Run() { } }")]),
            new ProjectSpec("Sample.App", [("Caller.cs", "namespace Sample.App; public class Caller { public void Invoke(Sample.Core.TargetService target) => target.Run(); }")], ProjectReferences: ["Sample.Core"]),
            new ProjectSpec("Sample.Tests", [("TargetServiceTests.cs", testSource)], ProjectReferences: ["Sample.Core"]));

        var production = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(
            handle.Solution, "Run", Scope: SymbolScopeType.Production));
        var tests = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(
            handle.Solution, "Run", Scope: SymbolScopeType.Tests));

        Assert.NotNull(production);
        Assert.NotNull(tests);
        Assert.Contains(production.Callers, caller => caller.CallerName.EndsWith("Invoke", StringComparison.Ordinal));
        Assert.DoesNotContain(production.Callers, caller => caller.CallerName.EndsWith("ExercisesTarget", StringComparison.Ordinal));
        Assert.Contains(tests.Callers, caller => caller.CallerName.EndsWith("ExercisesTarget", StringComparison.Ordinal));
        Assert.DoesNotContain(tests.Callers, caller => caller.CallerName.EndsWith("Invoke", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_UsesDocumentProjectForNeutralTestCallerScope()
    {
        const string testSource = "using System; namespace Xunit { public sealed class FactAttribute : Attribute { } } namespace Sample.App.Tests { public class TestRunner { [Xunit.Fact] public void Exercise(Sample.Core.TargetService target) => target.Run(); } }";
        const string pathSource = "namespace Sample.PathProject { public class PathRunner { public void ExercisePath(Sample.Core.TargetService target) => target.Run(); } }";
        using var handle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\FeatureProjectScopes.slnx",
            new ProjectSpec("Sample.Core", [("TargetService.cs", "namespace Sample.Core; public class TargetService { public void Run() { } }")]),
            new ProjectSpec("Sample.App", [("Caller.cs", "namespace Sample.App; public class Caller { public void Invoke(Sample.Core.TargetService target) => target.Run(); }")], ProjectReferences: ["Sample.Core"]),
            new ProjectSpec("Sample.App.Tests", [("Shared.cs", testSource)], ProjectReferences: ["Sample.Core"], VirtualProjectDirectory: "src"),
            new ProjectSpec("Sample.PathProject", [("tests/PathCaller.cs", pathSource)], ProjectReferences: ["Sample.Core"]));

        var all = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(
            handle.Solution, "Run", Scope: SymbolScopeType.All));
        var production = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(
            handle.Solution, "Run", Scope: SymbolScopeType.Production));
        var tests = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(
            handle.Solution, "Run", Scope: SymbolScopeType.Tests));

        Assert.NotNull(all);
        Assert.NotNull(production);
        Assert.NotNull(tests);
        Assert.Equal(3, all.TotalCallers);
        Assert.Equal(3, all.Callers.Count);
        Assert.Equal(1, production.TotalCallers);
        Assert.Single(production.Callers);
        Assert.Equal(2, tests.TotalCallers);
        Assert.Equal(2, tests.Callers.Count);
        Assert.Contains(production.Callers, caller => caller.CallerName.EndsWith("Invoke", StringComparison.Ordinal));
        Assert.DoesNotContain(production.Callers, caller => caller.CallerName.EndsWith("Exercise", StringComparison.Ordinal));
        Assert.Contains(tests.Callers, caller => caller.CallerName.EndsWith("ExercisePath", StringComparison.Ordinal));
        Assert.Contains(tests.Callers, caller => caller.CallerName.EndsWith("Exercise", StringComparison.Ordinal));
        Assert.DoesNotContain(tests.Callers, caller => caller.CallerName.EndsWith("Invoke", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_FiltersTestRecommendationsByScopeBeforeCounting()
    {
        const string fixtureSources = "using System; namespace Xunit { public sealed class FactAttribute : Attribute { } } namespace Sample.Tests { public class TargetServiceTest { [Xunit.Fact] public void Single() { } } public class TargetServiceTests { [Xunit.Fact] public void Plural() { } } }";
        const string pathFixtureSource = "using System; namespace Xunit { public sealed class FactAttribute : Attribute { } } namespace Sample.PathProject { public class TargetServiceSpec { [Xunit.Fact] public void PathBased() { } } }";
        using var handle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\FeatureRecommendationScopes.slnx",
            new ProjectSpec("Sample.Core", [("TargetService.cs", "namespace Sample.Core; public class TargetService { }")]),
            new ProjectSpec("Sample.App.Tests", [("Shared.cs", fixtureSources)], ProjectReferences: ["Sample.Core"], VirtualProjectDirectory: "src"),
            new ProjectSpec("Sample.PathProject", [("tests/PathFixtures.cs", pathFixtureSource)], ProjectReferences: ["Sample.Core"]));

        var all = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(
            handle.Solution, "Sample.Core.TargetService", MaxTests: 1, Scope: SymbolScopeType.All));
        var production = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(
            handle.Solution, "Sample.Core.TargetService", MaxTests: 1, Scope: SymbolScopeType.Production));
        var tests = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(
            handle.Solution, "Sample.Core.TargetService", MaxTests: 1, Scope: SymbolScopeType.Tests));

        Assert.NotNull(all);
        Assert.NotNull(production);
        Assert.NotNull(tests);
        Assert.Equal(3, all.TotalTests);
        Assert.Single(all.Tests);
        Assert.True(all.TestsTruncated);
        Assert.Empty(production.Tests);
        Assert.Equal(0, production.TotalTests);
        Assert.False(production.TestsTruncated);
        Assert.Equal(3, tests.TotalTests);
        Assert.Single(tests.Tests);
        Assert.True(tests.TestsTruncated);
    }

    [Fact]
    public async Task ScanAsync_ClampsLimitsAndReportsTruncation()
    {
        const int callCount = 60;
        const int testCount = 55;
        var calls = string.Join(Environment.NewLine, Enumerable.Repeat("target.Run();", callCount));
        var testMethods = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, testCount).Select(index => $"[Xunit.Fact] public void Case{index}() {{ }}"));
        var testSource = $$"""
            using System;
            namespace Xunit { public sealed class FactAttribute : Attribute { } }
            namespace Sample.Tests { public class TargetServiceTests { {{testMethods}} } }
            """;
        using var handle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\FeatureLimits.slnx",
            new ProjectSpec("Sample.Core", [("TargetService.cs", "namespace Sample.Core; public class TargetService { public void Run() { } }")]),
            new ProjectSpec("Sample.App", [("Caller.cs", $"namespace Sample.App; public class Caller {{ public void Invoke(Sample.Core.TargetService target) {{ {calls} }} }}")], ProjectReferences: ["Sample.Core"]),
            new ProjectSpec("Sample.Tests", [("TargetServiceTests.cs", testSource)], ProjectReferences: ["Sample.Core"]));

        var payload = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(
            handle.Solution,
            "Run",
            MaxCallers: 0,
            MaxTests: 500));

        Assert.NotNull(payload);
        Assert.Equal(callCount, payload.TotalCallers);
        Assert.Single(payload.Callers);
        Assert.True(payload.CallersTruncated);
        Assert.Equal(testCount, payload.TotalTests);
        Assert.Equal(50, payload.Tests.Count);
        Assert.True(payload.TestsTruncated);
    }

    [Fact]
    public async Task ScanAsync_ValidatesInputsAndPreservesRecoverableResolutionErrors()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        await Assert.ThrowsAsync<ArgumentNullException>(() => FeatureContextScanner.ScanAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => FeatureContextScanner.ScanAsync(new FeatureContextRequest(null!, "Greeter")));
        await Assert.ThrowsAsync<ArgumentException>(() => FeatureContextScanner.ScanAsync(new FeatureContextRequest(fixture.Solution, " ")));
        await Assert.ThrowsAsync<ArgumentNullException>(() => FeatureContextScanner.ResolveSymbolAsync(null!, "Greeter"));
        await Assert.ThrowsAsync<ArgumentException>(() => FeatureContextScanner.ResolveSymbolAsync(fixture.Solution, " "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => FeatureContextScanner.ResolveSymbolResultAsync(null!, "Greeter", null));

        var missing = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(fixture.Solution, "MissingType"));
        var invalidHandoff = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(fixture.Solution, "h:unknown99"));

        Assert.NotNull(missing);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, missing.Error?.Code);
        Assert.NotNull(invalidHandoff);
        Assert.Equal(NavigationErrorCodes.HandoffUnknown, invalidHandoff.Error?.Code);
    }

    [Fact]
    public async Task ResolveSymbolResultAsync_ReportsInvalidPositionAsRecoverableError()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var document = fixture.Solution.Projects.Single(project => project.Name == "Sample.Core")
            .Documents.Single(candidate => candidate.Name == "Greeter.cs");

        var resolution = await FeatureContextScanner.ResolveSymbolResultAsync(
            fixture.Solution,
            $"{document.FilePath}:0:1",
            null);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(NavigationErrorCodes.InvalidArgument, resolution.Error?.Code);
    }

    [Fact]
    public async Task ResolveSymbolResultAsync_RejectsMetadataOnlyTypesAndSymbolFreeTokens()
    {
        const string source = "namespace Demo;\npublic class Greeter\n{\n public string Greet()\n {\n  return \"hello\";\n }\n}";
        using var handle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\SourceOnlyResolution.slnx",
            new ProjectSpec("Demo", [("Greeter.cs", source)]));
        var document = handle.Solution.Projects.Single().Documents.Single();
        var literalLine = source.Split('\n')[5];
        var literalPosition = $"{document.FilePath}:6:{literalLine.IndexOf("hello", StringComparison.Ordinal) + 2}";
        var lineOnlyLiteralPosition = $"{document.FilePath}:6";
        var punctuationPosition = $"{document.FilePath}:7:2";

        var metadataType = await FeatureContextScanner.ResolveSymbolResultAsync(handle.Solution, "System.String", null);
        var literal = await FeatureContextScanner.ResolveSymbolResultAsync(handle.Solution, literalPosition, null);
        var lineOnlyLiteral = await FeatureContextScanner.ResolveSymbolResultAsync(handle.Solution, lineOnlyLiteralPosition, null);
        var punctuation = await FeatureContextScanner.ResolveSymbolResultAsync(handle.Solution, punctuationPosition, null);
        var body = await SourceSymbolBodyResolver.ResolveAsync(handle.Solution, "System.String", maxBodyLines: 10);
        var feature = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(handle.Solution, "System.String"));
        var structure = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(handle.Solution, "System.String"));

        Assert.False(literal.IsSuccess);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, literal.Error?.Code);
        Assert.False(lineOnlyLiteral.IsSuccess);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, lineOnlyLiteral.Error?.Code);
        Assert.False(punctuation.IsSuccess);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, punctuation.Error?.Code);
        Assert.False(metadataType.IsSuccess);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, metadataType.Error?.Code);
        Assert.Null(body.Body);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, body.Error?.Code);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, feature?.Error?.Code);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, structure?.Error?.Code);
    }

    [Fact]
    public async Task ResolveSymbolResultAsync_ResolvesDocumentationIdsAndQualifiedNames()
    {
        const string source = "namespace Sample.Core; public class Greeter { public string Greet() => \"hello\"; }";
        using var handle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\IdentifierForms.slnx",
            new ProjectSpec("Sample.Core", [("Greeter.cs", source)]));

        var byDocId = await FeatureContextScanner.ResolveSymbolResultAsync(
            handle.Solution, "M:Sample.Core.Greeter.Greet", null);
        var byQualifiedName = await FeatureContextScanner.ResolveSymbolResultAsync(
            handle.Solution, "Sample.Core.Greeter.Greet", null);

        Assert.True(byDocId.IsSuccess);
        Assert.Equal("Greet", byDocId.Value?.Name);
        Assert.True(byQualifiedName.IsSuccess);
        Assert.Equal("Greet", byQualifiedName.Value?.Name);
    }

    [Fact]
    public async Task ResolveSymbolResultAsync_ResolvesPositionAndReportsAmbiguousNames()
    {
        const string first = "namespace Sample.One; public class Worker { public void Run() { } }";
        const string second = "namespace Sample.Two; public class Worker { public void Run() { } }";
        using var handle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\PositionAndAmbiguity.slnx",
            new ProjectSpec("Sample.One", [("One.cs", first)]),
            new ProjectSpec("Sample.Two", [("Two.cs", second)]));
        var firstDocument = handle.Solution.Projects.Single(project => project.Name == "Sample.One").Documents.Single();
        var position = $"{firstDocument.FilePath}:1:{first.IndexOf("Run", StringComparison.Ordinal) + 1}";

        var byPosition = await FeatureContextScanner.ResolveSymbolResultAsync(handle.Solution, position, null);
        var ambiguous = await FeatureContextScanner.ResolveSymbolResultAsync(handle.Solution, "Run", null);

        Assert.True(byPosition.IsSuccess);
        Assert.Equal("Run", byPosition.Value?.Name);
        Assert.False(ambiguous.IsSuccess);
        Assert.Equal(NavigationErrorCodes.AmbiguousSymbol, ambiguous.Error?.Code);
    }

    [Fact]
    public async Task ScanAsync_AmbiguousNameReturnsSelectableHandoffCandidates()
    {
        using var handle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\AmbiguousCandidates.slnx",
            new ProjectSpec("Sample.One", [("One.cs", "namespace Sample.One; public class Worker { public void Run() { } }")]),
            new ProjectSpec("Sample.Two", [("Two.cs", "namespace Sample.Two; public class Worker { public void Run() { } }")]));

        var payload = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(handle.Solution, "Run"));

        Assert.NotNull(payload);
        Assert.Equal(NavigationErrorCodes.AmbiguousSymbol, payload.Error?.Code);
        Assert.Equal(2, payload.ResolutionCandidates.Count);
        Assert.All(payload.ResolutionCandidates, candidate => Assert.StartsWith("h:", candidate.HandoffId));
        Assert.Equal(2, payload.ResolutionCandidates.Select(candidate => candidate.ProjectName).Distinct().Count());

        var selected = payload.ResolutionCandidates[0];
        var body = await SourceSymbolBodyResolver.ResolveAsync(handle.Solution, selected.HandoffId!, maxBodyLines: 20);
        var feature = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(handle.Solution, selected.HandoffId!));
        var structure = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(handle.Solution, selected.HandoffId!));

        Assert.Null(body.Error);
        Assert.NotNull(body.Body);
        Assert.NotNull(feature);
        Assert.Equal("Run", feature.Declaration.SymbolName);
        Assert.NotNull(structure);
        Assert.Contains("Worker", structure.TypeName);
    }

    [Fact]
    public void RenderMarkdown_RejectsNullPayload()
    {
        Assert.Throws<ArgumentNullException>(() => FeatureContextScanner.RenderMarkdown(null!));
    }

    [Fact]
    public async Task RenderMarkdown_FormatsCleanMarkdownWithoutViolationsOrMetrics()
    {
        const string testSource = "using System; namespace Xunit { public sealed class FactAttribute : Attribute { } } namespace Sample.Tests { public class GreeterTests { [Xunit.Fact] public void NameMatch() { } } public class GreeterTest { public void LooksLikeTest() { } } }";
        using var handle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\FeatureTestEvidence.slnx",
            new ProjectSpec("Sample.Core", [("Greeter.cs", "namespace Sample.Core; public class Greeter { }")]),
            new ProjectSpec("Sample.Tests", [("GreeterTests.cs", testSource)]));

        var payload = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(handle.Solution, "Sample.Core.Greeter"));
        Assert.NotNull(payload);
        Assert.Equal(2, payload.TotalTests);
        Assert.Equal(2, payload.Tests.Count);
        Assert.Equal(TestContextPayload.StaticTestCandidatesOnlyEvidenceMode, payload.EvidenceMode);
        var attributedCandidate = Assert.Single(payload.Tests, test => test.Framework == "xUnit" && test.TestMethod == "NameMatch");
        var nameOnlyCandidate = Assert.Single(payload.Tests, test => test.Framework == "Unknown" && test.TestMethod == "(all)");
        Assert.StartsWith("h:", attributedCandidate.HandoffId);
        Assert.StartsWith("h:", nameOnlyCandidate.HandoffId);

        var attributedBody = await SourceSymbolBodyResolver.ResolveAsync(
            handle.Solution,
            attributedCandidate.HandoffId!,
            maxBodyLines: 20);
        Assert.Null(attributedBody.Error);
        Assert.Contains("NameMatch", attributedBody.Body?.Body);

        var nameOnlyBody = await SourceSymbolBodyResolver.ResolveAsync(
            handle.Solution,
            nameOnlyCandidate.HandoffId!,
            maxBodyLines: 20);
        Assert.Null(nameOnlyBody.Error);
        Assert.Contains("GreeterTest", nameOnlyBody.Body?.Body);

        var markdown = FeatureContextScanner.RenderMarkdown(payload);

        Assert.Contains("# Feature Context: Greeter", markdown);
        Assert.Contains("## Incoming Callers", markdown);
        Assert.Contains("## Associated Tests", markdown);
        Assert.Contains("static heuristic candidates only", markdown);
        Assert.Contains("static-test-candidates-only", markdown);
        Assert.Contains("test execution and coverage are not verified", markdown);
        Assert.DoesNotContain("Violations", markdown);
        Assert.DoesNotContain("Metrics", markdown);
        Assert.DoesNotContain("QualityGate", markdown);
    }
}
