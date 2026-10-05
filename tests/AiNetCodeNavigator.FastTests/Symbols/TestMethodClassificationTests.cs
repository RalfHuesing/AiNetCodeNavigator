#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class TestMethodClassificationTests
{
    [Theory]
    [InlineData("[Fact]", "", TestFrameworkKind.Xunit, TestActivityStatus.Active)]
    [InlineData("[Theory]", "", TestFrameworkKind.Xunit, TestActivityStatus.Active)]
    [InlineData("[ProjectFact]", "", TestFrameworkKind.Xunit, TestActivityStatus.Active)]
    [InlineData("[CustomTheory]", "", TestFrameworkKind.Xunit, TestActivityStatus.Active)]
    [InlineData("[Xunit.v3.InterfaceFact]", "", TestFrameworkKind.Xunit, TestActivityStatus.Active)]
    [InlineData("[Xunit.v3.DerivedInterfaceFact]", "", TestFrameworkKind.Xunit, TestActivityStatus.Active)]
    [InlineData("[Fact(Skip = \"reason\")]", "", TestFrameworkKind.Xunit, TestActivityStatus.Excluded)]
    [InlineData("[Fact(\"reason\")]", "", TestFrameworkKind.Xunit, TestActivityStatus.Excluded)]
    [InlineData("[Fact(Skip = null)]", "", TestFrameworkKind.Xunit, TestActivityStatus.Active)]
    [InlineData("[Fact(Explicit = true)]", "", TestFrameworkKind.Xunit, TestActivityStatus.Excluded)]
    [InlineData("[Fact(SkipWhen = \"Condition\")]", "", TestFrameworkKind.Xunit, TestActivityStatus.Conditional)]
    [InlineData("[Fact(SkipUnless = \"Condition\", Skip = \"reason\")]", "", TestFrameworkKind.Xunit, TestActivityStatus.Conditional)]
    [InlineData("[Fact(Explicit = true, SkipWhen = \"Condition\")]", "", TestFrameworkKind.Xunit, TestActivityStatus.Excluded)]
    [InlineData("[Fact(Skip = \"reason\"), Theory]", "", TestFrameworkKind.Xunit, TestActivityStatus.Active)]
    [InlineData("[Xunit.v3.InterfaceFact(Skip = \"reason\")]", "", TestFrameworkKind.Xunit, TestActivityStatus.Excluded)]
    [InlineData("[Test]", "", TestFrameworkKind.NUnit, TestActivityStatus.Active)]
    [InlineData("[CustomTest]", "", TestFrameworkKind.NUnit, TestActivityStatus.Active)]
    [InlineData("[CustomTestCase]", "", TestFrameworkKind.NUnit, TestActivityStatus.Active)]
    [InlineData("[CustomTestCaseSource]", "", TestFrameworkKind.NUnit, TestActivityStatus.Active)]
    [InlineData("[TestCase(Ignore = \"row\")]", "", TestFrameworkKind.NUnit, TestActivityStatus.Conditional)]
    [InlineData("[TestCase(Explicit = true)]", "", TestFrameworkKind.NUnit, TestActivityStatus.Conditional)]
    [InlineData("[TestCase(Explicit = true), TestCase(1)]", "", TestFrameworkKind.NUnit, TestActivityStatus.Active)]
    [InlineData("[Test, NUnit.Framework.CustomIgnore]", "", TestFrameworkKind.NUnit, TestActivityStatus.Excluded)]
    [InlineData("[Test, NUnit.Framework.CustomExplicit]", "", TestFrameworkKind.NUnit, TestActivityStatus.Excluded)]
    [InlineData("[Test]", "[CustomTestFixture(Ignore = \"fixture\")]", TestFrameworkKind.NUnit, TestActivityStatus.Excluded)]
    [InlineData("[Test]", "[TestFixture(Ignore = \"\")]", TestFrameworkKind.NUnit, TestActivityStatus.Active)]
    [InlineData("[Test]", "[CustomTestFixture(Explicit = true)]", TestFrameworkKind.NUnit, TestActivityStatus.Excluded)]
    [InlineData("[Test]", "[TestFixture(Ignore = \"fixture\"), TestFixture]", TestFrameworkKind.NUnit, TestActivityStatus.Conditional)]
    [InlineData("[Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]", "", null, null)]
    [InlineData("[Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]", "[Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]", TestFrameworkKind.MSTest, TestActivityStatus.Active)]
    [InlineData("[Microsoft.VisualStudio.TestTools.UnitTesting.CustomDataTestMethod]", "[Microsoft.VisualStudio.TestTools.UnitTesting.CustomTestClass]", TestFrameworkKind.MSTest, TestActivityStatus.Active)]
    [InlineData("[Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethod, Microsoft.VisualStudio.TestTools.UnitTesting.CustomIgnore]", "[Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]", TestFrameworkKind.MSTest, TestActivityStatus.Excluded)]
    [InlineData("[Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]", "[Microsoft.VisualStudio.TestTools.UnitTesting.TestClass, Microsoft.VisualStudio.TestTools.UnitTesting.Ignore]", TestFrameworkKind.MSTest, TestActivityStatus.Excluded)]
    [InlineData("[Unrelated.Fact]", "", null, null)]
    [InlineData("", "[TestFixture]", null, null)]
    public async Task ClassifyTestMethod_SeparatesFrameworkIdentityFromActivity(
        string attributes, string fixtureAttributes, TestFrameworkKind? framework, TestActivityStatus? status)
    {
        var source = $$"""
            using Xunit;
            using NUnit.Framework;
            public class ProjectFactAttribute : Xunit.FactAttribute { }
            namespace Unrelated { public class FactAttribute : System.Attribute { } }
            {{fixtureAttributes}}
            public class Behavior { {{attributes}} public void Check() { } }
            """;
        using var handle = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("Neutral", [("Behavior.cs", source)],
            AdditionalReferences: [TestFrameworkReferences.Reference]));
        var compilation = await handle.Solution.Projects.Single().GetCompilationAsync();
        Assert.Empty(compilation!.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var method = compilation.GetTypeByMetadataName("Behavior")!.GetMembers("Check").OfType<IMethodSymbol>().Single();

        var classification = TestDetector.ClassifyTestMethod(method);

        Assert.Equal(framework, classification?.Framework);
        Assert.Equal(status, classification?.ActivityStatus);
        Assert.Equal(framework is not null, TestDetector.IsTestMethod(method));
    }

    [Fact]
    public async Task ClassifyTestMethod_ExcludesNestedTestsInsideIgnoredFixture()
    {
        using var handle = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("Neutral", [("Behavior.cs", """
            [NUnit.Framework.Ignore]
            public class Outer { public class Inner { [NUnit.Framework.Test] public void Check() { } } }
            """)], AdditionalReferences: [TestFrameworkReferences.Reference]));
        var compilation = await handle.Solution.Projects.Single().GetCompilationAsync();
        var method = compilation!.GetTypeByMetadataName("Outer+Inner")!.GetMembers("Check").OfType<IMethodSymbol>().Single();

        Assert.Equal(TestActivityStatus.Excluded, TestDetector.ClassifyTestMethod(method)!.ActivityStatus);
        Assert.True(TestDetector.IsTestMethod(method));
    }

    [Fact]
    public async Task TestCandidates_RetainExcludedTestsAndReportDerivedFrameworkAndActivity()
    {
        using var handle = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("Neutral", [("Behavior.cs", """
            public class ProjectFactAttribute : Xunit.FactAttribute { }
            public static class Endpoint { public static void Run() { } }
            public class Behavior {
                [ProjectFact] public void Active() => Endpoint.Run();
                [ProjectFact(Skip = "reason")] public void Excluded() => Endpoint.Run();
                [ProjectFact(SkipWhen = "Condition")] public void Conditional() => Endpoint.Run();
                public void Helper() => Endpoint.Run();
            }
            """)], AdditionalReferences: [TestFrameworkReferences.Reference]));
        var compilation = await handle.Solution.Projects.Single().GetCompilationAsync();
        var target = compilation!.GetTypeByMetadataName("Endpoint")!.GetMembers("Run").Single();

        var result = await TestRecommendationBuilder.BuildAsync(target, handle.Solution);

        var fixture = Assert.Single(result.TestFixtures);
        Assert.Equal("xUnit", fixture.Framework);
        Assert.Equal(new[] { TestActivityStatus.Active, TestActivityStatus.Excluded, TestActivityStatus.Conditional },
            fixture.Methods.Select(method => method.ActivityStatus));
        Assert.DoesNotContain(fixture.Methods, method => method.MethodName == "Helper");
        Assert.All(fixture.Methods, method => Assert.Equal("direct-target-use", Assert.Single(method.Evidence!).EvidenceType));
    }
}
