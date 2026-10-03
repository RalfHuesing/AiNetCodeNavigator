using AiNetCodeNavigator.Cli;
using AiNetCodeNavigator.Configuration;
using AiNetCodeNavigator.Logging;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Assemblies;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.Mcp.Tools;
using AiNetCodeNavigator.Mcp.Validation;
using AiNetCodeNavigator.Mcp.TrafficCapture;
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

            await using var captureSession = CreateCaptureSession(startup.TrafficCapture, serverInfo.Name, serverInfo.Version);

            var mcpServerBuilder = builder.Services
                .AddMcpServer(options => options.ServerInfo = serverInfo);
            ConfigureTransport(mcpServerBuilder, captureSession)
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

    private static TrafficCaptureSession? CreateCaptureSession(
        AiNetCodeNavigator.Configuration.TrafficCaptureOptions options,
        string serverName,
        string serverVersion)
    {
        if (!options.Enabled) return null;
        try
        {
            return new TrafficCaptureSession(
                LoggingSetup.ActiveLogDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs"),
                options,
                serverName,
                serverVersion);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "MCP traffic capture could not start; continuing without capture.");
            return null;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The MCP stream transport owns and disposes the wrappers when its host stops.")]
    internal static IMcpServerBuilder ConfigureTransport(
        IMcpServerBuilder builder,
        TrafficCaptureSession? captureSession,
        Stream? inputStream = null,
        Stream? outputStream = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (captureSession is null)
        {
            if (inputStream is not null || outputStream is not null)
            {
                if (inputStream is null || outputStream is null) throw new ArgumentException("Both test transport streams must be supplied.");
                return builder.WithStreamServerTransport(inputStream, outputStream);
            }

            return builder.WithStdioServerTransport();
        }

        var maxFrameBytes = captureSession.MaximumFrameBytes;
        var input = inputStream ?? Console.OpenStandardInput();
        var output = outputStream ?? Console.OpenStandardOutput();
        return builder.WithStreamServerTransport(
            new TrafficCaptureStream(input, captureSession.RecordInbound, maxFrameBytes, leaveOpen: true, captureSession.StopForOversizedFrame),
            new TrafficCaptureStream(output, captureSession.RecordOutbound, maxFrameBytes, leaveOpen: true, captureSession.StopForOversizedFrame));
    }

    private static string GetDefaultConfigurationPath()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "AiNetCodeNavigator", "hostsettings.json");
    }
}
