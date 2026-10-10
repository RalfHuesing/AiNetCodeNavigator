using System.CommandLine;
using System.CommandLine.Invocation;

namespace AiNetCodeNavigator.Cli;

internal static class CommandLineOptions
{
    internal static Task<int> InvokeAsync(
        string[] args,
        Func<string?, bool, CancellationToken, Task<int>> runAsync,
        CancellationToken cancellationToken = default,
        TextWriter? errorOutput = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(runAsync);
        var errors = errorOutput ?? Console.Error;

        var configOption = new Option<string?>("--config")
        {
            Description = "Path to the JSON host settings file.",
        };
        var docOption = new Option<string?>("--doc")
        {
            Description = "Read embedded documentation: topics, overview, setup, or tools. No server is started.",
            HelpName = "topic",
            Arity = ArgumentArity.ExactlyOne,
        };
        var command = new RootCommand("Read-only C# source and managed .NET assembly navigation for agents. " +
            "Start without arguments as an MCP server over stdio; configure your MCP client with this executable's absolute path. " +
            "Use --doc topics to discover offline documentation. Help, documentation and errors use stderr; stdout is reserved for MCP JSON-RPC.")
        {
            configOption,
            docOption,
        };
        command.Validators.Add(result =>
        {
            if (result.GetResult(docOption) is not null && result.GetResult(configOption) is not null)
                result.AddError("--doc cannot be combined with --config. Read documentation separately from starting the server.");
        });
        command.SetAction((parseResult, token) =>
        {
            if (parseResult.GetValue(docOption) is { } topic)
                return Task.FromResult(EmbeddedDocumentation.Write(topic, errors));
            var configuredPath = parseResult.GetValue(configOption);
            return runAsync(configuredPath, configuredPath is null, token);
        });

        return command.Parse(args).InvokeAsync(new InvocationConfiguration
        {
            Output = errors,
            Error = errors,
        }, cancellationToken);
    }
}
