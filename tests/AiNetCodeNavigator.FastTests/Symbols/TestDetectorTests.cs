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

        using var handle = TestWorkspaceBuilder.CreateSolution(production, xunit, nunit, mstest);
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
}
