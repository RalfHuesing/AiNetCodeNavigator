using AiNetCodeNavigator.Configuration;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.Tools.Maintenance;
using AiNetCodeNavigator.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
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

    [Fact]
    public async Task MaintenanceHandlersDoNotLoadTargetsAndReloadPreflightsItsAcknowledgement()
    {
        using var fixture = new ConfigurationFixture();
        await File.WriteAllTextAsync(fixture.Path, "{\"minimumLogLevel\":\"Warning\"}");
        Assert.True((await fixture.Configuration.LoadStartupAsync(CancellationToken.None)).Succeeded);

        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(fixture.Configuration, host.Services.GetRequiredService<IHostApplicationLifetime>());
        var tools = new MaintenanceTools(runtime);
        using var target = TestTempDirectory.Create("ainet-health-target-");
        var solutionPath = target.CreateFile("Health.slnx", "<Solution />");
        var health = tools.GetServerHealth(solutionPath, maxResponseBytes: 512, maxResponseTokens: 512);
        var healthText = TextOf(health);
        Assert.False(health.IsError ?? false, healthText);
        Assert.Contains("targetResident: false", healthText, StringComparison.Ordinal);
        Assert.Empty(runtime.ProjectRegistry.Snapshots());

        var tinyHealth = Assert.Throws<McpProtocolException>(() => tools.GetServerHealth(maxResponseBytes: 512, maxResponseTokens: 1));
        Assert.Equal(McpErrorCode.InvalidParams, tinyHealth.ErrorCode);

        var smallHealth = tools.GetServerHealth(maxResponseBytes: 512, maxResponseTokens: 60);
        var smallHealthText = TextOf(smallHealth);
        Assert.True(smallHealth.IsError);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", smallHealthText, StringComparison.Ordinal);
        var healthMinimumBytes = ReadBudget(smallHealthText, "minimumResponseBytes");
        var healthMinimumTokens = ReadBudget(smallHealthText, "minimumResponseTokens");
        var recoveredHealth = tools.GetServerHealth(maxResponseBytes: healthMinimumBytes, maxResponseTokens: healthMinimumTokens);
        var recoveredHealthText = TextOf(recoveredHealth);
        Assert.False(recoveredHealth.IsError ?? false, recoveredHealthText);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(recoveredHealthText) <= healthMinimumBytes);
        Assert.True(McpResponseFormatter.CountTokens(recoveredHealthText) <= healthMinimumTokens);

        var missingTarget = tools.GetServerHealth(Path.Combine(target.DirectoryPath, "Missing.slnx"), 512, 512);
        var missingText = TextOf(missingTarget);
        Assert.True(missingTarget.IsError);
        Assert.Contains("INVALID_ARGUMENT", missingText, StringComparison.Ordinal);
        Assert.Contains("nextAction:", missingText, StringComparison.Ordinal);

        var previousSettings = fixture.Configuration.Current;
        await File.WriteAllTextAsync(fixture.Path, "{\"minimumLogLevel\":\"Error\"}");
        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await tools.ReloadConfig(CancellationToken.None, maxResponseBytes: 512, maxResponseTokens: 1));
        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Equal(previousSettings, fixture.Configuration.Current);
        Assert.Equal(LogEventLevel.Warning, fixture.LevelSwitch.MinimumLevel);

        await File.WriteAllTextAsync(fixture.Path, "{\"minimumLogLevel\":\"Trace\"}");
        var invalidBytes = 512;
        var invalidTokens = 80;
        var invalidReload = await tools.ReloadConfig(CancellationToken.None, maxResponseBytes: invalidBytes, maxResponseTokens: invalidTokens);
        Assert.True(invalidReload.IsError);
        var invalidReloadText = TextOf(invalidReload);
        if (invalidReloadText.Contains("RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
        {
            var minimumBytes = ReadBudget(invalidReloadText, "minimumResponseBytes");
            var minimumTokens = ReadBudget(invalidReloadText, "minimumResponseTokens");
            invalidBytes = minimumBytes;
            invalidTokens = minimumTokens;
            invalidReload = await tools.ReloadConfig(CancellationToken.None,
                maxResponseBytes: minimumBytes, maxResponseTokens: minimumTokens);
            invalidReloadText = TextOf(invalidReload);
        }
        Assert.Contains("CONFIG_INVALID", invalidReloadText, StringComparison.Ordinal);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(invalidReloadText) <= invalidBytes);
        Assert.True(McpResponseFormatter.CountTokens(invalidReloadText) <= invalidTokens);
        Assert.Equal(previousSettings, fixture.Configuration.Current);
        Assert.Equal(LogEventLevel.Warning, fixture.LevelSwitch.MinimumLevel);

        await File.WriteAllTextAsync(fixture.Path, "{\"minimumLogLevel\":\"Error\"}");
        var reloaded = await tools.ReloadConfig(CancellationToken.None, maxResponseBytes: 512, maxResponseTokens: 512);
        var reloadedText = TextOf(reloaded);
        Assert.False(reloaded.IsError ?? false, reloadedText);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(reloadedText) <= 512);
        Assert.True(McpResponseFormatter.CountTokens(reloadedText) <= 512);
        Assert.Equal(LogEventLevel.Error, fixture.Configuration.Current.MinimumLogLevel);
        var version = fixture.Configuration.Current.Version;
        var unchanged = await tools.ReloadConfig(CancellationToken.None);
        Assert.False(unchanged.IsError ?? false, TextOf(unchanged));
        Assert.Equal(version, fixture.Configuration.Current.Version);
    }

    private static string TextOf(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static int ReadBudget(string text, string name)
    {
        var prefix = name + ": ";
        var line = text.Split('\n').FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        Assert.True(line is not null, $"Missing {name} in response: {text}");
        return int.Parse(line![prefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
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
