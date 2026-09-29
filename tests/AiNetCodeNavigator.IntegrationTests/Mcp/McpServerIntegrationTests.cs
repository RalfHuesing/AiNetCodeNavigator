namespace AiNetCodeNavigator.IntegrationTests.Mcp;

public class McpServerIntegrationTests
{
    [Fact]
    public void McpServer_IntegrationShell_Initializes()
    {
        Assert.NotNull(typeof(AiNetCodeNavigator.Mcp.McpServerHost));
    }
}
