using System.Diagnostics;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

internal static class FixtureRestore
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OutputTimeout = TimeSpan.FromSeconds(10);

    public static async Task RunAsync(string projectOrSolutionPath, string workingDirectory, string nugetConfigPath, string description)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(projectOrSolutionPath);
        startInfo.ArgumentList.Add("--configfile");
        startInfo.ArgumentList.Add(nugetConfigPath);
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {description}.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        var processExit = process.WaitForExitAsync();
        try
        {
            await processExit.WaitAsync(ProcessTimeout);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            try
            {
                await processExit.WaitAsync(OutputTimeout);
            }
            catch (TimeoutException)
            {
                ObserveFault(standardOutput);
                ObserveFault(standardError);
                throw new Xunit.Sdk.XunitException($"{description} did not stop within ten seconds after its process tree was terminated.");
            }

            var timeoutOutput = await ReadOutputAsync(standardOutput, standardError, description);
            throw new Xunit.Sdk.XunitException($"{description} timed out after two minutes.\n{timeoutOutput.StandardError}\n{timeoutOutput.StandardOutput}");
        }

        var output = await ReadOutputAsync(standardOutput, standardError, description);
        if (process.ExitCode != 0)
            throw new Xunit.Sdk.XunitException($"{description} failed with exit code {process.ExitCode}.\n{output.StandardError}\n{output.StandardOutput}");
    }

    private static async Task<(string StandardOutput, string StandardError)> ReadOutputAsync(
        Task<string> standardOutput,
        Task<string> standardError,
        string description)
    {
        try
        {
            await Task.WhenAll(standardOutput, standardError).WaitAsync(OutputTimeout);
        }
        catch (TimeoutException)
        {
            ObserveFault(standardOutput);
            ObserveFault(standardError);
            throw new TimeoutException($"{description} exited, but its redirected output streams did not close within ten seconds.");
        }

        return (await standardOutput, await standardError);
    }

    private static void ObserveFault(Task task) => _ = task.ContinueWith(
        static completed => _ = completed.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);
}
