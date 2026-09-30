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

            var sourceTarget = Path.Combine(repositoryRoot, "AiNetCodeNavigator.slnx");
            await SendRequestAsync(process, 6, "tools/call", new { name = "get_server_health", arguments = new { targetPath = sourceTarget } }, timeout.Token);
            var targetedHealth = await ReadResponseAsync(process, 6, timeout.Token);
            var targetedHealthText = GetFirstText(targetedHealth);
            Assert.Contains("targetResident: false", targetedHealthText, StringComparison.Ordinal);
            Assert.Contains("targetState: not_resident", targetedHealthText, StringComparison.Ordinal);

            await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Debug\"}", timeout.Token);
            await SendRequestAsync(process, 7, "tools/call", new { name = "reload_config", arguments = new { } }, timeout.Token);
            var reload = await ReadResponseAsync(process, 7, timeout.Token);
            Assert.False(reload.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("minimumLogLevel: Debug", GetFirstText(reload), StringComparison.Ordinal);

            await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Information,Warning\"}", timeout.Token);
            await SendRequestAsync(process, 8, "tools/call", new { name = "reload_config", arguments = new { } }, timeout.Token);
            var rejectedReload = await ReadResponseAsync(process, 8, timeout.Token);
            Assert.True(rejectedReload.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("CONFIG_INVALID", GetFirstText(rejectedReload), StringComparison.Ordinal);

            await SendRequestAsync(process, 9, "tools/call", new { name = "get_server_health", arguments = new { } }, timeout.Token);
            var afterRejectedReload = await ReadResponseAsync(process, 9, timeout.Token);
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
}
