using AiNetCodeNavigator.Configuration;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Formatting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using Serilog.Core;
using Serilog.Events;

namespace AiNetCodeNavigator.FastTests.Mcp;

public sealed class NavigatorHostConfigurationTests
{
    [Fact]
    public async Task ReloadPublishesValidSnapshotAndLogLevelTogether()
    {
        using var fixture = new ConfigurationFixture();
        await File.WriteAllTextAsync(fixture.Path, "{\"minimumLogLevel\":\"Debug\"}");

        var result = await fixture.Configuration.LoadStartupAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Settings.Version);
        Assert.Equal(LogEventLevel.Debug, result.Settings.MinimumLogLevel);
        Assert.Equal(LogEventLevel.Debug, fixture.LevelSwitch.MinimumLevel);

        await File.WriteAllTextAsync(fixture.Path, "{\"minimumLogLevel\":\"Error\"}");
        result = await fixture.Configuration.ReloadAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Settings.Version);
        Assert.Equal(LogEventLevel.Error, result.Settings.MinimumLogLevel);
        Assert.Equal(LogEventLevel.Error, fixture.LevelSwitch.MinimumLevel);
    }

    [Theory]
    [InlineData("{\"minimumLogLevel\":\"2\"}")]
    [InlineData("{\"minimumLogLevel\":\"Information,Warning\"}")]
    [InlineData("{\"minimumLogLevel\":\"information\"}")]
    [InlineData("{\"minimumLogLevel\":\"Trace\"}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"unknown\":true}")]
    [InlineData("{\"minimumLogLevel\":\"Debug\",\"minimumLogLevel\":\"Error\"}")]
    [InlineData("{")]
    public async Task FailedReloadPreservesPublishedVersionAndEffectiveLogLevel(string contents)
    {
        using var fixture = new ConfigurationFixture();
        await File.WriteAllTextAsync(fixture.Path, "{\"minimumLogLevel\":\"Warning\"}");
        var initial = await fixture.Configuration.LoadStartupAsync(CancellationToken.None);
        Assert.True(initial.Succeeded);

        await File.WriteAllTextAsync(fixture.Path, contents);
        var result = await fixture.Configuration.ReloadAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(initial.Settings, result.Settings);
        Assert.Equal(LogEventLevel.Warning, fixture.Configuration.Current.MinimumLogLevel);
        Assert.Equal(LogEventLevel.Warning, fixture.LevelSwitch.MinimumLevel);
    }

    [Fact]
    public async Task MissingDefaultFileUsesBuiltInDefaultsButExplicitReloadFailsWithoutChangingThem()
    {
        using var fixture = new ConfigurationFixture(isDefaultPath: true);

        var startup = await fixture.Configuration.LoadStartupAsync(CancellationToken.None);
        var reload = await fixture.Configuration.ReloadAsync(CancellationToken.None);

        Assert.True(startup.Succeeded);
        Assert.Equal(1, startup.Settings.Version);
        Assert.Equal(LogEventLevel.Information, startup.Settings.MinimumLogLevel);
        Assert.False(reload.Succeeded);
        Assert.Equal(startup.Settings, reload.Settings);
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

        var request = new LongRunningToolCallRequest("test", "target", "args", Work);
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

        public void Dispose()
        {
            Configuration.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}
