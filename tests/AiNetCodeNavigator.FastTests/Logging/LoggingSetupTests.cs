#nullable enable

namespace AiNetCodeNavigator.FastTests.Logging;

using System;
using System.IO;
using System.Threading.Tasks;
using AiNetCodeNavigator.Logging;
using Serilog;
using Xunit;

public sealed class LoggingSetupTests
{
    [Fact]
    public async Task LoggingSetup_WritesDailyFileAndErrorsToStderrWithoutStdout()
    {
        using var tempDirectory = TestTempDirectory.Create("logging-setup-");
        var logDirectory = tempDirectory.GetPath("logs");
        using var capturedStdout = new StringWriter();
        using var capturedStderr = new StringWriter();
        var originalStdout = Console.Out;
        var originalStderr = Console.Error;

        try
        {
            Console.SetOut(capturedStdout);
            Console.SetError(capturedStderr);
            LoggingSetup.Initialize(command: "test-cmd", customLogDirectory: logDirectory);

            Log.Information("Test info message");
            Log.Error("Test error message");
            Log.Fatal(new InvalidOperationException("Test fatal details"), "Test fatal message");

            await LoggingSetup.CloseAndFlushAsync();
        }
        finally
        {
            Console.SetOut(originalStdout);
            Console.SetError(originalStderr);
        }

        Assert.True(Directory.Exists(logDirectory));
        Assert.Equal(logDirectory, LoggingSetup.ActiveLogDirectory);
        Assert.Empty(capturedStdout.ToString());
        Assert.Contains("Test error message", capturedStderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("Test fatal message", capturedStderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("Test fatal details", capturedStderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Test info message", capturedStderr.ToString(), StringComparison.Ordinal);

        var logFile = Assert.Single(Directory.GetFiles(logDirectory, "ainetcodenavigator-*.log"));
        Assert.Matches("^ainetcodenavigator-\\d{8}\\.log$", Path.GetFileName(logFile));

        var logContent = await File.ReadAllTextAsync(logFile);
        Assert.Contains("Test info message", logContent, StringComparison.Ordinal);
        Assert.Contains("Test error message", logContent, StringComparison.Ordinal);
        Assert.Contains("Test fatal message", logContent, StringComparison.Ordinal);
        Assert.Contains("Test fatal details", logContent, StringComparison.Ordinal);
        Assert.Contains("test-cmd", logContent, StringComparison.Ordinal);
    }
}
