#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.FileStructure;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit.Builders;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AiNetCodeNavigator.FastTests.FileStructure;

[Trait("Category", "Unit")]
public sealed class IndexScopeScannerTests
{
    [Fact]
    public async Task ScanAsync_ReturnsProjectAndDocumentBreakdown()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var payload = await IndexScopeScanner.ScanAsync(fixture.Solution);

        Assert.Equal(2, payload.ProjectCount);
        Assert.True(payload.TotalDocumentCount >= 5);
        Assert.True(payload.CSharpFileCount >= 5);
        Assert.Contains(payload.Projects, p => p.Name == "Sample.Core");
        Assert.Contains(payload.Projects, p => p.Name == "Sample.App");
        Assert.All(payload.Projects, project => Assert.False(project.ConfiguredFrameworksKnown));

        // Breakdown has .cs extension covered
        var csEntry = payload.FileTypes.FirstOrDefault(f => f.Extension == ".cs");
        Assert.NotNull(csEntry);
        Assert.True(csEntry.SymbolGraphCovered);

        // Formatted report
        Assert.Contains("# Index Scope:", payload.FormattedText);
        Assert.Contains("## Projects", payload.FormattedText);
        Assert.Contains("## File types in Roslyn index", payload.FormattedText);
        Assert.True(payload.ScanCompleted);
        Assert.False(payload.IsTruncated);
    }

    [Fact]
    public async Task ScanAsync_BoundsProjectBreakdown()
    {
        var projects = Enumerable.Range(0, 205)
            .Select(index => new ProjectSpec(
                $"Project{index:D3}",
                [("Source.cs", $"namespace Project{index}; public class Source {{}}")]))
            .ToArray();
        using var projectFixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\LargeIndexScope.slnx",
            projects);

        var projectPayload = await IndexScopeScanner.ScanAsync(projectFixture.Solution);

        Assert.Equal(205, projectPayload.ProjectCount);
        Assert.Equal(205, projectPayload.TotalDocumentCount);
        Assert.Equal(100, projectPayload.Projects.Count);
        Assert.Equal(100, projectPayload.ShownProjectCount);
        Assert.True(projectPayload.ScanCompleted);
        Assert.True(projectPayload.IsTruncated);
        Assert.Contains("maxProjects", projectPayload.TruncatedBy!);
        Assert.Contains("Projects: 205", projectPayload.FormattedText);
        Assert.Contains("Increase the result bounds", projectPayload.NextAction);
        Assert.Equal("Project000", projectPayload.Projects[0].Name);
        Assert.Equal(1, projectPayload.Projects[0].CSharpDocumentCount);
    }

    [Fact]
    public async Task ScanAsync_BoundsFileTypeBreakdown()
    {
        var documents = Enumerable.Range(0, 70)
            .Select(index => ($"Asset{index:D3}.e{index:D3}", "namespace Assets; public class Asset {}"))
            .ToArray();
        using var fileTypeFixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\ManyFileTypes.slnx",
            new ProjectSpec("Assets", documents));

        var fileTypePayload = await IndexScopeScanner.ScanAsync(fileTypeFixture.Solution);
        Assert.Equal(70, fileTypePayload.TotalDocumentCount);
        Assert.Equal(70, fileTypePayload.TotalFileTypeCount);
        Assert.Equal(64, fileTypePayload.FileTypes.Count);
        Assert.Equal(64, fileTypePayload.ShownFileTypeCount);
        Assert.True(fileTypePayload.IsTruncated);
        Assert.Contains("maxFileTypes", fileTypePayload.TruncatedBy!);
        Assert.Contains("70 total, 64 shown", fileTypePayload.FormattedText);
        Assert.Contains("MaxFileTypes", fileTypePayload.NextAction);
        Assert.Equal(".e000", fileTypePayload.FileTypes[0].Extension);
    }

    [Fact]
    public async Task ScanAsync_ValidatesNullSolution()
    {
        await Assert.ThrowsAsync<System.ArgumentNullException>(() =>
            IndexScopeScanner.ScanAsync(null!));
    }

    [Fact]
    public async Task ScanAsync_UsesEnglishReportLabelsAndKeepsDocumentsReadOnly()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\ReadOnlyIndexScope.slnx",
            new ProjectSpec("Production", [("Source.cs", "namespace ReadOnly; public class Source {}")]),
            new ProjectSpec("UnitTests", [("Spec.cs", "namespace ReadOnly.Tests; public class Spec {}") ]));
        var documents = fixture.Solution.Projects.SelectMany(project => project.Documents).ToArray();
        var before = await Task.WhenAll(documents.Select(document => document.GetTextAsync()));

        var payload = await IndexScopeScanner.ScanAsync(fixture.Solution);

        Assert.Contains("## Projects", payload.FormattedText);
        Assert.Contains("## File types in Roslyn index", payload.FormattedText);
        Assert.DoesNotContain("Projekte:", payload.FormattedText);
        Assert.True(payload.ScanCompleted);
        Assert.False(payload.IsTruncated);
        var after = await Task.WhenAll(documents.Select(document => document.GetTextAsync()));
        Assert.Equal(before.Select(text => text.ToString()), after.Select(text => text.ToString()));
    }

    [Fact]
    public async Task ScanAsync_ScopesToProjectAndReturnsUnknownProjectError()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\ScopedIndexScope.slnx",
            new ProjectSpec("Production", [("Source.cs", "namespace App; public class Source {}")]),
            new ProjectSpec("UnitTests", [("Spec.cs", "namespace App.Tests; public class Spec {}") ]));

        var scoped = await IndexScopeScanner.ScanAsync(
            fixture.Solution,
            options: new IndexScopeScanOptions(ProjectName: "production"));
        Assert.Equal("Production", scoped.ScopeProjectName);
        Assert.Equal(1, scoped.ProjectCount);
        Assert.Single(scoped.Projects);
        Assert.Equal(1, scoped.TotalDocumentCount);
        Assert.Equal(1, scoped.CSharpFileCount);
        Assert.Equal(0, scoped.TestProjectCount);

        var missing = await IndexScopeScanner.ScanAsync(
            fixture.Solution,
            options: new IndexScopeScanOptions(ProjectName: "Missing"));
        Assert.False(missing.ScanCompleted);
        Assert.False(string.IsNullOrWhiteSpace(missing.Error));
        Assert.Contains("Project 'Missing' was not found", missing.Error);
        Assert.Empty(missing.Projects);
        Assert.Empty(missing.FileTypes);
    }

    [Fact]
    public async Task ScanAsync_CountsGeneratedAndTestDocumentsAcrossMixedProjectsAndBounds()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\MixedIndexScope.slnx",
            new ProjectSpec("Production", [
                ("Plain.cs", "namespace App; public class Plain {}"),
                ("Build.g.cs", "namespace App; public class BuildGenerated {}"),
                ("tests/Inline.cs", "namespace App.Tests; public class InlineTest {}")]),
            new ProjectSpec("UnitTests", [
                ("Case.cs", "namespace App.Tests; public class CaseTest {}"),
                ("Case.g.cs", "namespace App.Tests; public class GeneratedCaseTest {}") ]));

        var payload = await IndexScopeScanner.ScanAsync(
            fixture.Solution,
            options: new IndexScopeScanOptions(MaxProjects: 1));

        Assert.Equal(5, payload.TotalDocumentCount);
        Assert.Equal(2, payload.GeneratedDocumentCount);
        Assert.Equal(3, payload.TestDocumentCount);
        Assert.Contains("Generated C# documents: 2", payload.FormattedText);
        Assert.Contains("Test documents: 3", payload.FormattedText);
        Assert.True(payload.IsTruncated);
        Assert.Equal(2, payload.ProjectCount);
        Assert.Single(payload.Projects);
    }

    [Fact]
    public async Task ScanAsync_CountsGeneratedAndTestDocumentsWithinSelectedProject()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\ScopedGeneratedIndexScope.slnx",
            new ProjectSpec("Production", [
                ("Plain.cs", "namespace App; public class Plain {}"),
                ("Build.g.cs", "namespace App; public class BuildGenerated {}"),
                ("tests/Inline.cs", "namespace App.Tests; public class InlineTest {}")]),
            new ProjectSpec("UnitTests", [
                ("Case.cs", "namespace App.Tests; public class CaseTest {}"),
                ("Case.g.cs", "namespace App.Tests; public class GeneratedCaseTest {}") ]));

        var production = await IndexScopeScanner.ScanAsync(
            fixture.Solution,
            options: new IndexScopeScanOptions(ProjectName: "production"));
        var tests = await IndexScopeScanner.ScanAsync(
            fixture.Solution,
            options: new IndexScopeScanOptions(ProjectName: "UnitTests"));

        Assert.Equal(1, production.GeneratedDocumentCount);
        Assert.Equal(1, production.TestDocumentCount);
        Assert.Equal(1, tests.GeneratedDocumentCount);
        Assert.Equal(2, tests.TestDocumentCount);
    }

    [Fact]
    public async Task ScanAsync_ClampsBoundsAndCountsCompileErrorDocuments()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\CompileErrorIndexScope.slnx",
            new ProjectSpec("Broken", [("Broken.cs", "namespace Broken; public class {" )]));

        var payload = await IndexScopeScanner.ScanAsync(
            fixture.Solution,
            options: new IndexScopeScanOptions(MaxProjects: 0, MaxFileTypes: 256));

        Assert.True(payload.BoundsWereClamped);
        Assert.Equal(1, payload.EffectiveMaxProjects);
        Assert.Equal(IndexScopeScanner.MaxFileTypesCap, payload.EffectiveMaxFileTypes);
        Assert.True(payload.ScanCompleted);
        Assert.Null(payload.Error);
        Assert.Equal(1, payload.TotalDocumentCount);
        Assert.Equal(1, payload.CSharpFileCount);
    }

    [Fact]
    public async Task ScanAsync_PropagatesCancellation()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(() =>
            IndexScopeScanner.ScanAsync(fixture.Solution, cancellation.Token));
    }

    [Fact]
    public async Task ScanAsync_ReportsOnlyConfiguredFrameworksAbsentFromLoadedProjectContexts()
    {
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\virtual\FrameworkScope.slnx",
            new ProjectSpec("FrameworkNet10", [("Source.cs", "namespace Frameworks; public sealed class Net10 {}")]),
            new ProjectSpec("FrameworkNet9", [("Source.cs", "namespace Frameworks; public sealed class Net9 {}") ]));
        var projects = fixture.Solution.Projects.ToArray();
        const string sharedProjectPath = @"C:\virtual\Shared\FrameworkScope.csproj";
        var solution = fixture.Solution
            .WithProjectFilePath(projects[0].Id, sharedProjectPath)
            .WithProjectFilePath(projects[1].Id, sharedProjectPath);
        solution = AddLoadedFramework(solution, projects[0].Id, "net10.0");
        solution = AddLoadedFramework(solution, projects[1].Id, "net9.0");
        var configured = new Dictionary<string, ConfiguredTargetFrameworks>(StringComparer.OrdinalIgnoreCase)
        {
            [sharedProjectPath.Replace('\\', '/')] = new(true, ["net8.0", "net9.0", "net10.0"]),
        };

        var bothContexts = await IndexScopeScanner.ScanAsync(solution,
            options: new IndexScopeScanOptions(ConfiguredFrameworksByProject: configured));
        Assert.All(bothContexts.Projects, project =>
        {
            Assert.True(project.ConfiguredFrameworksKnown);
            Assert.Equal(new[] { "net8.0" }, project.ConfiguredFrameworksNotAnalyzed);
        });

        var oneContext = await IndexScopeScanner.ScanAsync(solution.RemoveProject(projects[1].Id),
            options: new IndexScopeScanOptions(ConfiguredFrameworksByProject: configured));
        var onlyProject = Assert.Single(oneContext.Projects);
        Assert.True(onlyProject.ConfiguredFrameworksKnown);
        Assert.Equal(new[] { "net8.0", "net9.0" }, onlyProject.ConfiguredFrameworksNotAnalyzed);

        var loadedOnly = new Dictionary<string, ConfiguredTargetFrameworks>(StringComparer.OrdinalIgnoreCase)
        {
            [sharedProjectPath.Replace('\\', '/')] = new(true, ["net10.0"]),
        };
        var loadedOnlyScope = await IndexScopeScanner.ScanAsync(solution.RemoveProject(projects[1].Id),
            options: new IndexScopeScanOptions(ConfiguredFrameworksByProject: loadedOnly));
        Assert.Empty(Assert.Single(loadedOnlyScope.Projects).ConfiguredFrameworksNotAnalyzed!);
    }

    private static Solution AddLoadedFramework(Solution solution, ProjectId projectId, string framework)
    {
        var project = solution.GetProject(projectId)!;
        return project.AddAnalyzerConfigDocument(".globalconfig",
            SourceText.From($"is_global = true{Environment.NewLine}build_property.TargetFramework = {framework}"),
            filePath: Path.Combine(Path.GetDirectoryName(project.FilePath) ?? @"C:\virtual", framework + ".globalconfig"))
            .Project.Solution;
    }
}
