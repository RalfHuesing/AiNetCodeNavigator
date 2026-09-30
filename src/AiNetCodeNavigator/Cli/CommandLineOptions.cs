using System.CommandLine;
using System.CommandLine.Invocation;

namespace AiNetCodeNavigator.Cli;

internal static class CommandLineOptions
{
    internal static Task<int> InvokeAsync(
        string[] args,
        Func<string?, bool, CancellationToken, Task<int>> runAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(runAsync);

        var configOption = new Option<string?>("--config")
        {
            Description = "Path to the JSON host settings file.",
        };
        var command = new RootCommand("AiNetCodeNavigator MCP server (stdio transport).")
        {
            configOption,
        };
        command.SetAction((parseResult, token) =>
        {
            var configuredPath = parseResult.GetValue(configOption);
            return runAsync(configuredPath, configuredPath is null, token);
        });

        return command.Parse(args).InvokeAsync(new InvocationConfiguration
        {
            Output = Console.Error,
            Error = Console.Error,
        }, cancellationToken);
    }
}
