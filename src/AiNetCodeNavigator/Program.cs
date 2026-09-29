using AiNetCodeNavigator.Logging;

namespace AiNetCodeNavigator;

internal static class Program
{
    public static async Task<int> Main()
    {
        LoggingSetup.Initialize(command: "mcp");
        await LoggingSetup.CloseAndFlushAsync();
        return 0;
    }
}
