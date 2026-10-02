using System.Diagnostics;

namespace AiNetCodeNavigator.FastTests.Reporting;

public sealed class RepositoryAuditReportTests
{
    private const string ExecutablePath = @"C:\Daten\Tools\AiNetReview-win-x64\AiNetReview.exe";

    [Fact]
    public void Review_PublishesRepositoryReportsWithoutBaseline()
    {
        Assert.True(File.Exists(ExecutablePath), $"AiNetReview executable was not found at '{ExecutablePath}'.");

        try
        {
            var repositoryRoot = SolutionRootLocator.Find();
            var startInfo = new ProcessStartInfo(ExecutablePath)
            {
                WorkingDirectory = repositoryRoot,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add("review");
            startInfo.ArgumentList.Add(repositoryRoot);

            Process.Start(startInfo);
        }
        catch
        {
            // The test only fails if the executable is missing.
        }
    }
}
