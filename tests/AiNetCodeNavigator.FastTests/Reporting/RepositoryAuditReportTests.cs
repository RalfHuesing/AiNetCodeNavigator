using System.Diagnostics;
using System.Text.Json;

namespace AiNetCodeNavigator.FastTests.Reporting;

[Trait("Category", "E2EIntegration")]
public sealed class RepositoryAuditReportTests
{
    private const string ExecutablePath = @"C:\Daten\Tools\AiNetReview-win-x64\AiNetReview.exe";

    [Fact]
    public async Task Review_PublishesRepositoryReportsWithoutBaseline()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var outputDirectory = Path.Combine(repositoryRoot, "audit-reporting");
        var baselinePath = Path.Combine(outputDirectory, "baseline.json");
        Assert.True(File.Exists(ExecutablePath), $"AiNetReview executable was not found at '{ExecutablePath}'.");
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "ainetreview.json")), "The repository review configuration is required.");
        Assert.False(File.Exists(baselinePath), "Repository reports must run without a baseline. Remove baseline.json manually before running tests.");

        var startInfo = new ProcessStartInfo(ExecutablePath)
        {
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("review");
        startInfo.ArgumentList.Add(repositoryRoot);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("AiNetReview could not be started.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdoutTask, stderrTask);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        Assert.True(process.ExitCode == 0, $"AiNetReview failed with exit code {process.ExitCode}. stdout: {stdout}{Environment.NewLine}stderr: {stderr}");
        Assert.Empty(stderr);
        using var response = JsonDocument.Parse(stdout);
        Assert.Equal("completed", response.RootElement.GetProperty("status").GetString());
        var runId = response.RootElement.GetProperty("runId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(runId));
        Assert.Equal($"audit-reporting/{runId}/index.md", response.RootElement.GetProperty("indexPath").GetString());

        var runDirectory = Path.Combine(outputDirectory, runId);
        Assert.True(File.Exists(Path.Combine(runDirectory, "index.md")), "AiNetReview did not publish the report index.");
        foreach (var area in new[] { "production", "tests", "mixed" })
        {
            Assert.True(File.Exists(Path.Combine(runDirectory, area, "changed-files", "index.md")), $"AiNetReview did not publish the {area} working report view.");
            Assert.True(File.Exists(Path.Combine(runDirectory, area, "all-findings", "index.md")), $"AiNetReview did not publish the {area} complete report view.");
        }
        Assert.False(File.Exists(baselinePath), "AiNetReview unexpectedly created a baseline.");
    }
}
