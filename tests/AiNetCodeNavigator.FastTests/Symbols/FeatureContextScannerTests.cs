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

        Assert.Null(missing);
        Assert.NotNull(invalidHandoff);
        Assert.Equal(NavigationErrorCodes.HandoffUnknown, invalidHandoff.Error?.Code);
    }

    [Fact]
    public void RenderMarkdown_RejectsNullPayload()
    {
        Assert.Throws<ArgumentNullException>(() => FeatureContextScanner.RenderMarkdown(null!));
    }

    [Fact]
    public async Task RenderMarkdown_FormatsCleanMarkdownWithoutViolationsOrMetrics()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new FeatureContextRequest(
            Solution: fixture.Solution,
            SymbolIdentifier: "Greeter");

        var payload = await FeatureContextScanner.ScanAsync(request);
        Assert.NotNull(payload);

        var markdown = FeatureContextScanner.RenderMarkdown(payload);

        Assert.Contains("# Feature Context: Greeter", markdown);
        Assert.Contains("## Incoming Callers", markdown);
        Assert.Contains("## Associated Tests", markdown);
        Assert.DoesNotContain("Violations", markdown);
        Assert.DoesNotContain("Metrics", markdown);
        Assert.DoesNotContain("QualityGate", markdown);
    }
}
