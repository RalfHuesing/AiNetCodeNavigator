#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.FileStructure;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using Microsoft.CodeAnalysis;
using Xunit;

namespace AiNetCodeNavigator.IntegrationTests.Workspace;

[Trait("Category", "Integration")]
public sealed class MSBuildStructureInputCollectorTests
{
    [Fact]
    public async Task Collect_LeavesFrameworkCoverageUnknownWhenModernPropertiesAreEmpty()
    {
        using var fixture = TestTempDirectory.Create("framework-availability-");
        var legacyPath = fixture.CreateFile("Legacy/Legacy.csproj",
            "<Project><PropertyGroup><TargetFrameworkVersion>v4.8</TargetFrameworkVersion></PropertyGroup></Project>");
        var modernPath = fixture.CreateFile("Modern/Modern.csproj",
            "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        using var workspace = new AdhocWorkspace();
        var legacy = workspace.AddProject("Legacy", LanguageNames.CSharp);
        var modern = workspace.AddProject("Modern", LanguageNames.CSharp);
        var solution = workspace.CurrentSolution
            .WithProjectFilePath(legacy.Id, legacyPath)
            .WithProjectFilePath(modern.Id, modernPath);

        MSBuildSolutionLoader.EnsureMSBuildRegistered();
        var inputs = MSBuildStructureInputCollector.Collect(solution);
        var legacyFrameworks = inputs.ConfiguredTargetFrameworks[Normalize(legacyPath)];
        var modernFrameworks = inputs.ConfiguredTargetFrameworks[Normalize(modernPath)];

        Assert.False(legacyFrameworks.IsKnown);
        Assert.Empty(legacyFrameworks.Values);
        Assert.True(modernFrameworks.IsKnown);
        Assert.Equal(["net10.0"], modernFrameworks.Values);

        var scope = await IndexScopeScanner.ScanAsync(solution,
            options: new IndexScopeScanOptions(ConfiguredFrameworksByProject: inputs.ConfiguredTargetFrameworks));
        Assert.True(scope.ScanCompleted);
        Assert.False(scope.Projects.Single(project => project.Name == "Legacy").ConfiguredFrameworksKnown);
        Assert.True(scope.Projects.Single(project => project.Name == "Modern").ConfiguredFrameworksKnown);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
