using System.Diagnostics;
using System.Text.Json;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

public sealed class McpServerIntegrationTests
{
    [Fact]
    public async Task StdioHostCompletesHandshakeListsMaintenanceToolsServesCallsAndExitsOnEof()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var hostAssemblyPath = GetHostAssemblyPath(repositoryRoot);
        var configPath = Path.Combine(Path.GetTempPath(), "ainet-host-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Information\"}");

        using var process = StartHost(repositoryRoot, hostAssemblyPath, "--config", configPath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{},\"clientInfo\":{\"name\":\"integration-test\",\"version\":\"1.0\"}}}");
            await process.StandardInput.FlushAsync(timeout.Token);
            var initialize = await ReadResponseAsync(process, 1, timeout.Token);
            Assert.Equal("2025-03-26", initialize.GetProperty("result").GetProperty("protocolVersion").GetString());

            await SendNotificationAsync(process, "notifications/initialized", timeout.Token);
            await SendRequestAsync(process, 2, "tools/list", new { }, timeout.Token);
            var toolsResponse = await ReadResponseAsync(process, 2, timeout.Token);
            var tools = toolsResponse.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
                .ToArray();
            Assert.Equal(new[] { "get_server_health", "reload_config" }, tools.Order(StringComparer.Ordinal).ToArray());
            var reloadTool = toolsResponse.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == "reload_config");
            Assert.True(reloadTool.GetProperty("annotations").GetProperty("idempotentHint").GetBoolean());
            var healthSchema = toolsResponse.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == "get_server_health")
                .GetProperty("inputSchema").GetProperty("properties");
            Assert.Equal(512, healthSchema.GetProperty("maxResponseBytes").GetProperty("minimum").GetInt32());
            Assert.Equal(65_536, healthSchema.GetProperty("maxResponseBytes").GetProperty("maximum").GetInt32());
            Assert.Equal(1, healthSchema.GetProperty("maxResponseTokens").GetProperty("minimum").GetInt32());

            await SendRequestAsync(process, 3, "tools/call", new { name = "get_server_health", arguments = new { } }, timeout.Token);
            var health = await ReadResponseAsync(process, 3, timeout.Token);
            Assert.False(health.GetProperty("result").GetProperty("isError").GetBoolean());
            var healthText = GetFirstText(health);
            Assert.Contains("Navigator host: running", healthText, StringComparison.Ordinal);
            Assert.Contains("residentSolutions:", healthText, StringComparison.Ordinal);
            Assert.Contains("managedMemoryBytes:", healthText, StringComparison.Ordinal);
            Assert.Contains("minimumLogLevel: Information", healthText, StringComparison.Ordinal);

            await SendRequestAsync(process, 4, "tools/call", new { name = "get_server_health", arguments = new { maxResponseBytes = 512 } }, timeout.Token);
            var minimumBytesHealth = await ReadResponseAsync(process, 4, timeout.Token);
            Assert.False(minimumBytesHealth.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(GetFirstText(minimumBytesHealth)) <= 512);

            await SendRequestAsync(process, 5, "tools/call", new { name = "get_server_health", arguments = new { maxResponseBytes = 512, maxResponseTokens = 1 } }, timeout.Token);
            var tinyTokenResponse = await ReadResponseAsync(process, 5, timeout.Token);
            Assert.Equal(-32602, tinyTokenResponse.GetProperty("error").GetProperty("code").GetInt32());
            var tinyTokenMessage = tinyTokenResponse.GetProperty("error").GetProperty("message").GetString()!;
            Assert.Contains("response token budget is too small", tinyTokenMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ArgumentOutOfRangeException", tinyTokenMessage, StringComparison.Ordinal);

            await SendRequestAsync(process, 10, "tools/call", new { name = "get_server_health", arguments = new { maxResponseBytes = 512, maxResponseTokens = 40 } }, timeout.Token);
            var healthBudgetError = await ReadResponseAsync(process, 10, timeout.Token);
            Assert.Equal(-32602, healthBudgetError.GetProperty("error").GetProperty("code").GetInt32());

            await SendRequestAsync(process, 20, "tools/call", new { name = "get_server_health", arguments = new { maxResponseBytes = 512, maxResponseTokens = 90 } }, timeout.Token);
            healthBudgetError = await ReadResponseAsync(process, 20, timeout.Token);
            Assert.True(healthBudgetError.GetProperty("result").GetProperty("isError").GetBoolean());
            var healthBudgetText = GetFirstText(healthBudgetError);
            Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", healthBudgetText, StringComparison.Ordinal);
            Assert.DoesNotContain("continue at UTF-16 offset", healthBudgetText, StringComparison.Ordinal);
            var healthMinimumBytes = ReadIntegerLine(healthBudgetText, "minimumResponseBytes");
            var healthMinimumTokens = ReadIntegerLine(healthBudgetText, "minimumResponseTokens");
            Assert.True(healthMinimumBytes >= 512);
            Assert.True(healthMinimumTokens > 90);
            await SendRequestAsync(process, 11, "tools/call", new { name = "get_server_health", arguments = new { maxResponseBytes = healthMinimumBytes, maxResponseTokens = healthMinimumTokens } }, timeout.Token);
            var recoveredHealth = await ReadResponseAsync(process, 11, timeout.Token);
            Assert.False(recoveredHealth.GetProperty("result").GetProperty("isError").GetBoolean());
            var recoveredHealthText = GetFirstText(recoveredHealth);
            Assert.DoesNotContain("completeness=truncated", recoveredHealthText, StringComparison.Ordinal);
            foreach (var requiredField in new[]
            {
                "uptimeSeconds:", "residentSolutions:", "loadingSolutions:", "residentAssemblySessions:",
                "activeAssemblyAccesses:", "handoffHandles:", "cacheHits:", "cacheMisses:", "cachedSyntaxTrees:",
                "cachedCompilations:", "managedMemoryBytes:", "settingsVersion:", "minimumLogLevel:",
            })
            {
                Assert.Contains(requiredField, recoveredHealthText, StringComparison.Ordinal);
            }

            var sourceTarget = Path.Combine(repositoryRoot, "AiNetCodeNavigator.slnx");
            await SendRequestAsync(process, 6, "tools/call", new { name = "get_server_health", arguments = new { targetPath = sourceTarget } }, timeout.Token);
            var targetedHealth = await ReadResponseAsync(process, 6, timeout.Token);
            var targetedHealthText = GetFirstText(targetedHealth);
            Assert.Contains("targetResident: false", targetedHealthText, StringComparison.Ordinal);
            Assert.Contains("targetState: not_resident", targetedHealthText, StringComparison.Ordinal);

            await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Debug\"}", timeout.Token);
            await SendRequestAsync(process, 12, "tools/call", new { name = "reload_config", arguments = new { maxResponseTokens = 1 } }, timeout.Token);
            var oneTokenReload = await ReadResponseAsync(process, 12, timeout.Token);
            Assert.Equal(-32602, oneTokenReload.GetProperty("error").GetProperty("code").GetInt32());
            Assert.DoesNotContain("ArgumentOutOfRangeException", oneTokenReload.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);

            await SendRequestAsync(process, 13, "tools/call", new { name = "get_server_health", arguments = new { } }, timeout.Token);
            var afterOneTokenReload = await ReadResponseAsync(process, 13, timeout.Token);
            var afterOneTokenReloadText = GetFirstText(afterOneTokenReload);
            Assert.Contains("settingsVersion: 1", afterOneTokenReloadText, StringComparison.Ordinal);
            Assert.Contains("minimumLogLevel: Information", afterOneTokenReloadText, StringComparison.Ordinal);

            await SendRequestAsync(process, 14, "tools/call", new { name = "reload_config", arguments = new { maxResponseTokens = 22 } }, timeout.Token);
            var narrowReload = await ReadResponseAsync(process, 14, timeout.Token);
            Assert.Equal(-32602, narrowReload.GetProperty("error").GetProperty("code").GetInt32());

            await SendRequestAsync(process, 15, "tools/call", new { name = "get_server_health", arguments = new { } }, timeout.Token);
            var afterNarrowReload = await ReadResponseAsync(process, 15, timeout.Token);
            var afterNarrowReloadText = GetFirstText(afterNarrowReload);
            Assert.Contains("settingsVersion: 1", afterNarrowReloadText, StringComparison.Ordinal);
            Assert.Contains("minimumLogLevel: Information", afterNarrowReloadText, StringComparison.Ordinal);

            await SendRequestAsync(process, 16, "tools/call", new { name = "reload_config", arguments = new { } }, timeout.Token);
            var reload = await ReadResponseAsync(process, 16, timeout.Token);
            Assert.False(reload.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("version: 2", GetFirstText(reload), StringComparison.Ordinal);
            Assert.Contains("minimumLogLevel: Debug", GetFirstText(reload), StringComparison.Ordinal);

            await SendRequestAsync(process, 17, "tools/call", new { name = "reload_config", arguments = new { } }, timeout.Token);
            var repeatedReload = await ReadResponseAsync(process, 17, timeout.Token);
            Assert.False(repeatedReload.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("version: 2", GetFirstText(repeatedReload), StringComparison.Ordinal);

            await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Information,Warning\"}", timeout.Token);
            await SendRequestAsync(process, 18, "tools/call", new { name = "reload_config", arguments = new { } }, timeout.Token);
            var rejectedReload = await ReadResponseAsync(process, 18, timeout.Token);
            Assert.True(rejectedReload.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("CONFIG_INVALID", GetFirstText(rejectedReload), StringComparison.Ordinal);

            await SendRequestAsync(process, 19, "tools/call", new { name = "get_server_health", arguments = new { } }, timeout.Token);
            var afterRejectedReload = await ReadResponseAsync(process, 19, timeout.Token);
            var afterRejectedReloadText = GetFirstText(afterRejectedReload);
            Assert.Contains("settingsVersion: 2", afterRejectedReloadText, StringComparison.Ordinal);
            Assert.Contains("minimumLogLevel: Debug", afterRejectedReloadText, StringComparison.Ordinal);

            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await process.StandardOutput.ReadToEndAsync(timeout.Token));
            var stderr = await stderrTask;
            Assert.Contains("response token budget is too small", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ArgumentOutOfRangeException", stderr, StringComparison.Ordinal);

            var hostOutputDirectory = Path.GetDirectoryName(hostAssemblyPath)!;
            var logDirectory = Path.Combine(hostOutputDirectory, "logs");
            Assert.True(Directory.Exists(logDirectory));
            var logFile = Assert.Single(Directory.GetFiles(logDirectory, "ainetcodenavigator-*.log"));
            var logContent = await File.ReadAllTextAsync(logFile, timeout.Token);
            Assert.Contains("logging initialized", logContent, StringComparison.Ordinal);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task CommandLineErrorsGoToStderrAndNeverProtocolStdout()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        using var process = StartHost(repositoryRoot, GetHostAssemblyPath(repositoryRoot), "--unknown-option");
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotEqual(0, process.ExitCode);
        Assert.Empty(await process.StandardOutput.ReadToEndAsync());
        Assert.Contains("--unknown-option", await stderrTask, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReloadBudgetRejectionsPreserveConfigurationBeforeRetry()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var configPath = Path.Combine(Path.GetTempPath(), "ainet-host-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Information\"}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var process = await StartInitializedHostAsync(repositoryRoot, GetHostAssemblyPath(repositoryRoot), configPath, timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Debug\"}", timeout.Token);
            await SendRequestAsync(process, 2, "tools/call", new { name = "reload_config", arguments = new { maxResponseTokens = 1 } }, timeout.Token);
            var tinyBudgetReload = await ReadResponseAsync(process, 2, timeout.Token);
            Assert.Equal(-32602, tinyBudgetReload.GetProperty("error").GetProperty("code").GetInt32());
            await AssertSettingsAsync(process, 3, timeout.Token, 1, "Information");

            await SendRequestAsync(process, 4, "tools/call", new { name = "reload_config", arguments = new { maxResponseTokens = 22 } }, timeout.Token);
            var narrowBudgetReload = await ReadResponseAsync(process, 4, timeout.Token);
            Assert.Equal(-32602, narrowBudgetReload.GetProperty("error").GetProperty("code").GetInt32());
            await AssertSettingsAsync(process, 5, timeout.Token, 1, "Information");

            await SendRequestAsync(process, 6, "tools/call", new { name = "reload_config", arguments = new { } }, timeout.Token);
            var recoveredReload = await ReadResponseAsync(process, 6, timeout.Token);
            Assert.False(recoveredReload.GetProperty("result").GetProperty("isError").GetBoolean());
            var recoveredText = GetFirstText(recoveredReload);
            Assert.Contains("version: 2", recoveredText, StringComparison.Ordinal);
            Assert.Contains("minimumLogLevel: Debug", recoveredText, StringComparison.Ordinal);
            Assert.DoesNotContain("completeness=truncated", recoveredText, StringComparison.Ordinal);
            await AssertSettingsAsync(process, 7, timeout.Token, 2, "Debug");

            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await process.StandardOutput.ReadToEndAsync(timeout.Token));
            _ = await stderrTask;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task IdenticalReloadsRetainVersionAndAdvertisedIdempotency()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var configPath = Path.Combine(Path.GetTempPath(), "ainet-host-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Information\"}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var process = await StartInitializedHostAsync(repositoryRoot, GetHostAssemblyPath(repositoryRoot), configPath, timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await SendRequestAsync(process, 2, "tools/list", new { }, timeout.Token);
            var toolList = await ReadResponseAsync(process, 2, timeout.Token);
            var reloadTool = toolList.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == "reload_config");
            Assert.True(reloadTool.GetProperty("annotations").GetProperty("idempotentHint").GetBoolean());

            for (var id = 3; id <= 4; id++)
            {
                await SendRequestAsync(process, id, "tools/call", new { name = "reload_config", arguments = new { } }, timeout.Token);
                var reload = await ReadResponseAsync(process, id, timeout.Token);
                Assert.False(reload.GetProperty("result").GetProperty("isError").GetBoolean());
                Assert.Contains("version: 1", GetFirstText(reload), StringComparison.Ordinal);
            }

            await AssertSettingsAsync(process, 5, timeout.Token, 1, "Information");
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await process.StandardOutput.ReadToEndAsync(timeout.Token));
            _ = await stderrTask;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            File.Delete(configPath);
        }
    }

    private static Process StartHost(string repositoryRoot, string hostAssemblyPath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(hostAssemblyPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start MCP host process.");
    }

    private static async Task<Process> StartInitializedHostAsync(string repositoryRoot, string hostAssemblyPath, string configPath, CancellationToken cancellationToken)
    {
        var process = StartHost(repositoryRoot, hostAssemblyPath, "--config", configPath);
        await SendRequestAsync(process, 1, "initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = "integration-test", version = "1.0" },
        }, cancellationToken);
        var initialize = await ReadResponseAsync(process, 1, cancellationToken);
        Assert.Equal("2025-03-26", initialize.GetProperty("result").GetProperty("protocolVersion").GetString());
        await SendNotificationAsync(process, "notifications/initialized", cancellationToken);
        return process;
    }

    private static async Task AssertSettingsAsync(Process process, int requestId, CancellationToken cancellationToken, int version, string logLevel)
    {
        await SendRequestAsync(process, requestId, "tools/call", new { name = "get_server_health", arguments = new { } }, cancellationToken);
        var response = await ReadResponseAsync(process, requestId, cancellationToken);
        var text = GetFirstText(response);
        Assert.Contains($"settingsVersion: {version}", text, StringComparison.Ordinal);
        Assert.Contains($"minimumLogLevel: {logLevel}", text, StringComparison.Ordinal);
    }

    private static string GetHostAssemblyPath(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "src", "AiNetCodeNavigator", "bin", "Debug", "net10.0", "AiNetCodeNavigator.dll");
        Assert.True(File.Exists(path), $"Host assembly not found: {path}");
        return path;
    }

    private static async Task<JsonElement> ReadResponseAsync(Process process, int id, CancellationToken cancellationToken)
    {
        while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var responseId) && responseId.GetInt32() == id)
            {
                return document.RootElement.Clone();
            }
        }

        throw new EndOfStreamException($"MCP host exited before responding to request {id}.");
    }

    private static Task SendRequestAsync(Process process, int id, string method, object parameters, CancellationToken cancellationToken) =>
        WriteLineAsync(process, JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }), cancellationToken);

    private static Task SendNotificationAsync(Process process, string method, CancellationToken cancellationToken) =>
        WriteLineAsync(process, JsonSerializer.Serialize(new { jsonrpc = "2.0", method }), cancellationToken);

    private static async Task WriteLineAsync(Process process, string line, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static string GetFirstText(JsonElement response) => response.GetProperty("result").GetProperty("content").EnumerateArray()
        .First(block => block.GetProperty("type").GetString() == "text")
        .GetProperty("text").GetString()!;

    private static int ReadIntegerLine(string text, string name)
    {
        var line = text.Split('\n').Single(value => value.StartsWith(name + ": ", StringComparison.Ordinal));
        return int.Parse(line[(name.Length + 2)..], System.Globalization.CultureInfo.InvariantCulture);
    }
}
