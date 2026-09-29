#nullable enable

namespace AiNetCodeNavigator.FastTests.TestKit;

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.TestKit.Assertions;
using AiNetCodeNavigator.TestKit.Builders;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Xunit;

public class TestKitInfrastructureTests
{
    [Fact]
    public void TestWorkspaceBuilder_CreatesValidSingleProjectSolution()
    {
        using var solutionHandle = TestWorkspaceBuilder.CreateSolution(
            "public class Demo { public int Val => 42; }",
            projectName: "DemoProj",
            docName: "Demo.cs");

        Assert.NotNull(solutionHandle);
        Assert.NotNull(solutionHandle.Solution);
        Assert.Single(solutionHandle.Solution.Projects);

        var project = solutionHandle.Solution.Projects.First();
        Assert.Equal("DemoProj", project.Name);
        Assert.Single(project.Documents);
        Assert.Equal("Demo.cs", project.Documents.First().Name);
    }

    [Fact]
    public async Task TestWorkspaceBuilder_ProducesWorkingCompilation()
    {
        using var solutionHandle = TestWorkspaceBuilder.CreateSolution(
            "public class Worker { public string Work() => \"Done\"; }");

        var project = solutionHandle.Solution.Projects.First();
        var compilation = await project.GetCompilationAsync();

        Assert.NotNull(compilation);
        var typeSymbol = compilation.GetTypeByMetadataName("Worker");
        NavigationAssertions.AssertSymbolName(typeSymbol, "Worker");
    }

    [Fact]
    public async Task SampleCodeFixtures_CreatesStandardMultiProjectSolution()
    {
        using var solutionHandle = SampleCodeFixtures.CreateStandardTestSolution();

        Assert.Equal(2, solutionHandle.Solution.Projects.Count());

        var coreProj = solutionHandle.Solution.Projects.First(p => p.Name == "Sample.Core");
        var appProj = solutionHandle.Solution.Projects.First(p => p.Name == "Sample.App");

        Assert.Single(appProj.ProjectReferences);
        Assert.Equal(coreProj.Id, appProj.ProjectReferences.First().ProjectId);

        var coreCompilation = await coreProj.GetCompilationAsync();
        Assert.NotNull(coreCompilation);

        var greeterSymbol = coreCompilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        NavigationAssertions.AssertSymbolName(greeterSymbol, "Greeter");

        var processorSymbol = coreCompilation.GetTypeByMetadataName("SampleNamespace.Hierarchy.IProcessor");
        NavigationAssertions.AssertSymbolName(processorSymbol, "IProcessor");
    }

    [Theory]
    [InlineData("h:gwtQ")]
    [InlineData("h:abc-123")]
    [InlineData("h:foo_bar")]
    public void NavigationAssertions_ValidHandoff_Passes(string handoffId)
    {
        NavigationAssertions.AssertValidHandoffId(handoffId);
    }

    [Theory]
    [InlineData("gwtQ")]
    [InlineData("")]
    [InlineData("h:")]
    [InlineData("x:123")]
    public void NavigationAssertions_InvalidHandoff_Fails(string invalidHandoff)
    {
        Assert.ThrowsAny<Exception>(() => NavigationAssertions.AssertValidHandoffId(invalidHandoff));
    }
}
