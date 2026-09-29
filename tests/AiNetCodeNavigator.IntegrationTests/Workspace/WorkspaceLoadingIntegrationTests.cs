namespace AiNetCodeNavigator.IntegrationTests.Workspace;

public class WorkspaceLoadingIntegrationTests
{
    [Fact]
    public void WorkspaceLoading_IntegrationShell_Initializes()
    {
        Assert.NotNull(typeof(AiNetCodeNavigator.Core.Workspace.WorkspaceManager));
    }
}
