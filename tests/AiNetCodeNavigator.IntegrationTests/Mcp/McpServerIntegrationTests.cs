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
            Assert.Contains("\"projectName\": \"NavigationFixture\"", sourceOriginText, StringComparison.Ordinal);
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

            await SendRequestAsync(process, 8, "tools/call", new
            {
                name = "get_class_structure",
                arguments = new { targetPath = solutionPath, symbolIdentifier = sourceHandle, maxMembers = 20 },
            }, timeout.Token);
            var structure = await ReadResponseAsync(process, 8, timeout.Token);
            Assert.False(structure.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("CounterConsumer", GetFirstText(structure), StringComparison.Ordinal);
            Assert.Contains("[handoff: h:", GetFirstText(structure), StringComparison.Ordinal);

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

            await SendRequestAsync(process, 19, "tools/call", new { name = "get_assembly_context", arguments = new { targetPath = fixtureAssemblyPath, symbolIdentifier = assemblyHandle, includeBody = true, maxResults = 10 } }, timeout.Token);
            var assemblyContext = await ReadResponseAsync(process, 19, timeout.Token);
            Assert.False(assemblyContext.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("NavigationFixture.Counter", GetFirstText(assemblyContext), StringComparison.Ordinal);

            await SendRequestAsync(process, 23, "tools/call", new { name = "search_assembly", arguments = new { targetPath = fixtureAssemblyPath, pattern = "Counter", maxResults = 5 } }, timeout.Token);
            var assemblySearch = await ReadResponseAsync(process, 23, timeout.Token);
            Assert.False(assemblySearch.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains("Counter", GetFirstText(assemblySearch), StringComparison.Ordinal);

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
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"NavigationFixture.csproj\" /></Solution>");
        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        const string baseline = "namespace NavigationFixture;\npublic interface ICounter { int Read(); }\npublic sealed class Counter : ICounter { public int Read() => 1; }\npublic sealed class CounterConsumer { public int Run(ICounter counter) => counter.Read(); }\npublic static class CounterExtensions { public static int Double(this Counter counter) => counter.Read() * 2; }\n";
        await File.WriteAllTextAsync(sourcePath, baseline);
        await RestoreProjectAsync(projectPath, repositoryPath);
        await RunCommandAsync("dotnet", repositoryPath, "build", projectPath, "--no-restore", "--configuration", "Debug");
        await RunCommandAsync("git", repositoryPath, "init", "--quiet");
        await RunCommandAsync("git", repositoryPath, "config", "user.name", "Navigation Integration Test");
        await RunCommandAsync("git", repositoryPath, "config", "user.email", "navigation-test@example.invalid");
        await RunCommandAsync("git", repositoryPath, "add", "NavigationFixture.slnx", "NavigationFixture.csproj", "NavigationFixture.cs");
        await RunCommandAsync("git", repositoryPath, "commit", "--quiet", "-m", "fixture baseline");
        await RunCommandAsync("git", repositoryPath, "worktree", "add", "--quiet", "--detach", worktreePath, "HEAD");
        Assert.True(File.Exists(Path.Combine(worktreePath, ".git")), "The Git impact fixture must exercise a .git-file worktree.");
        var worktreeProjectPath = Path.Combine(worktreePath, "NavigationFixture.csproj");
        await RestoreProjectAsync(worktreeProjectPath, worktreePath);
        await RunCommandAsync("dotnet", worktreePath, "build", worktreeProjectPath, "--no-restore", "--configuration", "Debug");
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
