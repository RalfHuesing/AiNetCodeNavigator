#nullable enable

using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.Build.Locator;
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
        var solutionPath = tempDir.CreateFile("IntegrationApp.slnx", "");

        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        await using var resident = new ResidentSolution(fixture.Solution);

        await using var registry = new ProjectRegistry(new ProjectRegistryOptions(
            _ => ResidentSolutionCreation.Resident(resident),
            TimeProvider.System));

        var leaseResult = registry.Lease(solutionPath);
        Assert.True(leaseResult.Succeeded);
        using (leaseResult.Lease)
        {
            Assert.NotNull(leaseResult.Lease);
            Assert.Same(resident, leaseResult.Lease.ResidentSolution);
            Assert.Equal(solutionPath, leaseResult.Lease.RootPath);
        }
    }
}
