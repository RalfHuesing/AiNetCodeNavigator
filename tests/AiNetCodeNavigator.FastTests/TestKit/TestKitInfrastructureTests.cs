#nullable enable

namespace AiNetCodeNavigator.FastTests.TestKit;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.TestKit.Assertions;
using AiNetCodeNavigator.TestKit.Builders;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

public sealed class TestKitInfrastructureTests
{
    [Fact]
    public async Task TestWorkspaceBuilder_CreatesSyntaxTreeAndWorkingCompilation()
    {
        using var solutionHandle = TestWorkspaceBuilder.CreateSolution(
            "public class Worker { public string Work() => \"Done\"; }",
            projectName: "DemoProj",
            docName: "Demo.cs");

        var project = Assert.Single(solutionHandle.Solution.Projects);
        var document = Assert.Single(project.Documents);
        var syntaxTree = await document.GetSyntaxTreeAsync();
        var compilation = await project.GetCompilationAsync();

        Assert.Equal(solutionHandle.Solution.Id, solutionHandle.Workspace.CurrentSolution.Id);
        Assert.Equal(solutionHandle.Solution.ProjectIds, solutionHandle.Workspace.CurrentSolution.ProjectIds);
        Assert.Equal("DemoProj", project.Name);
        Assert.Equal("Demo.cs", document.Name);
        Assert.NotNull(syntaxTree);
        Assert.Contains("class Worker", syntaxTree.ToString(), StringComparison.Ordinal);
        Assert.NotNull(compilation);
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        NavigationAssertions.AssertSymbolName(compilation.GetTypeByMetadataName("Worker"), "Worker");
    }

    [Fact]
    public async Task TestWorkspaceBuilder_ProjectReferenceResolvesSymbolsAcrossProjects()
    {
        using var solutionHandle = TestWorkspaceBuilder.Create()
            .WithProject(new ProjectSpec("Provider", [("Gadget.cs", "namespace Widgets; public class Gadget {}")]))
            .WithProject(new ProjectSpec(
                "Consumer",
                [("Consumer.cs", "namespace Widgets.Consumers; public class Consumer { public Widgets.Gadget? Field; }")],
                ProjectReferences: ["Provider"]))
            .Build();

        var provider = solutionHandle.Solution.Projects.Single(project => project.Name == "Provider");
        var consumer = solutionHandle.Solution.Projects.Single(project => project.Name == "Consumer");
        var compilation = await consumer.GetCompilationAsync();

        Assert.Equal(provider.Id, Assert.Single(consumer.ProjectReferences).ProjectId);
        var workspaceConsumer = solutionHandle.Workspace.CurrentSolution.GetProject(consumer.Id);
        Assert.NotNull(workspaceConsumer);
        Assert.Equal(consumer.DocumentIds, workspaceConsumer.DocumentIds);
        Assert.Equal(consumer.ProjectReferences, workspaceConsumer.ProjectReferences);
        Assert.NotNull(compilation);
        NavigationAssertions.AssertSymbolName(compilation.GetTypeByMetadataName("Widgets.Gadget"), "Gadget");
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task TestWorkspaceBuilder_AppliesNullableAndPreprocessorOptions()
    {
        const string source = """
            #if PROBE_SYMBOL
            public class ConditionalType
            {
                public string Get()
                {
                    string? maybe = null;
                    string value = maybe;
                    return value;
                }
            }
            #endif
            """;

        using var enabled = TestWorkspaceBuilder.CreateSolution(
            new ProjectSpec("Enabled", [("Probe.cs", source)], Nullable: NullableContextOptions.Enable, PreprocessorSymbols: ["PROBE_SYMBOL"]));
        using var disabled = TestWorkspaceBuilder.CreateSolution(
            new ProjectSpec("Disabled", [("Probe.cs", source)], Nullable: NullableContextOptions.Disable));

        var enabledCompilation = await enabled.Solution.Projects.Single().GetCompilationAsync();
        var disabledCompilation = await disabled.Solution.Projects.Single().GetCompilationAsync();

        Assert.NotNull(enabledCompilation);
        Assert.NotNull(disabledCompilation);
        Assert.NotNull(enabledCompilation.GetTypeByMetadataName("ConditionalType"));
        Assert.Null(disabledCompilation.GetTypeByMetadataName("ConditionalType"));
        Assert.Contains(enabledCompilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS8600");
        Assert.DoesNotContain(disabledCompilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS8600");
    }

    [Fact]
    public void TestWorkspaceBuilder_ReusesCoreMetadataReferenceInstances()
    {
        using var first = TestWorkspaceBuilder.CreateSolution("public class FirstType {}");
        using var second = TestWorkspaceBuilder.CreateSolution("public class SecondType {}");

        var corlibLocation = typeof(object).Assembly.Location;
        var firstReference = FindCoreReference(first.Solution, corlibLocation);
        var secondReference = FindCoreReference(second.Solution, corlibLocation);

        Assert.Same(firstReference, secondReference);
    }

    [Fact]
    public void TestWorkspaceBuilder_UnknownProjectReferenceNamesMissingProject()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            TestWorkspaceBuilder.CreateSolution(
                new ProjectSpec("Consumer", [("Consumer.cs", "public class Consumer {}")], ProjectReferences: ["MissingProject"])));

        Assert.Contains("MissingProject", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TestWorkspaceBuilder_RejectsDuplicateProjectNames()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            TestWorkspaceBuilder.CreateSolution(
                new ProjectSpec("Shared", [("First.cs", "public class First {}")]),
                new ProjectSpec("Shared", [("Second.cs", "public class Second {}")])));

        Assert.Contains("Shared", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TestWorkspaceBuilder_RejectsNullProjectArrayAndEntries()
    {
        Assert.Throws<ArgumentNullException>(() => TestWorkspaceBuilder.CreateSolution((ProjectSpec[])null!));
        Assert.Throws<ArgumentNullException>(() => TestWorkspaceBuilder.CreateSolution([null!]));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void TestWorkspaceBuilder_RejectsBlankProjectNames(string projectName)
    {
        Assert.Throws<ArgumentException>(() =>
            TestWorkspaceBuilder.CreateSolution(new ProjectSpec(projectName, [])));
    }

    [Fact]
    public void TestWorkspaceBuilder_RejectsNullDocumentList()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TestWorkspaceBuilder.CreateSolution(new ProjectSpec("Project", null!)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Nested/")]
    public void TestWorkspaceBuilder_RejectsInvalidDocumentFileNames(string fileName)
    {
        Assert.Throws<ArgumentException>(() =>
            TestWorkspaceBuilder.CreateSolution(new ProjectSpec("Project", [(fileName, "public class Probe {}")])));
    }

    [Fact]
    public void TestWorkspaceBuilder_RejectsNullDocumentContent()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TestWorkspaceBuilder.CreateSolution(new ProjectSpec("Project", [("Probe.cs", null!)])));
    }

    [Fact]
    public void TestWorkspaceBuilder_RejectsNullAdditionalMetadataReference()
    {
        Assert.Throws<ArgumentException>(() =>
            TestWorkspaceBuilder.CreateSolution(new ProjectSpec(
                "Project",
                [("Probe.cs", "public class Probe {}")],
                AdditionalReferences: [null!])));
    }

    [Fact]
    public void TestWorkspaceBuilder_RejectsDuplicateProjectReferences()
    {
        Assert.Throws<ArgumentException>(() =>
            TestWorkspaceBuilder.CreateSolution(
                new ProjectSpec("Provider", []),
                new ProjectSpec("Consumer", [], ProjectReferences: ["Provider", "Provider"])));
    }

    [Fact]
    public void TestWorkspaceBuilder_RejectsNullOrBlankProjectReferenceNames()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TestWorkspaceBuilder.CreateSolution(
                new ProjectSpec("Provider", []),
                new ProjectSpec("Consumer", [], ProjectReferences: [null!])));
        Assert.Throws<ArgumentException>(() =>
            TestWorkspaceBuilder.CreateSolution(
                new ProjectSpec("Provider", []),
                new ProjectSpec("Consumer", [], ProjectReferences: [" "])));
    }

    [Fact]
    public void TestWorkspaceBuilder_FluentMethodsValidateInputs()
    {
        Assert.Throws<ArgumentException>(() => TestWorkspaceBuilder.Create().WithVirtualSolutionPath(" "));
        Assert.Throws<ArgumentNullException>(() => TestWorkspaceBuilder.Create().WithProject((ProjectSpec)null!).Build());
        Assert.Throws<ArgumentException>(() => TestWorkspaceBuilder.Create().WithProject("", ("Probe.cs", "class Probe {}")).Build());
        Assert.Throws<ArgumentException>(() => TestWorkspaceBuilder.Create().WithProject("Project", ("", "class Probe {}")).Build());
    }

    [Fact]
    public void TestWorkspaceBuilder_VirtualPathsAreNormalizedWithoutCreatingFiles()
    {
        var solutionPath = Path.Combine(Path.GetTempPath(), $"navigator-{Guid.NewGuid():N}", "Sample.slnx");
        using var solutionHandle = TestWorkspaceBuilder.CreateSolution(
            solutionPath,
            new ProjectSpec("Sample", [("Nested/Probe.cs", "public class Probe {}")], VirtualProjectDirectory: "src/Sample"));

        var normalizedSolutionPath = Path.GetFullPath(solutionPath);
        var expectedDocumentPath = Path.Combine(Path.GetDirectoryName(normalizedSolutionPath)!, "src", "Sample", "Nested", "Probe.cs");
        var document = Assert.Single(Assert.Single(solutionHandle.Solution.Projects).Documents);

        Assert.Equal(normalizedSolutionPath, solutionHandle.Solution.FilePath);
        Assert.Equal(expectedDocumentPath, document.FilePath);
        Assert.False(File.Exists(normalizedSolutionPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(expectedDocumentPath)!));
    }

    [Fact]
    public async Task SampleCodeFixtures_ExposeNavigableTypesAndRelationships()
    {
        using var solutionHandle = SampleCodeFixtures.CreateStandardTestSolution();
        var coreProject = solutionHandle.Solution.Projects.Single(project => project.Name == "Sample.Core");
        var appProject = solutionHandle.Solution.Projects.Single(project => project.Name == "Sample.App");
        var coreCompilation = await coreProject.GetCompilationAsync();
        var appCompilation = await appProject.GetCompilationAsync();

        Assert.NotNull(coreCompilation);
        Assert.NotNull(appCompilation);
        Assert.Empty(coreCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(appCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var greeter = coreCompilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        var caller = appCompilation.GetTypeByMetadataName("SampleNamespace.ServiceCaller");
        var processor = coreCompilation.GetTypeByMetadataName("SampleNamespace.Hierarchy.IProcessor");
        var person = coreCompilation.GetTypeByMetadataName("SampleNamespace.Types.Person");
        var coordinate = coreCompilation.GetTypeByMetadataName("SampleNamespace.Types.Coordinate");
        var extension = coreCompilation.GetTypeByMetadataName("SampleNamespace.Extensions.StringExtensions")?.GetMembers("DoubleString").OfType<IMethodSymbol>().Single();

        NavigationAssertions.AssertSymbolName(greeter, "Greeter");
        NavigationAssertions.AssertSymbolName(caller, "ServiceCaller");
        NavigationAssertions.AssertSymbolName(processor, "IProcessor");
        NavigationAssertions.AssertSymbolName(person, "Person");
        NavigationAssertions.AssertSymbolName(coordinate, "Coordinate");
        Assert.True(person!.IsRecord);
        Assert.True(coordinate!.IsRecord);
        Assert.Equal(TypeKind.Struct, coordinate.TypeKind);
        Assert.True(extension!.IsExtensionMethod);
        Assert.Contains(greeter!.GetMembers("Greet"), symbol => symbol is IMethodSymbol);
    }

    [Theory]
    [InlineData("h:gwtQ")]
    [InlineData("h:ABC123")]
    [InlineData("h:abc123XYZ")]
    public void NavigationAssertions_ValidHandoffPasses(string handoffId)
    {
        NavigationAssertions.AssertValidHandoffId(handoffId);
    }

    [Theory]
    [InlineData("gwtQ")]
    [InlineData("")]
    [InlineData("h:")]
    [InlineData("x:123")]
    [InlineData("h:foo-bar")]
    [InlineData("h:foo_bar")]
    public void NavigationAssertions_InvalidHandoffFails(string invalidHandoff)
    {
        Assert.Throws<Xunit.Sdk.TrueException>(() => NavigationAssertions.AssertValidHandoffId(invalidHandoff));
    }

    [Fact]
    public void NavigationAssertions_ValidatesLineRangesAndPatterns()
    {
        NavigationAssertions.AssertValidLineRange(3, 6, minimumLines: 4);
        NavigationAssertions.AssertContainsPattern(["class Greeter", "class Caller"], "Caller");
    }

    [Fact]
    public void NavigationAssertions_RejectsNullHandoff()
    {
        Assert.Throws<Xunit.Sdk.TrueException>(() => NavigationAssertions.AssertValidHandoffId(null));
    }

    private static MetadataReference FindCoreReference(Solution solution, string assemblyPath)
    {
        return Assert.Single(solution.Projects.Single().MetadataReferences.OfType<PortableExecutableReference>()
            .Where(reference => reference.FilePath == assemblyPath));
    }
}
