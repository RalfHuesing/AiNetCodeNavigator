using AiNetCodeNavigator.Cli;
using AiNetCodeNavigator.Configuration;
using AiNetCodeNavigator.Logging;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Assemblies;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.Mcp.Tools;
using AiNetCodeNavigator.Mcp.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Serilog;

namespace AiNetCodeNavigator.Mcp;

internal static class McpServerHost
{
    internal static Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default) =>
        CommandLineOptions.InvokeAsync(args, StartAsync, cancellationToken);

    private static async Task<int> StartAsync(string? configuredPath, bool isDefaultPath, CancellationToken cancellationToken)
    {
        var path = configuredPath ?? GetDefaultConfigurationPath();
        var minimumLevelSwitch = LoggingSetup.Initialize(command: "mcp");
        var configuration = new NavigatorHostConfiguration(path, isDefaultPath, minimumLevelSwitch);
        var startup = await configuration.LoadStartupAsync(cancellationToken).ConfigureAwait(false);
        if (!startup.Succeeded)
        {
            Log.Error("Host configuration could not be loaded: {ErrorCode}", startup.ErrorCode);
            Console.Error.WriteLine($"{startup.ErrorCode}: {startup.Message}");
            await LoggingSetup.CloseAndFlushAsync().ConfigureAwait(false);
            return 2;
        }

        try
        {
            var serverInfo = ServerBuildIdentity.Create();
            Log.Information("Starting MCP server {ServerName} {ServerVersion}", serverInfo.Name, serverInfo.Version);
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(Log.Logger, dispose: false);
            builder.Services.AddSingleton<NavigatorHostRuntime>(serviceProvider => new NavigatorHostRuntime(
                serviceProvider.GetRequiredService<IHostApplicationLifetime>()));

            builder.Services
                .AddMcpServer(options => options.ServerInfo = serverInfo)
                .WithStdioServerTransport()
                .WithTools<SymbolTools>()
                .WithTools<StructureTools>()
                .WithTools<RelationshipTools>()
                .WithTools<AssemblyTools>()
                .WithRequestFilters(McpArgumentValidationFilter.Configure);

            using (var host = builder.Build())
            {
                await host.RunAsync(cancellationToken).ConfigureAwait(false);
            }

            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "The MCP stdio host stopped unexpectedly.");
            return 1;
        }
        finally
        {
            await LoggingSetup.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }

    private static string GetDefaultConfigurationPath()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "AiNetCodeNavigator", "hostsettings.json");
    }
}
