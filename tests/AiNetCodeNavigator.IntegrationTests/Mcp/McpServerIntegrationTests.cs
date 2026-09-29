namespace AiNetCodeNavigator.IntegrationTests.Mcp;

using System.Diagnostics;
using AiNetCodeNavigator.Logging;
using AiNetCodeNavigator.Mcp;

public sealed class McpServerIntegrationTests
{
    [Fact]
    public void McpServerHost_TypeIsAvailable()
    {
        Assert.NotNull(typeof(McpServerHost));
    }

    [Fact]
    public async Task ProgramEntryPoint_InitializesFileLoggingWithoutConsoleOutput()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var hostOutputDirectory = Path.Combine(repositoryRoot, "src", "AiNetCodeNavigator", "bin", "Debug", "net10.0");
        var hostAssemblyPath = Path.Combine(hostOutputDirectory, "AiNetCodeNavigator.dll");
        Assert.True(File.Exists(hostAssemblyPath), $"Host assembly not found: {hostAssemblyPath}");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(hostAssemblyPath);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        Assert.Equal(0, process.ExitCode);
        Assert.Empty(stdout);
        Assert.Empty(stderr);

        var logDirectory = Path.Combine(hostOutputDirectory, "logs");
        Assert.True(Directory.Exists(logDirectory));
        var logFile = Assert.Single(Directory.GetFiles(logDirectory, "ainetcodenavigator-*.log"));
        var logContent = await File.ReadAllTextAsync(logFile);
        Assert.Contains("logging initialized", logContent, StringComparison.Ordinal);
        Assert.Contains("Command=mcp", logContent, StringComparison.Ordinal);
    }
}
