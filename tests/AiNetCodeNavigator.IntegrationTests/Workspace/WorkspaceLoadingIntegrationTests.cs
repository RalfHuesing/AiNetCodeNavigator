#nullable enable

using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    public async Task ResidentSolution_SynchronousOwnerReloadsChangedStructureFromItsSolutionPath()
    {
        using var tempDir = TestTempDirectory.Create("integration-sync-resident-reload-");
        WriteProject(tempDir, "src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var solutionPath = await WriteSolutionAsync(tempDir, "src/App/App.csproj");
        using var workspace = MSBuildSolutionLoader.CreateWorkspace();
        var solution = await workspace.OpenSolutionAsync(solutionPath);
        await using var resident = new ResidentSolution(solution, workspace, solutionPath);

        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        Assert.Single(initial.Solution!.Projects);

        WriteProject(tempDir, "src/Extra/Extra.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(tempDir.GetPath("src/Extra/Extra.cs"), "namespace Sample; public sealed class ExtraType;");
        await File.WriteAllTextAsync(solutionPath, SolutionXml("src/App/App.csproj", "src/Extra/Extra.csproj"));

        var refreshed = await resident.GetCurrentSnapshotAsync();

        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        Assert.Equal(new[] { "App", "Extra" }, refreshed.Solution!.Projects.Select(project => project.Name).Order(StringComparer.Ordinal).ToArray());
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

    [Fact]
    public async Task ResidentSnapshot_ExternalCompileGlob_IncludesNewFilesOutsideProjectDirectory()
    {
        using var tempDir = TestTempDirectory.Create("integration-external-compile-glob-");
        WriteProject(tempDir, "src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include=\"../Shared/*.cs\" /></ItemGroup></Project>");
        var existingSource = tempDir.CreateFile("src/Shared/Existing.cs", "namespace Shared; public sealed class ExistingType;");
        var solutionPath = await WriteSolutionAsync(tempDir, "src/App/App.csproj");
        await using var resident = MSBuildSolutionLoader.CreateResidentSolution(solutionPath);
        await resident.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));
        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        Assert.Contains(initial.Solution!.Projects.Single().Documents, document => document.FilePath == existingSource);

        var addedSource = tempDir.GetPath("src/Shared/Added.cs");
        await File.WriteAllTextAsync(addedSource, "namespace Shared; public sealed class AddedType;");
        var refreshed = await resident.GetCurrentSnapshotAsync();

        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        Assert.Contains(refreshed.Solution!.Projects.Single().Documents, document => document.FilePath == addedSource);
    }

    [Theory]
    [InlineData("props")]
    [InlineData("targets")]
    public async Task ResidentSnapshot_CustomImportChangeWithPreservedTimestamp_RefreshesProjectReferences(string importExtension)
    {
        using var tempDir = TestTempDirectory.Create("integration-custom-import-");
        const string initialImport = "<Project><ItemGroup><ProjectReference Include=\"$(MSBuildThisFileDirectory)..\\src\\Library\\Library.csproj\" /></ItemGroup></Project>";
        var importPath = tempDir.CreateFile(
            $"build/References.{importExtension}",
            initialImport);
        WriteProject(tempDir, "src/App/App.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><Import Project=\"../../build/References.{importExtension}\" /></Project>");
        WriteProject(tempDir, "src/Library/Library.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        WriteProject(tempDir, "src/Extra/Extra.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(tempDir.GetPath("src/App/App.cs"), "namespace App; public sealed class AppType;");
        await File.WriteAllTextAsync(tempDir.GetPath("src/Library/Library.cs"), "namespace Library; public sealed class LibraryType;");
        await File.WriteAllTextAsync(tempDir.GetPath("src/Extra/Extra.cs"), "namespace Extra; public sealed class ExtraType;");
        var solutionPath = await WriteSolutionAsync(tempDir, "src/App/App.csproj", "src/Library/Library.csproj", "src/Extra/Extra.csproj");
        await using var resident = MSBuildSolutionLoader.CreateResidentSolution(solutionPath);
        await resident.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));
        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        var app = initial.Solution!.Projects.Single(project => project.Name == "App");
        Assert.Equal("Library", initial.Solution.GetProject(app.ProjectReferences.Single().ProjectId)!.Name);

        var importTimestamp = File.GetLastWriteTimeUtc(importPath);
        const string updatedImport = "<Project><ItemGroup><ProjectReference Include=\"$(MSBuildThisFileDirectory)..\\src\\Extra\\Extra.csproj\" /></ItemGroup>    </Project>";
        Assert.Equal(initialImport.Length, updatedImport.Length);
        await File.WriteAllTextAsync(
            importPath,
            updatedImport);
        File.SetLastWriteTimeUtc(importPath, importTimestamp);
        Assert.Equal(importTimestamp, File.GetLastWriteTimeUtc(importPath));

        var refreshed = await resident.GetCurrentSnapshotAsync();
        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        var refreshedApp = refreshed.Solution!.Projects.Single(project => project.Name == "App");
        Assert.Equal("Extra", refreshed.Solution.GetProject(refreshedApp.ProjectReferences.Single().ProjectId)!.Name);
    }

    [Fact]
    public async Task ResidentSnapshot_CreatingMissingConditionalImport_RefreshesProjectReferences()
    {
        using var tempDir = TestTempDirectory.Create("integration-conditional-import-");
        WriteProject(
            tempDir,
            "src/App/App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><Import Project=\"../../build/Optional.props\" Condition=\"Exists('../../build/Optional.props')\" /></Project>");
        WriteProject(tempDir, "src/Library/Library.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        WriteProject(tempDir, "src/Extra/Extra.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(tempDir.GetPath("src/App/App.cs"), "namespace App; public sealed class AppType;");
        await File.WriteAllTextAsync(tempDir.GetPath("src/Library/Library.cs"), "namespace Library; public sealed class LibraryType;");
        await File.WriteAllTextAsync(tempDir.GetPath("src/Extra/Extra.cs"), "namespace Extra; public sealed class ExtraType;");
        var solutionPath = await WriteSolutionAsync(tempDir, "src/App/App.csproj", "src/Library/Library.csproj", "src/Extra/Extra.csproj");

        await using var resident = MSBuildSolutionLoader.CreateResidentSolution(solutionPath);
        await resident.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));
        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        var app = initial.Solution!.Projects.Single(project => project.Name == "App");
        Assert.Empty(app.ProjectReferences);

        tempDir.CreateFile(
            "build/Optional.props",
            "<Project><ItemGroup><ProjectReference Include=\"$(MSBuildThisFileDirectory)../src/Extra/Extra.csproj\" /></ItemGroup></Project>");

        var refreshed = await resident.GetCurrentSnapshotAsync();
        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        var refreshedApp = refreshed.Solution!.Projects.Single(project => project.Name == "App");
        Assert.Equal("Extra", refreshed.Solution.GetProject(refreshedApp.ProjectReferences.Single().ProjectId)!.Name);
    }

    [Fact]
    public async Task ResidentSnapshot_CreatingMissingImportGroupImport_RefreshesProjectReferences()
    {
        using var tempDir = TestTempDirectory.Create("integration-import-group-import-");
        WriteProject(
            tempDir,
            "src/App/App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ImportGroup Condition=\"Exists('../../build/Optional.props')\"><Import Project=\"../../build/Optional.props\" /></ImportGroup></Project>");
        WriteProject(tempDir, "src/Library/Library.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        WriteProject(tempDir, "src/Extra/Extra.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(tempDir.GetPath("src/App/App.cs"), "namespace App; public sealed class AppType;");
        await File.WriteAllTextAsync(tempDir.GetPath("src/Library/Library.cs"), "namespace Library; public sealed class LibraryType;");
        await File.WriteAllTextAsync(tempDir.GetPath("src/Extra/Extra.cs"), "namespace Extra; public sealed class ExtraType;");
        var solutionPath = await WriteSolutionAsync(tempDir, "src/App/App.csproj", "src/Library/Library.csproj", "src/Extra/Extra.csproj");

        await using var resident = MSBuildSolutionLoader.CreateResidentSolution(solutionPath);
        await resident.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));
        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        Assert.Empty(initial.Solution!.Projects.Single(project => project.Name == "App").ProjectReferences);

        tempDir.CreateFile(
            "build/Optional.props",
            "<Project><ItemGroup><ProjectReference Include=\"$(MSBuildThisFileDirectory)../src/Extra/Extra.csproj\" /></ItemGroup></Project>");

        var refreshed = await resident.GetCurrentSnapshotAsync();
        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        var refreshedApp = refreshed.Solution!.Projects.Single(project => project.Name == "App");
        Assert.Equal("Extra", refreshed.Solution.GetProject(refreshedApp.ProjectReferences.Single().ProjectId)!.Name);
    }

    [Fact]
    public async Task ResidentSnapshot_CreatingFileMatchedByWildcardImport_RefreshesProjectReferences()
    {
        using var tempDir = TestTempDirectory.Create("integration-wildcard-import-");
        WriteProject(
            tempDir,
            "src/App/App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><Import Project=\"../../build/*.props\" /></Project>");
        WriteProject(tempDir, "src/Library/Library.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        WriteProject(tempDir, "src/Extra/Extra.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(tempDir.GetPath("src/App/App.cs"), "namespace App; public sealed class AppType;");
        await File.WriteAllTextAsync(tempDir.GetPath("src/Library/Library.cs"), "namespace Library; public sealed class LibraryType;");
        await File.WriteAllTextAsync(tempDir.GetPath("src/Extra/Extra.cs"), "namespace Extra; public sealed class ExtraType;");
        var solutionPath = await WriteSolutionAsync(tempDir, "src/App/App.csproj", "src/Library/Library.csproj", "src/Extra/Extra.csproj");

        await using var resident = MSBuildSolutionLoader.CreateResidentSolution(solutionPath);
        await resident.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));
        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        Assert.Empty(initial.Solution!.Projects.Single(project => project.Name == "App").ProjectReferences);

        var importPath = tempDir.CreateFile(
            "build/Optional.props",
            "<Project><ItemGroup><ProjectReference Include=\"$(MSBuildThisFileDirectory)../src/Extra/Extra.csproj\" /></ItemGroup></Project>");

        var refreshed = await resident.GetCurrentSnapshotAsync();

        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        var refreshedApp = refreshed.Solution!.Projects.Single(project => project.Name == "App");
        Assert.Equal("Extra", refreshed.Solution.GetProject(refreshedApp.ProjectReferences.Single().ProjectId)!.Name);

        await File.WriteAllTextAsync(
            importPath,
            "<Project><ItemGroup><ProjectReference Include=\"$(MSBuildThisFileDirectory)../src/Library/Library.csproj\" /></ItemGroup></Project>");
        var changed = await resident.GetCurrentSnapshotAsync();
        Assert.True(changed.Succeeded, changed.Error?.Message);
        var changedApp = changed.Solution!.Projects.Single(project => project.Name == "App");
        Assert.Equal("Library", changed.Solution.GetProject(changedApp.ProjectReferences.Single().ProjectId)!.Name);

        File.Delete(importPath);
        var removed = await resident.GetCurrentSnapshotAsync();
        Assert.True(removed.Succeeded, removed.Error?.Message);
        Assert.Empty(removed.Solution!.Projects.Single(project => project.Name == "App").ProjectReferences);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResidentSnapshot_EmptyPropertyPrefixInWildcardImport_TracksEvaluatedPattern(bool declareEmptyPrefix)
    {
        using var tempDir = TestTempDirectory.Create("integration-empty-prefix-wildcard-import-");
        var prefixProperty = declareEmptyPrefix
            ? "<PropertyGroup><TargetFramework>net10.0</TargetFramework><Prefix /></PropertyGroup>"
            : "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>";
        WriteProject(
            tempDir,
            "src/App/App.csproj",
            $"<Project Sdk=\"Microsoft.NET.Sdk\">{prefixProperty}<Import Project=\"$(Prefix)../../build/*.props\" /></Project>");
        WriteProject(tempDir, "src/Extra/Extra.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(tempDir.GetPath("src/App/App.cs"), "namespace App; public sealed class AppType;");
        await File.WriteAllTextAsync(tempDir.GetPath("src/Extra/Extra.cs"), "namespace Extra; public sealed class ExtraType;");
        var solutionPath = await WriteSolutionAsync(tempDir, "src/App/App.csproj", "src/Extra/Extra.csproj");

        await using var resident = MSBuildSolutionLoader.CreateResidentSolution(solutionPath);
        await resident.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));
        var initial = await resident.GetCurrentSnapshotAsync();
        Assert.True(initial.Succeeded, initial.Error?.Message);
        Assert.Empty(initial.Solution!.Projects.Single(project => project.Name == "App").ProjectReferences);

        tempDir.CreateFile(
            "build/Optional.props",
            "<Project><ItemGroup><ProjectReference Include=\"$(MSBuildThisFileDirectory)../src/Extra/Extra.csproj\" /></ItemGroup></Project>");
        var refreshed = await resident.GetCurrentSnapshotAsync();

        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        var app = refreshed.Solution!.Projects.Single(project => project.Name == "App");
        Assert.Equal("Extra", refreshed.Solution.GetProject(app.ProjectReferences.Single().ProjectId)!.Name);
    }

    [Fact]
    public async Task ResidentSnapshot_UnresolvedMetadataExpressionForcesReloadAndRecoversAfterLoadFailure()
    {
        using var tempDir = TestTempDirectory.Create("integration-unresolved-msbuild-expression-");
        var projectPath = tempDir.CreateFile(
            "src/App/App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include=\"App.cs\" /><Compile Include=\"%(Compile.Identity).generated.cs\" /></ItemGroup></Project>");
        await File.WriteAllTextAsync(tempDir.GetPath("src/App/App.cs"), "namespace App; public sealed class AppType;");
        var solutionPath = await WriteSolutionAsync(tempDir, "src/App/App.csproj");

        await using var resident = MSBuildSolutionLoader.CreateResidentSolution(solutionPath);
        await resident.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));
        var initial = resident.GetCurrentSolution();
        Assert.NotNull(initial);
        var unresolvedInputs = MSBuildStructureInputCollector.Collect(initial);
        Assert.Contains(unresolvedInputs.UnresolvedExpressions, expression =>
            expression.Contains("%(Compile.Identity)", StringComparison.Ordinal));
        Assert.NotEqual(
            SolutionStructureFingerprint.Create(initial, solutionPath, unresolvedInputs),
            SolutionStructureFingerprint.Create(initial, solutionPath, unresolvedInputs with { UnresolvedExpressions = [] }));

        // The metadata reference remains literal in the successfully evaluated Compile item.
        // Requesting the same snapshot must re-evaluate it even though the old fingerprint is otherwise unchanged.
        var refreshed = await resident.GetCurrentSnapshotAsync();
        Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
        Assert.NotSame(initial, refreshed.Solution);

        await File.WriteAllTextAsync(projectPath, "<Project");
        var failed = await resident.GetCurrentSnapshotAsync();
        Assert.False(failed.Succeeded);
        Assert.Null(failed.Solution);
        Assert.NotNull(failed.Error);

        await File.WriteAllTextAsync(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include=\"App.cs\" /></ItemGroup></Project>");
        var recovered = await resident.GetCurrentSnapshotAsync();
        Assert.True(recovered.Succeeded, recovered.Error?.Message);
        Assert.Single(recovered.Solution!.Projects.Single(project => project.Name == "App").Documents.Where(document => Path.GetFileName(document.FilePath) == "App.cs"));
    }

    [Fact]
    [Trait("Category", "ExtendedIntegration")]
    public async Task MSBuildSolutionLoader_CustomTargetsAndScratchCleanupPreserveWorkspaceSnapshot()
    {
        using var tempDir = TestTempDirectory.Create("integration-readonly-targets-");
        var projectDirectory = tempDir.GetPath("src/App");
        var foreignOutputRoot = Path.Combine(
            Path.GetDirectoryName(tempDir.DirectoryPath)!,
            $"{Path.GetFileName(tempDir.DirectoryPath)}-foreign-output");
        var markerName = $"navigator-target-invoked-{Guid.NewGuid():N}.marker";
        var foreignOutputRelativePath = Path.GetRelativePath(projectDirectory, foreignOutputRoot)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var foreignOutputMsBuildPath = foreignOutputRelativePath.Replace(Path.DirectorySeparatorChar, '\\');
        WriteProject(
            tempDir,
            "src/App/App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        tempDir.CreateFile(
            "build/ReadOnlyProbe.targets",
            $"<Project><PropertyGroup><IntermediateOutputPath>$(MSBuildProjectDirectory)\\{foreignOutputMsBuildPath}\\intermediate\\</IntermediateOutputPath><OutputPath>$(MSBuildProjectDirectory)\\{foreignOutputMsBuildPath}\\output\\</OutputPath></PropertyGroup><Target Name=\"NavigatorReadOnlyProbe\" BeforeTargets=\"ResolveReferences\"><WriteLinesToFile File=\"$(NavigatorAnalysisScratchRoot)\\{markerName}\" Lines=\"$(IntermediateOutputPath)|$(OutputPath)\" Overwrite=\"true\" /><WriteLinesToFile File=\"$(IntermediateOutputPath)probe.txt\" Lines=\"intermediate\" Overwrite=\"true\" /><WriteLinesToFile File=\"$(OutputPath)probe.txt\" Lines=\"output\" Overwrite=\"true\" /></Target></Project>");
        tempDir.CreateFile("Directory.Build.targets", "<Project><Import Project=\"build/ReadOnlyProbe.targets\" /></Project>");
        await File.WriteAllTextAsync(tempDir.GetPath("src/App/App.cs"), "namespace App; public sealed class AppType;");
        var solutionPath = await WriteSolutionAsync(tempDir, "src/App/App.csproj");
        var before = SnapshotFilesAndDirectories(tempDir.DirectoryPath);
        string[] afterRepair;
        var processScratchRoot = Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator", "msbuild-analysis", Environment.ProcessId.ToString());
        var scratchEntriesBefore = Directory.Exists(processScratchRoot)
            ? Directory.EnumerateDirectories(processScratchRoot).Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

        await using (var resident = MSBuildSolutionLoader.CreateResidentSolution(solutionPath))
        {
            await resident.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(resident.IsLoaded);
            Assert.Contains("OutputPath", resident.LoadFailure?.Message, StringComparison.Ordinal);
            Assert.Contains(foreignOutputRoot, resident.LoadFailure?.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(foreignOutputRoot));
            Assert.False(Directory.Exists(processScratchRoot)
                && Directory.EnumerateFiles(processScratchRoot, markerName, SearchOption.AllDirectories).Any());
            var scratchEntriesAfterFailure = Directory.Exists(processScratchRoot)
                ? Directory.EnumerateDirectories(processScratchRoot).Order(StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
            Assert.Equal(scratchEntriesBefore, scratchEntriesAfterFailure);
            Assert.Equal(before, SnapshotFilesAndDirectories(tempDir.DirectoryPath));

            // Removing the unsupported redirection proves that the same resident can recover after preflight failure.
            await File.WriteAllTextAsync(tempDir.GetPath("Directory.Build.targets"), "<Project />");
            afterRepair = SnapshotFilesAndDirectories(tempDir.DirectoryPath);
            var loaded = await resident.GetCurrentSnapshotAsync();
            Assert.True(loaded.Succeeded, loaded.Error?.Message);
            Assert.Single(loaded.Solution!.Projects);
        }

        MSBuildSolutionLoader.CleanupDesignTimeScratch();

        Assert.Equal(afterRepair, SnapshotFilesAndDirectories(tempDir.DirectoryPath));
        Assert.False(Directory.Exists(foreignOutputRoot));
        Assert.False(Directory.Exists(processScratchRoot));
    }

    [Fact]
    [Trait("Category", "ExtendedIntegration")]
    public async Task MSBuildSolutionLoader_ColdSolutionsWithSameNamedProjectsHaveIsolatedSnapshots()
    {
        using var firstDirectory = TestTempDirectory.Create("integration-cold-same-name-a-");
        using var secondDirectory = TestTempDirectory.Create("integration-cold-same-name-b-");
        var firstSolutionPath = await CreateColdSameNamedSolution(firstDirectory, "FirstMarker");
        var secondSolutionPath = await CreateColdSameNamedSolution(secondDirectory, "SecondMarker");
        var firstBefore = SnapshotFilesAndDirectories(firstDirectory.DirectoryPath);
        var secondBefore = SnapshotFilesAndDirectories(secondDirectory.DirectoryPath);
        var processScratchRoot = Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator", "msbuild-analysis", Environment.ProcessId.ToString());

        await using (var first = MSBuildSolutionLoader.CreateResidentSolution(firstSolutionPath))
        await using (var second = MSBuildSolutionLoader.CreateResidentSolution(secondSolutionPath))
        {
            await Task.WhenAll(first.LoadTask!, second.LoadTask!).WaitAsync(TimeSpan.FromSeconds(30));
            var firstSnapshot = await first.GetCurrentSnapshotAsync();
            var secondSnapshot = await second.GetCurrentSnapshotAsync();
            Assert.True(firstSnapshot.Succeeded, firstSnapshot.Error?.Message);
            Assert.True(secondSnapshot.Succeeded, secondSnapshot.Error?.Message);
            var firstProject = Assert.Single(firstSnapshot.Solution!.Projects);
            var secondProject = Assert.Single(secondSnapshot.Solution!.Projects);
            Assert.Equal("App", firstProject.Name);
            Assert.Equal("App", secondProject.Name);
            Assert.NotEqual(firstProject.Id, secondProject.Id);
            var firstSourcePath = firstProject.Documents.Single(document => Path.GetFileName(document.FilePath) == "App.cs").FilePath!;
            var secondSourcePath = secondProject.Documents.Single(document => Path.GetFileName(document.FilePath) == "App.cs").FilePath!;
            Assert.Contains("FirstMarker", (await File.ReadAllTextAsync(firstSourcePath)), StringComparison.Ordinal);
            Assert.Contains("SecondMarker", (await File.ReadAllTextAsync(secondSourcePath)), StringComparison.Ordinal);
        }

        MSBuildSolutionLoader.CleanupDesignTimeScratch();
        Assert.Equal(firstBefore, SnapshotFilesAndDirectories(firstDirectory.DirectoryPath));
        Assert.Equal(secondBefore, SnapshotFilesAndDirectories(secondDirectory.DirectoryPath));
        Assert.False(Directory.Exists(processScratchRoot));
    }

    [Fact]
    public async Task MSBuildSolutionLoader_DeepSameNamedProjectsUseShortIsolatedScratchPaths()
    {
        using var tempDir = TestTempDirectory.Create("integration-scratch-paths-");
        var projectPaths = new[]
        {
            "src/DeepCompanyProduct/DeepCompanyProduct/First/App/App.csproj",
            "src/DeepCompanyProduct/DeepCompanyProduct/Second/App/App.csproj",
        };
        foreach (var projectPath in projectPaths)
        {
            WriteProject(tempDir, projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <Target Name="CaptureNavigatorScratchPaths" BeforeTargets="ResolveReferences">
                    <WriteLinesToFile File="$(NavigatorAnalysisScratchRoot)/$([MSBuild]::StableStringHash('$(MSBuildProjectFullPath)|$(TargetFramework)')).paths"
                                      Lines="$(IntermediateOutputPath);$(OutputPath)" Overwrite="true" />
                  </Target>
                </Project>
                """);
            tempDir.CreateFile(Path.Combine(Path.GetDirectoryName(projectPath)!, "App.cs"), "namespace Sample; public sealed class AppType;");
        }
        var solutionPath = tempDir.CreateFile("Integration.slnx",
            $"<Solution><Folder Name=\"/First/\"><Project Path=\"{projectPaths[0]}\" /></Folder><Folder Name=\"/Second/\"><Project Path=\"{projectPaths[1]}\" /></Folder></Solution>");
        var before = SnapshotFilesAndDirectories(tempDir.DirectoryPath);
        var (solution, workspace) = await MSBuildSolutionLoader.LoadSolutionAsync(solutionPath);
        using (workspace)
        {
            Assert.Equal(2, solution.Projects.Count());
            var scratchRoot = ((Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace)workspace).Properties["NavigatorAnalysisScratchRoot"];
            var captures = Directory.GetFiles(scratchRoot, "*.paths").Select(File.ReadAllLines).ToArray();
            Assert.Equal(2, captures.Length);
            Assert.Equal(2, captures.Select(paths => paths[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(2, captures.Select(paths => paths[1]).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var paths in captures)
            {
                Assert.Equal(2, paths.Length);
                foreach (var path in paths)
                {
                    Assert.StartsWith(scratchRoot, path, StringComparison.OrdinalIgnoreCase);
                    var generatedFile = Path.Combine(path, ".NETFramework,Version=v4.8.AssemblyAttributes.cs");
                    Assert.True(generatedFile.Length < 260, $"Design-time artifact exceeds legacy path limit: {generatedFile}");
                }
                Assert.True(Directory.Exists(paths[0]));
            }
        }
        Assert.Equal(before, SnapshotFilesAndDirectories(tempDir.DirectoryPath));
    }

    [Fact]
    public async Task MSBuildSolutionLoader_ResidentScratchIsReleasedOnReloadAndDisposal()
    {
        using var tempDir = TestTempDirectory.Create("integration-scratch-lifetime-");
        var projectPath = tempDir.CreateFile("App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var solutionPath = await WriteSolutionAsync(tempDir, "App.csproj");
        var processRoot = Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator", "msbuild-analysis", Environment.ProcessId.ToString());
        var rootsBefore = Directory.Exists(processRoot) ? Directory.GetDirectories(processRoot) : [];
        var resident = MSBuildSolutionLoader.CreateResidentSolution(solutionPath);
        string? currentRoot = null;
        try
        {
            await resident.LoadTask!;
            var initial = await resident.GetCurrentSnapshotAsync();
            Assert.True(initial.Succeeded, initial.Error?.Message);
            var workspace = Assert.IsType<Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace>(initial.Solution!.Workspace);
            var firstRoot = workspace.Properties["NavigatorAnalysisScratchRoot"];
            var parent = Path.GetDirectoryName(firstRoot)!;
            var entries = Directory.GetDirectories(parent).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            Assert.Single(entries.Except(rootsBefore, StringComparer.OrdinalIgnoreCase));
            for (var iteration = 0; iteration < 3; iteration++)
            {
                var snapshot = await resident.GetCurrentSnapshotAsync();
                Assert.True(snapshot.Succeeded, snapshot.Error?.Message);
            }
            Assert.Equal(entries, Directory.GetDirectories(parent).Order(StringComparer.OrdinalIgnoreCase).ToArray());
            await File.WriteAllTextAsync(projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>Changed</AssemblyName></PropertyGroup></Project>");
            var refreshed = await resident.GetCurrentSnapshotAsync();
            Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
            currentRoot = Assert.IsType<Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace>(refreshed.Solution!.Workspace).Properties["NavigatorAnalysisScratchRoot"];
            Assert.NotEqual(firstRoot, currentRoot);
            Assert.False(Directory.Exists(firstRoot));
            Assert.True(Directory.Exists(currentRoot));
        }
        finally
        {
            await resident.DisposeAsync();
        }
        Assert.False(Directory.Exists(currentRoot));
    }

    [Fact]
    public async Task MSBuildSolutionLoader_ScratchPathsAreStableAndIsolateBuildVariants()
    {
        MSBuildSolutionLoader.EnsureMSBuildRegistered();
        using var tempDir = TestTempDirectory.Create("integration-scratch-variants-");
        var projectPath = tempDir.CreateFile("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks></PropertyGroup>
              <Target Name="CaptureNavigatorScratchPaths" BeforeTargets="ResolveReferences">
                <WriteLinesToFile File="$(NavigatorAnalysisScratchRoot)/variant.paths" Lines="$(IntermediateOutputPath)" Overwrite="true" />
              </Target>
            </Project>
            """);
        var properties = MSBuildSolutionLoader.CreateWorkspaceProperties();
        var outputPaths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configuration in new[] { "Debug", "Release" })
        foreach (var framework in new[] { "net10.0", "net10.0-windows" })
        {
            properties["Configuration"] = configuration;
            properties["TargetFramework"] = framework;
            using var workspace = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create(properties);
            await workspace.OpenProjectAsync(projectPath);
            Assert.DoesNotContain(workspace.Diagnostics, diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure);
            var capturePath = Path.Combine(properties["NavigatorAnalysisScratchRoot"], "variant.paths");
            var intermediatePath = (await File.ReadAllTextAsync(capturePath)).Trim();
            Assert.True(outputPaths.Add(intermediatePath), $"Build variants share scratch output: {intermediatePath}");
            using var reloaded = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create(properties);
            await reloaded.OpenProjectAsync(projectPath);
            Assert.DoesNotContain(reloaded.Diagnostics, diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure);
            Assert.Equal(intermediatePath, (await File.ReadAllTextAsync(capturePath)).Trim());
        }
    }

    private static string WriteProject(TestTempDirectory tempDir, string relativePath, string content) => tempDir.CreateFile(relativePath, content);

    private static async Task<string> CreateColdSameNamedSolution(TestTempDirectory tempDir, string marker)
    {
        WriteProject(
            tempDir,
            "src/App/App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(tempDir.GetPath("src/App/App.cs"), $"namespace App; public sealed class {marker};");
        return await WriteSolutionAsync(tempDir, "src/App/App.csproj");
    }

    private static string[] SnapshotFilesAndDirectories(string root)
    {
        var directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Select(path => $"D:{Path.GetRelativePath(root, path)}");
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => $"F:{Path.GetRelativePath(root, path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}");
        return directories.Concat(files).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<string> WriteSolutionAsync(TestTempDirectory tempDir, params string[] projectPaths)
    {
        var solutionPath = tempDir.GetPath("Integration.slnx");
        await File.WriteAllTextAsync(solutionPath, SolutionXml(projectPaths));
        return solutionPath;
    }

    private static string SolutionXml(params string[] projectPaths) =>
        "<Solution>" + string.Concat(projectPaths.Select(path => $"<Project Path=\"{path}\" />")) + "</Solution>";
}
