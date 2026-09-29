#nullable enable

using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class TestDetectorTests
{
    [Theory]
    [InlineData("GreeterTests.cs", true)]
    [InlineData("GreeterTest.cs", true)]
    [InlineData("GreeterSpec.cs", true)]
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
