#nullable enable

namespace AiNetCodeNavigator.Logging;

using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Serilog.Events;

/// <summary>
/// Serilog-Initialisierung für den MCP-Host.
/// Garantiert, dass standardmäßig NIEMALS auf stdout geschrieben wird,
/// um das MCP JSON-RPC-Protokoll nicht zu beschädigen.
/// Ausgaben erfolgen in rotierende Logdateien und bei Fehlern optional auf stderr.
/// </summary>
public static class LoggingSetup
{
    private const long FileSizeLimitBytes = 10 * 1024 * 1024; // 10 MiB
    private const string LogFileNameTemplate = "ainetcodenavigator-.log";
    private static string? _activeLogDirectory;

    public static string? ActiveLogDirectory => _activeLogDirectory;

    /// <summary>
    /// Initialisiert Serilog mit Dateiausgabe und stderr-Kanal.
    /// </summary>
    public static void Initialize(string command = "mcp", string? customLogDirectory = null, LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        var logDirectory = customLogDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        _activeLogDirectory = logDirectory;

        var config = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Command", command)
            .WriteTo.File(
                Path.Combine(logDirectory, LogFileNameTemplate),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 30,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.Sink(new StderrSink(LogEventLevel.Error));

        Log.Logger = config.CreateLogger();
        Log.Information("AiNetCodeNavigator logging initialized. OutputDirectory={LogDirectory}, Command={Command}", logDirectory, command);
    }

    /// <summary>
    /// Schließt und flusht alle aktiven Serilog-Sinks asynchron.
    /// </summary>
    public static ValueTask CloseAndFlushAsync() => Log.CloseAndFlushAsync();

    private sealed class StderrSink : ILogEventSink
    {
        private readonly LogEventLevel _minimumLevel;

        public StderrSink(LogEventLevel minimumLevel)
        {
            _minimumLevel = minimumLevel;
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Level < _minimumLevel)
            {
                return;
            }

            var message = $"[{logEvent.Level:u3}] {logEvent.RenderMessage()}{(logEvent.Exception != null ? Environment.NewLine + logEvent.Exception : string.Empty)}";
            Console.Error.WriteLine(message);
        }
    }
}
