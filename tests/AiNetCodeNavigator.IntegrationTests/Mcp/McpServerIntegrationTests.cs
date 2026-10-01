using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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

        using var process = StartHost(repositoryRoot, hostAssemblyPath, null, "--config", configPath);
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
            Assert.Equal(new[] { "dependency_graph", "find_assembly_extensions", "find_implementations", "find_references", "find_symbol", "get_assembly_context", "get_call_tree", "get_class_structure", "get_feature_context", "get_file_skeleton", "get_file_tree", "get_impact", "get_index_scope", "get_namespace_tree", "get_server_health", "get_symbol_body", "get_test_context", "get_type_hierarchy", "inspect_assembly", "reload_config", "resolve_type_origin", "search_assembly" }, tools.Order(StringComparer.Ordinal).ToArray());
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
            var logFile = Path.Combine(logDirectory, $"ainetcodenavigator-{DateTime.Now:yyyyMMdd}.log");
            Assert.True(File.Exists(logFile), $"The host did not write the current day's rolling log file: {logFile}");
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
        using var process = StartHost(repositoryRoot, GetHostAssemblyPath(repositoryRoot), null, "--unknown-option");
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

    [Fact]
    public async Task SourceAndAssemblySymbolHandlesRoundTripThroughPublicStdioTools()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var hostAssemblyPath = GetHostAssemblyPath(repositoryRoot);
        var fixtureRoot = Directory.CreateTempSubdirectory("ainet-contract-fixture-").FullName;
        var configPath = Path.Combine(Path.GetTempPath(), "ainet-navigation-" + Guid.NewGuid().ToString("N") + ".json");
        var hostLogDirectory = Path.Combine(Path.GetTempPath(), "ainet-navigation-logs-" + Guid.NewGuid().ToString("N"));
        var (solutionPath, fixtureAssemblyPath) = await CreateNavigationFixtureAsync(fixtureRoot);
        await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Information\"}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var process = await StartInitializedHostAsync(repositoryRoot, hostAssemblyPath, configPath, timeout.Token, hostLogDirectory);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await SendRequestAsync(process, 2, "tools/list", new { }, timeout.Token);
            var listing = await ReadResponseAsync(process, 2, timeout.Token);
            var names = listing.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(new[] { "dependency_graph", "find_assembly_extensions", "find_implementations", "find_references", "find_symbol", "get_assembly_context", "get_call_tree", "get_class_structure", "get_feature_context", "get_file_skeleton", "get_file_tree", "get_impact", "get_index_scope", "get_namespace_tree", "get_server_health", "get_symbol_body", "get_test_context", "get_type_hierarchy", "inspect_assembly", "reload_config", "resolve_type_origin", "search_assembly" }, names);

            var workspaceBeforeNavigation = CaptureWorkspaceSnapshot(fixtureRoot);
            await SendRequestAsync(process, 3, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = solutionPath, pattern = "CounterConsumer", maxResults = 10 },
            }, timeout.Token);
            var sourceFind = await ReadResponseAsync(process, 3, timeout.Token);
            Assert.False(sourceFind.GetProperty("result").GetProperty("isError").GetBoolean());
            var sourceText = GetFirstText(sourceFind);
            Assert.Contains("CounterConsumer", sourceText, StringComparison.Ordinal);
            var sourceHandle = ExtractHandoff(sourceText);
            Assert.StartsWith("h:", sourceHandle, StringComparison.Ordinal);

            await SendRequestAsync(process, 30, "tools/call", new
            {
                name = "resolve_type_origin",
                arguments = new { targetPath = solutionPath, symbolIdentifier = sourceHandle },
            }, timeout.Token);
            var sourceTypeOrigin = await ReadResponseAsync(process, 30, timeout.Token);
            Assert.False(sourceTypeOrigin.GetProperty("result").GetProperty("isError").GetBoolean());
            var sourceOriginText = GetFirstText(sourceTypeOrigin);
            Assert.Contains("NavigationFixture.Counter", sourceOriginText, StringComparison.Ordinal);
            Assert.Equal("NavigationFixture", ParsePayload(sourceOriginText).GetProperty("projectName").GetString());
            Assert.Contains("NavigationFixture.cs", sourceOriginText, StringComparison.Ordinal);

            await SendRequestAsync(process, 31, "tools/call", new
            {
                name = "resolve_type_origin",
                arguments = new { targetPath = solutionPath, typeName = "System.String" },
            }, timeout.Token);
            var metadataTypeOrigin = await ReadResponseAsync(process, 31, timeout.Token);
            Assert.False(metadataTypeOrigin.GetProperty("result").GetProperty("isError").GetBoolean());
            var metadataOriginText = GetFirstText(metadataTypeOrigin);
            Assert.Contains("\"found\": true", metadataOriginText, StringComparison.Ordinal);
            Assert.Contains("\"assemblyOrigin\": \"reference\"", metadataOriginText, StringComparison.Ordinal);
            Assert.Contains("\"outputAssembly\": \"", metadataOriginText, StringComparison.Ordinal);
            Assert.Contains("Microsoft.NETCore.App.Ref", metadataOriginText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"searchedAssemblies\": [", metadataOriginText, StringComparison.Ordinal);
            Assert.Contains("\"assemblyOrigin\": \"source\"", sourceOriginText, StringComparison.Ordinal);

            await SendRequestAsync(process, 32, "tools/call", new
            {
                name = "resolve_type_origin",
                arguments = new { targetPath = solutionPath, typeName = string.Empty },
            }, timeout.Token);
            var invalidTypeOrigin = await ReadResponseAsync(process, 32, timeout.Token);
            Assert.True(invalidTypeOrigin.GetProperty("result").GetProperty("isError").GetBoolean());

            await SendRequestAsync(process, 4, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = solutionPath, symbolIdentifiers = new[] { sourceHandle } },
            }, timeout.Token);
            var sourceBody = await ReadResponseAsync(process, 4, timeout.Token);
            Assert.False(sourceBody.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("Content mode: source", GetFirstText(sourceBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 35, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = solutionPath, symbolIdentifiers = new[] { "h:unknown" } },
            }, timeout.Token);
            var unknownSourceHandoff = await ReadResponseAsync(process, 35, timeout.Token);
            Assert.True(unknownSourceHandoff.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(unknownSourceHandoff));
            Assert.Contains("HANDOFF_UNKNOWN", GetFirstText(unknownSourceHandoff), StringComparison.Ordinal);

            await SendRequestAsync(process, 12, "tools/call", new
            {
                name = "get_type_hierarchy",
                arguments = new { targetPath = solutionPath, symbolIdentifier = sourceHandle, maxResults = 5 },
            }, timeout.Token);
            var hierarchy = await ReadResponseAsync(process, 12, timeout.Token);
            Assert.False(hierarchy.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("CounterConsumer", GetFirstText(hierarchy), StringComparison.Ordinal);

            await SendRequestAsync(process, 13, "tools/call", new
            {
                name = "find_references",
                arguments = new { targetPath = solutionPath, symbolIdentifier = sourceHandle, maxResults = 10 },
            }, timeout.Token);
            var references = await ReadResponseAsync(process, 13, timeout.Token);
            Assert.False(references.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("CounterConsumer", GetFirstText(references), StringComparison.Ordinal);

            await SendRequestAsync(process, 14, "tools/call", new
            {
                name = "get_feature_context",
                arguments = new { targetPath = solutionPath, symbolIdentifier = sourceHandle, maxCallers = 5, maxTests = 5 },
            }, timeout.Token);
            var featureContext = await ReadResponseAsync(process, 14, timeout.Token);
            Assert.False(featureContext.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("CounterConsumer", GetFirstText(featureContext), StringComparison.Ordinal);
            Assert.Contains("static-test-candidates-only", GetFirstText(featureContext), StringComparison.Ordinal);

            await SendRequestAsync(process, 15, "tools/call", new { name = "get_test_context", arguments = new { targetPath = solutionPath, symbolIdentifier = sourceHandle, maxResults = 10 } }, timeout.Token);
            var testContext = await ReadResponseAsync(process, 15, timeout.Token);
            Assert.False(testContext.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("static-test-candidates-only", GetFirstText(testContext), StringComparison.Ordinal);

            await SendRequestAsync(process, 16, "tools/call", new { name = "get_call_tree", arguments = new { targetPath = solutionPath, symbolIdentifier = sourceHandle } }, timeout.Token);
            var callTree = await ReadResponseAsync(process, 16, timeout.Token);
            Assert.False(callTree.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("CounterConsumer", GetFirstText(callTree), StringComparison.Ordinal);

            await SendRequestAsync(process, 21, "tools/call", new { name = "get_impact", arguments = new { targetPath = solutionPath, symbolIdentifier = sourceHandle } }, timeout.Token);
            var impact = await ReadResponseAsync(process, 21, timeout.Token);
            Assert.False(impact.GetProperty("result").GetProperty("isError").GetBoolean());

            await SendRequestAsync(process, 22, "tools/call", new { name = "dependency_graph", arguments = new { targetPath = solutionPath, filePath = "NavigationFixture.cs", maxResults = 5 } }, timeout.Token);
            var dependencyGraph = await ReadResponseAsync(process, 22, timeout.Token);
            Assert.False(dependencyGraph.GetProperty("result").GetProperty("isError").GetBoolean());

            await SendRequestAsync(process, 26, "tools/call", new { name = "find_symbol", arguments = new { targetPath = solutionPath, pattern = "ICounter", kind = "interface", maxResults = 5 } }, timeout.Token);
            var interfaceFind = await ReadResponseAsync(process, 26, timeout.Token);
            Assert.False(interfaceFind.GetProperty("result").GetProperty("isError").GetBoolean());
            var interfaceHandle = ExtractHandoff(GetFirstText(interfaceFind));
            await SendRequestAsync(process, 27, "tools/call", new { name = "find_implementations", arguments = new { targetPath = solutionPath, symbolIdentifier = interfaceHandle, maxResults = 5 } }, timeout.Token);
            var implementations = await ReadResponseAsync(process, 27, timeout.Token);
            Assert.False(implementations.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("Counter", GetFirstText(implementations), StringComparison.Ordinal);

            await SendRequestAsync(process, 28, "tools/call", new { name = "get_impact", arguments = new { targetPath = solutionPath, detailLevel = "change-context", maxChangedSymbols = 2 } }, timeout.Token);
            var changeContext = await ReadResponseAsync(process, 28, timeout.Token);
            for (var requestId = 29; GetFirstText(changeContext).Contains("operation=running", StringComparison.Ordinal) && requestId < 35; requestId++)
            {
                var operationToken = ReadStringLine(GetFirstText(changeContext), "operationToken");
                await SendRequestAsync(process, requestId, "tools/call", new
                {
                    name = "get_impact",
                    arguments = new { targetPath = solutionPath, detailLevel = "change-context", maxChangedSymbols = 2, operationToken },
                }, timeout.Token);
                changeContext = await ReadResponseAsync(process, requestId, timeout.Token);
            }
            Assert.False(changeContext.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("impactStatus", GetFirstText(changeContext), StringComparison.Ordinal);
            Assert.Contains("completeness", GetFirstText(changeContext), StringComparison.Ordinal);

            await SendRequestAsync(process, 7, "tools/call", new
            {
                name = "get_file_skeleton",
                arguments = new { targetPath = solutionPath, filePaths = new[] { "NavigationFixture.cs" } },
            }, timeout.Token);
            var skeleton = await ReadResponseAsync(process, 7, timeout.Token);
            Assert.False(skeleton.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("CounterConsumer", GetFirstText(skeleton), StringComparison.Ordinal);
            Assert.Contains("handoffId: `h:", GetFirstText(skeleton), StringComparison.Ordinal);

            var absoluteSourcePath = Path.Combine(Path.GetDirectoryName(solutionPath)!, "NavigationFixture.cs");
            await SendRequestAsync(process, 36, "tools/call", new
            {
                name = "get_file_skeleton",
                arguments = new { targetPath = solutionPath, filePaths = new[] { absoluteSourcePath, "../Shared/LinkedFixture.cs" } },
            }, timeout.Token);
            var linkedSourceSkeleton = await ReadResponseAsync(process, 36, timeout.Token);
            Assert.False(linkedSourceSkeleton.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(linkedSourceSkeleton));
            Assert.Contains("ICounter", GetFirstText(linkedSourceSkeleton), StringComparison.Ordinal);
            Assert.Contains("multiple projects", GetFirstText(linkedSourceSkeleton), StringComparison.Ordinal);

            await SendRequestAsync(process, 42, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = solutionPath, pattern = "LinkedFeature", kind = "class", maxResults = 10 },
            }, timeout.Token);
            var linkedFeatureSearch = await ReadResponseAsync(process, 42, timeout.Token);
            Assert.False(linkedFeatureSearch.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(linkedFeatureSearch));
            var linkedFeatureLines = GetFirstText(linkedFeatureSearch).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("LinkedFeature", StringComparison.Ordinal) && line.Contains("[handoff: ", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(2, linkedFeatureLines.Length);
            var linkedFeatureHandle = ExtractHandoff(linkedFeatureLines[0]);
            await SendRequestAsync(process, 43, "tools/call", new
            {
                name = "get_file_skeleton",
                arguments = new { targetPath = solutionPath, filePaths = new[] { linkedFeatureHandle } },
            }, timeout.Token);
            var linkedFeatureSkeleton = await ReadResponseAsync(process, 43, timeout.Token);
            Assert.False(linkedFeatureSkeleton.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(linkedFeatureSkeleton));
            Assert.Contains("LinkedFeature", GetFirstText(linkedFeatureSkeleton), StringComparison.Ordinal);
            var linkedFeatureMemberHandle = ExtractSkeletonHandoff(GetFirstText(linkedFeatureSkeleton), "Value(");
            await SendRequestAsync(process, 44, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = solutionPath, symbolIdentifiers = new[] { linkedFeatureMemberHandle } },
            }, timeout.Token);
            var linkedFeatureBody = await ReadResponseAsync(process, 44, timeout.Token);
            Assert.False(linkedFeatureBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(linkedFeatureBody));
            Assert.Contains("Value()", GetFirstText(linkedFeatureBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 37, "tools/call", new
            {
                name = "get_file_skeleton",
                arguments = new { targetPath = solutionPath, filePaths = new[] { sourceHandle } },
            }, timeout.Token);
            var sourceHandleSkeleton = await ReadResponseAsync(process, 37, timeout.Token);
            Assert.False(sourceHandleSkeleton.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(sourceHandleSkeleton));
            Assert.Contains("CounterConsumer", GetFirstText(sourceHandleSkeleton), StringComparison.Ordinal);
            var sourceSkeletonHandle = ExtractSkeletonHandoff(GetFirstText(sourceHandleSkeleton), "Run(");
            await SendRequestAsync(process, 38, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = solutionPath, symbolIdentifiers = new[] { sourceSkeletonHandle } },
            }, timeout.Token);
            var sourceSkeletonBody = await ReadResponseAsync(process, 38, timeout.Token);
            Assert.False(sourceSkeletonBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(sourceSkeletonBody));
            Assert.Contains("Run(", GetFirstText(sourceSkeletonBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 8, "tools/call", new
            {
                name = "get_class_structure",
                arguments = new { targetPath = solutionPath, symbolIdentifier = sourceHandle, maxMembers = 20 },
            }, timeout.Token);
            var structure = await ReadResponseAsync(process, 8, timeout.Token);
            Assert.False(structure.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("CounterConsumer", GetFirstText(structure), StringComparison.Ordinal);
            Assert.Contains("[handoff: h:", GetFirstText(structure), StringComparison.Ordinal);

            await SendRequestAsync(process, 80, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = solutionPath, pattern = "ScopeWidget", kind = "class", maxResults = 10 },
            }, timeout.Token);
            var scopedWidgetFind = await ReadResponseAsync(process, 80, timeout.Token);
            Assert.False(scopedWidgetFind.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(scopedWidgetFind));
            var scopedWidgetHandle = ExtractHandoff(GetFirstText(scopedWidgetFind));

            await SendRequestAsync(process, 81, "tools/call", new
            {
                name = "get_class_structure",
                arguments = new { targetPath = solutionPath, symbolIdentifier = scopedWidgetHandle, scopeType = "production", includeGenerated = false, maxMembers = 50 },
            }, timeout.Token);
            var productionStructure = await ReadResponseAsync(process, 81, timeout.Token);
            var productionStructureText = GetFirstText(productionStructure);
            Assert.False(productionStructure.GetProperty("result").GetProperty("isError").GetBoolean(), productionStructureText);
            Assert.Contains("ProductionOnly", productionStructureText, StringComparison.Ordinal);
            Assert.DoesNotContain("TestOnly", productionStructureText, StringComparison.Ordinal);
            Assert.DoesNotContain("GeneratedOnly", productionStructureText, StringComparison.Ordinal);

            await SendRequestAsync(process, 82, "tools/call", new
            {
                name = "get_class_structure",
                arguments = new { targetPath = solutionPath, symbolIdentifier = scopedWidgetHandle, scopeType = "tests", includeGenerated = false, maxMembers = 50 },
            }, timeout.Token);
            var testStructure = await ReadResponseAsync(process, 82, timeout.Token);
            var testStructureText = GetFirstText(testStructure);
            Assert.False(testStructure.GetProperty("result").GetProperty("isError").GetBoolean(), testStructureText);
            Assert.Contains("TestOnly", testStructureText, StringComparison.Ordinal);
            Assert.DoesNotContain("ProductionOnly", testStructureText, StringComparison.Ordinal);
            Assert.DoesNotContain("GeneratedOnly", testStructureText, StringComparison.Ordinal);

            await SendRequestAsync(process, 83, "tools/call", new
            {
                name = "get_class_structure",
                arguments = new { targetPath = solutionPath, symbolIdentifier = scopedWidgetHandle, scopeType = "production", includeGenerated = true, maxMembers = 50 },
            }, timeout.Token);
            var generatedStructure = await ReadResponseAsync(process, 83, timeout.Token);
            var generatedStructureText = GetFirstText(generatedStructure);
            Assert.False(generatedStructure.GetProperty("result").GetProperty("isError").GetBoolean(), generatedStructureText);
            Assert.Contains("GeneratedOnly", generatedStructureText, StringComparison.Ordinal);
            Assert.DoesNotContain("TestOnly", generatedStructureText, StringComparison.Ordinal);

            await SendRequestAsync(process, 9, "tools/call", new
            {
                name = "get_file_tree",
                arguments = new { targetPath = solutionPath, root = ".", view = "files", maxResults = 10 },
            }, timeout.Token);
            var tree = await ReadResponseAsync(process, 9, timeout.Token);
            Assert.False(tree.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("NavigationFixture.cs", GetFirstText(tree), StringComparison.Ordinal);

            await SendRequestAsync(process, 10, "tools/call", new { name = "get_index_scope", arguments = new { targetPath = solutionPath } }, timeout.Token);
            var indexScope = await ReadResponseAsync(process, 10, timeout.Token);
            Assert.False(indexScope.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("Index Scope:", GetFirstText(indexScope), StringComparison.Ordinal);

            await SendRequestAsync(process, 11, "tools/call", new
            {
                name = "get_namespace_tree",
                arguments = new { targetPath = solutionPath, project = "NavigationFixture", namespacePrefix = "NavigationFixture", depth = 2 },
            }, timeout.Token);
            var namespaceTree = await ReadResponseAsync(process, 11, timeout.Token);
            Assert.False(namespaceTree.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("NavigationFixture", GetFirstText(namespaceTree), StringComparison.Ordinal);

            await SendRequestAsync(process, 12, "tools/call", new
            {
                name = "get_namespace_tree",
                arguments = new { targetPath = solutionPath },
            }, timeout.Token);
            var namespaceOverview = await ReadResponseAsync(process, 12, timeout.Token);
            Assert.False(namespaceOverview.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(namespaceOverview));
            Assert.Contains("Projects:", GetFirstText(namespaceOverview), StringComparison.Ordinal);
            Assert.Contains("NavigationFixture", GetFirstText(namespaceOverview), StringComparison.Ordinal);

            AssertWorkspaceUnchanged(workspaceBeforeNavigation, CaptureWorkspaceSnapshot(fixtureRoot));

            await SendRequestAsync(process, 5, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = fixtureAssemblyPath, pattern = "Counter", maxResults = 10 },
            }, timeout.Token);
            var assemblyFind = await ReadResponseAsync(process, 5, timeout.Token);
            Assert.False(assemblyFind.GetProperty("result").GetProperty("isError").GetBoolean());
            var assemblyHandle = ExtractHandoff(GetFirstText(assemblyFind));
            Assert.StartsWith("h:", assemblyHandle, StringComparison.Ordinal);

            await SendRequestAsync(process, 33, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = hostAssemblyPath, pattern = "NavigatorHostRuntime", maxResults = 5 },
            }, timeout.Token);
            var foreignAssemblyFind = await ReadResponseAsync(process, 33, timeout.Token);
            Assert.False(foreignAssemblyFind.GetProperty("result").GetProperty("isError").GetBoolean());
            var foreignAssemblyHandle = ExtractHandoff(GetFirstText(foreignAssemblyFind));
            await SendRequestAsync(process, 34, "tools/call", new
            {
                name = "resolve_type_origin",
                arguments = new { targetPath = fixtureAssemblyPath, symbolIdentifier = foreignAssemblyHandle },
            }, timeout.Token);
            var foreignAssemblyOrigin = await ReadResponseAsync(process, 34, timeout.Token);
            Assert.True(foreignAssemblyOrigin.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("INVALID_ARGUMENT", GetFirstText(foreignAssemblyOrigin), StringComparison.Ordinal);

            await SendRequestAsync(process, 17, "tools/call", new
            {
                name = "inspect_assembly",
                arguments = new { targetPath = fixtureAssemblyPath, typeName = "NavigationFixture.Counter", exactTypeName = true, maxResults = 1000, maxMembers = 20, maxResponseBytes = 512 },
            }, timeout.Token);
            var assemblyInspection = await ReadResponseAsync(process, 17, timeout.Token);
            var inspectionText = GetFirstText(assemblyInspection);
            var inspectionPageText = inspectionText;
            var inspectionPaged = false;
            var budgetRecoveryCount = 0;
            Assert.False(assemblyInspection.GetProperty("result").GetProperty("isError").GetBoolean());
            var pageRequestId = 40;
            while (pageRequestId < 60 && TryReadStringLine(inspectionPageText, "continuationToken") is { } pageToken)
            {
                var pageBudget = 512;
                JsonElement page;
                var retry = 0;
                while (true)
                {
                    await SendRequestAsync(process, pageRequestId++, "tools/call", new
                    {
                        name = "inspect_assembly",
                        arguments = new { targetPath = fixtureAssemblyPath, typeName = "NavigationFixture.Counter", exactTypeName = true, maxResults = 1000, maxMembers = 20, maxResponseBytes = pageBudget, continuationToken = pageToken },
                    }, timeout.Token);
                    page = await ReadResponseAsync(process, pageRequestId - 1, timeout.Token);
                    if (!page.GetProperty("result").GetProperty("isError").GetBoolean()) break;
                    var errorText = GetFirstText(page);
                    Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", errorText, StringComparison.Ordinal);
                    Assert.True(retry++ < 5, errorText);
                    pageBudget = ReadIntegerLine(errorText, "minimumResponseBytes");
                    budgetRecoveryCount++;
                }
                inspectionPageText = GetFirstText(page);
                inspectionText += "\n" + inspectionPageText;
                inspectionPaged = true;
            }
            Assert.True(inspectionPaged, "A 512-byte Assembly inspection response should require a continuation page.");
            Assert.True(budgetRecoveryCount > 0, "The line-safe Assembly page should report and recover from the too-small response budget.");
            Assert.Null(TryReadStringLine(inspectionPageText, "continuationToken"));
            Assert.Contains("NavigationFixture.Counter", inspectionText, StringComparison.Ordinal);

            await SendRequestAsync(process, 50, "tools/call", new
            {
                name = "inspect_assembly",
                arguments = new { targetPath = fixtureAssemblyPath, maxResults = 1, maxMembers = 20, includeReferences = false },
            }, timeout.Token);
            var firstDomainPage = await ReadResponseAsync(process, 50, timeout.Token);
            var firstDomainText = GetFirstText(firstDomainPage);
            Assert.False(firstDomainPage.GetProperty("result").GetProperty("isError").GetBoolean(), firstDomainText);
            Assert.Contains("Status: operation=ok, completeness=truncated", firstDomainText, StringComparison.Ordinal);
            var firstDomainJson = ParsePayload(firstDomainText);
            Assert.True(firstDomainJson.GetProperty("truncated").GetBoolean());
            var domainCursor = firstDomainJson.GetProperty("continuationToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(domainCursor));

            var domainNames = new List<string>();
            var domainIds = new List<string>();
            domainNames.AddRange(firstDomainJson.GetProperty("types").EnumerateArray().Select(type => type.GetProperty("name").GetString()!));
            domainIds.AddRange(firstDomainJson.GetProperty("types").EnumerateArray().Select(type => type.GetProperty("id").GetString()!));
            var expectedDomainTypeCount = firstDomainJson.GetProperty("totalTypes").GetInt32();
            var finalDomainText = firstDomainText;
            var domainPageCount = 0;
            while (domainCursor is not null && domainPageCount++ < 10)
            {
                await SendRequestAsync(process, 51 + domainPageCount, "tools/call", new
                {
                    name = "inspect_assembly",
                    arguments = new { targetPath = fixtureAssemblyPath, maxResults = 1, maxMembers = 20, includeReferences = false, continuationToken = domainCursor },
                }, timeout.Token);
                var domainPage = await ReadResponseAsync(process, 51 + domainPageCount, timeout.Token);
                var domainText = GetFirstText(domainPage);
                Assert.False(domainPage.GetProperty("result").GetProperty("isError").GetBoolean(), domainText);
                finalDomainText = domainText;
                var payload = ParsePayload(domainText);
                domainNames.AddRange(payload.GetProperty("types").EnumerateArray().Select(type => type.GetProperty("name").GetString()!));
                domainIds.AddRange(payload.GetProperty("types").EnumerateArray().Select(type => type.GetProperty("id").GetString()!));
                domainCursor = payload.TryGetProperty("continuationToken", out var nextCursor)
                    && nextCursor.ValueKind != JsonValueKind.Null
                    ? nextCursor.GetString()
                    : null;
                Assert.Equal(domainCursor is not null, payload.GetProperty("truncated").GetBoolean());
                Assert.Contains(domainCursor is null
                    ? "Status: operation=ok, completeness=complete"
                    : "Status: operation=ok, completeness=truncated", domainText, StringComparison.Ordinal);
            }
            Assert.Null(domainCursor);
            Assert.Equal(expectedDomainTypeCount, domainNames.Count);
            Assert.Equal(domainNames.Count, domainNames.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(expectedDomainTypeCount, domainIds.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains("Status: operation=ok, completeness=complete", finalDomainText, StringComparison.Ordinal);

            await SendRequestAsync(process, 70, "tools/call", new
            {
                name = "inspect_assembly",
                arguments = new { targetPath = fixtureAssemblyPath, maxResults = 1, maxMembers = 20, includeReferences = false, continuationToken = firstDomainJson.GetProperty("continuationToken").GetString() },
            }, timeout.Token);
            var replayedDomainPage = await ReadResponseAsync(process, 70, timeout.Token);
            Assert.False(replayedDomainPage.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(replayedDomainPage));
            var replayedDomainPayload = ParsePayload(GetFirstText(replayedDomainPage));
            Assert.Contains("Status: operation=ok, completeness=truncated", GetFirstText(replayedDomainPage), StringComparison.Ordinal);
            Assert.Equal(domainNames[1], replayedDomainPayload.GetProperty("types")[0].GetProperty("name").GetString());
            Assert.Equal(domainIds[1], replayedDomainPayload.GetProperty("types")[0].GetProperty("id").GetString());

            await SendRequestAsync(process, 71, "tools/call", new
            {
                name = "inspect_assembly",
                arguments = new { targetPath = fixtureAssemblyPath, maxResults = 2, maxMembers = 20, includeReferences = false, continuationToken = firstDomainJson.GetProperty("continuationToken").GetString() },
            }, timeout.Token);
            var mismatchedDomainPage = await ReadResponseAsync(process, 71, timeout.Token);
            Assert.True(mismatchedDomainPage.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("INVALID_ARGUMENT", GetFirstText(mismatchedDomainPage), StringComparison.Ordinal);

            await SendRequestAsync(process, 19, "tools/call", new { name = "get_assembly_context", arguments = new { targetPath = fixtureAssemblyPath, symbolIdentifier = assemblyHandle, includeBody = true, maxResults = 10 } }, timeout.Token);
            var assemblyContext = await ReadResponseAsync(process, 19, timeout.Token);
            Assert.False(assemblyContext.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("NavigationFixture.Counter", GetFirstText(assemblyContext), StringComparison.Ordinal);

            await SendRequestAsync(process, 23, "tools/call", new { name = "search_assembly", arguments = new { targetPath = fixtureAssemblyPath, pattern = "Counter", maxResults = 5 } }, timeout.Token);
            var assemblySearch = await ReadResponseAsync(process, 23, timeout.Token);
            Assert.False(assemblySearch.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("Counter", GetFirstText(assemblySearch), StringComparison.Ordinal);

            await SendRequestAsync(process, 26, "tools/call", new
            {
                name = "search_assembly",
                arguments = new
                {
                    targetPath = fixtureAssemblyPath,
                    pattern = "Counter|Missing",
                    isRegex = (bool?)null,
                    kind = "type",
                    fileFilter = "*.cs",
                    maxFiles = 1,
                    maxResults = 10,
                },
            }, timeout.Token);
            var filteredAssemblySearch = await ReadResponseAsync(process, 26, timeout.Token);
            var filteredAssemblySearchText = GetFirstText(filteredAssemblySearch);
            Assert.False(filteredAssemblySearch.GetProperty("result").GetProperty("isError").GetBoolean(), filteredAssemblySearchText);
            var filteredAssemblySearchPayload = ParsePayload(filteredAssemblySearchText);
            var filteredAssemblyHits = filteredAssemblySearchPayload.GetProperty("results").EnumerateArray().ToArray();
            Assert.NotEmpty(filteredAssemblyHits);
            Assert.Contains(filteredAssemblyHits, hit => hit.GetProperty("symbol").GetString()?.Contains("Counter", StringComparison.Ordinal) == true
                && hit.GetProperty("text").GetString()?.Contains("Counter", StringComparison.Ordinal) == true);
            Assert.All(filteredAssemblyHits, hit => Assert.EndsWith(".cs", hit.GetProperty("filePath").GetString(), StringComparison.OrdinalIgnoreCase));

            await SendRequestAsync(process, 29, "tools/call", new
            {
                name = "search_assembly",
                arguments = new
                {
                    targetPath = fixtureAssemblyPath,
                    pattern = "Counter|Read",
                    isRegex = true,
                    fileFilter = "*.cs",
                    maxFiles = 1,
                    maxResults = 1,
                },
            }, timeout.Token);
            var matchedFileLimitedSearch = await ReadResponseAsync(process, 29, timeout.Token);
            var matchedFileLimitedText = GetFirstText(matchedFileLimitedSearch);
            Assert.False(matchedFileLimitedSearch.GetProperty("result").GetProperty("isError").GetBoolean(), matchedFileLimitedText);
            var matchedFileLimitedPayload = ParsePayload(matchedFileLimitedText);
            var limitedHits = matchedFileLimitedPayload.GetProperty("results").EnumerateArray().ToArray();
            Assert.NotEmpty(limitedHits);
            Assert.Single(limitedHits.Select(hit => hit.GetProperty("filePath").GetString()).Distinct(StringComparer.OrdinalIgnoreCase));
            Assert.True(matchedFileLimitedPayload.GetProperty("truncated").GetBoolean());
            Assert.Contains("maxFiles", matchedFileLimitedPayload.GetProperty("truncatedBy").EnumerateArray().Select(value => value.GetString()));
            var searchPageIdentity = limitedHits.Select(hit => (File: hit.GetProperty("filePath").GetString(),
                Line: hit.GetProperty("lineNumber").GetInt32(), Text: hit.GetProperty("text").GetString())).ToList();
            Assert.True(matchedFileLimitedPayload.TryGetProperty("continuationToken", out var initialSearchCursor),
                $"Expected the selected maxFiles file to have additional domain results. Payload: {matchedFileLimitedText}");
            var searchCursor = initialSearchCursor.GetString();
            Assert.False(string.IsNullOrWhiteSpace(searchCursor), matchedFileLimitedText);
            var searchPageNumber = 0;
            var finalSearchPageText = matchedFileLimitedText;
            while (searchCursor is not null && searchPageNumber < 10)
            {
                var requestId = 33 + searchPageNumber++;
                await SendRequestAsync(process, requestId, "tools/call", new
                {
                    name = "search_assembly",
                    arguments = new
                    {
                        targetPath = fixtureAssemblyPath,
                        pattern = "Counter|Read",
                        isRegex = true,
                        fileFilter = "*.cs",
                        maxFiles = 1,
                        maxResults = 1,
                        continuationToken = searchCursor,
                    },
                }, timeout.Token);
                var searchPage = await ReadResponseAsync(process, requestId, timeout.Token);
                finalSearchPageText = GetFirstText(searchPage);
                Assert.False(searchPage.GetProperty("result").GetProperty("isError").GetBoolean(), finalSearchPageText);
                var searchPagePayload = ParsePayload(finalSearchPageText);
                var pageHits = searchPagePayload.GetProperty("results").EnumerateArray().ToArray();
                Assert.Single(pageHits);
                Assert.Equal(limitedHits[0].GetProperty("filePath").GetString(), pageHits[0].GetProperty("filePath").GetString());
                searchPageIdentity.Add((pageHits[0].GetProperty("filePath").GetString(),
                    pageHits[0].GetProperty("lineNumber").GetInt32(), pageHits[0].GetProperty("text").GetString()));
                Assert.Contains("maxFiles", searchPagePayload.GetProperty("truncatedBy").EnumerateArray().Select(value => value.GetString()));
                searchCursor = searchPagePayload.TryGetProperty("continuationToken", out var nextSearchCursor)
                    && nextSearchCursor.ValueKind != JsonValueKind.Null
                    ? nextSearchCursor.GetString()
                    : null;
                Assert.Equal(searchCursor is null, !searchPagePayload.GetProperty("truncatedBy").EnumerateArray().Any(value => value.GetString() == "maxResults"));
            }
            Assert.Null(searchCursor);
            Assert.True(searchPageNumber > 0);
            Assert.Equal(searchPageIdentity.Count, searchPageIdentity.Select(hit => (hit.File, hit.Line, hit.Text)).Distinct().Count());
            Assert.Contains("Status: operation=ok, completeness=truncated", finalSearchPageText, StringComparison.Ordinal);

            await SendRequestAsync(process, 32, "tools/call", new
            {
                name = "search_assembly",
                arguments = new
                {
                    targetPath = fixtureAssemblyPath,
                    pattern = "Counter",
                    isRegex = false,
                    kind = "type",
                    fileFilter = "*.cs",
                    maxFiles = 10,
                    maxResults = 50,
                },
            }, timeout.Token);
            var unlimitedMatchedFiles = await ReadResponseAsync(process, 32, timeout.Token);
            var unlimitedMatchedFilePayload = ParsePayload(GetFirstText(unlimitedMatchedFiles));
            var unlimitedHits = unlimitedMatchedFilePayload.GetProperty("results").EnumerateArray().ToArray();
            Assert.True(unlimitedHits.Length > limitedHits.Length);
            Assert.True(unlimitedHits.Select(hit => hit.GetProperty("filePath").GetString())
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

            await SendRequestAsync(process, 30, "tools/call", new
            {
                name = "get_namespace_tree",
                arguments = new { targetPath = fixtureAssemblyPath, namespacePrefix = "NavigationFixture", depth = 1, includeTypes = true },
            }, timeout.Token);
            var assemblyNamespaceTree = await ReadResponseAsync(process, 30, timeout.Token);
            Assert.False(assemblyNamespaceTree.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(assemblyNamespaceTree));
            var assemblyNamespaceText = GetFirstText(assemblyNamespaceTree);
            var assemblyCounterLine = assemblyNamespaceText.Split('\n').Single(line => line.Contains("class Counter (", StringComparison.Ordinal));
            var assemblyNamespaceHandle = ExtractHandoff(assemblyCounterLine);
            await SendRequestAsync(process, 31, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = fixtureAssemblyPath, symbolIdentifiers = new[] { assemblyNamespaceHandle } },
            }, timeout.Token);
            var assemblyNamespaceBody = await ReadResponseAsync(process, 31, timeout.Token);
            Assert.False(assemblyNamespaceBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(assemblyNamespaceBody));
            Assert.Contains("class Counter", GetFirstText(assemblyNamespaceBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 24, "tools/call", new { name = "find_assembly_extensions", arguments = new { targetPath = fixtureAssemblyPath, receiverType = "NavigationFixture.Counter", extensionName = "Double", maxResults = 5 } }, timeout.Token);
            var assemblyExtensions = await ReadResponseAsync(process, 24, timeout.Token);
            Assert.False(assemblyExtensions.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("Double", GetFirstText(assemblyExtensions), StringComparison.Ordinal);

            await SendRequestAsync(process, 18, "tools/call", new
            {
                name = "resolve_type_origin",
                arguments = new { targetPath = fixtureAssemblyPath, typeName = "NavigationFixture.Counter" },
            }, timeout.Token);
            var typeOrigin = await ReadResponseAsync(process, 18, timeout.Token);
            Assert.False(typeOrigin.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("NavigationFixture.Counter", GetFirstText(typeOrigin), StringComparison.Ordinal);

            await SendRequestAsync(process, 25, "tools/call", new { name = "get_impact", arguments = new { targetPath = fixtureAssemblyPath, symbolIdentifier = assemblyHandle } }, timeout.Token);
            var assemblyImpact = await ReadResponseAsync(process, 25, timeout.Token);
            Assert.False(assemblyImpact.GetProperty("result").GetProperty("isError").GetBoolean());

            await SendRequestAsync(process, 6, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = fixtureAssemblyPath, symbolIdentifiers = new[] { assemblyHandle } },
            }, timeout.Token);
            var assemblyBody = await ReadResponseAsync(process, 6, timeout.Token);
            Assert.False(assemblyBody.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("Content mode: decompiled", GetFirstText(assemblyBody), StringComparison.Ordinal);

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
            if (Directory.Exists(hostLogDirectory)) Directory.Delete(hostLogDirectory, recursive: true);
            if (Directory.Exists(Path.Combine(fixtureRoot, "repository", ".git")))
            {
                await RunCommandAsync("git", Path.Combine(fixtureRoot, "repository"), "worktree", "remove", "--force", Path.Combine(fixtureRoot, "worktree"));
                await RunCommandAsync("git", Path.Combine(fixtureRoot, "repository"), "worktree", "prune");
            }
            if (Directory.Exists(fixtureRoot))
            {
                ClearReadOnlyAttributesWithinOwnedFixture(fixtureRoot);
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AssemblyReferenceSearchHandoffUsesTheOwningAssemblyTarget()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var hostAssemblyPath = GetHostAssemblyPath(repositoryRoot);
        using var fixture = TestTempDirectory.Create("assembly-owner-stdio-");
        var configPath = Path.Combine(Path.GetTempPath(), "ainet-owner-" + Guid.NewGuid().ToString("N") + ".json");
        var hostLogDirectory = Path.Combine(Path.GetTempPath(), "ainet-owner-logs-" + Guid.NewGuid().ToString("N"));
        var cPath = AssemblyTestHelper.EmitAssembly(fixture, "ClosureC", "namespace ClosureFixture; public class ClosureOnlyC { public string Value => \"from C\"; }");
        var bPath = AssemblyTestHelper.EmitAssembly(fixture, "ClosureB", "namespace ClosureFixture; public class ClosureB : ClosureOnlyC { }", cPath);
        var aPath = AssemblyTestHelper.EmitAssembly(fixture, "ClosureA", "namespace ClosureFixture; public class ClosureA { public string Read() => new ClosureB().GetType().Name; }", bPath, cPath);
        await using var aImage = File.OpenRead(aPath);
        using var aPe = new PEReader(aImage);
        var aMetadata = aPe.GetMetadataReader();
        var aReferences = aMetadata.AssemblyReferences
            .Select(handle => aMetadata.GetString(aMetadata.GetAssemblyReference(handle).Name))
            .ToArray();
        Assert.Contains("ClosureB", aReferences, StringComparer.Ordinal);
        Assert.DoesNotContain("ClosureC", aReferences, StringComparer.Ordinal);
        var ownedCPath = Path.GetFullPath(cPath);
        var wrongRootPath = Path.GetFullPath(aPath);
        await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Information\"}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var process = await StartInitializedHostAsync(repositoryRoot, hostAssemblyPath, configPath, timeout.Token, hostLogDirectory);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await SendRequestAsync(process, 2, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = aPath, pattern = "ClosureOnlyC", kind = "class", includeReferences = true, maxResults = 10 },
            }, timeout.Token);
            var find = await ReadResponseAsync(process, 2, timeout.Token);
            for (var requestId = 3; GetFirstText(find).Contains("operation=running", StringComparison.Ordinal) && requestId < 12; requestId++)
            {
                var operationToken = ReadStringLine(GetFirstText(find), "operationToken");
                await SendRequestAsync(process, requestId, "tools/call", new
                {
                    name = "find_symbol",
                    arguments = new { targetPath = aPath, pattern = "ClosureOnlyC", kind = "class", includeReferences = true, maxResults = 10, operationToken },
                }, timeout.Token);
                find = await ReadResponseAsync(process, requestId, timeout.Token);
            }
            Assert.False(find.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(find));
            Assert.Contains("ClosureOnlyC", GetFirstText(find), StringComparison.Ordinal);
            Assert.Contains($"targetPath: {ownedCPath}", GetFirstText(find), StringComparison.OrdinalIgnoreCase);
            var handoff = ExtractHandoff(GetFirstText(find));

            await SendRequestAsync(process, 20, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { handoff } },
            }, timeout.Token);
            var ownerBody = await ReadResponseAsync(process, 20, timeout.Token);
            Assert.False(ownerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(ownerBody));
            Assert.Contains("ClosureOnlyC", GetFirstText(ownerBody), StringComparison.Ordinal);
            Assert.Contains("from C", GetFirstText(ownerBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 22, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { "h:unknown" } },
            }, timeout.Token);
            var unknownHandoff = await ReadResponseAsync(process, 22, timeout.Token);
            Assert.True(unknownHandoff.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(unknownHandoff));
            Assert.True(GetFirstText(unknownHandoff).Contains("HANDOFF_UNKNOWN", StringComparison.Ordinal), GetFirstText(unknownHandoff));

            await SendRequestAsync(process, 39, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { handoff, "h:unknown" } },
            }, timeout.Token);
            var mixedBodyBatch = await ReadResponseAsync(process, 39, timeout.Token);
            Assert.False(mixedBodyBatch.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(mixedBodyBatch));
            Assert.Contains("completeness=truncated", GetFirstText(mixedBodyBatch), StringComparison.Ordinal);
            Assert.Contains("ClosureOnlyC", GetFirstText(mixedBodyBatch), StringComparison.Ordinal);
            Assert.Contains("HANDOFF_UNKNOWN", GetFirstText(mixedBodyBatch), StringComparison.Ordinal);

            await SendRequestAsync(process, 21, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = wrongRootPath, symbolIdentifiers = new[] { handoff } },
            }, timeout.Token);
            var wrongOwnerBody = await ReadResponseAsync(process, 21, timeout.Token);
            Assert.True(wrongOwnerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(wrongOwnerBody));
            Assert.Contains("TARGET_MISMATCH", GetFirstText(wrongOwnerBody), StringComparison.Ordinal);

            using var replacementFixture = TestTempDirectory.Create("assembly-owner-replacement-");
            var replacementCPath = AssemblyTestHelper.EmitAssembly(replacementFixture, "ClosureC", "namespace ClosureFixture; public class ClosureOnlyC { public string Value => \"replacement C\"; }");
            File.Copy(replacementCPath, cPath, overwrite: true);
            await SendRequestAsync(process, 23, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { handoff } },
            }, timeout.Token);
            var replacedOldBody = await ReadResponseAsync(process, 23, timeout.Token);
            Assert.True(replacedOldBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(replacedOldBody));
            Assert.Contains("STALE_SNAPSHOT", GetFirstText(replacedOldBody), StringComparison.Ordinal);
            Assert.DoesNotContain("from C", GetFirstText(replacedOldBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 24, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = aPath, pattern = "ClosureOnlyC", kind = "class", includeReferences = true, maxResults = 10 },
            }, timeout.Token);
            var replacementFind = await ReadResponseAsync(process, 24, timeout.Token);
            for (var requestId = 25; GetFirstText(replacementFind).Contains("operation=running", StringComparison.Ordinal) && requestId < 34; requestId++)
            {
                var operationToken = ReadStringLine(GetFirstText(replacementFind), "operationToken");
                await SendRequestAsync(process, requestId, "tools/call", new
                {
                    name = "find_symbol",
                    arguments = new { targetPath = aPath, pattern = "ClosureOnlyC", kind = "class", includeReferences = true, maxResults = 10, operationToken },
                }, timeout.Token);
                replacementFind = await ReadResponseAsync(process, requestId, timeout.Token);
            }
            Assert.False(replacementFind.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(replacementFind));
            Assert.Contains($"targetPath: {ownedCPath}", GetFirstText(replacementFind), StringComparison.OrdinalIgnoreCase);
            var replacementHandoff = ExtractHandoff(GetFirstText(replacementFind));
            Assert.NotEqual(handoff, replacementHandoff);

            await SendRequestAsync(process, 40, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { replacementHandoff } },
            }, timeout.Token);
            var replacementBody = await ReadResponseAsync(process, 40, timeout.Token);
            Assert.False(replacementBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(replacementBody));
            Assert.Contains("replacement C", GetFirstText(replacementBody), StringComparison.Ordinal);

            File.Delete(cPath);
            await SendRequestAsync(process, 42, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = aPath, pattern = "ClosureOnlyC", kind = "class", includeReferences = true, maxResults = 10 },
            }, timeout.Token);
            var missingFind = await ReadResponseAsync(process, 42, timeout.Token);
            for (var requestId = 43; GetFirstText(missingFind).Contains("operation=running", StringComparison.Ordinal) && requestId < 52; requestId++)
            {
                var operationToken = ReadStringLine(GetFirstText(missingFind), "operationToken");
                await SendRequestAsync(process, requestId, "tools/call", new
                {
                    name = "find_symbol",
                    arguments = new { targetPath = aPath, pattern = "ClosureOnlyC", kind = "class", includeReferences = true, maxResults = 10, operationToken },
                }, timeout.Token);
                missingFind = await ReadResponseAsync(process, requestId, timeout.Token);
            }
            Assert.False(missingFind.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(missingFind));
            Assert.Contains("No symbols matched 'ClosureOnlyC'", GetFirstText(missingFind), StringComparison.Ordinal);
            Assert.Contains("completeness=truncated", GetFirstText(missingFind), StringComparison.Ordinal);
            Assert.Contains("unresolvedReferences", GetFirstText(missingFind), StringComparison.Ordinal);

            await SendRequestAsync(process, 53, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = wrongRootPath, symbolIdentifiers = new[] { replacementHandoff } },
            }, timeout.Token);
            var missingOwnerBody = await ReadResponseAsync(process, 53, timeout.Token);
            Assert.True(missingOwnerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(missingOwnerBody));
            Assert.DoesNotContain("replacement C", GetFirstText(missingOwnerBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 54, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { replacementHandoff } },
            }, timeout.Token);
            var missingOwnerTarget = await ReadResponseAsync(process, 54, timeout.Token);
            Assert.True(missingOwnerTarget.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(missingOwnerTarget));
            Assert.Contains("INVALID_ARGUMENT", GetFirstText(missingOwnerTarget), StringComparison.Ordinal);
            Assert.DoesNotContain("replacement C", GetFirstText(missingOwnerTarget), StringComparison.Ordinal);

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
            if (Directory.Exists(hostLogDirectory)) Directory.Delete(hostLogDirectory, recursive: true);
            var resolvedFixtureRoot = Path.GetFullPath(fixture.DirectoryPath);
            Assert.StartsWith(Path.GetFullPath(TestTempDirectory.RootTempDirectory) + Path.DirectorySeparatorChar, resolvedFixtureRoot, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AssemblySearchDomainPagesBindQueriesAndReturnNavigableDeclarations()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var hostAssemblyPath = GetHostAssemblyPath(repositoryRoot);
        using var fixture = TestTempDirectory.Create("assembly-search-cursor-");
        var budgetTypeSource = string.Join(Environment.NewLine, Enumerable.Range(0, 600).Select(index =>
            $"namespace BudgetFixture.GeneratedNamespaceWithLongLabel{index:D4} {{ public sealed class BudgetPaddingTypeWithLongName{index:D4} {{ }} }}"));
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "SearchCursorFixture", """
            namespace SearchCursorFixture
            {
                public class PagedAlpha
                {
                    public virtual string Label { get; set; } = "alpha";
                    public virtual string ReadAlpha() => "alpha";
                }
                public interface IBetaContract { string ReadBeta(); string ContractLabel { get; set; } }
                public class PagedBeta : PagedAlpha, IBetaContract
                {
                    public override string Label { get; set; } = "beta";
                    public override string ReadAlpha() => "beta";
                    public string ContractLabel { get; set; } = "beta";
                    public string ReadBeta()
                    {
                        var result = "beta";
                        return result;
                    }
                }
                public sealed class BetaInvoker { public string Invoke() => new PagedBeta().ReadBeta(); }
                public class PagedGamma { }
            }
            """ + Environment.NewLine + budgetTypeSource);
        var replacementPath = AssemblyTestHelper.EmitAssembly(fixture, "SearchCursorReplacement", "namespace SearchCursorFixture; public class Replacement { }");
        var neighborSentinelPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, "NeighborSentinel.cs");
        await File.WriteAllTextAsync(neighborSentinelPath, "public sealed class NeighborSentinel { }");
        var originalBytes = await File.ReadAllBytesAsync(assemblyPath);
        var configPath = Path.Combine(Path.GetTempPath(), "ainet-search-cursor-" + Guid.NewGuid().ToString("N") + ".json");
        var logDirectory = Path.Combine(Path.GetTempPath(), "ainet-search-cursor-logs-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Information\"}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var process = await StartInitializedHostAsync(repositoryRoot, hostAssemblyPath, configPath, timeout.Token, logDirectory);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await SendRequestAsync(process, 2, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = assemblyPath, pattern = "Paged", kind = "type", isRegex = false, maxFiles = 0, maxResults = 1 },
            }, timeout.Token);
            var first = await ReadResponseAsync(process, 2, timeout.Token);
            var firstText = GetFirstText(first);
            Assert.False(first.GetProperty("result").GetProperty("isError").GetBoolean(), firstText);
            var firstPayload = ParsePayload(firstText);
            var firstHits = firstPayload.GetProperty("results").EnumerateArray().ToArray();
            Assert.Single(firstHits);
            Assert.Equal("PagedAlpha", firstHits[0].GetProperty("symbol").GetString());
            Assert.Equal(Path.GetFullPath(assemblyPath), firstHits[0].GetProperty("ownerTargetPath").GetString(), StringComparer.OrdinalIgnoreCase);
            Assert.StartsWith("h:", firstHits[0].GetProperty("handoffId").GetString(), StringComparison.Ordinal);
            Assert.True(firstPayload.GetProperty("truncated").GetBoolean());
            Assert.Contains("maxResults", firstPayload.GetProperty("truncatedBy").EnumerateArray().Select(value => value.GetString()));
            var cursor = firstPayload.GetProperty("continuationToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(cursor));

            await SendRequestAsync(process, 3, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = assemblyPath, pattern = "Paged", kind = "type", isRegex = false, maxFiles = 0, maxResults = 1, continuationToken = cursor },
            }, timeout.Token);
            var second = await ReadResponseAsync(process, 3, timeout.Token);
            var secondText = GetFirstText(second);
            Assert.False(second.GetProperty("result").GetProperty("isError").GetBoolean(), secondText);
            var secondPayload = ParsePayload(secondText);
            Assert.Equal("PagedBeta", secondPayload.GetProperty("results")[0].GetProperty("symbol").GetString());
            Assert.StartsWith("h:", secondPayload.GetProperty("results")[0].GetProperty("handoffId").GetString(), StringComparison.Ordinal);
            Assert.NotEqual(cursor, secondPayload.GetProperty("continuationToken").GetString());

            await SendRequestAsync(process, 4, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = assemblyPath, pattern = "Paged", kind = "type", isRegex = false, maxFiles = 0, maxResults = 1, continuationToken = cursor },
            }, timeout.Token);
            var replay = await ReadResponseAsync(process, 4, timeout.Token);
            Assert.False(replay.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(replay));
            var replayPayload = ParsePayload(GetFirstText(replay));
            var replayHit = replayPayload.GetProperty("results")[0];
            var secondHit = secondPayload.GetProperty("results")[0];
            Assert.Equal(secondHit.GetProperty("symbol").GetString(), replayHit.GetProperty("symbol").GetString());
            Assert.Equal(secondHit.GetProperty("filePath").GetString(), replayHit.GetProperty("filePath").GetString());
            Assert.Equal(secondHit.GetProperty("lineNumber").GetInt32(), replayHit.GetProperty("lineNumber").GetInt32());
            Assert.Equal(secondHit.GetProperty("ownerTargetPath").GetString(), replayHit.GetProperty("ownerTargetPath").GetString());
            Assert.Equal(secondPayload.GetProperty("continuationToken").GetString(), replayPayload.GetProperty("continuationToken").GetString());

            await SendRequestAsync(process, 15, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = assemblyPath, pattern = "Paged", kind = "type", isRegex = false, maxFiles = 0, maxResults = 1, continuationToken = secondPayload.GetProperty("continuationToken").GetString() },
            }, timeout.Token);
            var third = await ReadResponseAsync(process, 15, timeout.Token);
            var thirdText = GetFirstText(third);
            Assert.False(third.GetProperty("result").GetProperty("isError").GetBoolean(), thirdText);
            var thirdPayload = ParsePayload(thirdText);
            Assert.Equal("PagedGamma", thirdPayload.GetProperty("results")[0].GetProperty("symbol").GetString());
            Assert.False(thirdPayload.GetProperty("truncated").GetBoolean());
            Assert.Contains("Status: operation=ok, completeness=complete", thirdText, StringComparison.Ordinal);

            await SendRequestAsync(process, 5, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = assemblyPath, pattern = "Paged", kind = "type", isRegex = false, maxFiles = 0, maxResults = 2, continuationToken = cursor },
            }, timeout.Token);
            var wrongArguments = await ReadResponseAsync(process, 5, timeout.Token);
            Assert.True(wrongArguments.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("INVALID_ARGUMENT", GetFirstText(wrongArguments), StringComparison.Ordinal);

            await SendRequestAsync(process, 6, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = hostAssemblyPath, pattern = "Paged", kind = "type", isRegex = false, maxFiles = 0, maxResults = 1, continuationToken = cursor },
            }, timeout.Token);
            var wrongTarget = await ReadResponseAsync(process, 6, timeout.Token);
            Assert.True(wrongTarget.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("INVALID_ARGUMENT", GetFirstText(wrongTarget), StringComparison.Ordinal);

            await SendRequestAsync(process, 42, "tools/call", new
            {
                name = "get_type_hierarchy",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = firstHits[0].GetProperty("handoffId").GetString(), maxResults = 10 },
            }, timeout.Token);
            var hierarchy = await ReadResponseAsync(process, 42, timeout.Token);
            var hierarchyText = GetFirstText(hierarchy);

            await SendRequestAsync(process, 43, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = assemblyPath, pattern = "IBetaContract", kind = "type", isRegex = false, maxFiles = 0, maxResults = 10 },
            }, timeout.Token);
            var interfaceSearch = await ReadResponseAsync(process, 43, timeout.Token);
            var interfaceSearchText = GetFirstText(interfaceSearch);
            Assert.False(interfaceSearch.GetProperty("result").GetProperty("isError").GetBoolean(), interfaceSearchText);
            var interfaceHandle = ParsePayload(interfaceSearchText).GetProperty("results")[0].GetProperty("handoffId").GetString();
            await SendRequestAsync(process, 44, "tools/call", new
            {
                name = "find_implementations",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = interfaceHandle, maxResults = 10 },
            }, timeout.Token);
            var implementations = await ReadResponseAsync(process, 44, timeout.Token);
            var implementationsText = GetFirstText(implementations);
            Assert.False(hierarchy.GetProperty("result").GetProperty("isError").GetBoolean(),
                $"Assembly hierarchy failed: {hierarchyText}\nAssembly implementation call: {implementationsText}");
            Assert.False(implementations.GetProperty("result").GetProperty("isError").GetBoolean(), implementationsText);
            var hierarchyPayload = ParsePayload(hierarchyText);
            var betaSubtype = hierarchyPayload.GetProperty("subtypes").EnumerateArray()
                .Single(entry => entry.GetProperty("name").GetString()!.Contains("PagedBeta", StringComparison.Ordinal));
            Assert.StartsWith("h:", betaSubtype.GetProperty("handoffId").GetString(), StringComparison.Ordinal);
            var betaImplementation = ParsePayload(implementationsText).GetProperty("implementations").EnumerateArray()
                .Single(entry => entry.GetProperty("symbolName").GetString() == "PagedBeta");
            Assert.StartsWith("h:", betaImplementation.GetProperty("handoffId").GetString(), StringComparison.Ordinal);

            await SendRequestAsync(process, 45, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { betaSubtype.GetProperty("handoffId").GetString() } },
            }, timeout.Token);
            var hierarchyBody = await ReadResponseAsync(process, 45, timeout.Token);
            Assert.False(hierarchyBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(hierarchyBody));
            Assert.Contains("class PagedBeta", GetFirstText(hierarchyBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 46, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { betaImplementation.GetProperty("handoffId").GetString() } },
            }, timeout.Token);
            var implementationBody = await ReadResponseAsync(process, 46, timeout.Token);
            Assert.False(implementationBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(implementationBody));
            Assert.Contains("class PagedBeta", GetFirstText(implementationBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 47, "tools/call", new
            {
                name = "get_type_hierarchy",
                arguments = new { targetPath = hostAssemblyPath, symbolIdentifier = firstHits[0].GetProperty("handoffId").GetString(), maxResults = 10 },
            }, timeout.Token);
            var foreignHierarchy = await ReadResponseAsync(process, 47, timeout.Token);
            Assert.True(foreignHierarchy.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(foreignHierarchy));
            Assert.Contains("TARGET_MISMATCH", GetFirstText(foreignHierarchy), StringComparison.Ordinal);

            await SendRequestAsync(process, 48, "tools/call", new
            {
                name = "find_implementations",
                arguments = new { targetPath = hostAssemblyPath, symbolIdentifier = interfaceHandle, maxResults = 10 },
            }, timeout.Token);
            var foreignImplementations = await ReadResponseAsync(process, 48, timeout.Token);
            Assert.True(foreignImplementations.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(foreignImplementations));
            Assert.Contains("TARGET_MISMATCH", GetFirstText(foreignImplementations), StringComparison.Ordinal);

            await SendRequestAsync(process, 49, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = assemblyPath, pattern = "ReadAlpha", kind = "method", isRegex = false, maxFiles = 0, maxResults = 10 },
            }, timeout.Token);
            var virtualMethodSearch = await ReadResponseAsync(process, 49, timeout.Token);
            var virtualMethodSearchText = GetFirstText(virtualMethodSearch);
            Assert.False(virtualMethodSearch.GetProperty("result").GetProperty("isError").GetBoolean(), virtualMethodSearchText);
            var methodHits = ParsePayload(virtualMethodSearchText).GetProperty("results").EnumerateArray().ToArray();
            var baseMethodHandle = methodHits.Single(hit => hit.GetProperty("text").GetString()!.Contains("virtual", StringComparison.Ordinal))
                .GetProperty("handoffId").GetString();
            await SendRequestAsync(process, 50, "tools/call", new
            {
                name = "find_implementations",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = baseMethodHandle, maxResults = 10 },
            }, timeout.Token);
            var methodOverrides = await ReadResponseAsync(process, 50, timeout.Token);
            var methodOverridesText = GetFirstText(methodOverrides);
            Assert.False(methodOverrides.GetProperty("result").GetProperty("isError").GetBoolean(), methodOverridesText);
            var methodOverride = ParsePayload(methodOverridesText).GetProperty("implementations").EnumerateArray()
                .Single(entry => entry.GetProperty("symbolName").GetString() == "ReadAlpha");
            await SendRequestAsync(process, 51, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { methodOverride.GetProperty("handoffId").GetString() } },
            }, timeout.Token);
            var methodOverrideBody = await ReadResponseAsync(process, 51, timeout.Token);
            Assert.False(methodOverrideBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(methodOverrideBody));
            Assert.Contains("beta", GetFirstText(methodOverrideBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 52, "tools/call", new
            {
                name = "get_class_structure",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = firstHits[0].GetProperty("handoffId").GetString(), nameFilter = "Label" },
            }, timeout.Token);
            var propertySearch = await ReadResponseAsync(process, 52, timeout.Token);
            var propertySearchText = GetFirstText(propertySearch);
            Assert.False(propertySearch.GetProperty("result").GetProperty("isError").GetBoolean(), propertySearchText);
            Assert.Contains("Label", propertySearchText, StringComparison.Ordinal);
            var basePropertyHandle = ExtractHandoff(propertySearchText);
            await SendRequestAsync(process, 53, "tools/call", new
            {
                name = "find_implementations",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = basePropertyHandle, maxResults = 10 },
            }, timeout.Token);
            var propertyOverrides = await ReadResponseAsync(process, 53, timeout.Token);
            var propertyOverridesText = GetFirstText(propertyOverrides);
            Assert.False(propertyOverrides.GetProperty("result").GetProperty("isError").GetBoolean(), propertyOverridesText);
            var propertyOverride = ParsePayload(propertyOverridesText).GetProperty("implementations").EnumerateArray()
                .Single(entry => entry.GetProperty("symbolName").GetString() == "Label");
            await SendRequestAsync(process, 54, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { propertyOverride.GetProperty("handoffId").GetString() } },
            }, timeout.Token);
            var propertyOverrideBody = await ReadResponseAsync(process, 54, timeout.Token);
            Assert.False(propertyOverrideBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(propertyOverrideBody));
            Assert.Contains("beta", GetFirstText(propertyOverrideBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 55, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = assemblyPath, pattern = "ReadBeta", kind = "method", isRegex = false, maxFiles = 0, maxResults = 10 },
            }, timeout.Token);
            var contractMethodSearch = await ReadResponseAsync(process, 55, timeout.Token);
            var contractMethodSearchText = GetFirstText(contractMethodSearch);
            Assert.False(contractMethodSearch.GetProperty("result").GetProperty("isError").GetBoolean(), contractMethodSearchText);
            var contractMethodHits = ParsePayload(contractMethodSearchText).GetProperty("results").EnumerateArray().ToArray();
            var contractMethodHandle = contractMethodHits.Single(hit => hit.GetProperty("text").GetString()!.TrimEnd().EndsWith(";", StringComparison.Ordinal))
                .GetProperty("handoffId").GetString();
            await SendRequestAsync(process, 56, "tools/call", new
            {
                name = "find_implementations",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = contractMethodHandle, maxResults = 10 },
            }, timeout.Token);
            var contractMethodImplementations = await ReadResponseAsync(process, 56, timeout.Token);
            var contractMethodImplementationsText = GetFirstText(contractMethodImplementations);
            Assert.False(contractMethodImplementations.GetProperty("result").GetProperty("isError").GetBoolean(), contractMethodImplementationsText);
            var contractMethodImplementation = ParsePayload(contractMethodImplementationsText).GetProperty("implementations").EnumerateArray()
                .Single(entry => entry.GetProperty("symbolName").GetString() == "ReadBeta");
            await SendRequestAsync(process, 57, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { contractMethodImplementation.GetProperty("handoffId").GetString() } },
            }, timeout.Token);
            var contractMethodBody = await ReadResponseAsync(process, 57, timeout.Token);
            Assert.False(contractMethodBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(contractMethodBody));
            Assert.Contains("ReadBeta", GetFirstText(contractMethodBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 58, "tools/call", new
            {
                name = "get_class_structure",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = interfaceHandle, nameFilter = "ContractLabel" },
            }, timeout.Token);
            var contractPropertySearch = await ReadResponseAsync(process, 58, timeout.Token);
            var contractPropertySearchText = GetFirstText(contractPropertySearch);
            Assert.False(contractPropertySearch.GetProperty("result").GetProperty("isError").GetBoolean(), contractPropertySearchText);
            Assert.Contains("ContractLabel", contractPropertySearchText, StringComparison.Ordinal);
            var contractPropertyHandle = ExtractHandoff(contractPropertySearchText);
            await SendRequestAsync(process, 59, "tools/call", new
            {
                name = "find_implementations",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = contractPropertyHandle, maxResults = 10 },
            }, timeout.Token);
            var contractPropertyImplementations = await ReadResponseAsync(process, 59, timeout.Token);
            var contractPropertyImplementationsText = GetFirstText(contractPropertyImplementations);
            Assert.False(contractPropertyImplementations.GetProperty("result").GetProperty("isError").GetBoolean(), contractPropertyImplementationsText);
            var contractPropertyImplementation = ParsePayload(contractPropertyImplementationsText).GetProperty("implementations").EnumerateArray()
                .Single(entry => entry.GetProperty("symbolName").GetString() == "ContractLabel");
            await SendRequestAsync(process, 60, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { contractPropertyImplementation.GetProperty("handoffId").GetString() } },
            }, timeout.Token);
            var contractPropertyBody = await ReadResponseAsync(process, 60, timeout.Token);
            Assert.False(contractPropertyBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(contractPropertyBody));
            Assert.Contains("ContractLabel", GetFirstText(contractPropertyBody), StringComparison.Ordinal);

            var betaHandle = secondPayload.GetProperty("results")[0].GetProperty("handoffId").GetString()!;
            await SendRequestAsync(process, 7, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { betaHandle } },
            }, timeout.Token);
            var betaBody = await ReadResponseAsync(process, 7, timeout.Token);
            Assert.False(betaBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(betaBody));
            Assert.Contains("class PagedBeta", GetFirstText(betaBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 11, "tools/call", new
            {
                name = "get_class_structure",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = betaHandle },
            }, timeout.Token);
            var betaStructure = await ReadResponseAsync(process, 11, timeout.Token);
            Assert.False(betaStructure.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(betaStructure));
            Assert.Contains("PagedBeta", GetFirstText(betaStructure), StringComparison.Ordinal);

            await SendRequestAsync(process, 23, "tools/call", new
            {
                name = "get_assembly_context",
                arguments = new
                {
                    targetPath = assemblyPath,
                    symbolIdentifier = betaHandle,
                    includeBody = true,
                    includeClassStructure = true,
                    includeCallers = true,
                    includeImpact = true,
                    maxBodyLines = 1,
                    maxCallers = 10,
                    depth = 1,
                    topN = 10,
                    detailLevel = " STANDARD ",
                },
            }, timeout.Token);
            var composedAssemblyContext = await ReadResponseAsync(process, 23, timeout.Token);
            var composedAssemblyContextText = GetFirstText(composedAssemblyContext);
            Assert.False(composedAssemblyContext.GetProperty("result").GetProperty("isError").GetBoolean(), composedAssemblyContextText);
            Assert.Contains("## Class Structure", composedAssemblyContextText, StringComparison.Ordinal);
            Assert.Contains("## Callers", composedAssemblyContextText, StringComparison.Ordinal);
            Assert.Contains("BetaInvoker.Invoke", composedAssemblyContextText, StringComparison.Ordinal);
            Assert.Contains("## Impact", composedAssemblyContextText, StringComparison.Ordinal);
            Assert.Contains("## Body", composedAssemblyContextText, StringComparison.Ordinal);
            Assert.Contains("completeness=truncated", composedAssemblyContextText, StringComparison.Ordinal);
            var betaInvokerLine = composedAssemblyContextText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .First(line => line.Contains("BetaInvoker.Invoke", StringComparison.Ordinal) && line.Contains("[handoff: ", StringComparison.Ordinal));
            var betaInvokerHandleStart = betaInvokerLine.IndexOf("[handoff: ", StringComparison.Ordinal);
            Assert.True(betaInvokerHandleStart >= 0, $"Expected a proven assembly-owner handoff on the caller line: {betaInvokerLine}");
            var betaInvokerHandle = betaInvokerLine[(betaInvokerHandleStart + "[handoff: ".Length)..].Split(']')[0];
            await SendRequestAsync(process, 24, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { betaInvokerHandle } },
            }, timeout.Token);
            var betaInvokerBody = await ReadResponseAsync(process, 24, timeout.Token);
            Assert.False(betaInvokerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(betaInvokerBody));
            Assert.Contains("Invoke()", GetFirstText(betaInvokerBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 25, "tools/call", new
            {
                name = "get_assembly_context",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = betaHandle, detailLevel = "tiny" },
            }, timeout.Token);
            var invalidContextDetail = await ReadResponseAsync(process, 25, timeout.Token);
            Assert.True(invalidContextDetail.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(invalidContextDetail));
            Assert.Contains("detailLevel", GetFirstText(invalidContextDetail), StringComparison.Ordinal);

            await SendRequestAsync(process, 26, "tools/call", new
            {
                name = "get_assembly_context",
                arguments = new { targetPath = assemblyPath, includeReferences = true, maxResults = 100, detailLevel = "FULL", maxResponseBytes = 512 },
            }, timeout.Token);
            var smallBudgetContext = await ReadResponseAsync(process, 26, timeout.Token);
            Assert.False(smallBudgetContext.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(smallBudgetContext));
            Assert.Contains("completeness=truncated", GetFirstText(smallBudgetContext), StringComparison.Ordinal);

            await SendRequestAsync(process, 27, "tools/call", new
            {
                name = "get_assembly_context",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = " " },
            }, timeout.Token);
            var blankContextSymbol = await ReadResponseAsync(process, 27, timeout.Token);
            Assert.True(blankContextSymbol.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(blankContextSymbol));
            Assert.Contains("symbolIdentifier", GetFirstText(blankContextSymbol), StringComparison.Ordinal);

            await SendRequestAsync(process, 28, "tools/call", new
            {
                name = "get_assembly_context",
                arguments = new { targetPath = assemblyPath, maxResults = 1000, detailLevel = "standard" },
            }, timeout.Token);
            var standardDefaultBudget = await ReadResponseAsync(process, 28, timeout.Token);
            Assert.False(standardDefaultBudget.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(standardDefaultBudget));
            Assert.Contains("completeness=truncated", GetFirstText(standardDefaultBudget), StringComparison.Ordinal);
            var standardDefaultText = GetFirstText(standardDefaultBudget);
            Assert.NotNull(TryReadStringLine(standardDefaultText, "continuationToken"));

            await SendRequestAsync(process, 29, "tools/call", new
            {
                name = "get_assembly_context",
                arguments = new { targetPath = assemblyPath, maxResults = 1000, detailLevel = "full" },
            }, timeout.Token);
            var fullDefaultBudget = await ReadResponseAsync(process, 29, timeout.Token);
            Assert.False(fullDefaultBudget.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(fullDefaultBudget));
            var fullDefaultText = GetFirstText(fullDefaultBudget);
            Assert.True(fullDefaultText.Length > standardDefaultText.Length,
                "The 64 KiB full default should return a larger first response window than the 32 KiB standard default.");
            var fullDefaultPageCount = 1;
            var fullDefaultRequestId = 30;
            while (TryReadStringLine(fullDefaultText, "continuationToken") is { } fullDefaultToken)
            {
                Assert.True(fullDefaultRequestId < 40, "The full-default response should finish within the bounded page loop.");
                await SendRequestAsync(process, fullDefaultRequestId, "tools/call", new
                {
                    name = "get_assembly_context",
                    arguments = new { targetPath = assemblyPath, maxResults = 1000, detailLevel = "full", continuationToken = fullDefaultToken },
                }, timeout.Token);
                var page = await ReadResponseAsync(process, fullDefaultRequestId++, timeout.Token);
                fullDefaultText = GetFirstText(page);
                Assert.False(page.GetProperty("result").GetProperty("isError").GetBoolean(), fullDefaultText);
                fullDefaultPageCount++;
            }
            Assert.True(fullDefaultPageCount > 1, "The large context should exercise full-default response paging.");
            Assert.Contains("completeness=complete", fullDefaultText, StringComparison.Ordinal);

            await SendRequestAsync(process, 40, "tools/call", new
            {
                name = "get_file_skeleton",
                arguments = new { targetPath = assemblyPath, filePaths = new[] { betaHandle } },
            }, timeout.Token);
            var assemblyHandleSkeleton = await ReadResponseAsync(process, 40, timeout.Token);
            Assert.False(assemblyHandleSkeleton.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(assemblyHandleSkeleton));
            Assert.Contains("PagedBeta", GetFirstText(assemblyHandleSkeleton), StringComparison.Ordinal);
            var assemblySkeletonHandle = ExtractSkeletonHandoff(GetFirstText(assemblyHandleSkeleton));
            await SendRequestAsync(process, 41, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { assemblySkeletonHandle } },
            }, timeout.Token);
            var assemblyHandleBody = await ReadResponseAsync(process, 41, timeout.Token);
            Assert.False(assemblyHandleBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(assemblyHandleBody));
            Assert.Contains("ReadBeta", GetFirstText(assemblyHandleBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 16, "tools/call", new
            {
                name = "get_class_structure",
                arguments = new { targetPath = hostAssemblyPath, symbolIdentifier = betaHandle },
            }, timeout.Token);
            var foreignTargetStructure = await ReadResponseAsync(process, 16, timeout.Token);
            Assert.True(foreignTargetStructure.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(foreignTargetStructure));
            Assert.Contains("INVALID_ARGUMENT", GetFirstText(foreignTargetStructure), StringComparison.Ordinal);

            await SendRequestAsync(process, 17, "tools/call", new
            {
                name = "get_file_tree",
                arguments = new { targetPath = assemblyPath, view = "files", includeExtensions = new[] { ".cs" }, fileFilter = "*PagedBeta.cs", includeMetadata = false, maxResults = 100 },
            }, timeout.Token);
            var assemblyFileTree = await ReadResponseAsync(process, 17, timeout.Token);
            var assemblyFileTreeText = GetFirstText(assemblyFileTree);
            Assert.False(assemblyFileTree.GetProperty("result").GetProperty("isError").GetBoolean(), assemblyFileTreeText);
            Assert.Contains("PagedBeta.cs", assemblyFileTreeText, StringComparison.Ordinal);
            Assert.DoesNotContain("NeighborSentinel.cs", assemblyFileTreeText, StringComparison.Ordinal);
            var betaTreePath = assemblyFileTreeText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Single(line => line.Contains("PagedBeta.cs", StringComparison.Ordinal));
            betaTreePath = betaTreePath.StartsWith("- ", StringComparison.Ordinal) ? betaTreePath[2..] : betaTreePath;

            await SendRequestAsync(process, 18, "tools/call", new
            {
                name = "get_file_skeleton",
                arguments = new { targetPath = assemblyPath, filePaths = new[] { betaTreePath } },
            }, timeout.Token);
            var assemblySkeleton = await ReadResponseAsync(process, 18, timeout.Token);
            var assemblySkeletonText = GetFirstText(assemblySkeleton);
            Assert.False(assemblySkeleton.GetProperty("result").GetProperty("isError").GetBoolean(), assemblySkeletonText);
            Assert.Contains("ReadBeta", assemblySkeletonText, StringComparison.Ordinal);
            var skeletonMemberHandle = ExtractSkeletonHandoff(assemblySkeletonText);
            await SendRequestAsync(process, 19, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { skeletonMemberHandle } },
            }, timeout.Token);
            var skeletonMemberBody = await ReadResponseAsync(process, 19, timeout.Token);
            Assert.False(skeletonMemberBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(skeletonMemberBody));
            Assert.Contains("ReadBeta()", GetFirstText(skeletonMemberBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 20, "tools/call", new
            {
                name = "get_file_skeleton",
                arguments = new { targetPath = assemblyPath, filePaths = new[] { betaTreePath, "MissingNavigationFixture.cs" } },
            }, timeout.Token);
            var mixedAssemblySkeleton = await ReadResponseAsync(process, 20, timeout.Token);
            var mixedAssemblySkeletonText = GetFirstText(mixedAssemblySkeleton);
            Assert.False(mixedAssemblySkeleton.GetProperty("result").GetProperty("isError").GetBoolean(), mixedAssemblySkeletonText);
            Assert.Contains("ReadBeta", mixedAssemblySkeletonText, StringComparison.Ordinal);
            Assert.Contains("completeness=truncated", mixedAssemblySkeletonText, StringComparison.Ordinal);

            await SendRequestAsync(process, 21, "tools/call", new
            {
                name = "get_file_skeleton",
                arguments = new { targetPath = assemblyPath, filePaths = new[] { "MissingNavigationFixture.cs" } },
            }, timeout.Token);
            var allMissingSkeleton = await ReadResponseAsync(process, 21, timeout.Token);
            Assert.True(allMissingSkeleton.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(allMissingSkeleton));
            Assert.Contains("INVALID_ARGUMENT", GetFirstText(allMissingSkeleton), StringComparison.Ordinal);

            await SendRequestAsync(process, 22, "tools/call", new
            {
                name = "get_file_skeleton",
                arguments = new { targetPath = assemblyPath, filePaths = new[] { secondPayload.GetProperty("results")[0].GetProperty("filePath").GetString() } },
            }, timeout.Token);
            var absoluteAssemblySkeleton = await ReadResponseAsync(process, 22, timeout.Token);
            Assert.False(absoluteAssemblySkeleton.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(absoluteAssemblySkeleton));
            Assert.Contains("ReadBeta", GetFirstText(absoluteAssemblySkeleton), StringComparison.Ordinal);

            await SendRequestAsync(process, 12, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = assemblyPath, pattern = "ReadBeta", kind = "method", isRegex = false, maxFiles = 0, maxResults = 1 },
            }, timeout.Token);
            var methodSearch = await ReadResponseAsync(process, 12, timeout.Token);
            var methodSearchText = GetFirstText(methodSearch);
            Assert.False(methodSearch.GetProperty("result").GetProperty("isError").GetBoolean(), methodSearchText);
            var methodHit = ParsePayload(methodSearchText).GetProperty("results")[0];
            Assert.Equal("ReadBeta", methodHit.GetProperty("symbol").GetString());
            var methodHandle = methodHit.GetProperty("handoffId").GetString()!;
            await SendRequestAsync(process, 13, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { methodHandle } },
            }, timeout.Token);
            var methodBody = await ReadResponseAsync(process, 13, timeout.Token);
            Assert.False(methodBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(methodBody));
            Assert.Contains("ReadBeta()", GetFirstText(methodBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 14, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = hostAssemblyPath, symbolIdentifiers = new[] { methodHandle } },
            }, timeout.Token);
            var foreignTargetBody = await ReadResponseAsync(process, 14, timeout.Token);
            Assert.True(foreignTargetBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(foreignTargetBody));

            await SendRequestAsync(process, 8, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = assemblyPath, pattern = "SearchCursorFixture.PagedGamma", kind = "type", isRegex = false, maxFiles = 0, maxResults = 0 },
            }, timeout.Token);
            var qualified = await ReadResponseAsync(process, 8, timeout.Token);
            var qualifiedText = GetFirstText(qualified);
            Assert.False(qualified.GetProperty("result").GetProperty("isError").GetBoolean(), qualifiedText);
            var qualifiedPayload = ParsePayload(qualifiedText);
            Assert.Single(qualifiedPayload.GetProperty("results").EnumerateArray());
            Assert.Equal("PagedGamma", qualifiedPayload.GetProperty("results")[0].GetProperty("symbol").GetString());
            Assert.False(qualifiedPayload.GetProperty("truncated").GetBoolean());

            File.Copy(replacementPath, assemblyPath, overwrite: true);
            await SendRequestAsync(process, 9, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = assemblyPath, pattern = "Paged", kind = "type", isRegex = false, maxFiles = 0, maxResults = 1, continuationToken = cursor },
            }, timeout.Token);
            var staleCursor = await ReadResponseAsync(process, 9, timeout.Token);
            Assert.True(staleCursor.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("INVALID_ARGUMENT", GetFirstText(staleCursor), StringComparison.Ordinal);

            await SendRequestAsync(process, 10, "shutdown", new { }, timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await process.StandardOutput.ReadToEndAsync(timeout.Token));
            _ = await stderrTask;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await File.WriteAllBytesAsync(assemblyPath, originalBytes);
            File.Delete(configPath);
            if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DependencyGraphKeepsSourceHandoffProjectIdentityForDuplicateTypeNames()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var hostAssemblyPath = GetHostAssemblyPath(repositoryRoot);
        using var fixture = TestTempDirectory.Create("dependency-identity-stdio-");
        var configPath = Path.Combine(Path.GetTempPath(), "ainet-dependency-" + Guid.NewGuid().ToString("N") + ".json");
        var hostLogDirectory = Path.Combine(Path.GetTempPath(), "ainet-dependency-logs-" + Guid.NewGuid().ToString("N"));
        var firstProject = Path.Combine(fixture.DirectoryPath, "First", "Shared.csproj");
        var secondProject = Path.Combine(fixture.DirectoryPath, "Second", "Shared.csproj");
        var testProject = Path.Combine(fixture.DirectoryPath, "First.Tests", "First.Tests.csproj");
        var solutionPath = Path.Combine(fixture.DirectoryPath, "DuplicateTypes.slnx");
        Directory.CreateDirectory(Path.GetDirectoryName(firstProject)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondProject)!);
        Directory.CreateDirectory(Path.GetDirectoryName(testProject)!);
        await File.WriteAllTextAsync(solutionPath, "<Solution><Folder Name=\"/First/\"><Project Path=\"First/Shared.csproj\" /></Folder><Folder Name=\"/Second/\"><Project Path=\"Second/Shared.csproj\" /></Folder><Folder Name=\"/First.Tests/\"><Project Path=\"First.Tests/First.Tests.csproj\" /></Folder></Solution>");
        const string projectText = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>Same.Graph.Assembly</AssemblyName><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>";
        await File.WriteAllTextAsync(firstProject, projectText);
        await File.WriteAllTextAsync(secondProject, projectText);
        await File.WriteAllTextAsync(testProject,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include=\"../First/Shared.csproj\" /></ItemGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(fixture.DirectoryPath, "First", "Shared.cs"),
            "namespace SharedGraph; public sealed class LeafFirst { } public sealed class Consumer { public LeafFirst Read() => new(); }");
        await File.WriteAllTextAsync(Path.Combine(fixture.DirectoryPath, "Second", "Shared.cs"),
            "namespace SharedGraph; public sealed class LeafSecond { } public sealed class Consumer { public LeafSecond Read() => new(); }");
        await File.WriteAllTextAsync(Path.Combine(fixture.DirectoryPath, "First", "Generated.g.cs"),
            "namespace SharedGraph; public sealed class GeneratedConsumer { public LeafFirst Read() => new(); }");
        await File.WriteAllTextAsync(Path.Combine(fixture.DirectoryPath, "First.Tests", "TestConsumer.cs"),
            "namespace SharedGraph.Tests; public sealed class TestConsumer { public SharedGraph.LeafFirst Read() => new(); }");
        await RestoreProjectAsync(firstProject, fixture.DirectoryPath);
        await RestoreProjectAsync(secondProject, fixture.DirectoryPath);
        await RestoreProjectAsync(testProject, fixture.DirectoryPath);
        await using (var resident = AiNetCodeNavigator.Core.Workspace.MSBuildSolutionLoader.CreateResidentSolution(solutionPath))
        {
            var snapshot = await resident.GetCurrentSnapshotAsync(CancellationToken.None);
            Assert.True(snapshot.Succeeded, snapshot.Error?.Message);
            var firstLoaded = snapshot.Solution!.Projects.Single(project => project.FilePath == firstProject);
            var secondLoaded = snapshot.Solution.Projects.Single(project => project.FilePath == secondProject);
            Assert.Equal(firstLoaded.Name, secondLoaded.Name);
            Assert.NotEqual(firstLoaded.FilePath, secondLoaded.FilePath);
        }
        await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Information\"}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var process = await StartInitializedHostAsync(repositoryRoot, hostAssemblyPath, configPath, timeout.Token, hostLogDirectory);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await SendRequestAsync(process, 2, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = solutionPath, pattern = "Consumer", kind = "class", maxResults = 10 },
            }, timeout.Token);
            var found = await ReadResponseAsync(process, 2, timeout.Token);
            Assert.False(found.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(found));
            var findText = GetFirstText(found);
            Assert.Contains("(Shared)", findText, StringComparison.Ordinal);
            Assert.DoesNotContain("(Same.Graph.Assembly)", findText, StringComparison.Ordinal);
            var firstEntry = findText.Split('\n').FirstOrDefault(line => line.Contains("First/Shared.cs", StringComparison.Ordinal));
            Assert.True(firstEntry is not null, findText);
            var handoff = ExtractHandoff(firstEntry!);

            await SendRequestAsync(process, 3, "tools/call", new
            {
                name = "dependency_graph",
                arguments = new { targetPath = solutionPath, symbolIdentifier = handoff, direction = "outgoing", depth = 1 },
            }, timeout.Token);
            var graph = await ReadResponseAsync(process, 3, timeout.Token);
            Assert.False(graph.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(graph));
            var graphText = GetFirstText(graph);
            Assert.Contains("LeafFirst", graphText, StringComparison.Ordinal);
            Assert.DoesNotContain("LeafSecond", graphText, StringComparison.Ordinal);

            await SendRequestAsync(process, 4, "tools/call", new
            {
                name = "dependency_graph",
                arguments = new { targetPath = solutionPath, filePath = "First/Shared.cs", direction = "outgoing", depth = 1 },
            }, timeout.Token);
            var fileGraph = await ReadResponseAsync(process, 4, timeout.Token);
            Assert.False(fileGraph.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(fileGraph));
            Assert.Contains("LeafFirst", GetFirstText(fileGraph), StringComparison.Ordinal);
            Assert.DoesNotContain("LeafSecond", GetFirstText(fileGraph), StringComparison.Ordinal);

            await SendRequestAsync(process, 5, "tools/call", new
            {
                name = "dependency_graph",
                arguments = new { targetPath = solutionPath, filePath = "Shared.cs", direction = "outgoing" },
            }, timeout.Token);
            var ambiguousFileGraph = await ReadResponseAsync(process, 5, timeout.Token);
            Assert.True(ambiguousFileGraph.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(ambiguousFileGraph));
            Assert.Contains("multiple documents", GetFirstText(ambiguousFileGraph), StringComparison.OrdinalIgnoreCase);

            await SendRequestAsync(process, 6, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = solutionPath, pattern = "GeneratedConsumer", kind = "class", includeGenerated = true, maxResults = 5 },
            }, timeout.Token);
            var generatedFind = await ReadResponseAsync(process, 6, timeout.Token);
            Assert.False(generatedFind.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(generatedFind));
            var generatedHandoff = ExtractHandoff(GetFirstText(generatedFind));
            await SendRequestAsync(process, 7, "tools/call", new
            {
                name = "dependency_graph",
                arguments = new { targetPath = solutionPath, symbolIdentifier = generatedHandoff, direction = "outgoing" },
            }, timeout.Token);
            var generatedExcludedGraph = await ReadResponseAsync(process, 7, timeout.Token);
            Assert.False(generatedExcludedGraph.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(generatedExcludedGraph));
            Assert.DoesNotContain("LeafFirst", GetFirstText(generatedExcludedGraph), StringComparison.Ordinal);
            await SendRequestAsync(process, 8, "tools/call", new
            {
                name = "dependency_graph",
                arguments = new { targetPath = solutionPath, symbolIdentifier = generatedHandoff, direction = "outgoing", includeGenerated = true },
            }, timeout.Token);
            var generatedIncludedGraph = await ReadResponseAsync(process, 8, timeout.Token);
            Assert.False(generatedIncludedGraph.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(generatedIncludedGraph));
            Assert.Contains("LeafFirst", GetFirstText(generatedIncludedGraph), StringComparison.Ordinal);

            await SendRequestAsync(process, 9, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = solutionPath, pattern = "TestConsumer", kind = "class", maxResults = 5 },
            }, timeout.Token);
            var testFind = await ReadResponseAsync(process, 9, timeout.Token);
            Assert.False(testFind.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(testFind));
            var testHandoff = ExtractHandoff(GetFirstText(testFind));
            await SendRequestAsync(process, 10, "tools/call", new
            {
                name = "dependency_graph",
                arguments = new { targetPath = solutionPath, symbolIdentifier = testHandoff, direction = "outgoing", scopeType = "tests" },
            }, timeout.Token);
            var testScopeGraph = await ReadResponseAsync(process, 10, timeout.Token);
            Assert.False(testScopeGraph.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(testScopeGraph));
            Assert.Contains("LeafFirst", GetFirstText(testScopeGraph), StringComparison.Ordinal);
            await SendRequestAsync(process, 11, "tools/call", new
            {
                name = "dependency_graph",
                arguments = new { targetPath = solutionPath, symbolIdentifier = testHandoff, direction = "outgoing", scopeType = "production" },
            }, timeout.Token);
            var productionScopeGraph = await ReadResponseAsync(process, 11, timeout.Token);
            Assert.False(productionScopeGraph.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(productionScopeGraph));
            Assert.DoesNotContain("LeafFirst", GetFirstText(productionScopeGraph), StringComparison.Ordinal);

            await SendRequestAsync(process, 13, "tools/call", new
            {
                name = "get_namespace_tree",
                arguments = new { targetPath = solutionPath },
            }, timeout.Token);
            var projectOverview = await ReadResponseAsync(process, 13, timeout.Token);
            Assert.False(projectOverview.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(projectOverview));
            Assert.Contains("First/Shared.csproj", GetFirstText(projectOverview), StringComparison.Ordinal);
            Assert.Contains("Second/Shared.csproj", GetFirstText(projectOverview), StringComparison.Ordinal);

            await SendRequestAsync(process, 14, "tools/call", new
            {
                name = "get_namespace_tree",
                arguments = new { targetPath = solutionPath, namespacePrefix = "SharedGraph" },
            }, timeout.Token);
            var ambiguousNamespace = await ReadResponseAsync(process, 14, timeout.Token);
            Assert.True(ambiguousNamespace.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(ambiguousNamespace));
            Assert.Contains("AMBIGUOUS_SYMBOL", GetFirstText(ambiguousNamespace), StringComparison.Ordinal);
            Assert.Contains("First", GetFirstText(ambiguousNamespace), StringComparison.Ordinal);
            Assert.Contains("Second", GetFirstText(ambiguousNamespace), StringComparison.Ordinal);

            await SendRequestAsync(process, 15, "tools/call", new
            {
                name = "get_namespace_tree",
                arguments = new { targetPath = solutionPath, project = "Shared" },
            }, timeout.Token);
            var ambiguousProject = await ReadResponseAsync(process, 15, timeout.Token);
            Assert.True(ambiguousProject.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(ambiguousProject));
            Assert.Contains("AMBIGUOUS_SYMBOL", GetFirstText(ambiguousProject), StringComparison.Ordinal);

            await SendRequestAsync(process, 16, "tools/call", new
            {
                name = "get_namespace_tree",
                arguments = new { targetPath = solutionPath, project = firstProject },
            }, timeout.Token);
            var firstProjectTree = await ReadResponseAsync(process, 16, timeout.Token);
            Assert.False(firstProjectTree.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(firstProjectTree));
            var firstProjectText = GetFirstText(firstProjectTree);
            Assert.Contains("class LeafFirst", firstProjectText, StringComparison.Ordinal);
            Assert.DoesNotContain("LeafSecond", firstProjectText, StringComparison.Ordinal);
            var firstTypeLine = firstProjectText.Split('\n').Single(line => line.Contains("class LeafFirst", StringComparison.Ordinal));
            var namespaceTypeHandle = ExtractHandoff(firstTypeLine);
            await SendRequestAsync(process, 17, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = solutionPath, symbolIdentifiers = new[] { namespaceTypeHandle } },
            }, timeout.Token);
            var namespaceTypeBody = await ReadResponseAsync(process, 17, timeout.Token);
            Assert.False(namespaceTypeBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(namespaceTypeBody));
            Assert.Contains("class LeafFirst", GetFirstText(namespaceTypeBody), StringComparison.Ordinal);
            Assert.DoesNotContain("LeafSecond", GetFirstText(namespaceTypeBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 18, "tools/call", new
            {
                name = "get_namespace_tree",
                arguments = new { targetPath = solutionPath, project = "NoSuchProject" },
            }, timeout.Token);
            var unknownProject = await ReadResponseAsync(process, 18, timeout.Token);
            Assert.True(unknownProject.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(unknownProject));
            Assert.Contains("INVALID_ARGUMENT", GetFirstText(unknownProject), StringComparison.Ordinal);
            Assert.Contains("$.project", GetFirstText(unknownProject), StringComparison.Ordinal);

            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await process.StandardOutput.ReadToEndAsync(timeout.Token));
            _ = await stderrTask;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            File.Delete(configPath);
            if (Directory.Exists(hostLogDirectory)) Directory.Delete(hostLogDirectory, recursive: true);
            var resolvedFixtureRoot = Path.GetFullPath(fixture.DirectoryPath);
            Assert.StartsWith(Path.GetFullPath(TestTempDirectory.RootTempDirectory) + Path.DirectorySeparatorChar, resolvedFixtureRoot, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ColdSourceSolutionNavigationDoesNotAddOrChangeWorkspaceFiles()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var hostAssemblyPath = GetHostAssemblyPath(repositoryRoot);
        var fixtureRoot = Directory.CreateTempSubdirectory("ainet-cold-source-").FullName;
        var configPath = Path.Combine(Path.GetTempPath(), "ainet-cold-config-" + Guid.NewGuid().ToString("N") + ".json");
        var logDirectory = Path.Combine(Path.GetTempPath(), "ainet-cold-logs-" + Guid.NewGuid().ToString("N"));
        var solutionPath = Path.Combine(fixtureRoot, "Cold.slnx");
        var projectPath = Path.Combine(fixtureRoot, "Cold.csproj");
        Process? process = null;
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"Cold.csproj\" /></Solution>");
        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(fixtureRoot, "ColdMarker.cs"),
            "using System.Text;\nnamespace Cold.Sample;\npublic sealed class ColdMarker { public StringBuilder Build() => new(); }\n");
        await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Information\"}");

        try
        {
            await RestoreProjectAsync(projectPath, fixtureRoot);
            Assert.False(Directory.Exists(Path.Combine(fixtureRoot, "bin")), "The cold fixture must not have compiler output before navigation.");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            process = await StartInitializedHostAsync(repositoryRoot, hostAssemblyPath, configPath, timeout.Token, logDirectory);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            var before = CaptureWorkspaceSnapshot(fixtureRoot);

            await SendRequestAsync(process, 2, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = solutionPath, pattern = "ColdMarker", maxResults = 10 },
            }, timeout.Token);
            var found = await ReadResponseAsync(process, 2, timeout.Token);
            Assert.False(found.GetProperty("result").GetProperty("isError").GetBoolean());
            var handoff = ExtractHandoff(GetFirstText(found));

            await SendRequestAsync(process, 3, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = solutionPath, symbolIdentifiers = new[] { handoff } },
            }, timeout.Token);
            var body = await ReadResponseAsync(process, 3, timeout.Token);
            Assert.False(body.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("StringBuilder Build()", GetFirstText(body), StringComparison.Ordinal);

            AssertWorkspaceUnchanged(before, CaptureWorkspaceSnapshot(fixtureRoot));
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await process.StandardOutput.ReadToEndAsync(timeout.Token));
            Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator", "msbuild-analysis", process.Id.ToString())),
                "The host must release its temporary MSBuild outputs after resident workspaces are disposed.");
            _ = await stderrTask;
        }
        finally
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            process?.Dispose();
            File.Delete(configPath);
            if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, recursive: true);
            if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private static Process StartHost(string repositoryRoot, string hostAssemblyPath, string? logDirectory = null, params string[] arguments)
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
        if (logDirectory is not null) startInfo.Environment["AINET_CODE_NAVIGATOR_LOG_DIRECTORY"] = logDirectory;
        startInfo.ArgumentList.Add(hostAssemblyPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start MCP host process.");
    }

    private static async Task RestoreProjectAsync(string projectPath, string workingDirectory)
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
        startInfo.ArgumentList.Add(projectPath);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet restore for the temporary fixture.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        Assert.True(process.ExitCode == 0, $"Temporary source fixture restore failed: {await stderr}\n{await stdout}");
    }

    private static async Task<(string SolutionPath, string AssemblyPath)> CreateNavigationFixtureAsync(string fixtureRoot)
    {
        var repositoryPath = Path.Combine(fixtureRoot, "repository");
        var worktreePath = Path.Combine(fixtureRoot, "worktree");
        Directory.CreateDirectory(repositoryPath);
        var solutionPath = Path.Combine(repositoryPath, "NavigationFixture.slnx");
        var projectPath = Path.Combine(repositoryPath, "NavigationFixture.csproj");
        var sourcePath = Path.Combine(repositoryPath, "NavigationFixture.cs");
        var sharedDirectory = Path.Combine(fixtureRoot, "Shared");
        Directory.CreateDirectory(sharedDirectory);
        await File.WriteAllTextAsync(Path.Combine(sharedDirectory, "LinkedFixture.cs"),
            "namespace NavigationFixture; public sealed class LinkedFeature { public int Value() => 42; }");
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"NavigationFixture.csproj\" /><Project Path=\"LinkedConsumer.csproj\" /></Solution>");
        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><Compile Include=\"..\\Shared\\LinkedFixture.cs\" Link=\"LinkedFixture.cs\" /></ItemGroup></Project>");
        var linkedConsumerProject = Path.Combine(repositoryPath, "LinkedConsumer.csproj");
        await File.WriteAllTextAsync(linkedConsumerProject,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include=\"..\\Shared\\LinkedFixture.cs\" Link=\"LinkedFixture.cs\" /></ItemGroup></Project>");
        const string baseline = "namespace NavigationFixture;\npublic interface ICounter { int Read(); }\npublic sealed class Counter : ICounter { public int Read() => 1; }\npublic sealed class CounterConsumer { public int Run(ICounter counter) => counter.Read(); }\npublic static class CounterExtensions { public static int Double(this Counter counter) => counter.Read() * 2; }\n";
        await File.WriteAllTextAsync(sourcePath, baseline);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "ScopeWidget.cs"),
            "namespace NavigationFixture; public partial class ScopeWidget { public void ProductionOnly() { } }");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "ScopeWidget.Tests.cs"),
            "namespace NavigationFixture; public partial class ScopeWidget { public void TestOnly() { } }");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "ScopeWidget.g.cs"),
            "namespace NavigationFixture; public partial class ScopeWidget { public void GeneratedOnly() { } }");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "AFirstNonMatching.cs"), "namespace NavigationFixture; public sealed class Alpha { }\n");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "ZLastCounter.cs"), "namespace NavigationFixture; public sealed class CounterTail { }\n");
        await RestoreProjectAsync(projectPath, repositoryPath);
        await RestoreProjectAsync(linkedConsumerProject, repositoryPath);
        await RunCommandAsync("dotnet", repositoryPath, "build", projectPath, "--no-restore", "--configuration", "Debug");
        await RunCommandAsync("dotnet", repositoryPath, "build", linkedConsumerProject, "--no-restore", "--configuration", "Debug");
        await RunCommandAsync("git", repositoryPath, "init", "--quiet");
        await RunCommandAsync("git", repositoryPath, "config", "user.name", "Navigation Integration Test");
        await RunCommandAsync("git", repositoryPath, "config", "user.email", "navigation-test@example.invalid");
        await RunCommandAsync("git", repositoryPath, "add", "NavigationFixture.slnx", "NavigationFixture.csproj", "LinkedConsumer.csproj", "NavigationFixture.cs", "ScopeWidget.cs", "ScopeWidget.Tests.cs", "ScopeWidget.g.cs", "AFirstNonMatching.cs", "ZLastCounter.cs");
        await RunCommandAsync("git", repositoryPath, "commit", "--quiet", "-m", "fixture baseline");
        await RunCommandAsync("git", repositoryPath, "worktree", "add", "--quiet", "--detach", worktreePath, "HEAD");
        Assert.True(File.Exists(Path.Combine(worktreePath, ".git")), "The Git impact fixture must exercise a .git-file worktree.");
        var worktreeProjectPath = Path.Combine(worktreePath, "NavigationFixture.csproj");
        await RestoreProjectAsync(worktreeProjectPath, worktreePath);
        var worktreeLinkedConsumerProject = Path.Combine(worktreePath, "LinkedConsumer.csproj");
        await RestoreProjectAsync(worktreeLinkedConsumerProject, worktreePath);
        await RunCommandAsync("dotnet", worktreePath, "build", worktreeProjectPath, "--no-restore", "--configuration", "Debug");
        await RunCommandAsync("dotnet", worktreePath, "build", worktreeLinkedConsumerProject, "--no-restore", "--configuration", "Debug");
        var worktreeSourcePath = Path.Combine(worktreePath, "NavigationFixture.cs");
        await File.WriteAllTextAsync(worktreeSourcePath, baseline.Replace("Read() => 1", "Read() => 2", StringComparison.Ordinal));
        return (Path.Combine(worktreePath, "NavigationFixture.slnx"), Path.Combine(worktreePath, "bin", "Debug", "net10.0", "NavigationFixture.dll"));
    }

    private static async Task RunCommandAsync(string executable, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {executable} for the temporary fixture.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        var output = await stdout;
        var error = await stderr;
        Assert.True(process.ExitCode == 0, $"Temporary fixture command failed ({executable} {string.Join(' ', arguments)}):\n{error}\n{output}");
    }

    private static void ClearReadOnlyAttributesWithinOwnedFixture(string fixtureRoot)
    {
        var fullRoot = Path.GetFullPath(fixtureRoot);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Assert.StartsWith(temporaryRoot, fullRoot, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("ainet-contract-fixture-", Path.GetFileName(fullRoot), StringComparison.Ordinal);
        var pending = new Stack<string>();
        pending.Push(fullRoot);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
    }

    private static async Task<Process> StartInitializedHostAsync(string repositoryRoot, string hostAssemblyPath, string configPath, CancellationToken cancellationToken, string? logDirectory = null)
    {
        var process = StartHost(repositoryRoot, hostAssemblyPath, logDirectory, "--config", configPath);
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

    private static JsonElement ParsePayload(string text)
    {
        var payloadStart = text.IndexOf('{');
        Assert.True(payloadStart >= 0, "The tool response omitted its JSON payload.");
        using var document = JsonDocument.Parse(text[payloadStart..]);
        return document.RootElement.Clone();
    }

    private static int ReadIntegerLine(string text, string name)
    {
        var line = text.Split('\n').Single(value => value.StartsWith(name + ": ", StringComparison.Ordinal));
        return int.Parse(line[(name.Length + 2)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ReadStringLine(string text, string name)
    {
        var line = text.Split('\n').Single(value => value.StartsWith(name + "=", StringComparison.Ordinal));
        return line[(name.Length + 1)..].Trim();
    }

    private static string? TryReadStringLine(string text, string name)
    {
        var line = text.Split('\n').FirstOrDefault(value => value.StartsWith(name + "=", StringComparison.Ordinal));
        return line is null ? null : line[(name.Length + 1)..].Trim();
    }

    private static string ExtractHandoff(string text)
    {
        const string marker = "[handoff: ";
        var markerStart = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerStart >= 0, "The navigation result did not include a reusable opaque handoff.");
        var valueStart = markerStart + marker.Length;
        var valueEnd = text.IndexOf(']', valueStart);
        Assert.True(valueEnd > valueStart, "The navigation result included a malformed opaque handoff.");
        return text[valueStart..valueEnd];
    }

    private static string ExtractSkeletonHandoff(string text, string? lineContains = null)
    {
        const string marker = "handoffId: `";
        var candidateText = lineContains is null
            ? text
            : text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.Contains(lineContains, StringComparison.Ordinal) && line.Contains(marker, StringComparison.Ordinal)) ?? string.Empty;
        var markerStart = candidateText.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerStart >= 0, "The file skeleton did not include a reusable opaque handoff.");
        var valueStart = markerStart + marker.Length;
        var valueEnd = candidateText.IndexOf('`', valueStart);
        Assert.True(valueEnd > valueStart, "The file skeleton included a malformed opaque handoff.");
        return candidateText[valueStart..valueEnd];
    }

    private static Dictionary<string, string> CaptureWorkspaceSnapshot(string root)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            var relativeDirectory = Path.GetRelativePath(root, directory);
            snapshot[relativeDirectory == "." ? "." : relativeDirectory + Path.DirectorySeparatorChar] = "directory";
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (string.Equals(Path.GetFileName(entry), ".git", StringComparison.OrdinalIgnoreCase)) continue;
                var relativeEntry = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
                if (relativeEntry is "temp/build.log" or "temp/test-fast.log" or "temp/test-integration.log" or "temp/test.log") continue;
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                var relative = relativeEntry;
                var bytes = File.ReadAllBytes(entry);
                snapshot[relative] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            }
        }

        return snapshot;
    }

    private static void AssertWorkspaceUnchanged(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        var added = after.Keys.Except(before.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
        var removed = before.Keys.Except(after.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.True(added.Length == 0 && removed.Length == 0,
            $"Source navigation changed workspace entries. Added: {string.Join(", ", added)}; removed: {string.Join(", ", removed)}.");
        foreach (var (relativePath, fingerprint) in before)
        {
            Assert.True(after.TryGetValue(relativePath, out var afterFingerprint), $"Navigation removed workspace entry: {relativePath}");
            Assert.True(string.Equals(fingerprint, afterFingerprint, StringComparison.Ordinal), $"Navigation modified workspace file: {relativePath}");
        }
    }
}
