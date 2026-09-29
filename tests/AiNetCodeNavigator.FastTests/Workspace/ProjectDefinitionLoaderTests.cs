#nullable enable

using System;
using System.IO;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Workspace;

[Trait("Category", "Unit")]
public sealed class ProjectDefinitionLoaderTests
{
    [Fact]
    public void Load_ExistingSolution_ReturnsSuccessWithCanonicalPath()
    {
        using var tempDir = TestTempDirectory.Create("project-def-loader-");
        var solutionPath = tempDir.CreateFile("workspace/app.slnx", "");

        var loaded = AsLoaded(ProjectDefinitionLoader.LoadSolutionTarget(solutionPath));

        Assert.Equal(solutionPath, loaded.SolutionPath);
    }

    [Fact]
    public void Load_UsesExactSolutionPath_WhenWorkspaceContainsTwoSolutions()
    {
        using var tempDir = TestTempDirectory.Create("project-def-loader-two-solutions-");
        var first = tempDir.CreateFile("workspace/first.sln", "");
        var second = tempDir.CreateFile("workspace/second.slnx", "");

        var loaded = AsLoaded(ProjectDefinitionLoader.LoadSolutionTarget(second));

        Assert.Equal(second, loaded.SolutionPath);
        Assert.NotEqual(first, loaded.SolutionPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_MissingOrWhitespaceTarget_ReportsRequired(string? targetPath)
    {
        var failed = AsFailed(ProjectDefinitionLoader.LoadSolutionTarget(targetPath));

        Assert.Equal(NavigationErrorCodes.InvalidArgument, failed.ErrorCode);
        Assert.Contains("targetPath", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsDirectory_AndNonSolutionFile()
    {
        using var tempDir = TestTempDirectory.Create("project-def-loader-invalid-target-");
        var directory = tempDir.CreateSubdirectory("workspace");
        var textFile = tempDir.CreateFile("workspace/readme.txt", "");

        var directoryResult = AsFailed(ProjectDefinitionLoader.LoadSolutionTarget(directory));
        var textResult = AsFailed(ProjectDefinitionLoader.LoadSolutionTarget(textFile));

        Assert.Equal(ProjectErrorCodes.SolutionNotFound, directoryResult.ErrorCode);
        Assert.Equal(ProjectErrorCodes.SolutionNotFound, textResult.ErrorCode);
    }

    [Fact]
    public void Load_MissingSolution_ReportsSolutionNotFoundWithoutSearchingWorkspace()
    {
        using var tempDir = TestTempDirectory.Create("project-def-loader-missing-solution-");
        tempDir.CreateFile("workspace/other.slnx", "");
        var missing = Path.Combine(tempDir.DirectoryPath, "workspace", "selected.slnx");

        var failed = AsFailed(ProjectDefinitionLoader.LoadSolutionTarget(missing));

        Assert.Equal(ProjectErrorCodes.SolutionNotFound, failed.ErrorCode);
        Assert.Contains(missing, failed.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("*.sln")]
    [InlineData("C:\\repo\\*.slnx")]
    [InlineData("C:\\repo\\???.sln")]
    public void Load_WildcardTarget_ReportsInvalidArgument(string wildcardPath)
    {
        var failed = AsFailed(ProjectDefinitionLoader.LoadSolutionTarget(wildcardPath));

        Assert.Equal(NavigationErrorCodes.InvalidArgument, failed.ErrorCode);
        Assert.Contains("Wildcards oder Suchmasken", failed.Message, StringComparison.Ordinal);
    }

    private static ProjectDefinition AsLoaded(ProjectDefinitionLoadResult result)
    {
        Assert.True(result.Succeeded, result.Message);
        return result.Definition!;
    }

    private static ProjectDefinitionLoadResult AsFailed(ProjectDefinitionLoadResult result)
    {
        Assert.False(result.Succeeded, $"expected failure, got definition '{result.Definition?.SolutionPath}'");
        return result;
    }
}
