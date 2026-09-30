using AiNetCodeNavigator.Logging;
using AiNetCodeNavigator.Mcp;

namespace AiNetCodeNavigator;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await McpServerHost.RunAsync(args).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"AiNetCodeNavigator failed to start: {exception.Message}");
            await LoggingSetup.CloseAndFlushAsync().ConfigureAwait(false);
            return 1;
        }
    }
}
