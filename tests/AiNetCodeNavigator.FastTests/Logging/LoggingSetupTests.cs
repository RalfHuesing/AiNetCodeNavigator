#nullable enable

namespace AiNetCodeNavigator.FastTests.Logging;

using System;
using System.IO;
using System.Threading.Tasks;
using AiNetCodeNavigator.Logging;
using Serilog;
using Xunit;

public class LoggingSetupTests
{
    [Fact]
    public async Task LoggingSetup_InitializesDirectoryAndLogsCorrectly()
    {
        var tempLogDir = Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator_LogTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            LoggingSetup.Initialize(command: "test-cmd", customLogDirectory: tempLogDir);

            Assert.True(Directory.Exists(tempLogDir));
            Assert.Equal(tempLogDir, LoggingSetup.ActiveLogDirectory);

            Log.Information("Test info message");
            Log.Error("Test error message");

            await LoggingSetup.CloseAndFlushAsync();

            var logFiles = Directory.GetFiles(tempLogDir, "ainetcodenavigator-*.log");
            Assert.Single(logFiles);

            var logContent = await File.ReadAllTextAsync(logFiles[0]);
            Assert.Contains("Test info message", logContent, StringComparison.Ordinal);
            Assert.Contains("Test error message", logContent, StringComparison.Ordinal);
            Assert.Contains("test-cmd", logContent, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempLogDir))
            {
                try { Directory.Delete(tempLogDir, recursive: true); } catch { /* ignore */ }
            }
        }
    }
}
