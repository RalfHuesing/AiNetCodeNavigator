using AiNetCodeNavigator.Configuration;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Formatting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Serilog.Core;
using Serilog.Events;

namespace AiNetCodeNavigator.FastTests.Mcp;

[CollectionDefinition(HostRuntimeIsolationCollection.Name, DisableParallelization = true)]
public sealed class HostRuntimeIsolationCollection
{
    public const string Name = "Process-wide host runtime lifecycle";
}

[Collection(HostRuntimeIsolationCollection.Name)]
public sealed class NavigatorHostConfigurationTests
{
    [Fact]
    public void ServerBuildIdentity_UsesInformationalVersionAndMcpName()
    {
        var implementation = ServerBuildIdentity.Create();

        Assert.Equal("AiNetCodeNavigator", implementation.Name);
        Assert.Equal(ServerBuildIdentity.GetVersion(typeof(ServerBuildIdentity).Assembly), implementation.Version);
        Assert.NotEqual("unknown", implementation.Version);
    }

    [Theory]
    [InlineData("Verbose", LogEventLevel.Verbose)]
    [InlineData("Debug", LogEventLevel.Debug)]
    [InlineData("Information", LogEventLevel.Information)]
    [InlineData("Warning", LogEventLevel.Warning)]
    [InlineData("Error", LogEventLevel.Error)]
    [InlineData("Fatal", LogEventLevel.Fatal)]
    public async Task StartupLoadsConfiguredMinimumLogLevel(string configuredLevel, LogEventLevel expectedLevel)
    {
        using var fixture = new ConfigurationFixture();
        await File.WriteAllTextAsync(fixture.Path, $"{{\"minimumLogLevel\":\"{configuredLevel}\"}}");

        var result = await fixture.Configuration.LoadStartupAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedLevel, result.MinimumLogLevel);
        Assert.Equal(expectedLevel, fixture.LevelSwitch.MinimumLevel);
        Assert.Equal(TrafficCaptureOptions.Default, result.TrafficCapture);
    }

    [Fact]
    public async Task StartupLoadsTrafficCaptureOptionsAndUsesDefaultsForOmittedNestedFields()
    {
        using var fixture = new ConfigurationFixture();
        await File.WriteAllTextAsync(fixture.Path,
            "{\"minimumLogLevel\":\"Warning\",\"trafficCapture\":{\"enabled\":true,\"retentionDays\":14,\"maxTotalBytes\":1073741824}}");

        var result = await fixture.Configuration.LoadStartupAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new TrafficCaptureOptions(true, 14, 1_073_741_824), result.TrafficCapture);
        Assert.Equal(LogEventLevel.Warning, result.MinimumLogLevel);
    }

    [Fact]
    public async Task StartupAllowsPartialTrafficCaptureObjectUsingBuiltInValues()
    {
        using var fixture = new ConfigurationFixture();
        await File.WriteAllTextAsync(fixture.Path, "{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":{\"enabled\":true}}");

        var result = await fixture.Configuration.LoadStartupAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new TrafficCaptureOptions(true, 7, 536_870_912), result.TrafficCapture);
    }

    [Fact]
    public async Task ShippedHostSettingsUsesDisabledCaptureDefaults()
    {
        var configPath = FindRepositoryFile("hostsettings.json");
        var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
        var configuration = new NavigatorHostConfiguration(configPath, isDefaultPath: false, levelSwitch);

        var result = await configuration.LoadStartupAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(LogEventLevel.Information, result.MinimumLogLevel);
        Assert.Equal(new TrafficCaptureOptions(false, 7, 536_870_912), result.TrafficCapture);
    }

    [Fact]
    public async Task TrafficCaptureAcceptsConfiguredUpperBounds()
    {
        using var fixture = new ConfigurationFixture();
        await File.WriteAllTextAsync(fixture.Path,
            "{\"minimumLogLevel\":\"Information\",\"trafficCapture\":{\"enabled\":true,\"retentionDays\":365,\"maxTotalBytes\":10737418240}}");

        var result = await fixture.Configuration.LoadStartupAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new TrafficCaptureOptions(true, 365, 10_737_418_240), result.TrafficCapture);
    }

    [Theory]
    [InlineData("{\"minimumLogLevel\":\"2\"}")]
    [InlineData("{\"minimumLogLevel\":\"Information,Warning\"}")]
    [InlineData("{\"minimumLogLevel\":\"information\"}")]
    [InlineData("{\"minimumLogLevel\":\"Trace\"}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"unknown\":true}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"minimumLogLevel\":\"Error\"}")]
    [InlineData("{\"minimumLogLevel\":2}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":null}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":{},\"trafficCapture\":{}}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":[]}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":{\"enabled\":1}}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":{\"enabled\":true,\"enabled\":false}}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":{\"unknown\":true}}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":{\"retentionDays\":0}}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":{\"retentionDays\":366}}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":{\"retentionDays\":2147483648}}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":{\"retentionDays\":1.5}}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":{\"maxTotalBytes\":0}}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":{\"maxTotalBytes\":10737418241}}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":{\"maxTotalBytes\":9223372036854775808}}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"trafficCapture\":{\"maxTotalBytes\":1.5}}")]
    [InlineData("{}")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task InvalidStartupSettingsFailWithoutChangingEffectiveLogLevel(string contents)
    {
        using var fixture = new ConfigurationFixture();
        await File.WriteAllTextAsync(fixture.Path, contents);

        var result = await fixture.Configuration.LoadStartupAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("CONFIG_INVALID", result.ErrorCode);
        Assert.Equal(LogEventLevel.Information, fixture.LevelSwitch.MinimumLevel);
    }

    [Fact]
    public async Task MissingDefaultFileUsesBuiltInDefaults()
    {
        using var fixture = new ConfigurationFixture(isDefaultPath: true);

        var result = await fixture.Configuration.LoadStartupAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(LogEventLevel.Information, result.MinimumLogLevel);
        Assert.Equal(TrafficCaptureOptions.Default, result.TrafficCapture);
        Assert.Equal(LogEventLevel.Information, fixture.LevelSwitch.MinimumLevel);
    }

    [Fact]
    public async Task MissingExplicitFileFailsStartup()
    {
        using var fixture = new ConfigurationFixture();

        var result = await fixture.Configuration.LoadStartupAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("CONFIG_NOT_FOUND", result.ErrorCode);
        Assert.Equal(LogEventLevel.Information, fixture.LevelSwitch.MinimumLevel);
    }

    [Fact]
    public async Task LockedSettingsFileFailsStartupAsUnavailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new ConfigurationFixture();
        await using var lockedFile = new FileStream(fixture.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await lockedFile.WriteAsync("{\"minimumLogLevel\":\"Debug\"}"u8.ToArray());
        await lockedFile.FlushAsync();

        var result = await fixture.Configuration.LoadStartupAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("CONFIG_UNAVAILABLE", result.ErrorCode);
        Assert.Equal(LogEventLevel.Information, fixture.LevelSwitch.MinimumLevel);
    }

    [Fact]
    public async Task HostStoppingCancelsActiveOperationBeforeRuntimeDisposal()
    {
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        using var host = builder.Build();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        await using var store = new LongRunningToolCallStore(TimeSpan.FromMilliseconds(20), lifetime.ApplicationStopping);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<CallToolResult> Work(CancellationToken token)
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return McpToolResults.Success("unreachable");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancelled.TrySetResult();
                throw;
            }
        }

        var request = new LongRunningToolCallRequest("test", "target", "args", (_, token) => Work(token));
        var firstResponse = await store.RunAsync(request);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(firstResponse.IsError ?? false);

        lifetime.StopApplication();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class ConfigurationFixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ainet-navigator-config-" + Guid.NewGuid().ToString("N"));

        internal ConfigurationFixture(bool isDefaultPath = false)
        {
            Directory.CreateDirectory(directory);
            Path = System.IO.Path.Combine(directory, "settings.json");
            LevelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
            Configuration = new NavigatorHostConfiguration(Path, isDefaultPath, LevelSwitch);
        }

        internal string Path { get; }
        internal LoggingLevelSwitch LevelSwitch { get; }
        internal NavigatorHostConfiguration Configuration { get; }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }

    private static string FindRepositoryFile(string fileName)
    {
        foreach (var startPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(startPath);
            while (directory is not null)
            {
                var candidate = System.IO.Path.Combine(directory.FullName, fileName);
                if (File.Exists(candidate) && File.Exists(System.IO.Path.Combine(directory.FullName, "AiNetCodeNavigator.slnx")))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing hostsettings.json.");
    }
}
