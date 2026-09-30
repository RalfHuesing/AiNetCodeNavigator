#nullable enable

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Xunit;

namespace AiNetCodeNavigator.IntegrationTests.Workspace;

[Trait("Category", "Integration")]
public sealed class WorkspaceLoadingIntegrationTests
{
    [Fact]
    public void MSBuildSolutionLoader_EnsureRegistered_RegistersDefaults()
    {
        MSBuildSolutionLoader.EnsureMSBuildRegistered();
        Assert.True(MSBuildLocator.IsRegistered);
    }

    [Fact]
    public void MSBuildSolutionLoader_CreateWorkspace_CreatesMSBuildWorkspaceWithDesignTimeProperties()
    {
        using var workspace = MSBuildSolutionLoader.CreateWorkspace();
        Assert.NotNull(workspace);
        Assert.True(workspace.Properties.TryGetValue("DesignTimeBuild", out var dtb) && dtb == "true");
        Assert.True(workspace.Properties.TryGetValue("SkipCompilerExecution", out var sce) && sce == "true");
    }

    [Fact]
    public async Task ProjectRegistry_IntegrationLifecycle_LeasesAndEvicts()
    {
        using var tempDir = TestTempDirectory.Create("integration-registry-");
        WriteProject(tempDir, "src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(tempDir.GetPath("src/App/App.cs"), "namespace Sample; public sealed class AppType;");
        var solutionPath = await WriteSolutionAsync(tempDir, "src/App/App.csproj");
        await using var registry = new ProjectRegistry(ProjectRegistryOptions.ForMSBuild());

        var leaseResult = registry.Lease(solutionPath);
        Assert.True(leaseResult.Succeeded);
        using var lease = leaseResult.Lease;
        Assert.NotNull(lease);
        Assert.Equal(solutionPath, lease.RootPath);
        await lease.ResidentSolution.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));
        var snapshot = await lease.ResidentSolution.GetCurrentSnapshotAsync();
        Assert.True(snapshot.Succeeded, snapshot.Error?.Message);
        Assert.Single(snapshot.Solution!.Projects);
    }

    [Fact]
    public async Task MSBuildSolutionLoader_RealSlnx_LoadsProjectsAndResidentRefreshesSourceAndStructure()
    {
        using var tempDir = TestTempDirectory.Create("integration-real-slnx-");
        WriteProject(tempDir, "src/Library/Library.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var appProject = WriteProject(tempDir, "src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"../Library/Library.csproj\" /></ItemGroup></Project>");
        var librarySource = tempDir.CreateFile("src/Library/Library.cs", "namespace Sample; public sealed class LibraryType { public int Value => 1; }");
        tempDir.CreateFile("src/App/App.cs", "namespace Sample; public sealed class AppType { public LibraryType Create() => new(); }");
        var solutionPath = await WriteSolutionAsync(tempDir, "src/Library/Library.csproj", "src/App/App.csproj");

        await using var resident = MSBuildSolutionLoader.CreateResidentSolution(solutionPath);
        await resident.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));
        var initialResult = await resident.GetCurrentSnapshotAsync();
        Assert.True(initialResult.Succeeded, initialResult.Error?.Message);
        var initial = initialResult.Solution;
        Assert.NotNull(initial);
        Assert.Equal(2, initial.Projects.Count());
        Assert.Single(initial.GetProject(initial.Projects.Single(project => project.Name == "App").Id)!.ProjectReferences);

        await File.WriteAllTextAsync(librarySource, "namespace Sample; public sealed class LibraryType { public int Value => 2; }");
        await File.WriteAllTextAsync(tempDir.GetPath("src/Library/Added.cs"), "namespace Sample; public sealed class AddedType;");
        var refreshedResult = await resident.GetCurrentSnapshotAsync();

        Assert.True(refreshedResult.Succeeded, refreshedResult.Error?.Message);
        var refreshed = refreshedResult.Solution!;
        var library = refreshed.Projects.Single(project => project.Name == "Library");
        Assert.Contains(library.Documents, document => document.FilePath == tempDir.GetPath("src/Library/Added.cs"));
        Assert.Contains("Value => 2", (await library.Documents.Single(document => document.FilePath == librarySource).GetTextAsync()).ToString(), StringComparison.Ordinal);

        File.Delete(tempDir.GetPath("src/Library/Added.cs"));
        var afterRemoval = (await resident.GetCurrentSnapshotAsync()).Solution;
        Assert.DoesNotContain(afterRemoval!.Projects.Single(project => project.Name == "Library").Documents, document => document.FilePath == tempDir.GetPath("src/Library/Added.cs"));

        WriteProject(tempDir, "src/Extra/Extra.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(tempDir.GetPath("src/Extra/Extra.cs"), "namespace Sample; public sealed class ExtraType;");
        await File.WriteAllTextAsync(appProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"../Extra/Extra.csproj\" /></ItemGroup></Project>");
        await File.WriteAllTextAsync(solutionPath, SolutionXml("src/App/App.csproj", "src/Extra/Extra.csproj"));

        var afterStructureResult = await resident.GetCurrentSnapshotAsync();
        Assert.True(afterStructureResult.Succeeded, afterStructureResult.Error?.Message);
        var afterStructureChange = afterStructureResult.Solution;
        Assert.NotNull(afterStructureChange);
        Assert.Equal(2, afterStructureChange.Projects.Count());
        Assert.DoesNotContain(afterStructureChange.Projects, project => project.Name == "Library");
        Assert.Single(afterStructureChange.Projects.Single(project => project.Name == "App").ProjectReferences);

        await File.WriteAllTextAsync(solutionPath, SolutionXml("src/App/App.csproj", "src/Extra/Extra.csproj", "src/Missing/Missing.csproj"));
        var failedReload = await resident.GetCurrentSnapshotAsync();
        Assert.False(failedReload.Succeeded);
        Assert.NotNull(failedReload.Error);
        Assert.Contains("Missing.csproj", failedReload.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(failedReload.Error.Retryable);

        await File.WriteAllTextAsync(solutionPath, SolutionXml("src/App/App.csproj", "src/Extra/Extra.csproj"));
        var retriedReload = await resident.GetCurrentSnapshotAsync();
        Assert.True(retriedReload.Succeeded, retriedReload.Error?.Message);
        Assert.Equal(2, retriedReload.Solution!.Projects.Count());
    }

    [Fact]
    public async Task ProjectRegistry_MSBuildLoadFailureIncludesCauseAndCanRetryAfterRepair()
    {
        using var tempDir = TestTempDirectory.Create("integration-slnx-retry-");
        var projectPath = tempDir.GetPath("src/App/App.csproj");
        var solutionPath = await WriteSolutionAsync(tempDir, "src/App/App.csproj");
        await using var registry = new ProjectRegistry(ProjectRegistryOptions.ForMSBuild());

        var first = registry.Lease(solutionPath);
        Assert.True(first.Succeeded);
        var failedResident = first.Lease!.ResidentSolution;
        await failedResident.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));
        var failure = failedResident.LoadFailure;
        Assert.NotNull(failure);
        Assert.Equal(ProjectErrorCodes.ProjectLoadFailed, failure.ErrorCode);
        Assert.Equal(solutionPath, failure.TargetPath);
        Assert.True(failure.Retryable);
        Assert.Contains("App.csproj", failure.Message, StringComparison.OrdinalIgnoreCase);

        first.Lease.MarkLoadFailedResponseEmitted();
        first.Lease.Dispose();
        WriteProject(tempDir, "src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(tempDir.GetPath("src/App/App.cs"), "namespace Sample; public sealed class RecoveredType;");

        var retry = registry.Lease(solutionPath);
        Assert.True(retry.Succeeded);
        Assert.NotSame(failedResident, retry.Lease!.ResidentSolution);
        await retry.Lease.ResidentSolution.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));
        var snapshot = await retry.Lease.ResidentSolution.GetCurrentSnapshotAsync();
        Assert.True(snapshot.Succeeded, snapshot.Error?.Message);
        Assert.Contains(snapshot.Solution!.Projects, project => project.Name == "App");
        retry.Lease.Dispose();
        Assert.True(File.Exists(projectPath));
    }

    private static string WriteProject(TestTempDirectory tempDir, string relativePath, string content) => tempDir.CreateFile(relativePath, content);

    private static async Task<string> WriteSolutionAsync(TestTempDirectory tempDir, params string[] projectPaths)
    {
        var solutionPath = tempDir.GetPath("Integration.slnx");
        await File.WriteAllTextAsync(solutionPath, SolutionXml(projectPaths));
        return solutionPath;
    }

    private static string SolutionXml(params string[] projectPaths) =>
        "<Solution>" + string.Concat(projectPaths.Select(path => $"<Project Path=\"{path}\" />")) + "</Solution>";
}
