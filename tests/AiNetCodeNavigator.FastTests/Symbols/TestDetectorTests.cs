#nullable enable

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class TestDetectorTests
{
    [Theory]
    [InlineData("GreeterTests.cs", true)]
    [InlineData("GreeterTest.cs", true)]
    [InlineData("GreeterSpec.cs", true)]
    [InlineData("Latest.cs", false)]
    [InlineData("Contest.cs", false)]
    [InlineData("contest/Helpers.cs", false)]
    [InlineData("tests/Helpers.cs", true)]
    [InlineData("test/Helpers.cs", true)]
    [InlineData("src/tests/Helpers.cs", true)]
    [InlineData(".UnitTests/Runner.cs", true)]
    [InlineData("GreeterService.cs", false)]
    [InlineData("src/Services/Greeter.cs", false)]
    public void IsTestFile_DetectsTestPaths(string path, bool expected)
    {
        var result = TestDetector.IsTestFile(path);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsTestProject_DoesNotTreatOrdinaryWordEndingInTestAsTestProject()
    {
        using var handle = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("Contest", [("Service.cs", "public class Service { }\n")]));
        var project = handle.Solution.Projects.Single();

        Assert.False(TestDetector.IsTestProject(project));
    }

    [Fact]
    public void IsTestProject_DoesNotTreatMockingLibraryReferenceAsTestFramework()
    {
        var mockLibrary = CSharpCompilation.Create(
            "Moq",
            [CSharpSyntaxTree.ParseText("namespace Moq; public class Mock { }")],
            TestWorkspaceBuilder.CoreReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        Assert.True(mockLibrary.Emit(output).Success);
        var moqReference = MetadataReference.CreateFromImage(output.ToArray(), filePath: "Moq.dll");
        using var handle = TestWorkspaceBuilder.CreateSolution(
            new ProjectSpec("Sample.App", [("Service.cs", "public class Service { }")], AdditionalReferences: [moqReference]));

        Assert.False(TestDetector.IsTestProject(handle.Solution.Projects.Single()));
    }

    [Fact]
    public async Task IsTestClass_DoesNotTreatOrdinaryTypeNameEndingInTestAsTestClass()
    {
        using var handle = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("Sample.Core", [("Latest.cs", "public class Latest { }\n")]));
        var project = handle.Solution.Projects.Single();
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var type = compilation.GetTypeByMetadataName("Latest");
        Assert.NotNull(type);

        Assert.False(TestDetector.IsTestClass(type));
    }

    [Fact]
    public async Task TestRecommendationBuilderRejectsNullRequiredInputs()
    {
        using var handle = TestWorkspaceBuilder.CreateSolution("public class Target { }");
        var project = handle.Solution.Projects.Single();
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Target");
        Assert.NotNull(target);

        await Assert.ThrowsAsync<System.ArgumentNullException>(() => TestRecommendationBuilder.BuildAsync(null!, handle.Solution));
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => TestRecommendationBuilder.BuildAsync(target, null!));
    }

    [Fact]
    public async Task TestRecommendationBuilderRecognizesFrameworksAcrossProjects()
    {
        var production = new ProjectSpec(
            "Sample.Core",
            [("OrderService.cs", "namespace Sample.Core; public class OrderService { }")]);
        var xunit = new ProjectSpec(
            "Sample.XunitSuite",
            [("OrderServiceTests.cs", """
                using System;
                namespace Xunit { public sealed class FactAttribute : Attribute { } }
                namespace Sample.XunitSuite { public class OrderServiceTests { [Xunit.Fact] public void PlacesOrder() { } } }
                """)]);
        var nunit = new ProjectSpec(
            "Sample.NunitSuite",
            [("OrderServiceSpecs.cs", """
                using System;
                namespace NUnit.Framework { public sealed class TestCaseAttribute : Attribute { } }
                namespace Sample.NunitSuite { public class OrderServiceSpecs { [NUnit.Framework.TestCase] public void PlacesOrder() { } } }
                """)]);
        var mstest = new ProjectSpec(
            "Sample.MstestSuite",
            [("TestOrderService.cs", """
                using System;
                namespace Microsoft.VisualStudio.TestTools.UnitTesting { public sealed class TestMethodAttribute : Attribute { } }
                namespace Sample.MstestSuite { public class TestOrderService { [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod] public void PlacesOrder() { } } }
                """)]);

        using var handle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\FrameworkResolution.slnx", production, xunit, nunit, mstest);
        var compilation = await handle.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Sample.Core.OrderService");
        Assert.NotNull(target);

        var recommendation = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);

        Assert.Equal(3, recommendation.TotalTestFixtures);
        Assert.Collection(
            recommendation.TestFixtures.OrderBy(fixture => fixture.ClassName),
            fixture => Assert.Equal(("OrderServiceSpecs", "NUnit"), (fixture.ClassName, fixture.Framework)),
            fixture => Assert.Equal(("OrderServiceTests", "xUnit"), (fixture.ClassName, fixture.Framework)),
            fixture => Assert.Equal(("TestOrderService", "MSTest"), (fixture.ClassName, fixture.Framework)));

        var mstestFixture = Assert.Single(recommendation.TestFixtures, fixture => fixture.ClassName == "TestOrderService");
        var mstestMethod = Assert.Single(mstestFixture.Methods);
        Assert.Equal("PlacesOrder", mstestMethod.MethodName);
        Assert.NotNull(mstestMethod.HandoffId);
    }

    [Fact]
    public async Task TestRecommendationBuilder_PreservesSameNamedFixturesAcrossProjects()
    {
        var production = new ProjectSpec(
            "Sample.Core",
            [("OrderService.cs", "namespace Sample.Core; public class OrderService { }")]);
        var xunit = new ProjectSpec(
            "Sample.XunitSuite",
            [("OrderServiceTests.cs", "using System; namespace Xunit { public sealed class FactAttribute : Attribute { } } namespace Sample.XunitSuite { public class OrderServiceTests { [Xunit.Fact] public void PlacesOrder() { } } }")],
            VirtualProjectDirectory: "tests/xunit");
        var nunit = new ProjectSpec(
            "Sample.NunitSuite",
            [("OrderServiceTests.cs", "using System; namespace NUnit.Framework { public sealed class TestCaseAttribute : Attribute { } } namespace Sample.NunitSuite { public class OrderServiceTests { [NUnit.Framework.TestCase] public void CancelsOrder() { } } }")],
            VirtualProjectDirectory: "tests/nunit");
        using var handle = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\DuplicateTestFixtures.slnx", production, xunit, nunit);
        var compilation = await handle.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Sample.Core.OrderService");
        Assert.NotNull(target);

        var recommendation = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);

        Assert.Equal(2, recommendation.TotalTestFixtures);
        Assert.All(recommendation.TestFixtures, fixture => Assert.Equal("OrderServiceTests", fixture.ClassName));
        Assert.Equal(2, recommendation.TestFixtures.Select(fixture => fixture.FilePath).Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(2, recommendation.TestFixtures.Select(fixture => fixture.ProjectName).Distinct(System.StringComparer.Ordinal).Count());
        Assert.Equal(2, recommendation.TestFixtures.Select(fixture => fixture.ProjectIdentity).Distinct(System.StringComparer.Ordinal).Count());
        Assert.Equal(2, recommendation.TestFixtures.Select(fixture => fixture.HandoffId).Distinct().Count());
        Assert.All(recommendation.TestFixtures, fixture => Assert.Contains(fixture.Evidence!, evidence => evidence.EvidenceType == "target-name-heuristic"));
        Assert.Contains(recommendation.TestFixtures, fixture => fixture.Framework == "xUnit" && fixture.Methods.Single().MethodName == "PlacesOrder");
        Assert.Contains(recommendation.TestFixtures, fixture => fixture.Framework == "NUnit" && fixture.Methods.Single().MethodName == "CancelsOrder");
    }

    [Fact]
    public async Task TestRecommendationBuilderLabelsNameOnlyMatchesAsHeuristicWithUnknownFramework()
    {
        var production = new ProjectSpec(
            "Sample.Core",
            [("OrderService.cs", "namespace Sample.Core; public class OrderService { }")]);
        var candidate = new ProjectSpec(
            "Sample.TestCandidates",
            [("OrderServiceTests.cs", "namespace Sample.TestCandidates; public class OrderServiceTests { public void LooksLikeATest() { } }")]);
        using var handle = TestWorkspaceBuilder.CreateSolution(production, candidate);
        var compilation = await handle.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Sample.Core.OrderService");
        Assert.NotNull(target);

        var recommendation = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);

        var fixture = Assert.Single(recommendation.TestFixtures);
        Assert.Equal("OrderServiceTests", fixture.ClassName);
        Assert.Equal("Unknown", fixture.Framework);
        Assert.Empty(fixture.Methods);
        Assert.Equal("target-name-heuristic", Assert.Single(fixture.Evidence!).EvidenceType);
        Assert.Equal(TestContextPayload.StaticTestCandidatesOnlyEvidenceMode, recommendation.EvidenceMode);
    }

    [Fact]
    public async Task TestRecommendationBuilder_MatchesProductionClassToTestFixture()
    {
        const string prodSource = """
            namespace Sample.Core;
            public class OrderService
            {
                public void PlaceOrder() { }
            }
            """;

        const string testSource = """
            namespace Sample.Tests;
            public class OrderServiceTests
            {
                [Xunit.Fact]
                public void PlaceOrder_ShouldSucceed() { }
            }
            """;

        var prodProj = new ProjectSpec(
            Name: "Sample.Core",
            Documents: [("OrderService.cs", prodSource)]);

        var testProj = new ProjectSpec(
            Name: "Sample.Tests",
            Documents: [("OrderServiceTests.cs", testSource)],
            ProjectReferences: ["Sample.Core"]);

        using var handle = TestWorkspaceBuilder.CreateSolution(@"C:\Virtual\Solution.slnx", prodProj, testProj);
        var compilation = await handle.Solution.Projects.Single(p => p.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);

        var orderService = compilation.GetTypeByMetadataName("Sample.Core.OrderService");
        Assert.NotNull(orderService);

        var recommendation = await TestRecommendationBuilder.BuildAsync(orderService, handle.Solution);

        Assert.Equal("OrderService", recommendation.TargetSymbol);
        Assert.Single(recommendation.TestFixtures);
        var fixture = recommendation.TestFixtures[0];
        Assert.Equal("OrderServiceTests", fixture.ClassName);
        Assert.Contains(fixture.Methods, m => m.MethodName == "PlaceOrder_ShouldSucceed");
    }

    [Fact]
    public async Task TestRecommendationBuilder_FindsDirectContractUseInDifferentlyNamedTestMethod()
    {
        var production = new ProjectSpec(
            "Sample.Core",
            [("IOrderService.cs", "namespace Sample.Core; public interface IOrderService { void PlaceOrder(); }")]);
        var tests = new ProjectSpec(
            "Sample.Tests",
            [("OrderBehavior.cs", """
                using System;
                namespace Xunit { public sealed class FactAttribute : Attribute { } }
                namespace Sample.Tests
                {
                    public sealed class OrderBehavior
                    {
                        [Xunit.Fact]
                        public void SubmitsTheRequestedOrder(Sample.Core.IOrderService service) { service.PlaceOrder(); service.PlaceOrder(); }

                        [Xunit.Fact]
                        public void DoesNotUseTheContract() { }
                    }

                    public sealed class HelperPath
                    {
                        [Xunit.Fact]
                        public void CallsHelper(Sample.Core.IOrderService service) => ContractHelper.Use(service);
                    }

                    public static class ContractHelper
                    {
                        public static void Use(Sample.Core.IOrderService service) => service.PlaceOrder();
                    }
                }
                """)],
            ProjectReferences: ["Sample.Core"]);
        using var handle = TestWorkspaceBuilder.CreateSolution(production, tests);
        var compilation = await handle.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Sample.Core.IOrderService")?.GetMembers("PlaceOrder").OfType<IMethodSymbol>().Single();
        Assert.NotNull(target);
        var recommendation = await TestRecommendationBuilder.BuildAsync(target, handle.Solution, testHelperDepth: 0);

        var fixture = Assert.Single(recommendation.TestFixtures);
        Assert.Equal("OrderBehavior", fixture.ClassName);
        var testMethod = Assert.Single(fixture.Methods);
        Assert.Equal("SubmitsTheRequestedOrder", testMethod.MethodName);
        Assert.Equal(2, testMethod.Evidence!.Count);
        Assert.All(testMethod.Evidence, evidence => Assert.Equal("direct-target-use", evidence.EvidenceType));
        Assert.Equal(2, testMethod.Evidence.Select(evidence => evidence.Column).Distinct().Count());
        Assert.All(testMethod.Evidence, evidence => Assert.Equal("OrderBehavior.cs", evidence.FilePath));
        var withHelpers = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);
        var helperFixture = withHelpers.TestFixtures.Single(item => item.ClassName == "HelperPath");
        var helperEvidence = Assert.Single(Assert.Single(helperFixture.Methods).Evidence!);
        Assert.Equal("indirect-helper-use", helperEvidence.EvidenceType);
        Assert.Equal(2, helperEvidence.HelperPath!.Count);
        Assert.Equal(2, withHelpers.TestFixtures.Single(item => item.ClassName == "OrderBehavior").Methods.Single().Evidence!.Count);
    }

    [Fact]
    public async Task TestRecommendationBuilder_DoesNotTreatAmbiguousCandidateBindingAsDirectEvidence()
    {
        var production = new ProjectSpec("Sample.Core", [
            ("IOrderService.cs", "namespace Sample.Core { public interface IOrderService { void PlaceOrder(); } } namespace Sample.Other { public interface IOrderService { void PlaceOrder(); } }")]);
        var tests = new ProjectSpec("Sample.Tests", [
            ("AmbiguousBehavior.cs", "using System; using Sample.Core; using Sample.Other; namespace Xunit { public sealed class FactAttribute : Attribute { } } public sealed class AmbiguousBehavior { [Xunit.Fact] public void CallsAmbiguousService(IOrderService service) => service.PlaceOrder(); }")],
            ProjectReferences: ["Sample.Core"]);
        using var handle = TestWorkspaceBuilder.CreateSolution(production, tests);
        var compilation = await handle.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Sample.Core.IOrderService")?.GetMembers("PlaceOrder").OfType<IMethodSymbol>().Single();
        Assert.NotNull(target);
        var testProject = handle.Solution.Projects.Single(project => project.Name == "Sample.Tests");
        var testDocument = testProject.Documents.Single();
        var testModel = await testDocument.GetSemanticModelAsync();
        var testRoot = await testDocument.GetSyntaxRootAsync();
        var invocation = Assert.Single(testRoot!.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>());
        var memberAccess = Assert.IsType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax>(invocation.Expression);
        Assert.Equal("Error", testModel!.GetTypeInfo(memberAccess.Expression).Type?.TypeKind.ToString() ?? "null");
        Assert.Contains((await testProject.GetCompilationAsync())!.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0104");

        var recommendation = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);

        Assert.Empty(recommendation.TestFixtures);
    }

    [Fact]
    public async Task TestRecommendationBuilder_BindsTypePropertyAndEventReferencesAtTheirOwnSyntax()
    {
        var production = new ProjectSpec("Sample.Core", [
            ("Widget.cs", "using System; namespace Sample.Core { public interface IWidget { string Value { get; } event Action? Changed; void Run(); } public sealed class Widget : IWidget { public string Value => string.Empty; public event Action? Changed; public void Run() { } } }")]);
        var tests = new ProjectSpec("Sample.Tests", [
            ("FeatureScenarios.cs", "using System; using Sample.Core; namespace Xunit { public sealed class FactAttribute : Attribute { } } namespace Sample.Tests { public sealed class FeatureScenarios { [Xunit.Fact] public void UsesMembers() { var widget = new Widget(); widget.Run(); _ = new Widget().Value.ToString(); widget.Changed += static () => { }; } } }")],
            ProjectReferences: ["Sample.Core"]);
        using var handle = TestWorkspaceBuilder.CreateSolution(production, tests);
        var compilation = await handle.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var widgetType = compilation.GetTypeByMetadataName("Sample.Core.IWidget");
        Assert.NotNull(widgetType);
        var concreteType = compilation.GetTypeByMetadataName("Sample.Core.Widget");
        Assert.NotNull(concreteType);
        var targets = new ISymbol[]
        {
            widgetType,
            concreteType.InstanceConstructors.Single(),
            widgetType.GetMembers("Run").OfType<IMethodSymbol>().Single(),
            widgetType.GetMembers("Value").OfType<IPropertySymbol>().Single(),
            widgetType.GetMembers("Changed").OfType<IEventSymbol>().Single()
        };

        foreach (var target in targets)
        {
            var recommendation = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);
            Assert.True(recommendation.TestFixtures.Count > 0, $"No direct reference candidate for {target.Kind}:{target.Name}.");
            var fixture = Assert.Single(recommendation.TestFixtures);
            var method = Assert.Single(fixture.Methods);
            Assert.Equal("UsesMembers", method.MethodName);
            Assert.NotEmpty(method.Evidence!);
            Assert.All(method.Evidence!, evidence => Assert.True(evidence.EvidenceType is "direct-target-use" or "implementation-use"));
            var evidenceNeedle = target is INamedTypeSymbol or IMethodSymbol { MethodKind: MethodKind.Constructor }
                ? "Widget.Widget()"
                : target.Name;
            Assert.Contains(method.Evidence!, evidence => evidence.SourceSymbol.Contains(evidenceNeedle, StringComparison.Ordinal));
            Assert.All(method.Evidence!, evidence => Assert.Equal("FeatureScenarios.cs", evidence.FilePath));
        }
    }

    [Fact]
    public async Task TestRecommendationBuilder_DistinguishesImplementationUseFromImplementationNameHeuristic()
    {
        var production = new ProjectSpec(
            "Sample.Core",
            [("IOrderService.cs", "namespace Sample.Core; public interface IOrderService { void PlaceOrder(); } public sealed class DefaultOrderService : IOrderService { public void PlaceOrder() { } }")]);
        var tests = new ProjectSpec(
            "Sample.Tests",
            [("OrderBehavior.cs", """
                using System;
                namespace Xunit { public sealed class FactAttribute : Attribute { } }
                namespace Sample.Tests
                {
                    public sealed class ImplementationBehavior
                    {
                        [Xunit.Fact]
                        public void ExercisesImplementation() => new Sample.Core.DefaultOrderService().PlaceOrder();
                    }
                    public sealed class DefaultOrderServiceSpec { }
                }
                """)],
            ProjectReferences: ["Sample.Core"]);
        using var handle = TestWorkspaceBuilder.CreateSolution(production, tests);
        var compilation = await handle.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Sample.Core.IOrderService")?.GetMembers("PlaceOrder").OfType<IMethodSymbol>().Single();
        Assert.NotNull(target);

        var recommendation = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);

        var direct = Assert.Single(recommendation.TestFixtures, fixture => fixture.ClassName == "ImplementationBehavior");
        var directMethod = Assert.Single(direct.Methods);
        var implementationEvidence = Assert.Single(directMethod.Evidence!);
        Assert.Equal("implementation-use", implementationEvidence.EvidenceType);
        Assert.Contains("DefaultOrderService.PlaceOrder", implementationEvidence.SourceSymbol, StringComparison.Ordinal);
        var nameOnly = Assert.Single(recommendation.TestFixtures, fixture => fixture.ClassName == "DefaultOrderServiceSpec");
        Assert.Contains(nameOnly.Evidence!, evidence => evidence.EvidenceType == "implementation-type-name-heuristic");
        Assert.DoesNotContain(nameOnly.Methods, method => method.Evidence?.Any() == true);
    }

    [Fact]
    public async Task TestRecommendationBuilder_UsesSemanticProjectIdentityForDuplicateProductionTypeNames()
    {
        var projectA = new ProjectSpec("Sample.CoreA", [("Service.cs", "namespace Sample.A; public sealed class Service { public void Run() { } }")]);
        var projectB = new ProjectSpec("Sample.CoreB", [("Service.cs", "namespace Sample.B; public sealed class Service { public void Run() { } }")]);
        var testsA = new ProjectSpec("Sample.TestsA", [("UseA.cs", "using System; namespace Xunit { public sealed class FactAttribute : Attribute { } } public sealed class UseA { [Xunit.Fact] public void ExercisesA() => new Sample.A.Service().Run(); }")], ProjectReferences: ["Sample.CoreA"]);
        var testsB = new ProjectSpec("Sample.TestsB", [("UseB.cs", "using System; namespace Xunit { public sealed class FactAttribute : Attribute { } } public sealed class UseB { [Xunit.Fact] public void ExercisesB() => new Sample.B.Service().Run(); }")], ProjectReferences: ["Sample.CoreB"]);
        using var handle = TestWorkspaceBuilder.CreateSolution(projectA, projectB, testsA, testsB);
        var compilation = await handle.Solution.Projects.Single(project => project.Name == "Sample.CoreA").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Sample.A.Service")?.GetMembers("Run").OfType<IMethodSymbol>().Single();
        Assert.NotNull(target);

        var recommendation = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);

        var fixture = Assert.Single(recommendation.TestFixtures);
        Assert.Equal("UseA", fixture.ClassName);
        Assert.Equal("ExercisesA", Assert.Single(fixture.Methods).MethodName);
        Assert.Contains("Sample.TestsA", fixture.ProjectIdentity, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestRecommendationBuilder_ReportsBoundedImplementationExpansion()
    {
        var implementations = string.Join("\n", Enumerable.Range(0, TestRecommendationBuilder.MaxImplementationExpansion + 1)
            .Select(index => $"public sealed class Implementation{index:D3} : IContract {{ }}"));
        using var handle = TestWorkspaceBuilder.CreateSolution(new ProjectSpec(
            "Sample.Core",
            [("IContract.cs", $"namespace Sample.Core; public interface IContract {{ }} {implementations}")]));
        var compilation = await handle.Solution.Projects.Single().GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Sample.Core.IContract");
        Assert.NotNull(target);

        var recommendation = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);

        Assert.Equal(TestRecommendationBuilder.MaxImplementationExpansion, recommendation.ExpandedImplementationCount);
        Assert.True(recommendation.ImplementationExpansionLimitReached);
        Assert.False(recommendation.CandidateExpansionLimitReached);
        Assert.False(recommendation.ReferenceInspectionLimitReached);
    }

    [Fact]
    public async Task TestRecommendationBuilder_RetainsEveryCandidateReachedAtTheFixtureExpansionLimit()
    {
        var testTypes = string.Join("\n", Enumerable.Range(0, TestRecommendationBuilder.MaxCandidateFixtures + 2)
            .Select(index => $"public sealed class DirectTest{index:D3} {{ [Xunit.Fact] public void CallsTarget() => new Sample.Core.Target().Run(); }}"));
        using var handle = TestWorkspaceBuilder.CreateSolution(
            new ProjectSpec("Sample.Core", [("Target.cs", "namespace Sample.Core; public sealed class Target { public void Run() { } }")]),
            new ProjectSpec("Sample.Tests", [("DirectTests.cs", $"using System; namespace Xunit {{ public sealed class FactAttribute : Attribute {{ }} }} namespace Sample.Tests {{ {testTypes} }}")], ProjectReferences: ["Sample.Core"]));
        var compilation = await handle.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Sample.Core.Target")?.GetMembers("Run").OfType<IMethodSymbol>().Single();
        Assert.NotNull(target);

        var recommendation = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);

        Assert.Equal(TestRecommendationBuilder.MaxCandidateFixtures + 1, recommendation.TotalTestFixtures);
        Assert.True(recommendation.CandidateExpansionLimitReached);
        Assert.False(recommendation.ReferenceInspectionLimitReached);
        Assert.All(recommendation.TestFixtures, fixture => Assert.Single(fixture.Methods));
        Assert.All(recommendation.TestFixtures, fixture => Assert.All(fixture.Methods, method => Assert.NotEmpty(method.Evidence!)));
    }

    [Fact]
    public async Task TestRecommendationBuilder_ReportsReferenceInspectionLimitWithoutDroppingFoundEvidence()
    {
        // Keep semantic inspection in smaller method bodies while retaining all 4,097 reference locations needed to hit the limit.
        const int callsPerMethod = 64;
        var testMethods = string.Join("\n", Enumerable.Range(0, TestRecommendationBuilder.MaxReferenceLocations + 1)
            .Chunk(callsPerMethod)
            .Select((calls, index) =>
                $"[Xunit.Fact] public void CallsTarget{index:D3}(Sample.Core.Target target) {{ {string.Join(" ", Enumerable.Repeat("target.Run();", calls.Length))} }}"));
        using var handle = TestWorkspaceBuilder.CreateSolution(
            Path.Combine(Path.GetTempPath(), "TestRecommendationBuilder_ReferenceLimit.sln"),
            new ProjectSpec("Sample.Core", [("Target.cs", "namespace Sample.Core; public sealed class Target { public void Run() { } }")]),
            new ProjectSpec("Sample.Tests",
                [
                    ("ATargetChecks.cs", $"using System; namespace Xunit {{ public sealed class FactAttribute : Attribute {{ }} }} public sealed class TargetChecks {{ {testMethods} }}")
                ],
                ProjectReferences: ["Sample.Core"]));
        var compilation = await handle.Solution.Projects.Single(project => project.Name == "Sample.Core").GetCompilationAsync();
        Assert.NotNull(compilation);
        var target = compilation.GetTypeByMetadataName("Sample.Core.Target")?.GetMembers("Run").OfType<IMethodSymbol>().Single();
        Assert.NotNull(target);

        var recommendation = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);

        var fixture = Assert.Single(recommendation.TestFixtures);
        var expectedEvidenceMethodCount = (TestRecommendationBuilder.MaxReferenceLocations + callsPerMethod - 1) / callsPerMethod;
        var lastEvidenceMethodIndex = (TestRecommendationBuilder.MaxReferenceLocations - 1) / callsPerMethod;
        var methodPastLimitIndex = TestRecommendationBuilder.MaxReferenceLocations / callsPerMethod;
        Assert.Equal(expectedEvidenceMethodCount, fixture.Methods.Count);
        var allEvidence = fixture.Methods.SelectMany(method => method.Evidence!).ToArray();
        Assert.Equal(TestRecommendationBuilder.MaxReferenceLocations, allEvidence.Length);
        Assert.Equal(TestRecommendationBuilder.MaxReferenceLocations, allEvidence.Distinct().Count());
        var firstMethod = fixture.Methods.Single(method => method.MethodName == "CallsTarget000");
        var lastMethodWithEvidence = fixture.Methods.Single(method => method.MethodName == $"CallsTarget{lastEvidenceMethodIndex:D3}");
        Assert.Equal(callsPerMethod, firstMethod.Evidence!.Count);
        Assert.Equal("direct-target-use", firstMethod.Evidence!.First().EvidenceType);
        Assert.Equal(callsPerMethod, lastMethodWithEvidence.Evidence!.Count);
        Assert.Equal("direct-target-use", lastMethodWithEvidence.Evidence!.Last().EvidenceType);
        Assert.DoesNotContain(fixture.Methods, method => method.MethodName == $"CallsTarget{methodPastLimitIndex:D3}");
        Assert.True(recommendation.ReferenceInspectionLimitReached);
        Assert.False(recommendation.CandidateExpansionLimitReached);
    }
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 3)]
    [InlineData(2, 4)]
    public async Task TestRecommendationBuilder_HelperDepthPreservesShortestPathsAndRejectsNonCalls(int depth, int expectedMethods)
    {
        using var handle = TestWorkspaceBuilder.CreateSolution(Path.Combine(Path.GetTempPath(), "HelperDepth", "Sample.slnx"), new ProjectSpec("Sample.Tests", [("Behavior.cs", """
            using System;
            namespace Xunit { public sealed class FactAttribute : Attribute { } }
            namespace Sample {
                public static class Target { public static void Run() { } }
                public static class Helpers {
                    public static void One() { Target.Run(); Two(); }
                    public static void Two() => One();
                    public static void FinalNonCall() { Action value = Target.Run; }
                    public static void Ambiguous(string value) => Target.Run();
                    public static void Ambiguous(Uri value) => Target.Run();
                }
                public class Behavior {
                    [Xunit.Fact] public void ZDirect() { Target.Run(); Helpers.One(); }
                    [Xunit.Fact] public void OneHelper() => Helpers.One();
                    [Xunit.Fact] public void TwoHelpers() => Helpers.Two();
                    [Xunit.Fact] public void LastUseCanBeMethodGroup() => Helpers.FinalNonCall();
                    [Xunit.Fact] public void MethodGroupOnly() { Action value = Helpers.One; }
                    [Xunit.Fact] public void UnknownDelegate(Action value) => value();
                    [Xunit.Fact] public void CandidateBinding() => Helpers.Ambiguous(null);
                }
                public class TargetTests { [Xunit.Fact] public void NameOnly() { } }
            }
            """)]));
        var compilation = await handle.Solution.Projects.Single().GetCompilationAsync();
        var target = compilation!.GetTypeByMetadataName("Sample.Target")!.GetMembers("Run").Single();
        var result = await TestRecommendationBuilder.BuildAsync(target, handle.Solution, testHelperDepth: depth);
        var behavior = result.TestFixtures.Single(fixture => fixture.ClassName == "Behavior");
        Assert.Equal(expectedMethods, behavior.Methods.Count);
        Assert.Equal("ZDirect", behavior.Methods[0].MethodName);
        Assert.Equal("direct-target-use", behavior.Methods[0].Evidence![0].EvidenceType);
        Assert.DoesNotContain(behavior.Methods, method => method.MethodName == "MethodGroupOnly" || method.MethodName == "UnknownDelegate" || method.MethodName == "CandidateBinding");
        Assert.Equal("TargetTests", result.TestFixtures.Last().ClassName);
        Assert.Equal(handle.Solution.Projects.Single().FilePath, behavior.ProjectPath);
        Assert.Equal("Sample.Behavior", behavior.QualifiedName);
        Assert.All(behavior.Methods, method => {
            Assert.Contains("Sample.Behavior.", method.QualifiedName!, StringComparison.Ordinal);
            Assert.NotNull(method.HandoffId);
            Assert.NotNull(method.FilePath);
        });
        if (depth > 0)
        {
            var path = Assert.Single(behavior.Methods.Single(method => method.MethodName == "OneHelper").Evidence!).HelperPath!;
            Assert.Equal(2, path.Count);
            Assert.All(path, step => Assert.NotNull(step.CallerHandoffId));
            var nonCall = Assert.Single(behavior.Methods.Single(method => method.MethodName == "LastUseCanBeMethodGroup").Evidence!).HelperPath!;
            Assert.Equal(RelationshipEvidence.MemberAccess, nonCall.Last().RelationshipKind);
        }
        if (depth == 2)
        {
            var path = Assert.Single(behavior.Methods.Single(method => method.MethodName == "TwoHelpers").Evidence!).HelperPath!;
            Assert.Equal(3, path.Count);
            var shortest = behavior.Methods[0].Evidence!.Single(item => item.HelperPath is not null).HelperPath!;
            Assert.Equal(2, shortest.Count);
        }
        Assert.False(result.HelperExpansionLimitReached);
        Assert.False(result.ReferenceInspectionLimitReached);
    }

    [Fact]
    public async Task TestRecommendationBuilder_HelperCallsKeepStaticDispatchAndConstructorEvidence()
    {
        using var handle = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("Sample.Tests", [("Behavior.cs", """
            using System;
            namespace Xunit { public sealed class FactAttribute : Attribute { } }
            namespace Sample {
                public static class Target { public static int Value; }
                public class Helper {
                    public Helper() { _ = Target.Value; }
                    public virtual void Use() { _ = Target.Value; }
                }
                public class Behavior {
                    [Xunit.Fact] public void StaticTarget(Helper helper) => helper.Use();
                    [Xunit.Fact] public void Constructor() => _ = new Helper();
                    [Xunit.Fact] public void NonCall(Helper helper) { Action value = helper.Use; }
                }
            }
            """)]));
        var compilation = await handle.Solution.Projects.Single().GetCompilationAsync();
        var target = compilation!.GetTypeByMetadataName("Sample.Target")!.GetMembers("Value").Single();
        var result = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);
        var methods = Assert.Single(result.TestFixtures).Methods;
        Assert.Equal(2, methods.Count);
        var virtualPath = Assert.Single(methods.Single(method => method.MethodName == "StaticTarget").Evidence!).HelperPath!;
        Assert.Equal(RelationshipEvidence.StaticVirtualOrInterfaceTarget, virtualPath[0].RelationshipKind);
        Assert.NotNull(virtualPath[0].DispatchLimitation);
        Assert.Equal(RelationshipEvidence.MemberAccess, virtualPath[1].RelationshipKind);
        var constructorPath = Assert.Single(methods.Single(method => method.MethodName == "Constructor").Evidence!).HelperPath!;
        Assert.Equal(RelationshipEvidence.Call, constructorPath[0].RelationshipKind);
    }

    [Fact]
    public async Task TestRecommendationBuilder_HelperLimitsAreSharedAcrossContractAndImplementationSeeds()
    {
        using var handle = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("Sample.Tests", [("Behavior.cs", """
            using System;
            namespace Xunit { public sealed class FactAttribute : Attribute { } }
            namespace Sample {
                public interface ITarget { void Run(); }
                public class Target : ITarget { public void Run() { } }
                public static class Helpers {
                    public static void A(ITarget target) => target.Run();
                    public static void B() => new Target().Run();
                    public static void C() => new Target().Run();
                }
                public class Behavior {
                    [Xunit.Fact] public void Direct(ITarget target) => target.Run();
                    [Xunit.Fact] public void ViaA(ITarget target) => Helpers.A(target);
                    [Xunit.Fact] public void ViaB() => Helpers.B();
                    [Xunit.Fact] public void ViaC() => Helpers.C();
                }
            }
            """)]));
        var compilation = await handle.Solution.Projects.Single().GetCompilationAsync();
        var target = compilation!.GetTypeByMetadataName("Sample.ITarget")!.GetMembers("Run").Single();
        var capped = await TestRecommendationBuilder.BuildWithLimitsAsync(target, handle.Solution, 1, 2, 4096);
        Assert.Equal(2, capped.ExpandedHelperCount);
        Assert.True(capped.HelperExpansionLimitReached);
        Assert.False(capped.ReferenceInspectionLimitReached);
        Assert.Contains(Assert.Single(capped.TestFixtures).Methods, method => method.MethodName == "Direct");
        var referenceCapped = await TestRecommendationBuilder.BuildWithLimitsAsync(target, handle.Solution, 1, 200, 2);
        Assert.True(referenceCapped.ReferenceInspectionLimitReached);
        Assert.Contains(Assert.Single(referenceCapped.TestFixtures).Methods, method => method.MethodName == "Direct");
        var full = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);
        Assert.Equal(4, Assert.Single(full.TestFixtures).Methods.Count);
        Assert.False(full.HelperExpansionLimitReached);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task TestRecommendationBuilder_RejectsInvalidHelperDepth(int depth)
    {
        using var handle = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("Sample.Core", [("Target.cs", "public class Target { }")]));
        var compilation = await handle.Solution.Projects.Single().GetCompilationAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => TestRecommendationBuilder.BuildAsync(
            compilation!.GetTypeByMetadataName("Target")!, handle.Solution, testHelperDepth: depth));
    }

    [Fact]
    public async Task TestRecommendationBuilder_EqualHelperNamesRetainSourceProjectOwners()
    {
        const string helper = "public static class Helpers { public static void Use() => Target.Run(); }";
        const string test = "using System; namespace Xunit { public sealed class FactAttribute : Attribute { } } public class Behavior { [Xunit.Fact] public void ViaHelper() => Helpers.Use(); }";
        using var handle = TestWorkspaceBuilder.CreateSolution(Path.Combine(Path.GetTempPath(), "HelperOwners", "Sample.slnx"),
            new ProjectSpec("Core", [("Target.cs", "public static class Target { public static void Run() { } }")]),
            new ProjectSpec("First", [("Helper.cs", helper)], ProjectReferences: ["Core"]),
            new ProjectSpec("Second", [("Helper.cs", helper)], ProjectReferences: ["Core"]),
            new ProjectSpec("First.Tests", [("Behavior.cs", test)], ProjectReferences: ["First"]),
            new ProjectSpec("Second.Tests", [("Behavior.cs", test)], ProjectReferences: ["Second"]));
        var compilation = await handle.Solution.Projects.Single(project => project.Name == "Core").GetCompilationAsync();
        var target = compilation!.GetTypeByMetadataName("Target")!.GetMembers("Run").Single();
        var result = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);
        Assert.Equal(2, result.TestFixtures.Count);
        Assert.Equal(2, result.ExpandedHelperCount);
        var paths = result.TestFixtures.Select(fixture => Assert.Single(Assert.Single(fixture.Methods).Evidence!).HelperPath!).ToArray();
        Assert.Equal(2, paths.Select(path => path[1].ProjectPath).Distinct().Count());
        Assert.Equal(2, paths.Select(path => path[1].CallerHandoffId).Distinct().Count());
        Assert.All(paths, path => Assert.Equal(RelationshipEvidence.Call, path[0].RelationshipKind));
    }

    [Fact]
    public async Task TestRecommendationBuilder_SortsEqualEvidenceFixturesBySourceLineBeforeName()
    {
        const string source = """
            namespace Xunit { public class FactAttribute : System.Attribute { } }
            public static class Endpoint { public static void Run() { } }
            public class ZEarlier { [Xunit.Fact] public void Check() => Endpoint.Run(); }
            public class ALater { [Xunit.Fact] public void Check() => Endpoint.Run(); }
            """;
        using var handle = TestWorkspaceBuilder.CreateSolution(Path.Combine(Path.GetTempPath(), "FixtureOrdering.sln"),
            new ProjectSpec("Ordering.Tests", [("Tests.cs", source)]));
        var compilation = await handle.Solution.Projects.Single().GetCompilationAsync();
        var target = compilation!.GetTypeByMetadataName("Endpoint")!.GetMembers("Run").Single();
        var result = await TestRecommendationBuilder.BuildAsync(target, handle.Solution, testHelperDepth: 0);
        Assert.Equal(new[] { "ZEarlier", "ALater" }, result.TestFixtures.Select(fixture => fixture.ClassName));
        Assert.All(result.TestFixtures, fixture => Assert.Equal("direct-target-use", Assert.Single(Assert.Single(fixture.Methods).Evidence!).EvidenceType));
        Assert.Equal(2, result.TestFixtures.Select(fixture => fixture.HandoffId).Distinct().Count());
        Assert.All(result.TestFixtures, fixture => Assert.StartsWith("src:", fixture.HandoffId));
    }

    [Fact]
    public async Task TestRecommendationBuilder_SortsPartialFixtureMethodsByOwnFileLineColumnBeforeName()
    {
        const string header = "namespace Xunit { public class FactAttribute : System.Attribute { } } public static class Endpoint { public static void Run() {} } public partial class Behavior {";
        var first = header + new string('\n', 19) + "[Xunit.Fact] public void ZEarlier() => Endpoint.Run(); [Xunit.Fact] public void ALater() => Endpoint.Run(); }";
        const string second = "public partial class Behavior {\n\n[Xunit.Fact] public void BFile() => Endpoint.Run(); }";
        using var handle = TestWorkspaceBuilder.CreateSolution(Path.Combine(Path.GetTempPath(), "MethodOrdering.sln"),
            new ProjectSpec("Ordering.Tests", [("A.cs", first), ("B.cs", second)]));
        var compilation = await handle.Solution.Projects.Single().GetCompilationAsync();
        var target = compilation!.GetTypeByMetadataName("Endpoint")!.GetMembers("Run").Single();
        var result = await TestRecommendationBuilder.BuildAsync(target, handle.Solution, testHelperDepth: 0);
        var methods = Assert.Single(result.TestFixtures).Methods;
        Assert.Equal(new[] { "ZEarlier", "ALater", "BFile" }, methods.Select(method => method.MethodName));
        Assert.Equal(new[] { "Ordering.Tests/A.cs", "Ordering.Tests/A.cs", "Ordering.Tests/B.cs" }, methods.Select(method => method.FilePath));
        Assert.Equal(new[] { 20, 20, 3 }, methods.Select(method => method.Line));
        Assert.True(methods[0].Column < methods[1].Column);
        Assert.All(methods, method => Assert.Equal("direct-target-use", Assert.Single(method.Evidence!).EvidenceType));
        Assert.Equal(3, methods.Select(method => method.HandoffId).Distinct().Count());
        Assert.All(methods, method => Assert.StartsWith("src:", method.HandoffId));
    }
}
