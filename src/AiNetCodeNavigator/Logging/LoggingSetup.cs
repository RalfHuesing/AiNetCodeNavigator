#nullable enable

namespace AiNetCodeNavigator.Logging;

using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Serilog.Events;

/// <summary>
/// Configures Serilog for the MCP host without writing protocol output to stdout.
/// Events are written to rolling files, with errors also sent to stderr.
/// </summary>
public static class LoggingSetup
{
    private const long FileSizeLimitBytes = 10 * 1024 * 1024; // 10 MiB
    private const string LogFileNameTemplate = "ainetcodenavigator-.log";
    private static string? _activeLogDirectory;

    public static string? ActiveLogDirectory => _activeLogDirectory;

    /// <summary>
    /// Initializes Serilog with file output and an error channel on stderr.
    /// </summary>
    public static LoggingLevelSwitch Initialize(string command = "mcp", string? customLogDirectory = null, LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        var logDirectory = customLogDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        _activeLogDirectory = logDirectory;

        var levelSwitch = new LoggingLevelSwitch(minimumLevel);
        var config = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
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
            .WriteTo.Sink(new StderrSink(Console.Error));

        Log.Logger = config.CreateLogger();
        Log.Information("AiNetCodeNavigator logging initialized. OutputDirectory={LogDirectory}, Command={Command}", logDirectory, command);
        return levelSwitch;
    }

    public static void SetMinimumLevel(LoggingLevelSwitch levelSwitch, LogEventLevel minimumLevel)
    {
        ArgumentNullException.ThrowIfNull(levelSwitch);
        levelSwitch.MinimumLevel = minimumLevel;
    }

    /// <summary>
    /// Closes and asynchronously flushes all active Serilog sinks.
    /// </summary>
    public static ValueTask CloseAndFlushAsync() => Log.CloseAndFlushAsync();

    private sealed class StderrSink : ILogEventSink
    {
        private readonly TextWriter _writer;

        public StderrSink(TextWriter writer)
        {
            _writer = writer;
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Level is not (LogEventLevel.Error or LogEventLevel.Fatal))
            {
                return;
            }

            var message = $"[{logEvent.Level.ToString().ToUpperInvariant()}] {logEvent.RenderMessage()}{(logEvent.Exception != null ? Environment.NewLine + logEvent.Exception : string.Empty)}";
            _writer.WriteLine(message);
        }
    }
}
