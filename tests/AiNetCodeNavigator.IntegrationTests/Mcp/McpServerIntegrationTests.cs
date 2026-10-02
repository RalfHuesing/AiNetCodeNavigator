using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "E2EIntegration")]
public sealed class McpServerIntegrationTests
{
    [Fact]
    public async Task NavigationToolListExplainsPurposeAndCriticalWireValues()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var configPath = Path.Combine(Path.GetTempPath(), "ainet-tool-descriptions-" + Guid.NewGuid().ToString("N") + ".json");
        var logDirectory = Path.Combine(Path.GetTempPath(), "ainet-tool-description-logs-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Information\"}");
        Process? process = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            process = await StartInitializedHostAsync(repositoryRoot, GetHostAssemblyPath(repositoryRoot), configPath, timeout.Token, logDirectory);
            await SendRequestAsync(process, 2, "tools/list", new { }, timeout.Token);
            var response = await ReadResponseAsync(process, 2, timeout.Token);
            var registered = response.GetProperty("result").GetProperty("tools").EnumerateArray()
                .ToDictionary(tool => tool.GetProperty("name").GetString()!, StringComparer.Ordinal);

            var navigationTools = new[]
            {
                "find_symbol", "get_symbol_body", "get_file_skeleton", "get_class_structure",
                "get_namespace_tree", "get_index_scope", "get_call_tree", "find_references", "get_type_hierarchy",
                "find_implementations", "get_impact", "dependency_graph", "resolve_type_origin", "get_assembly_context",
                "inspect_assembly", "search_assembly", "find_assembly_extensions", "get_feature_context", "get_test_context",
            };
            foreach (var name in navigationTools)
            {
                var tool = registered[name];
                Assert.True(tool.TryGetProperty("description", out var description), $"{name} has no tool description in tools/list.");
                Assert.True((description.GetString()?.Length ?? 0) >= 32, $"{name} has no useful purpose description.");
            }

            AssertPropertyDescriptionContains(registered["get_call_tree"], "direction", "incoming", "outgoing", "both", "default");
            AssertPropertyDescriptionContains(registered["get_call_tree"], "format", "ascii", "mermaid", "default");
            AssertPropertyDescriptionContains(registered["get_call_tree"], "includeDiagnostics", "assembly", "false", "source");
            AssertPropertyDescriptionContains(registered["find_symbol"], "namePatterns", "exactly one", "pattern");
            AssertPropertyDescriptionContains(registered["find_symbol"], "kind", "record class", "record struct", "delegate");
            Assert.DoesNotContain("get_file_tree", registered.Keys);
            AssertPropertyDescriptionContains(registered["get_namespace_tree"], "kind", "all", "class", "interface", "record", "struct", "enum");
            AssertPropertyDescriptionContains(registered["inspect_assembly"], "detailLevel", "compact", "standard", "full");
            AssertPropertyDescriptionContains(registered["search_assembly"], "detailLevel", "compact", "standard", "full");
            AssertPropertyDescriptionContains(registered["find_assembly_extensions"], "detailLevel", "compact", "standard", "full");
            AssertPropertyDescriptionContains(registered["get_assembly_context"], "detailLevel", "compact", "standard", "full");
            AssertPropertyDescriptionContains(registered["inspect_assembly"], "includeReferences", "omitted", "typeName", "memberNames");
            AssertPropertyDescriptionContains(registered["search_assembly"], "isRegex", "null", "false", "literal");
            AssertPropertyDescriptionContains(registered["search_assembly"], "searchKind", "text", "external_calls", "data_access");

            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await process.StandardOutput.ReadToEndAsync(timeout.Token));
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
        }
    }

    [Fact]
    public async Task AssemblyZeroLimitsAndZeroByteBudgetUseTheirToolDefaults()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "ainet-zero-default-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        var projectPath = Path.Combine(fixtureRoot, "ZeroMatrix.csproj");
        var sourcePath = Path.Combine(fixtureRoot, "ZeroMatrix.cs");
        var assemblyPath = Path.Combine(fixtureRoot, "bin", "Debug", "net10.0", "ZeroMatrix.dll");
        await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(sourcePath, "namespace ZeroMatrix; public sealed class ZeroBox { public int Read() => 1; public int First() => 2; public int Second() => 3; }");
        await File.WriteAllTextAsync(Path.Combine(fixtureRoot, "ZeroMatrixTwo.cs"), "namespace ZeroMatrix; public sealed class ZeroBoxTwo { public int Read() => 2; public int First() => 3; public int Second() => 4; }");
        await File.WriteAllTextAsync(Path.Combine(fixtureRoot, "ZeroMatrixThree.cs"), "namespace ZeroMatrix; public sealed class ZeroBoxThree { public int Read() => 3; public int First() => 4; public int Second() => 5; }");
        await File.WriteAllTextAsync(Path.Combine(fixtureRoot, "ZeroExtensions.cs"), "namespace ZeroMatrix; public static class ZeroExtensions { public static int PlusOne(this ZeroBox value) => value.Read() + 1; public static int PlusTwo(this ZeroBox value) => value.Read() + 2; public static int PlusThree(this ZeroBox value) => value.Read() + 3; }");
        await RestoreProjectAsync(projectPath, fixtureRoot);
        await RunCommandAsync("dotnet", fixtureRoot, "build", projectPath, "--no-restore", "--configuration", "Debug");
        var pageFixtureRoot = Path.Combine(fixtureRoot, "large-page-fixture");
        Directory.CreateDirectory(pageFixtureRoot);
        var pageProjectPath = Path.Combine(pageFixtureRoot, "LargePageMatrix.csproj");
        var pageAssemblyPath = Path.Combine(pageFixtureRoot, "bin", "Debug", "net10.0", "LargePageMatrix.dll");
        await File.WriteAllTextAsync(pageProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        var manyMethods = string.Join(Environment.NewLine, Enumerable.Range(0, 200).Select(index => $"    public int Many{index:D4}() => {index};"));
        await File.WriteAllTextAsync(Path.Combine(pageFixtureRoot, "ManyMethods.cs"), $"namespace LargePageMatrix; public sealed class ManyMethods\n{{\n{manyMethods}\n}}");
        var longBodyLines = string.Join(Environment.NewLine, Enumerable.Range(0, 800).Select(index => $"        System.Console.WriteLine(\"line-{index:D4}\");"));
        await File.WriteAllTextAsync(Path.Combine(pageFixtureRoot, "LargeBody.cs"), $"namespace LargePageMatrix; public sealed class LargeBody {{ public void WriteMany() {{\n{longBodyLines}\n    }} }}");
        await RestoreProjectAsync(pageProjectPath, pageFixtureRoot);
        await RunCommandAsync("dotnet", pageFixtureRoot, "build", pageProjectPath, "--no-restore", "--configuration", "Debug");
        using var process = StartHost(repositoryRoot, GetHostAssemblyPath(repositoryRoot), null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await SendRequestAsync(process, 1, "initialize", new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "assembly-zero-default-test", version = "1.0" },
            }, timeout.Token);
            _ = await ReadResponseAsync(process, 1, timeout.Token);
            await SendNotificationAsync(process, "notifications/initialized", timeout.Token);

            await SendRequestAsync(process, 2, "tools/call", new
            {
                name = "inspect_assembly",
                arguments = new { targetPath = assemblyPath, includeReferences = false },
            }, timeout.Token);
            var inspect = await ReadResponseAsync(process, 2, timeout.Token);
            var inspectText = GetFirstText(inspect);
            Assert.False(inspect.GetProperty("result").GetProperty("isError").GetBoolean(), inspectText);
            var inspectPayload = ParsePayload(inspectText);
            Assert.True(inspectPayload.GetProperty("shownCount").GetInt32() >= 3, inspectText);
            Assert.True(inspectPayload.GetProperty("totalTypes").GetInt32() >= 3, inspectText);
            Assert.True(inspectPayload.GetProperty("types").EnumerateArray().Max(type => type.GetProperty("members").GetArrayLength()) >= 3, inspectText);

            await SendRequestAsync(process, 6, "tools/call", new
            {
                name = "inspect_assembly",
                arguments = new { targetPath = assemblyPath, includeReferences = false, maxResults = 0, maxMembers = 0, maxResponseBytes = 0 },
            }, timeout.Token);
            var zeroInspect = await ReadResponseAsync(process, 6, timeout.Token);
            var zeroInspectText = GetFirstText(zeroInspect);
            Assert.False(zeroInspect.GetProperty("result").GetProperty("isError").GetBoolean(), zeroInspectText);
            var zeroInspectPayload = ParsePayload(zeroInspectText);
            Assert.Equal(inspectPayload.GetProperty("totalTypes").GetInt32(), zeroInspectPayload.GetProperty("totalTypes").GetInt32());
            Assert.Equal(inspectPayload.GetProperty("shownCount").GetInt32(), zeroInspectPayload.GetProperty("shownCount").GetInt32());
            Assert.Equal(inspectPayload.GetProperty("types").EnumerateArray().Select(type => type.GetProperty("name").GetString()),
                zeroInspectPayload.GetProperty("types").EnumerateArray().Select(type => type.GetProperty("name").GetString()));
            Assert.True(zeroInspectPayload.GetProperty("types").EnumerateArray().Max(type => type.GetProperty("members").GetArrayLength()) >= 3, zeroInspectText);

            await SendRequestAsync(process, 3, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = assemblyPath, searchKind = "text", pattern = "Read", declarationOnly = true, kind = "method", isRegex = false },
            }, timeout.Token);
            var search = await ReadResponseAsync(process, 3, timeout.Token);
            var searchText = GetFirstText(search);
            Assert.False(search.GetProperty("result").GetProperty("isError").GetBoolean(), searchText);
            var searchPayload = ParsePayload(searchText);
            Assert.NotEmpty(searchPayload.GetProperty("results").EnumerateArray());

            await SendRequestAsync(process, 7, "tools/call", new
            {
                name = "search_assembly",
                arguments = new { targetPath = assemblyPath, searchKind = "text", pattern = "Read", declarationOnly = true, kind = "method", isRegex = false, maxResults = 0, maxFiles = 0, maxResponseBytes = 0 },
            }, timeout.Token);
            var zeroSearch = await ReadResponseAsync(process, 7, timeout.Token);
            var zeroSearchText = GetFirstText(zeroSearch);
            Assert.False(zeroSearch.GetProperty("result").GetProperty("isError").GetBoolean(), zeroSearchText);
            var zeroSearchPayload = ParsePayload(zeroSearchText);
            Assert.True(searchPayload.GetProperty("totalCount").GetInt32() >= 3, searchText);
            Assert.True(searchPayload.GetProperty("results").EnumerateArray().Select(hit => hit.GetProperty("filePath").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 3, searchText);
            Assert.Equal(searchPayload.GetProperty("totalCount").GetInt32(), zeroSearchPayload.GetProperty("totalCount").GetInt32());
            Assert.Equal(searchPayload.GetProperty("results").EnumerateArray().Select(hit => hit.GetProperty("filePath").GetString()),
                zeroSearchPayload.GetProperty("results").EnumerateArray().Select(hit => hit.GetProperty("filePath").GetString()));

            await SendRequestAsync(process, 4, "tools/call", new
            {
                name = "find_assembly_extensions",
                arguments = new { targetPath = assemblyPath, receiverType = "ZeroMatrix.ZeroBox" },
            }, timeout.Token);
            var extensions = await ReadResponseAsync(process, 4, timeout.Token);
            var extensionsText = GetFirstText(extensions);
            Assert.False(extensions.GetProperty("result").GetProperty("isError").GetBoolean(), extensionsText);
            var extensionPayload = ParsePayload(extensionsText);
            Assert.True(extensionPayload.GetProperty("totalCount").GetInt32() >= 3, extensionsText);
            Assert.Contains(extensionPayload.GetProperty("extensions").EnumerateArray(), extension => extension.GetProperty("name").GetString() == "PlusOne");

            await SendRequestAsync(process, 8, "tools/call", new
            {
                name = "find_assembly_extensions",
                arguments = new { targetPath = assemblyPath, receiverType = "ZeroMatrix.ZeroBox", maxResults = 0, maxResponseBytes = 0 },
            }, timeout.Token);
            var zeroExtensions = await ReadResponseAsync(process, 8, timeout.Token);
            var zeroExtensionsText = GetFirstText(zeroExtensions);
            Assert.False(zeroExtensions.GetProperty("result").GetProperty("isError").GetBoolean(), zeroExtensionsText);
            var zeroExtensionPayload = ParsePayload(zeroExtensionsText);
            Assert.Equal(extensionPayload.GetProperty("totalCount").GetInt32(), zeroExtensionPayload.GetProperty("totalCount").GetInt32());
            Assert.Equal(extensionPayload.GetProperty("extensions").EnumerateArray().Select(extension => extension.GetProperty("name").GetString()),
                zeroExtensionPayload.GetProperty("extensions").EnumerateArray().Select(extension => extension.GetProperty("name").GetString()));
            Assert.True(zeroExtensionPayload.GetProperty("extensions").GetArrayLength() >= 3, zeroExtensionsText);

            await SendRequestAsync(process, 5, "tools/call", new
            {
                name = "get_assembly_context",
                arguments = new { targetPath = assemblyPath, detailLevel = "standard" },
            }, timeout.Token);
            var context = await ReadResponseAsync(process, 5, timeout.Token);
            var contextText = GetFirstText(context);
            Assert.False(context.GetProperty("result").GetProperty("isError").GetBoolean(), contextText);
            var contextPayload = ParsePayload(contextText);
            Assert.True(contextPayload.GetProperty("shownCount").GetInt32() > 0, contextText);

            await SendRequestAsync(process, 9, "tools/call", new
            {
                name = "get_assembly_context",
                arguments = new { targetPath = assemblyPath, maxResults = 0, maxResponseBytes = 0, detailLevel = "standard" },
            }, timeout.Token);
            var zeroContext = await ReadResponseAsync(process, 9, timeout.Token);
            var zeroContextText = GetFirstText(zeroContext);
            Assert.False(zeroContext.GetProperty("result").GetProperty("isError").GetBoolean(), zeroContextText);
            var zeroContextPayload = ParsePayload(zeroContextText);
            Assert.Equal(contextPayload.GetProperty("totalTypes").GetInt32(), zeroContextPayload.GetProperty("totalTypes").GetInt32());
            Assert.Equal(contextPayload.GetProperty("shownCount").GetInt32(), zeroContextPayload.GetProperty("shownCount").GetInt32());

            await SendRequestAsync(process, 10, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = "T:ZeroMatrix.ZeroBox", includeDiagnostics = false },
            }, timeout.Token);
            var callTreeWithoutDiagnostics = await ReadResponseAsync(process, 10, timeout.Token);
            var callTreeWithoutDiagnosticsText = GetFirstText(callTreeWithoutDiagnostics);
            Assert.False(callTreeWithoutDiagnostics.GetProperty("result").GetProperty("isError").GetBoolean(), callTreeWithoutDiagnosticsText);
            Assert.Contains("Diagnostics count:", callTreeWithoutDiagnosticsText, StringComparison.Ordinal);
            Assert.DoesNotContain("## Diagnostics", callTreeWithoutDiagnosticsText, StringComparison.Ordinal);

            await SendRequestAsync(process, 11, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = "T:ZeroMatrix.ZeroBox", includeDiagnostics = true },
            }, timeout.Token);
            var callTreeWithDiagnostics = await ReadResponseAsync(process, 11, timeout.Token);
            var callTreeWithDiagnosticsText = GetFirstText(callTreeWithDiagnostics);
            Assert.False(callTreeWithDiagnostics.GetProperty("result").GetProperty("isError").GetBoolean(), callTreeWithDiagnosticsText);
            Assert.Contains("Diagnostics count:", callTreeWithDiagnosticsText, StringComparison.Ordinal);
            Assert.Contains("## Diagnostics", callTreeWithDiagnosticsText, StringComparison.Ordinal);

            async Task<(string Text, int Pages)> ReadAllTextPagesAsync(string toolName, Dictionary<string, object?> arguments, int firstRequestId)
            {
                var text = new System.Text.StringBuilder();
                var pages = 0;
                var requestId = firstRequestId;
                while (true)
                {
                    Assert.True(requestId < firstRequestId + 80, $"{toolName} response paging exceeded its bounded page loop.");
                    await SendRequestAsync(process, requestId, "tools/call", new { name = toolName, arguments }, timeout.Token);
                    var response = await ReadResponseAsync(process, requestId++, timeout.Token);
                    var pageText = GetFirstText(response);
                    Assert.False(response.GetProperty("result").GetProperty("isError").GetBoolean(), pageText);
                    text.AppendLine(pageText);
                    pages++;
                    var continuation = TryReadStringLine(pageText, "continuationToken");
                    if (continuation is null) return (text.ToString(), pages);
                    arguments = new Dictionary<string, object?>(arguments, StringComparer.Ordinal)
                    {
                        ["continuationToken"] = continuation,
                    };
                }
            }

            var manyMethodPages = await ReadAllTextPagesAsync("find_symbol", new Dictionary<string, object?>
            {
                ["targetPath"] = pageAssemblyPath,
                ["pattern"] = "Many",
                ["kind"] = "method",
                ["maxResults"] = 1000,
                ["maxResponseBytes"] = 8192,
            }, 12);
            Assert.True(manyMethodPages.Pages > 1, $"A large find_symbol result should continue across line-safe response windows (pages={manyMethodPages.Pages}, chars={manyMethodPages.Text.Length}, preview={manyMethodPages.Text[..Math.Min(1000, manyMethodPages.Text.Length)]}).");
            Assert.Contains("Many0000", manyMethodPages.Text, StringComparison.Ordinal);
            Assert.Contains("Many0199", manyMethodPages.Text, StringComparison.Ordinal);

            await SendRequestAsync(process, 100, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = pageAssemblyPath, pattern = "WriteMany", kind = "method", maxResults = 10, maxResponseBytes = 8192 },
            }, timeout.Token);
            var largeBodyFind = await ReadResponseAsync(process, 100, timeout.Token);
            var largeBodyFindText = GetFirstText(largeBodyFind);
            Assert.False(largeBodyFind.GetProperty("result").GetProperty("isError").GetBoolean(), largeBodyFindText);
            Assert.Contains("WriteMany", largeBodyFindText, StringComparison.Ordinal);
            var largeBodyHandle = ExtractHandoff(largeBodyFindText);

            var bodyPages = await ReadAllTextPagesAsync("get_symbol_body", new Dictionary<string, object?>
            {
                ["targetPath"] = pageAssemblyPath,
                ["symbolIdentifiers"] = new[] { largeBodyHandle },
                ["maxBodyLines"] = 1000,
                ["maxResponseBytes"] = 8192,
            }, 101);
            Assert.True(bodyPages.Pages > 1, $"The large method body should continue across response windows (pages={bodyPages.Pages}, chars={bodyPages.Text.Length}, preview={bodyPages.Text[..Math.Min(1000, bodyPages.Text.Length)]}).");
            Assert.Contains("line-0000", bodyPages.Text, StringComparison.Ordinal);
            Assert.Contains("line-0799", bodyPages.Text, StringComparison.Ordinal);
            Assert.Equal(800, bodyPages.Text.Split("line-", StringSplitOptions.None).Length - 1);

            var contextPages = await ReadAllTextPagesAsync("get_assembly_context", new Dictionary<string, object?>
            {
                ["targetPath"] = pageAssemblyPath,
                ["symbolIdentifier"] = largeBodyHandle,
                ["includeBody"] = true,
                ["maxBodyLines"] = 1000,
                ["maxResponseBytes"] = 8192,
                ["detailLevel"] = "full",
            }, 190);
            Assert.True(contextPages.Pages > 1, "A context with a large body should continue across response windows.");
            Assert.Contains("## Body", contextPages.Text, StringComparison.Ordinal);
            Assert.Contains("line-0000", contextPages.Text, StringComparison.Ordinal);
            Assert.Contains("line-0799", contextPages.Text, StringComparison.Ordinal);
            Assert.Equal(800, contextPages.Text.Split("line-", StringSplitOptions.None).Length - 1);
            Assert.Contains("completeness=complete", contextPages.Text, StringComparison.Ordinal);

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
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            var resolvedFixtureRoot = Path.GetFullPath(fixtureRoot);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Assert.StartsWith(temporaryRoot, resolvedFixtureRoot, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("ainet-zero-default-", Path.GetFileName(resolvedFixtureRoot), StringComparison.Ordinal);
            if (Directory.Exists(resolvedFixtureRoot)) Directory.Delete(resolvedFixtureRoot, recursive: true);
        }
    }

    [Fact]
    public async Task TinyTokenBudgetOnEarlyNavigationErrorsReturnsSanitizedInvalidParams()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        using var process = StartHost(repositoryRoot, GetHostAssemblyPath(repositoryRoot), null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await SendRequestAsync(process, 1, "initialize", new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "tiny-budget-test", version = "1.0" },
            }, timeout.Token);
            var initialized = await ReadResponseAsync(process, 1, timeout.Token);
            Assert.Equal("2025-03-26", initialized.GetProperty("result").GetProperty("protocolVersion").GetString());
            await SendNotificationAsync(process, "notifications/initialized", timeout.Token);

            await SendRequestAsync(process, 101, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = "missing.slnx", symbolIdentifier = "T:Missing.Type", scopeType = "unsupported", maxResponseTokens = 1 },
            }, timeout.Token);
            var invalidScope = await ReadResponseAsync(process, 101, timeout.Token);
            AssertTinyBudgetErrorIsSanitized(invalidScope);

            await SendRequestAsync(process, 104, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = "missing.slnx", symbolIdentifier = "T:Missing.Type", direction = "sideways", maxResponseTokens = 1 },
            }, timeout.Token);
            var invalidDirection = await ReadResponseAsync(process, 104, timeout.Token);
            AssertTinyBudgetErrorIsSanitized(invalidDirection);

            await SendRequestAsync(process, 102, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".slnx"), symbolIdentifier = "T:Missing.Type", maxResponseTokens = 1 },
            }, timeout.Token);
            var invalidTarget = await ReadResponseAsync(process, 102, timeout.Token);
            AssertTinyBudgetErrorIsSanitized(invalidTarget);

            await SendRequestAsync(process, 103, "tools/call", new
            {
                name = "inspect_assembly",
                arguments = new { targetPath = GetHostAssemblyPath(repositoryRoot), detailLevel = "unsupported", maxResponseTokens = 1 },
            }, timeout.Token);
            var invalidAssemblyDetail = await ReadResponseAsync(process, 103, timeout.Token);
            AssertTinyBudgetErrorIsSanitized(invalidAssemblyDetail);

            await SendRequestAsync(process, 105, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = "missing.slnx", symbolIdentifier = "T:Missing.Type", scopeType = "unsupported", maxResponseTokens = 16 },
            }, timeout.Token);
            var narrowInvalidScope = await ReadResponseAsync(process, 105, timeout.Token);
            AssertTinyBudgetErrorIsSanitized(narrowInvalidScope);

            await SendRequestAsync(process, 106, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = "missing.slnx", symbolIdentifier = "T:Missing.Type", scopeType = "unsupported", maxResponseTokens = 32 },
            }, timeout.Token);
            var mediumInvalidScope = await ReadResponseAsync(process, 106, timeout.Token);
            AssertTinyBudgetErrorIsSanitized(mediumInvalidScope);

            await SendRequestAsync(process, 107, "tools/call", new
            {
                name = "inspect_assembly",
                arguments = new { targetPath = GetHostAssemblyPath(repositoryRoot), detailLevel = "unsupported", maxResponseTokens = 16 },
            }, timeout.Token);
            var narrowInvalidAssemblyDetail = await ReadResponseAsync(process, 107, timeout.Token);
            AssertTinyBudgetErrorIsSanitized(narrowInvalidAssemblyDetail);

            await SendRequestAsync(process, 108, "tools/call", new
            {
                name = "inspect_assembly",
                arguments = new { targetPath = GetHostAssemblyPath(repositoryRoot), detailLevel = "unsupported", maxResponseTokens = 32 },
            }, timeout.Token);
            var mediumInvalidAssemblyDetail = await ReadResponseAsync(process, 108, timeout.Token);
            AssertTinyBudgetErrorIsSanitized(mediumInvalidAssemblyDetail);

            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            var stderr = await stderrTask;
            Assert.DoesNotContain("InternalError", stderr, StringComparison.Ordinal);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static void AssertTinyBudgetErrorIsSanitized(JsonElement response)
    {
        Assert.True(response.TryGetProperty("error", out var error), response.ToString());
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        var message = error.GetProperty("message").GetString()!;
        Assert.Contains("response token budget is too small", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ArgumentOutOfRangeException", message, StringComparison.Ordinal);
        Assert.DoesNotContain("InternalError", message, StringComparison.Ordinal);
    }

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
            Assert.Equal(new[] { "dependency_graph", "find_assembly_extensions", "find_implementations", "find_references", "find_symbol", "get_assembly_context", "get_call_tree", "get_class_structure", "get_feature_context", "get_file_skeleton", "get_impact", "get_index_scope", "get_namespace_tree", "get_server_health", "get_symbol_body", "get_test_context", "get_type_hierarchy", "inspect_assembly", "reload_config", "resolve_type_origin", "search_assembly" }, tools.Order(StringComparer.Ordinal).ToArray());
            Assert.DoesNotContain("get_file_tree", tools);
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
    public async Task HostRejectsMalformedUnknownMissingAndOutOfRangeArgumentsThenContinues()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        using var process = StartHost(repositoryRoot, GetHostAssemblyPath(repositoryRoot), null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await SendRequestAsync(process, 1, "initialize", new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "argument-contract-test", version = "1.0" },
            }, timeout.Token);
            Assert.Equal("2025-03-26", (await ReadResponseAsync(process, 1, timeout.Token)).GetProperty("result").GetProperty("protocolVersion").GetString());
            await SendNotificationAsync(process, "notifications/initialized", timeout.Token);

            var invalidCases = new (int Id, object Arguments)[]
            {
                (2, new { pattern = "Counter" }),
                (3, new { targetPath = 42, pattern = "Counter" }),
                (4, new { targetPath = "unused.slnx", pattern = "Counter", maxResponseBytes = 100 }),
                (5, new { targetPath = "unused.slnx", pattern = "Counter", unexpected = true }),
            };
            foreach (var invalid in invalidCases)
            {
                await SendRequestAsync(process, invalid.Id, "tools/call", new { name = "find_symbol", arguments = invalid.Arguments }, timeout.Token);
                var response = await ReadResponseAsync(process, invalid.Id, timeout.Token);
                if (response.TryGetProperty("error", out var error))
                {
                    Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                }
                else
                {
                    Assert.True(response.GetProperty("result").GetProperty("isError").GetBoolean(), response.ToString());
                    Assert.Contains("INVALID_ARGUMENT", GetFirstText(response), StringComparison.Ordinal);
                }
            }

            await SendRequestAsync(process, 6, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = Path.Combine(repositoryRoot, "AiNetCodeNavigator.slnx"), symbolIdentifier = "T:Missing.Type", direction = "sideways" },
            }, timeout.Token);
            var semantic = await ReadResponseAsync(process, 6, timeout.Token);
            Assert.True(semantic.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(semantic));
            Assert.Contains("INVALID_ARGUMENT", GetFirstText(semantic), StringComparison.Ordinal);

            await SendRequestAsync(process, 7, "tools/call", new { name = "get_server_health", arguments = new { } }, timeout.Token);
            var health = await ReadResponseAsync(process, 7, timeout.Token);
            Assert.False(health.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(health));
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await process.StandardOutput.ReadToEndAsync(timeout.Token));
            _ = await stderrTask;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task NavigationToolsReturnTypedErrorsForConcreteToolSpecificInvalidInputs()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var fixtureRoot = Directory.CreateTempSubdirectory("ainet-public-error-matrix-").FullName;
        var configPath = Path.Combine(Path.GetTempPath(), "ainet-navigation-" + Guid.NewGuid().ToString("N") + ".json");
        var (solutionPath, assemblyPath) = await CreateNavigationFixtureAsync(fixtureRoot);
        await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Warning\"}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var process = await StartInitializedHostAsync(repositoryRoot, GetHostAssemblyPath(repositoryRoot), configPath,
            timeout.Token);
        try
        {
            var cases = new (string Tool, Dictionary<string, object?> Arguments, string ExpectedCode)[]
            {
                ("find_symbol", new(StringComparer.Ordinal) { ["targetPath"] = solutionPath }, "INVALID_ARGUMENT"),
                ("get_symbol_body", new(StringComparer.Ordinal) { ["targetPath"] = assemblyPath, ["symbolIdentifiers"] = new[] { "h:unknown" } }, "HANDOFF_UNKNOWN"),
                ("get_file_skeleton", new(StringComparer.Ordinal) { ["targetPath"] = solutionPath, ["filePaths"] = new[] { "Missing.cs" } }, "INVALID_ARGUMENT"),
                ("get_class_structure", new(StringComparer.Ordinal) { ["targetPath"] = assemblyPath, ["symbolIdentifier"] = "h:unknown" }, "HANDOFF_UNKNOWN"),
                ("get_namespace_tree", new(StringComparer.Ordinal) { ["targetPath"] = solutionPath, ["kind"] = "unsupported" }, "INVALID_ARGUMENT"),
                ("get_index_scope", new(StringComparer.Ordinal) { ["targetPath"] = assemblyPath }, "INVALID_ARGUMENT"),
                ("get_call_tree", new(StringComparer.Ordinal) { ["targetPath"] = solutionPath, ["symbolIdentifier"] = "h:unknown", ["direction"] = "sideways" }, "INVALID_ARGUMENT"),
                ("find_references", new(StringComparer.Ordinal) { ["targetPath"] = solutionPath, ["symbolIdentifier"] = "h:unknown" }, "HANDOFF_UNKNOWN"),
                ("get_type_hierarchy", new(StringComparer.Ordinal) { ["targetPath"] = assemblyPath, ["symbolIdentifier"] = "h:unknown" }, "HANDOFF_UNKNOWN"),
                ("find_implementations", new(StringComparer.Ordinal) { ["targetPath"] = assemblyPath, ["symbolIdentifier"] = "h:unknown" }, "HANDOFF_UNKNOWN"),
                ("get_impact", new(StringComparer.Ordinal) { ["targetPath"] = solutionPath, ["symbolIdentifier"] = " " }, "INVALID_ARGUMENT"),
                ("dependency_graph", new(StringComparer.Ordinal) { ["targetPath"] = solutionPath }, "INVALID_ARGUMENT"),
                ("resolve_type_origin", new(StringComparer.Ordinal) { ["targetPath"] = assemblyPath, ["symbolIdentifier"] = "", ["typeName"] = "" }, "INVALID_ARGUMENT"),
                ("get_assembly_context", new(StringComparer.Ordinal) { ["targetPath"] = assemblyPath, ["symbolIdentifier"] = "h:unknown", ["detailLevel"] = "unsupported" }, "INVALID_ARGUMENT"),
                ("inspect_assembly", new(StringComparer.Ordinal) { ["targetPath"] = assemblyPath, ["detailLevel"] = "unsupported" }, "INVALID_ARGUMENT"),
                ("search_assembly", new(StringComparer.Ordinal) { ["targetPath"] = assemblyPath, ["pattern"] = "Counter", ["searchKind"] = "unsupported" }, "INVALID_ARGUMENT"),
                ("find_assembly_extensions", new(StringComparer.Ordinal) { ["targetPath"] = assemblyPath, ["detailLevel"] = "unsupported" }, "INVALID_ARGUMENT"),
                ("get_feature_context", new(StringComparer.Ordinal) { ["targetPath"] = solutionPath, ["symbolIdentifier"] = "" }, "INVALID_ARGUMENT"),
                ("get_test_context", new(StringComparer.Ordinal) { ["targetPath"] = solutionPath, ["symbolIdentifier"] = "" }, "INVALID_ARGUMENT"),
            };

            var requestId = 10;
            foreach (var (tool, arguments, expectedCode) in cases)
            {
                var response = await CallAndDrainAsync(process, tool, arguments, requestId, timeout.Token);
                Assert.True(response.TryGetProperty("error", out _) || response.GetProperty("result").GetProperty("isError").GetBoolean(),
                    $"{tool} accepted its invalid case: {response}");
                var text = response.TryGetProperty("result", out _) ? GetFirstText(response) : response.GetProperty("error").GetProperty("message").GetString()!;
                Assert.True(text.Contains(expectedCode, StringComparison.Ordinal), $"{tool}: {text}");
                requestId += 3;
            }

            await SendRequestAsync(process, requestId, "tools/call", new { name = "get_server_health", arguments = new { } }, timeout.Token);
            var health = await ReadResponseAsync(process, requestId, timeout.Token);
            Assert.False(health.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(health));
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await process.StandardOutput.ReadToEndAsync(timeout.Token));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            File.Delete(configPath);
ClearReadOnlyAttributesWithinOwnedFixture(fixtureRoot);
            Directory.Delete(fixtureRoot, recursive: true);
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
            Assert.Equal(new[] { "dependency_graph", "find_assembly_extensions", "find_implementations", "find_references", "find_symbol", "get_assembly_context", "get_call_tree", "get_class_structure", "get_feature_context", "get_file_skeleton", "get_impact", "get_index_scope", "get_namespace_tree", "get_server_health", "get_symbol_body", "get_test_context", "get_type_hierarchy", "inspect_assembly", "reload_config", "resolve_type_origin", "search_assembly" }, names);
            Assert.DoesNotContain("get_file_tree", names);

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
                name = "get_file_skeleton",
                arguments = new { targetPath = solutionPath, filePaths = new[] { "NavigationFixture.cs" } },
            }, timeout.Token);
            var fixtureSkeleton = await ReadResponseAsync(process, 9, timeout.Token);
            Assert.False(fixtureSkeleton.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(fixtureSkeleton));
            Assert.Contains("NavigationFixture", GetFirstText(fixtureSkeleton), StringComparison.Ordinal);

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
            var foreignAssemblyOriginText = GetFirstText(foreignAssemblyOrigin);
            Assert.Contains("TARGET_MISMATCH", foreignAssemblyOriginText, StringComparison.Ordinal);
            Assert.Contains("fieldPath: $.symbolIdentifier", foreignAssemblyOriginText, StringComparison.Ordinal);

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

            await SendRequestAsync(process, 20, "tools/call", new
            {
                name = "resolve_type_origin",
                arguments = new { targetPath = fixtureAssemblyPath, symbolIdentifier = "T:NavigationFixture.Counter" },
            }, timeout.Token);
            var rawTypeOrigin = await ReadResponseAsync(process, 20, timeout.Token);
            var rawTypeOriginText = GetFirstText(rawTypeOrigin);
            Assert.False(rawTypeOrigin.GetProperty("result").GetProperty("isError").GetBoolean(), rawTypeOriginText);
            Assert.Equal("local", ParsePayload(rawTypeOriginText).GetProperty("originKind").GetString());
            Assert.Contains("NavigationFixture.Counter", rawTypeOriginText, StringComparison.Ordinal);

            await SendRequestAsync(process, 25, "tools/call", new { name = "get_impact", arguments = new { targetPath = fixtureAssemblyPath, symbolIdentifier = assemblyHandle } }, timeout.Token);
            var assemblyImpact = await ReadResponseAsync(process, 25, timeout.Token);
            Assert.False(assemblyImpact.GetProperty("result").GetProperty("isError").GetBoolean());

            await SendRequestAsync(process, 125, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = fixtureAssemblyPath, pattern = "Counter.Read", kind = "method", maxResults = 10 },
            }, timeout.Token);
            var assemblyReadFind = await ReadResponseAsync(process, 125, timeout.Token);
            var assemblyReadFindText = GetFirstText(assemblyReadFind);
            Assert.False(assemblyReadFind.GetProperty("result").GetProperty("isError").GetBoolean(), assemblyReadFindText);
            var assemblyReadHandoff = ExtractHandoff(assemblyReadFindText);
            var assemblyReadLocation = assemblyReadFindText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .First(line => line.Contains("Read()", StringComparison.Ordinal) && line.Contains(" in ", StringComparison.Ordinal));
            var assemblyReadLocationToken = assemblyReadLocation[(assemblyReadLocation.LastIndexOf(" in ", StringComparison.Ordinal) + 4)..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

            var impactInputs = new[]
            {
                new { RequestId = 126, Identifier = "M:NavigationFixture.Counter.Read", IncludeReferences = (bool?)null },
                new { RequestId = 127, Identifier = "M:NavigationFixture.Counter.Read", IncludeReferences = (bool?)false },
                new { RequestId = 128, Identifier = "NavigationFixture.Counter.Read", IncludeReferences = (bool?)null },
                new { RequestId = 129, Identifier = "NavigationFixture.Counter.Read", IncludeReferences = (bool?)false },
                new { RequestId = 131, Identifier = assemblyReadLocationToken, IncludeReferences = (bool?)null },
                new { RequestId = 132, Identifier = assemblyReadLocationToken, IncludeReferences = (bool?)false },
                new { RequestId = 133, Identifier = assemblyReadHandoff, IncludeReferences = (bool?)null },
            };
            JsonElement? baselineImpactPayload = null;
            string? callerHandoff = null;
            foreach (var input in impactInputs)
            {
                var arguments = new Dictionary<string, object?>
                {
                    ["targetPath"] = fixtureAssemblyPath,
                    ["symbolIdentifier"] = input.Identifier,
                    ["maxResults"] = 20,
                };
                if (input.IncludeReferences is { } includeReferences) arguments["includeReferences"] = includeReferences;
                await SendRequestAsync(process, input.RequestId, "tools/call", new { name = "get_impact", arguments }, timeout.Token);
                var response = await ReadResponseAsync(process, input.RequestId, timeout.Token);
                var responseText = GetFirstText(response);
                Assert.False(response.GetProperty("result").GetProperty("isError").GetBoolean(), responseText);
                var payload = ParsePayload(responseText);
                var caller = payload.GetProperty("callSites").EnumerateArray()
                    .FirstOrDefault(site => site.GetProperty("callingMember").GetString()?.EndsWith("CounterConsumer.Run", StringComparison.Ordinal) == true);
                Assert.False(caller.ValueKind == JsonValueKind.Undefined, responseText);
                Assert.True(payload.GetProperty("directCallersCount").GetInt32() > 0, responseText);
                callerHandoff ??= caller.GetProperty("callingMemberHandoffId").GetString();
                Assert.StartsWith("h:", callerHandoff, StringComparison.Ordinal);
                if (baselineImpactPayload is { } expected)
                {
                    Assert.Equal(expected.GetProperty("directCallersCount").GetInt32(), payload.GetProperty("directCallersCount").GetInt32());
                    Assert.Equal(expected.GetProperty("transitiveImpactCount").GetInt32(), payload.GetProperty("transitiveImpactCount").GetInt32());
                }
                else
                {
                    baselineImpactPayload = payload.Clone();
                }
            }
            await SendRequestAsync(process, 130, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = fixtureAssemblyPath, symbolIdentifiers = new[] { callerHandoff } },
            }, timeout.Token);
            var assemblyImpactCallerBody = await ReadResponseAsync(process, 130, timeout.Token);
            Assert.False(assemblyImpactCallerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(assemblyImpactCallerBody));
            Assert.Contains("Run", GetFirstText(assemblyImpactCallerBody), StringComparison.Ordinal);

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
        var cPath = AssemblyTestHelper.EmitAssembly(fixture, "ClosureC", "[assembly: System.Reflection.AssemblyVersion(\"4.2.0.0\")] namespace ClosureFixture; public class ClosureOnlyC { public string Value => \"from C\"; public virtual string Read() => Value; public string LocalRun() => Read(); } public sealed class ClosureDerivedC : ClosureOnlyC { public override string Read() => \"derived C\"; }");
        var bPath = AssemblyTestHelper.EmitAssembly(fixture, "ClosureB", "[assembly: System.Reflection.AssemblyVersion(\"3.1.0.0\")] namespace ClosureFixture; public class ClosureB : ClosureOnlyC { public string Run() => Read(); public string Another() => Read(); }", cPath);
        var aPath = AssemblyTestHelper.EmitAssembly(fixture, "ClosureA", "namespace ClosureFixture; public class ClosureA { public string Run() => new ClosureB().Run(); }", bPath, cPath);
        var foreignPath = AssemblyTestHelper.EmitAssembly(fixture, "ClosureForeign", "namespace ClosureFixture; public class ForeignMarker { }");
        await using (var cImage = File.OpenRead(cPath))
        using (var cPe = new PEReader(cImage))
            Assert.Equal(new Version(4, 2, 0, 0), cPe.GetMetadataReader().GetAssemblyDefinition().Version);
        await using (var bImage = File.OpenRead(bPath))
        using (var bPe = new PEReader(bImage))
            Assert.Equal(new Version(3, 1, 0, 0), bPe.GetMetadataReader().GetAssemblyDefinition().Version);
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

            await SendRequestAsync(process, 17, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = aPath, namePatterns = new[] { "ClosureOnlyC.Read" }, kind = "method", includeReferences = true, maxResults = 10 },
            }, timeout.Token);
            var methodFind = await ReadResponseAsync(process, 17, timeout.Token);
            for (var requestId = 18; GetFirstText(methodFind).Contains("operation=running", StringComparison.Ordinal) && requestId < 19; requestId++)
            {
                var operationToken = ReadStringLine(GetFirstText(methodFind), "operationToken");
                await SendRequestAsync(process, requestId, "tools/call", new
                {
                    name = "find_symbol",
                    arguments = new { targetPath = aPath, namePatterns = new[] { "ClosureOnlyC.Read" }, kind = "method", includeReferences = true, maxResults = 10, operationToken },
                }, timeout.Token);
                methodFind = await ReadResponseAsync(process, requestId, timeout.Token);
            }
            Assert.False(methodFind.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(methodFind));
            Assert.Contains($"targetPath: {ownedCPath}", GetFirstText(methodFind), StringComparison.OrdinalIgnoreCase);
            var methodHandoff = ExtractHandoff(GetFirstText(methodFind));

            await SendRequestAsync(process, 19, "tools/call", new
            {
                name = "find_references",
                arguments = new { targetPath = aPath, symbolIdentifier = methodHandoff, includeReferences = true, depth = 1, maxResults = 20 },
            }, timeout.Token);
            var closureReferences = await ReadResponseAsync(process, 19, timeout.Token);
            Assert.False(closureReferences.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(closureReferences));
            var closureText = GetFirstText(closureReferences);
            Assert.Contains("ClosureB.Run", closureText, StringComparison.Ordinal);
            var closurePayload = ParsePayload(closureText);
            var bCaller = closurePayload.GetProperty("references").EnumerateArray()
                .Single(entry => entry.GetProperty("enclosingSymbolName").GetString() == "ClosureB.Run");
            Assert.Equal(Path.GetFullPath(bPath), bCaller.GetProperty("ownerTargetPath").GetString(), StringComparer.OrdinalIgnoreCase);
            var bCallerHandoff = bCaller.GetProperty("enclosingSymbolHandoffId").GetString();
            Assert.StartsWith("h:", bCallerHandoff, StringComparison.Ordinal);
            await SendRequestAsync(process, 28, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = bPath, symbolIdentifiers = new[] { bCallerHandoff } },
            }, timeout.Token);
            var bCallerBody = await ReadResponseAsync(process, 28, timeout.Token);
            Assert.False(bCallerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(bCallerBody));
            Assert.Contains("Run()", GetFirstText(bCallerBody), StringComparison.Ordinal);

            var cCaller = closurePayload.GetProperty("references").EnumerateArray()
                .Single(entry => entry.GetProperty("enclosingSymbolName").GetString() == "ClosureOnlyC.LocalRun");
            Assert.Equal(ownedCPath, cCaller.GetProperty("ownerTargetPath").GetString(), StringComparer.OrdinalIgnoreCase);
            var cCallerHandoff = cCaller.GetProperty("enclosingSymbolHandoffId").GetString();
            Assert.StartsWith("h:", cCallerHandoff, StringComparison.Ordinal);
            await SendRequestAsync(process, 29, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { cCallerHandoff } },
            }, timeout.Token);
            var cCallerBody = await ReadResponseAsync(process, 29, timeout.Token);
            Assert.False(cCallerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(cCallerBody));
            Assert.Contains("LocalRun()", GetFirstText(cCallerBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 33, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = aPath, symbolIdentifier = methodHandoff, direction = "incoming", depth = 1, topN = 20, includeReferences = true },
            }, timeout.Token);
            var closureCallTree = await ReadResponseAsync(process, 33, timeout.Token);
            var closureCallTreeText = GetFirstText(closureCallTree);
            Assert.False(closureCallTree.GetProperty("result").GetProperty("isError").GetBoolean(), closureCallTreeText);
            Assert.Contains("ClosureB.Run", closureCallTreeText, StringComparison.Ordinal);
            Assert.Contains("ClosureOnlyC.LocalRun", closureCallTreeText, StringComparison.Ordinal);
            var bCallTreeHandoffLine = closureCallTreeText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Single(line => line.Contains("ClosureB.Run", StringComparison.Ordinal) && line.Contains("targetPath:", StringComparison.OrdinalIgnoreCase));
            var bCallTreeHandoff = bCallTreeHandoffLine.Split('`')[1];
            Assert.Contains(Path.GetFullPath(bPath), bCallTreeHandoffLine, StringComparison.OrdinalIgnoreCase);
            await SendRequestAsync(process, 34, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = bPath, symbolIdentifiers = new[] { bCallTreeHandoff } },
            }, timeout.Token);
            var bCallTreeBody = await ReadResponseAsync(process, 34, timeout.Token);
            Assert.False(bCallTreeBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(bCallTreeBody));
            Assert.Contains("Run()", GetFirstText(bCallTreeBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 35, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = aPath, symbolIdentifier = bCallerHandoff, direction = "outgoing", depth = 1, topN = 20, includeReferences = true },
            }, timeout.Token);
            var outgoingClosureTree = await ReadResponseAsync(process, 35, timeout.Token);
            var outgoingClosureText = GetFirstText(outgoingClosureTree);
            Assert.False(outgoingClosureTree.GetProperty("result").GetProperty("isError").GetBoolean(), outgoingClosureText);
            var cReadHandoffLines = outgoingClosureText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("ClosureOnlyC.Read", StringComparison.Ordinal) && line.Contains("targetPath:", StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.True(cReadHandoffLines.Length == 1, outgoingClosureText);
            var cReadHandoffLine = cReadHandoffLines[0];
            Assert.Contains(ownedCPath, cReadHandoffLine, StringComparison.OrdinalIgnoreCase);
            var cReadCallTreeHandoff = cReadHandoffLine.Split('`')[1];
            await SendRequestAsync(process, 36, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { cReadCallTreeHandoff } },
            }, timeout.Token);
            var cReadCallTreeBody = await ReadResponseAsync(process, 36, timeout.Token);
            Assert.False(cReadCallTreeBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(cReadCallTreeBody));
            Assert.Contains("Read()", GetFirstText(cReadCallTreeBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 37, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = aPath, symbolIdentifier = methodHandoff, direction = "both", depth = 1, topN = 1, includeReferences = true },
            }, timeout.Token);
            var limitedClosureTree = await ReadResponseAsync(process, 37, timeout.Token);
            var limitedClosureText = GetFirstText(limitedClosureTree);
            Assert.False(limitedClosureTree.GetProperty("result").GetProperty("isError").GetBoolean(), limitedClosureText);
            var limitedOwnerLine = limitedClosureText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Single(line => line.Contains("targetPath:", StringComparison.OrdinalIgnoreCase)
                    && (line.Contains("ClosureB.Run", StringComparison.Ordinal) || line.Contains("ClosureOnlyC.LocalRun", StringComparison.Ordinal)));
            Assert.Contains("completeness=truncated", limitedClosureText, StringComparison.Ordinal);
            var limitedOwnerHandoff = limitedOwnerLine.Split('`')[1];
            var limitedOwnerPath = limitedOwnerLine.Split("targetPath:", StringSplitOptions.None)[1].Trim();
            Assert.True(string.Equals(Path.GetFullPath(bPath), limitedOwnerPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFullPath(ownedCPath), limitedOwnerPath, StringComparison.OrdinalIgnoreCase), limitedOwnerLine);
            await SendRequestAsync(process, 38, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = limitedOwnerPath, symbolIdentifiers = new[] { limitedOwnerHandoff } },
            }, timeout.Token);
            var limitedOwnerBody = await ReadResponseAsync(process, 38, timeout.Token);
            Assert.False(limitedOwnerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(limitedOwnerBody));
            Assert.Contains("Run()", GetFirstText(limitedOwnerBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 39, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = aPath, symbolIdentifier = bCallerHandoff, direction = "outgoing", depth = 1, topN = 20, includeReferences = true, format = "mermaid" },
            }, timeout.Token);
            var mermaidClosureTree = await ReadResponseAsync(process, 39, timeout.Token);
            var mermaidClosureText = GetFirstText(mermaidClosureTree);
            Assert.False(mermaidClosureTree.GetProperty("result").GetProperty("isError").GetBoolean(), mermaidClosureText);
            Assert.Contains("flowchart TD", mermaidClosureText, StringComparison.Ordinal);
            var mermaidOwnerLine = mermaidClosureText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Single(line => line.Contains("handoffId:", StringComparison.Ordinal)
                    && line.Contains(Path.GetFullPath(ownedCPath), StringComparison.OrdinalIgnoreCase));
            var mermaidHandoff = mermaidOwnerLine.Split("handoffId:", StringSplitOptions.None)[1].Split(';')[0].Trim();
            Assert.StartsWith("h:", mermaidHandoff, StringComparison.Ordinal);
            await SendRequestAsync(process, 40, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { mermaidHandoff } },
            }, timeout.Token);
            var mermaidOwnerBody = await ReadResponseAsync(process, 40, timeout.Token);
            Assert.False(mermaidOwnerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(mermaidOwnerBody));
            Assert.Contains("Read()", GetFirstText(mermaidOwnerBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 41, "tools/call", new
            {
                name = "get_impact",
                arguments = new { targetPath = aPath, symbolIdentifier = methodHandoff, includeReferences = true, depth = 1, maxResults = 20 },
            }, timeout.Token);
            var closureImpact = await ReadResponseAsync(process, 41, timeout.Token);
            for (var requestId = 42; GetFirstText(closureImpact).Contains("operation=running", StringComparison.Ordinal) && requestId < 44; requestId++)
            {
                var operationToken = ReadStringLine(GetFirstText(closureImpact), "operationToken");
                await SendRequestAsync(process, requestId, "tools/call", new
                {
                    name = "get_impact",
                    arguments = new { targetPath = aPath, symbolIdentifier = methodHandoff, includeReferences = true, depth = 1, maxResults = 20, operationToken },
                }, timeout.Token);
                closureImpact = await ReadResponseAsync(process, requestId, timeout.Token);
            }
            var closureImpactText = GetFirstText(closureImpact);
            Assert.False(closureImpact.GetProperty("result").GetProperty("isError").GetBoolean(), closureImpactText);
            var closureImpactPayload = ParsePayload(closureImpactText);
            Assert.Equal(3, closureImpactPayload.GetProperty("directCallersCount").GetInt32());
            Assert.Equal(3, closureImpactPayload.GetProperty("transitiveImpactCount").GetInt32());
            var impactBCaller = closureImpactPayload.GetProperty("callSites").EnumerateArray()
                .Single(site => site.GetProperty("callingMember").GetString() == "ClosureB.Run");
            Assert.Equal(Path.GetFullPath(bPath), impactBCaller.GetProperty("ownerTargetPath").GetString(), StringComparer.OrdinalIgnoreCase);
            var impactBHandle = impactBCaller.GetProperty("callingMemberHandoffId").GetString();
            Assert.StartsWith("h:", impactBHandle, StringComparison.Ordinal);
            await SendRequestAsync(process, 44, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = bPath, symbolIdentifiers = new[] { impactBHandle } },
            }, timeout.Token);
            var impactBBody = await ReadResponseAsync(process, 44, timeout.Token);
            Assert.False(impactBBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(impactBBody));
            Assert.Contains("Run()", GetFirstText(impactBBody), StringComparison.Ordinal);
            var impactCCaller = closureImpactPayload.GetProperty("callSites").EnumerateArray()
                .Single(site => site.GetProperty("callingMember").GetString() == "ClosureOnlyC.LocalRun");
            Assert.Equal(Path.GetFullPath(ownedCPath), impactCCaller.GetProperty("ownerTargetPath").GetString(), StringComparer.OrdinalIgnoreCase);
            var impactCHandle = impactCCaller.GetProperty("callingMemberHandoffId").GetString();
            await SendRequestAsync(process, 45, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { impactCHandle } },
            }, timeout.Token);
            var impactCBody = await ReadResponseAsync(process, 45, timeout.Token);
            Assert.False(impactCBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(impactCBody));
            Assert.Contains("LocalRun()", GetFirstText(impactCBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 48, "tools/call", new
            {
                name = "get_impact",
                arguments = new { targetPath = aPath, symbolIdentifier = methodHandoff, includeReferences = true, depth = 1, maxResults = 1 },
            }, timeout.Token);
            var cappedClosureImpact = await ReadResponseAsync(process, 48, timeout.Token);
            var cappedImpactText = GetFirstText(cappedClosureImpact);
            Assert.False(cappedClosureImpact.GetProperty("result").GetProperty("isError").GetBoolean(), cappedImpactText);
            var cappedImpactPayload = ParsePayload(cappedImpactText);
            Assert.Equal(3, cappedImpactPayload.GetProperty("directCallersCount").GetInt32());
            Assert.Equal(3, cappedImpactPayload.GetProperty("transitiveImpactCount").GetInt32());
            Assert.Single(cappedImpactPayload.GetProperty("callSites").EnumerateArray());
            Assert.True(cappedImpactPayload.GetProperty("isTruncated").GetBoolean(), cappedImpactText);

            await SendRequestAsync(process, 46, "tools/call", new
            {
                name = "get_assembly_context",
                arguments = new
                {
                    targetPath = aPath,
                    symbolIdentifier = methodHandoff,
                    includeReferences = true,
                    includeBody = true,
                    includeClassStructure = true,
                    includeCallers = true,
                    includeImpact = true,
                    maxBodyLines = 20,
                    maxCallers = 10,
                    depth = 1,
                    topN = 10,
                    detailLevel = "full",
                },
            }, timeout.Token);
            var closureContext = await ReadResponseAsync(process, 46, timeout.Token);
            var closureContextText = GetFirstText(closureContext);
            Assert.False(closureContext.GetProperty("result").GetProperty("isError").GetBoolean(), closureContextText);
            Assert.Contains("## Callers", closureContextText, StringComparison.Ordinal);
            Assert.Contains("## Impact", closureContextText, StringComparison.Ordinal);
            Assert.Contains("ClosureB.Run", closureContextText, StringComparison.Ordinal);
            Assert.Contains("ClosureOnlyC.LocalRun", closureContextText, StringComparison.Ordinal);
            Assert.Contains($"targetPath: {bPath}", closureContextText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"targetPath: {ownedCPath}", closureContextText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ClosureOnlyC.Read()", closureContextText, StringComparison.Ordinal);
            var contextLocalRunLine = closureContextText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .First(line => line.Contains("ClosureOnlyC.LocalRun", StringComparison.Ordinal)
                    && line.Contains("targetPath:", StringComparison.OrdinalIgnoreCase)
                    && line.Contains("handoff:", StringComparison.Ordinal));
            var contextLocalRunHandle = contextLocalRunLine.Split("handoff:", StringSplitOptions.None)[1].Split(']')[0].Trim();
            await SendRequestAsync(process, 49, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { contextLocalRunHandle } },
            }, timeout.Token);
            var contextLocalRunBody = await ReadResponseAsync(process, 49, timeout.Token);
            Assert.False(contextLocalRunBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(contextLocalRunBody));
            Assert.Contains("LocalRun()", GetFirstText(contextLocalRunBody), StringComparison.Ordinal);
            var contextCallerLine = closureContextText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .First(line => line.Contains("ClosureB.Run", StringComparison.Ordinal)
                    && line.Contains("targetPath:", StringComparison.OrdinalIgnoreCase)
                    && line.Contains("handoff:", StringComparison.Ordinal));
            var contextCallerHandle = contextCallerLine.Split("handoff:", StringSplitOptions.None)[1].Split(']')[0].Trim();
            await SendRequestAsync(process, 47, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = bPath, symbolIdentifiers = new[] { contextCallerHandle } },
            }, timeout.Token);
            var contextCallerBody = await ReadResponseAsync(process, 47, timeout.Token);
            Assert.False(contextCallerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(contextCallerBody));
            Assert.Contains("Run()", GetFirstText(contextCallerBody), StringComparison.Ordinal);
            Assert.Contains("completeness=complete", closureContextText, StringComparison.Ordinal);

            await SendRequestAsync(process, 32, "tools/call", new
            {
                name = "find_references",
                arguments = new { targetPath = aPath, symbolIdentifier = methodHandoff, includeReferences = true, depth = 2, maxResults = 20 },
            }, timeout.Token);
            var depthTwoReferences = await ReadResponseAsync(process, 32, timeout.Token);
            var depthTwoReferencesText = GetFirstText(depthTwoReferences);
            Assert.False(depthTwoReferences.GetProperty("result").GetProperty("isError").GetBoolean(), depthTwoReferencesText);
            var depthTwoReferenceEntries = ParsePayload(depthTwoReferencesText).GetProperty("references").EnumerateArray().ToArray();
            var aCallerReferences = depthTwoReferenceEntries
                .Where(entry => entry.GetProperty("enclosingSymbolName").GetString() == "ClosureA.Run").ToArray();
            Assert.True(aCallerReferences.Length == 1, depthTwoReferencesText);
            var aCallerReference = aCallerReferences[0];
            Assert.Equal(Path.GetFullPath(aPath), aCallerReference.GetProperty("ownerTargetPath").GetString(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(2, aCallerReference.GetProperty("depth").GetInt32());
            var aCallerReferenceHandoff = aCallerReference.GetProperty("enclosingSymbolHandoffId").GetString();
            Assert.StartsWith("h:", aCallerReferenceHandoff, StringComparison.Ordinal);
            await SendRequestAsync(process, 85, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = aPath, symbolIdentifiers = new[] { aCallerReferenceHandoff } },
            }, timeout.Token);
            var aCallerReferenceBody = await ReadResponseAsync(process, 85, timeout.Token);
            Assert.False(aCallerReferenceBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(aCallerReferenceBody));
            Assert.Contains("new ClosureB().Run()", GetFirstText(aCallerReferenceBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 93, "tools/call", new
            {
                name = "find_references",
                arguments = new { targetPath = aPath, symbolIdentifier = methodHandoff, includeReferences = true, depth = 3, maxResults = 20 },
            }, timeout.Token);
            var depthThreeReferences = await ReadResponseAsync(process, 93, timeout.Token);
            var depthThreeReferencesText = GetFirstText(depthThreeReferences);
            Assert.False(depthThreeReferences.GetProperty("result").GetProperty("isError").GetBoolean(), depthThreeReferencesText);
            var depthThreeReferencePayload = ParsePayload(depthThreeReferencesText);
            Assert.Contains(depthThreeReferencePayload.GetProperty("references").EnumerateArray(),
                entry => entry.GetProperty("enclosingSymbolName").GetString() == "ClosureA.Run"
                    && entry.GetProperty("depth").GetInt32() == 2);
            Assert.Contains("completeness=complete", depthThreeReferencesText, StringComparison.Ordinal);

            await SendRequestAsync(process, 86, "tools/call", new
            {
                name = "get_impact",
                arguments = new { targetPath = aPath, symbolIdentifier = methodHandoff, includeReferences = true, depth = 2, maxResults = 20 },
            }, timeout.Token);
            var composedDepthTwoImpact = await ReadResponseAsync(process, 86, timeout.Token);
            Assert.False(composedDepthTwoImpact.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(composedDepthTwoImpact));
            var composedImpactSite = ParsePayload(GetFirstText(composedDepthTwoImpact)).GetProperty("callSites").EnumerateArray()
                .Single(site => site.GetProperty("callingMember").GetString() == "ClosureA.Run");
            Assert.Equal(Path.GetFullPath(aPath), composedImpactSite.GetProperty("ownerTargetPath").GetString(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(2, composedImpactSite.GetProperty("depth").GetInt32());
            var composedImpactHandoff = composedImpactSite.GetProperty("callingMemberHandoffId").GetString();
            await SendRequestAsync(process, 87, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = aPath, symbolIdentifiers = new[] { composedImpactHandoff } },
            }, timeout.Token);
            var composedImpactBody = await ReadResponseAsync(process, 87, timeout.Token);
            Assert.False(composedImpactBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(composedImpactBody));
            Assert.Contains("new ClosureB().Run()", GetFirstText(composedImpactBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 95, "tools/call", new
            {
                name = "get_impact",
                arguments = new { targetPath = aPath, symbolIdentifier = methodHandoff, includeReferences = true, depth = 3, maxResults = 20 },
            }, timeout.Token);
            var depthThreeImpact = await ReadResponseAsync(process, 95, timeout.Token);
            var depthThreeImpactText = GetFirstText(depthThreeImpact);
            Assert.False(depthThreeImpact.GetProperty("result").GetProperty("isError").GetBoolean(), depthThreeImpactText);
            var depthThreeImpactPayload = ParsePayload(depthThreeImpactText);
            Assert.Equal(4, depthThreeImpactPayload.GetProperty("transitiveImpactCount").GetInt32());
            Assert.Equal(2, depthThreeImpactPayload.GetProperty("maxDepthReached").GetInt32());
            Assert.Contains("completeness=complete", depthThreeImpactText, StringComparison.Ordinal);

            await SendRequestAsync(process, 88, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = aPath, symbolIdentifier = methodHandoff, includeReferences = true, direction = "incoming", depth = 2, topN = 20 },
            }, timeout.Token);
            var composedIncomingTree = await ReadResponseAsync(process, 88, timeout.Token);
            var composedIncomingTreeText = GetFirstText(composedIncomingTree);
            Assert.False(composedIncomingTree.GetProperty("result").GetProperty("isError").GetBoolean(), composedIncomingTreeText);
            Assert.Contains("completeness=complete", composedIncomingTreeText, StringComparison.Ordinal);
            var aRunNodeLine = composedIncomingTreeText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Single(line => line.Contains("(ClosureA.Run)", StringComparison.Ordinal));
            Assert.Contains(aPath, aRunNodeLine, StringComparison.OrdinalIgnoreCase);
            var aRunNodeHandle = ExtractBacktickHandoff(aRunNodeLine);
            await SendRequestAsync(process, 89, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = aPath, symbolIdentifiers = new[] { aRunNodeHandle } },
            }, timeout.Token);
            var aRunNodeBody = await ReadResponseAsync(process, 89, timeout.Token);
            Assert.False(aRunNodeBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(aRunNodeBody));
            Assert.Contains("new ClosureB().Run()", GetFirstText(aRunNodeBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 51, "tools/call", new
            {
                name = "get_assembly_context",
                arguments = new
                {
                    targetPath = aPath,
                    symbolIdentifier = methodHandoff,
                    includeReferences = true,
                    includeCallers = true,
                    includeImpact = true,
                    depth = 2,
                    maxCallers = 10,
                    topN = 10,
                    detailLevel = "full",
                },
            }, timeout.Token);
            var depthTwoContext = await ReadResponseAsync(process, 51, timeout.Token);
            var depthTwoContextText = GetFirstText(depthTwoContext);
            Assert.False(depthTwoContext.GetProperty("result").GetProperty("isError").GetBoolean(), depthTwoContextText);
            Assert.Contains("completeness=complete", depthTwoContextText, StringComparison.Ordinal);
            Assert.Contains("ClosureA.Run", depthTwoContextText, StringComparison.Ordinal);

            await SendRequestAsync(process, 90, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = aPath, namePatterns = new[] { "ClosureA.Run" }, kind = "method", includeReferences = true, maxResults = 10 },
            }, timeout.Token);
            var aRunFind = await ReadResponseAsync(process, 90, timeout.Token);
            Assert.False(aRunFind.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(aRunFind));
            var aRunAssemblyHandoff = ExtractHandoff(GetFirstText(aRunFind));
            await SendRequestAsync(process, 91, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = aPath, symbolIdentifier = aRunAssemblyHandoff, includeReferences = true, direction = "outgoing", depth = 3, topN = 10 },
            }, timeout.Token);
            var outgoingDepthTwo = await ReadResponseAsync(process, 91, timeout.Token);
            var outgoingDepthTwoText = GetFirstText(outgoingDepthTwo);
            Assert.False(outgoingDepthTwo.GetProperty("result").GetProperty("isError").GetBoolean(), outgoingDepthTwoText);
            Assert.Contains("completeness=complete", outgoingDepthTwoText, StringComparison.Ordinal);
            Assert.DoesNotContain("not composed yet", outgoingDepthTwoText, StringComparison.OrdinalIgnoreCase);
            var cReadNodeLine = outgoingDepthTwoText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.Contains("(ClosureOnlyC.Read)", StringComparison.Ordinal));
            Assert.NotNull(cReadNodeLine);
            Assert.Contains(ownedCPath, cReadNodeLine, StringComparison.OrdinalIgnoreCase);
            var cReadNodeHandle = ExtractBacktickHandoff(cReadNodeLine!);
            await SendRequestAsync(process, 92, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { cReadNodeHandle } },
            }, timeout.Token);
            var cReadNodeBody = await ReadResponseAsync(process, 92, timeout.Token);
            Assert.False(cReadNodeBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(cReadNodeBody));
            Assert.Contains("Read()", GetFirstText(cReadNodeBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 94, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = aPath, symbolIdentifier = methodHandoff, includeReferences = true, direction = "both", depth = 2, topN = 1 },
            }, timeout.Token);
            var cappedBothTree = await ReadResponseAsync(process, 94, timeout.Token);
            var cappedBothTreeText = GetFirstText(cappedBothTree);
            Assert.False(cappedBothTree.GetProperty("result").GetProperty("isError").GetBoolean(), cappedBothTreeText);
            var directRootEdges = cappedBothTreeText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.Contains("->", StringComparison.Ordinal) && line.Contains("ClosureOnlyC.Read", StringComparison.Ordinal));
            Assert.Equal(1, directRootEdges);

            await SendRequestAsync(process, 20, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { handoff } },
            }, timeout.Token);
            var ownerBody = await ReadResponseAsync(process, 20, timeout.Token);
            Assert.False(ownerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(ownerBody));
            Assert.Contains("ClosureOnlyC", GetFirstText(ownerBody), StringComparison.Ordinal);
            Assert.Contains("from C", GetFirstText(ownerBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 101, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { "M:ClosureFixture.ClosureOnlyC.Read" } },
            }, timeout.Token);
            var rawDocumentationIdBody = await ReadResponseAsync(process, 101, timeout.Token);
            Assert.False(rawDocumentationIdBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(rawDocumentationIdBody));
            Assert.Contains("Read()", GetFirstText(rawDocumentationIdBody), StringComparison.Ordinal);
            Assert.Contains($"Owner targetPath: {Path.GetFullPath(cPath)}", GetFirstText(rawDocumentationIdBody), StringComparison.OrdinalIgnoreCase);
            var rawBodyHandoff = ExtractBodyHandoff(GetFirstText(rawDocumentationIdBody));

            await SendRequestAsync(process, 105, "tools/call", new
            {
                name = "get_class_structure",
                arguments = new { targetPath = ownedCPath, symbolIdentifier = "T:ClosureFixture.ClosureOnlyC", maxMembers = 20 },
            }, timeout.Token);
            var rawTypeStructure = await ReadResponseAsync(process, 105, timeout.Token);
            Assert.False(rawTypeStructure.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(rawTypeStructure));
            Assert.Contains("ClosureOnlyC", GetFirstText(rawTypeStructure), StringComparison.Ordinal);

            await SendRequestAsync(process, 112, "tools/call", new
            {
                name = "get_type_hierarchy",
                arguments = new { targetPath = ownedCPath, symbolIdentifier = "T:ClosureFixture.ClosureOnlyC", maxResults = 20 },
            }, timeout.Token);
            var rawTypeHierarchy = await ReadResponseAsync(process, 112, timeout.Token);
            Assert.False(rawTypeHierarchy.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(rawTypeHierarchy));
            Assert.Contains("ClosureOnlyC", GetFirstText(rawTypeHierarchy), StringComparison.Ordinal);

            await SendRequestAsync(process, 113, "tools/call", new
            {
                name = "find_implementations",
                arguments = new { targetPath = ownedCPath, symbolIdentifier = "M:ClosureFixture.ClosureOnlyC.Read", maxResults = 20 },
            }, timeout.Token);
            var rawImplementations = await ReadResponseAsync(process, 113, timeout.Token);
            Assert.False(rawImplementations.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(rawImplementations));
            Assert.Contains("ClosureDerivedC.Read", GetFirstText(rawImplementations), StringComparison.Ordinal);
            Assert.Contains("ClosureDerivedC.Read", GetFirstText(rawImplementations), StringComparison.Ordinal);

            await SendRequestAsync(process, 114, "tools/call", new
            {
                name = "dependency_graph",
                arguments = new { targetPath = ownedCPath, symbolIdentifier = "T:ClosureFixture.ClosureOnlyC", depth = 1, maxResults = 20 },
            }, timeout.Token);
            var rawDependencies = await ReadResponseAsync(process, 114, timeout.Token);
            Assert.False(rawDependencies.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(rawDependencies));
            Assert.Contains("ClosureOnlyC", GetFirstText(rawDependencies), StringComparison.Ordinal);

            await SendRequestAsync(process, 115, "tools/call", new
            {
                name = "resolve_type_origin",
                arguments = new { targetPath = ownedCPath, symbolIdentifier = "T:ClosureFixture.ClosureOnlyC" },
            }, timeout.Token);
            var rawTypeOrigin = await ReadResponseAsync(process, 115, timeout.Token);
            Assert.False(rawTypeOrigin.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(rawTypeOrigin));
            Assert.Contains("ClosureOnlyC", GetFirstText(rawTypeOrigin), StringComparison.Ordinal);

            await SendRequestAsync(process, 106, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { "ClosureOnlyC.Read" } },
            }, timeout.Token);
            var rawNameBody = await ReadResponseAsync(process, 106, timeout.Token);
            Assert.False(rawNameBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(rawNameBody));
            Assert.Contains("Read()", GetFirstText(rawNameBody), StringComparison.Ordinal);

            var methodLocationLine = GetFirstText(methodFind).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Single(line => line.Contains("ClosureOnlyC.Read", StringComparison.Ordinal) && line.Contains(" in ", StringComparison.Ordinal));
            var locationToken = methodLocationLine[(methodLocationLine.LastIndexOf(" in ", StringComparison.Ordinal) + 4)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            var locationSeparator = locationToken.LastIndexOf(':');
            Assert.True(locationSeparator > 0, methodLocationLine);
            var sourceFilePath = locationToken[..locationSeparator];
            var sourceLine = int.Parse(locationToken[(locationSeparator + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            foreach (var (requestId, identifier) in new[]
            {
                (107, $"{sourceFilePath}:{sourceLine}"),
                (108, $"{sourceFilePath}:{sourceLine}:27"),
            })
            {
                await SendRequestAsync(process, requestId, "tools/call", new
                {
                    name = "get_symbol_body",
                    arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { identifier } },
                }, timeout.Token);
                var positionedBody = await ReadResponseAsync(process, requestId, timeout.Token);
                Assert.False(positionedBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(positionedBody));
                Assert.Contains("Read()", GetFirstText(positionedBody), StringComparison.Ordinal);
            }

            await SendRequestAsync(process, 116, "tools/call", new
            {
                name = "find_references",
                arguments = new { targetPath = aPath, symbolIdentifier = $"{sourceFilePath}:99999", includeReferences = true, depth = 1, maxResults = 20 },
            }, timeout.Token);
            var invalidRawPosition = await ReadResponseAsync(process, 116, timeout.Token);
            Assert.True(invalidRawPosition.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(invalidRawPosition));
            Assert.Contains("INVALID_ARGUMENT", GetFirstText(invalidRawPosition), StringComparison.Ordinal);

            await SendRequestAsync(process, 102, "tools/call", new
            {
                name = "get_class_structure",
                arguments = new { targetPath = ownedCPath, symbolIdentifier = rawBodyHandoff, maxMembers = 20 },
            }, timeout.Token);
            var rawBodyFollowupStructure = await ReadResponseAsync(process, 102, timeout.Token);
            Assert.False(rawBodyFollowupStructure.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(rawBodyFollowupStructure));
            Assert.Contains("ClosureOnlyC", GetFirstText(rawBodyFollowupStructure), StringComparison.Ordinal);

            await SendRequestAsync(process, 103, "tools/call", new
            {
                name = "find_references",
                arguments = new { targetPath = aPath, symbolIdentifier = "M:ClosureFixture.ClosureOnlyC.Read", includeReferences = true, depth = 1, maxResults = 20 },
            }, timeout.Token);
            var rawCrossOwnerReferences = await ReadResponseAsync(process, 103, timeout.Token);
            var rawCrossOwnerText = GetFirstText(rawCrossOwnerReferences);
            Assert.False(rawCrossOwnerReferences.GetProperty("result").GetProperty("isError").GetBoolean(), rawCrossOwnerText);
            var rawCrossOwnerPayload = ParsePayload(rawCrossOwnerText);
            var rawBCaller = rawCrossOwnerPayload.GetProperty("references").EnumerateArray()
                .FirstOrDefault(entry => entry.GetProperty("enclosingSymbolName").GetString()?.EndsWith(".Run", StringComparison.Ordinal) == true);
            Assert.False(rawBCaller.ValueKind == JsonValueKind.Undefined, rawCrossOwnerText);
            Assert.Equal(Path.GetFullPath(bPath), rawBCaller.GetProperty("ownerTargetPath").GetString(), StringComparer.OrdinalIgnoreCase);
            Assert.StartsWith("h:", rawBCaller.GetProperty("enclosingSymbolHandoffId").GetString(), StringComparison.Ordinal);

            await SendRequestAsync(process, 104, "tools/call", new
            {
                name = "find_references",
                arguments = new { targetPath = aPath, symbolIdentifier = "Run", includeReferences = true, depth = 1, maxResults = 20 },
            }, timeout.Token);
            var ambiguousRawOwners = await ReadResponseAsync(process, 104, timeout.Token);
            var ambiguousRawText = GetFirstText(ambiguousRawOwners);
            Assert.True(ambiguousRawOwners.GetProperty("result").GetProperty("isError").GetBoolean(), ambiguousRawText);
            Assert.Contains("AMBIGUOUS_SYMBOL", ambiguousRawText, StringComparison.Ordinal);
            Assert.Contains(Path.GetFullPath(aPath), ambiguousRawText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Path.GetFullPath(bPath), ambiguousRawText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("handoffId: `h:", ambiguousRawText, StringComparison.Ordinal);
            var bCandidateHandoff = ExtractOwnerCandidateHandoff(ambiguousRawText, Path.GetFullPath(bPath));
            await SendRequestAsync(process, 118, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = bPath, symbolIdentifiers = new[] { bCandidateHandoff } },
            }, timeout.Token);
            var bAmbiguousCandidateBody = await ReadResponseAsync(process, 118, timeout.Token);
            Assert.False(bAmbiguousCandidateBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(bAmbiguousCandidateBody));
            Assert.Contains("Run()", GetFirstText(bAmbiguousCandidateBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 109, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = aPath, symbolIdentifier = "M:ClosureFixture.ClosureOnlyC.Read", includeReferences = true, direction = "incoming", depth = 1, topN = 10 },
            }, timeout.Token);
            var rawCallTree = await ReadResponseAsync(process, 109, timeout.Token);
            Assert.False(rawCallTree.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(rawCallTree));
            Assert.Contains("ClosureB.Run", GetFirstText(rawCallTree), StringComparison.Ordinal);
            Assert.Contains(Path.GetFullPath(bPath), GetFirstText(rawCallTree), StringComparison.OrdinalIgnoreCase);

            await SendRequestAsync(process, 110, "tools/call", new
            {
                name = "get_impact",
                arguments = new { targetPath = aPath, symbolIdentifier = "M:ClosureFixture.ClosureOnlyC.Read", includeReferences = true, depth = 1, maxResults = 10 },
            }, timeout.Token);
            var rawImpact = await ReadResponseAsync(process, 110, timeout.Token);
            var rawImpactText = GetFirstText(rawImpact);
            Assert.False(rawImpact.GetProperty("result").GetProperty("isError").GetBoolean(), rawImpactText);
            var rawImpactPayload = ParsePayload(rawImpactText);
            var rawImpactBCaller = rawImpactPayload.GetProperty("callSites").EnumerateArray()
                .FirstOrDefault(site => site.GetProperty("callingMember").GetString()?.EndsWith("ClosureB.Run", StringComparison.Ordinal) == true);
            Assert.False(rawImpactBCaller.ValueKind == JsonValueKind.Undefined, rawImpactText);
            Assert.Equal(Path.GetFullPath(bPath), rawImpactBCaller.GetProperty("ownerTargetPath").GetString(), StringComparer.OrdinalIgnoreCase);

            await SendRequestAsync(process, 111, "tools/call", new
            {
                name = "get_assembly_context",
                arguments = new { targetPath = aPath, symbolIdentifier = "M:ClosureFixture.ClosureOnlyC.Read", includeReferences = true, includeCallers = true, includeImpact = true, includeBody = true, includeClassStructure = true, depth = 1 },
            }, timeout.Token);
            var rawContext = await ReadResponseAsync(process, 111, timeout.Token);
            var rawContextText = GetFirstText(rawContext);
            Assert.False(rawContext.GetProperty("result").GetProperty("isError").GetBoolean(), rawContextText);
            Assert.Contains("## Body", rawContextText, StringComparison.Ordinal);
            Assert.Contains("## Class Structure", rawContextText, StringComparison.Ordinal);
            Assert.Contains("ClosureB.Run", rawContextText, StringComparison.Ordinal);
            Assert.Contains(Path.GetFullPath(bPath), rawContextText, StringComparison.OrdinalIgnoreCase);

            await SendRequestAsync(process, 14, "tools/call", new
            {
                name = "find_references",
                arguments = new { targetPath = aPath, symbolIdentifier = handoff, includeReferences = true, depth = 1, maxResults = 20 },
            }, timeout.Token);
            var typeReferences = await ReadResponseAsync(process, 14, timeout.Token);
            Assert.False(typeReferences.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(typeReferences));
            var typeReferencePayload = ParsePayload(GetFirstText(typeReferences));
            var baseReference = typeReferencePayload.GetProperty("references").EnumerateArray()
                .FirstOrDefault(entry => entry.GetProperty("enclosingSymbolName").GetString() == "ClosureB");
            Assert.False(baseReference.ValueKind == JsonValueKind.Undefined, GetFirstText(typeReferences));
            Assert.Equal(Path.GetFullPath(bPath), baseReference.GetProperty("ownerTargetPath").GetString(), StringComparer.OrdinalIgnoreCase);
            var derivedTypeHandoff = baseReference.GetProperty("enclosingSymbolHandoffId").GetString();
            Assert.StartsWith("h:", derivedTypeHandoff, StringComparison.Ordinal);
            await SendRequestAsync(process, 15, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = bPath, symbolIdentifiers = new[] { derivedTypeHandoff } },
            }, timeout.Token);
            var derivedTypeBody = await ReadResponseAsync(process, 15, timeout.Token);
            Assert.False(derivedTypeBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(derivedTypeBody));
            Assert.Contains("ClosureB", GetFirstText(derivedTypeBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 30, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = foreignPath, pattern = "ForeignMarker", kind = "class", maxResults = 10 },
            }, timeout.Token);
            var foreignFind = await ReadResponseAsync(process, 30, timeout.Token);
            Assert.False(foreignFind.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(foreignFind));
            var foreignHandoff = ExtractHandoff(GetFirstText(foreignFind));
            await SendRequestAsync(process, 31, "tools/call", new
            {
                name = "find_references",
                arguments = new { targetPath = aPath, symbolIdentifier = foreignHandoff, includeReferences = true, depth = 1, maxResults = 20 },
            }, timeout.Token);
            var foreignReference = await ReadResponseAsync(process, 31, timeout.Token);
            Assert.True(foreignReference.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(foreignReference));
            Assert.Contains("TARGET_MISMATCH", GetFirstText(foreignReference), StringComparison.Ordinal);
            await SendRequestAsync(process, 37, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = aPath, symbolIdentifier = foreignHandoff, includeReferences = true, direction = "incoming", depth = 1 },
            }, timeout.Token);
            var foreignCallTree = await ReadResponseAsync(process, 37, timeout.Token);
            Assert.True(foreignCallTree.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(foreignCallTree));
            Assert.Contains("TARGET_MISMATCH", GetFirstText(foreignCallTree), StringComparison.Ordinal);

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

            var sourceProjectPath = Path.Combine(repositoryRoot, "AiNetCodeNavigator.slnx");
            await SendRequestAsync(process, 119, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = sourceProjectPath, pattern = "AssemblyReferenceSearchHandoffUsesTheOwningAssemblyTarget", kind = "method", maxResults = 1 },
            }, timeout.Token);
            var sourceMethodFind = await ReadResponseAsync(process, 119, timeout.Token);
            for (var requestId = 120; GetFirstText(sourceMethodFind).Contains("operation=running", StringComparison.Ordinal) && requestId < 121; requestId++)
            {
                var operationToken = ReadStringLine(GetFirstText(sourceMethodFind), "operationToken");
                await SendRequestAsync(process, requestId, "tools/call", new
                {
                    name = "find_symbol",
                    arguments = new { targetPath = sourceProjectPath, pattern = "AssemblyReferenceSearchHandoffUsesTheOwningAssemblyTarget", kind = "method", maxResults = 1, operationToken },
                }, timeout.Token);
                sourceMethodFind = await ReadResponseAsync(process, requestId, timeout.Token);
            }
            Assert.False(sourceMethodFind.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(sourceMethodFind));
            var actualSourceHandoff = ExtractHandoff(GetFirstText(sourceMethodFind));
            await SendRequestAsync(process, 121, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = ownedCPath, symbolIdentifiers = new[] { actualSourceHandoff } },
            }, timeout.Token);
            var sourceHandoffOnAssembly = await ReadResponseAsync(process, 121, timeout.Token);
            Assert.True(sourceHandoffOnAssembly.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(sourceHandoffOnAssembly));
            Assert.Contains("TARGET_MISMATCH", GetFirstText(sourceHandoffOnAssembly), StringComparison.Ordinal);

            using var replacementFixture = TestTempDirectory.Create("assembly-owner-replacement-");
            var replacementCPath = AssemblyTestHelper.EmitAssembly(replacementFixture, "ClosureC", "[assembly: System.Reflection.AssemblyVersion(\"4.2.0.0\")] namespace ClosureFixture; public class ClosureOnlyC { public string Value => \"replacement C\"; public virtual string Read() => Value; public string LocalRun() => Read(); } public sealed class ClosureDerivedC : ClosureOnlyC { public override string Read() => \"derived replacement C\"; }");
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

            await SendRequestAsync(process, 33, "tools/call", new
            {
                name = "find_references",
                arguments = new { targetPath = aPath, symbolIdentifier = methodHandoff, includeReferences = true, depth = 1, maxResults = 20 },
            }, timeout.Token);
            var staleClosure = await ReadResponseAsync(process, 33, timeout.Token);
            Assert.True(staleClosure.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(staleClosure));
            Assert.Contains("STALE_SNAPSHOT", GetFirstText(staleClosure), StringComparison.Ordinal);

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
    public async Task AssemblyCallTreeReferenceMergeKeepsGlobalNodeAndEdgeCap()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var hostAssemblyPath = GetHostAssemblyPath(repositoryRoot);
        using var fixture = TestTempDirectory.Create("assembly-calltree-cap-");
        var configPath = Path.Combine(Path.GetTempPath(), "ainet-calltree-cap-" + Guid.NewGuid().ToString("N") + ".json");
        var cSource = "namespace CallTreeCap; public class Callee { public void Read() { } "
            + string.Join(" ", Enumerable.Range(0, 260).Select(index => $"public void Local{index:D3}() => Read();")) + " }";
        var callerNames = Enumerable.Range(0, 260).Select(index => $"Caller{index:D3}").ToArray();
        var startCallerNames = callerNames.Take(130).ToArray();
        var bSource = "namespace CallTreeCap; public class Bridge : Callee { public void Start() { "
            + string.Join(" ", startCallerNames.Select(name => $"{name}();")) + " } "
            + string.Join(" ", callerNames.Select(name => $"public void {name}() => Read();")) + " }";
        var cPath = AssemblyTestHelper.EmitAssembly(fixture, "CallTreeCapC", cSource);
        var bPath = AssemblyTestHelper.EmitAssembly(fixture, "CallTreeCapB", bSource, cPath);
        var aPath = AssemblyTestHelper.EmitAssembly(fixture, "CallTreeCapA", "namespace CallTreeCap; public class Entry { public void Start(Bridge bridge) => bridge.Caller000(); }", bPath, cPath);
        await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Information\"}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var process = await StartInitializedHostAsync(repositoryRoot, hostAssemblyPath, configPath, timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await SendRequestAsync(process, 2, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = aPath, namePatterns = new[] { "Callee.Read" }, kind = "method", includeReferences = true, maxResults = 5 },
            }, timeout.Token);
            var found = await ReadResponseAsync(process, 2, timeout.Token);
            for (var requestId = 3; GetFirstText(found).Contains("operation=running", StringComparison.Ordinal) && requestId < 8; requestId++)
            {
                var operationToken = ReadStringLine(GetFirstText(found), "operationToken");
                await SendRequestAsync(process, requestId, "tools/call", new
                {
                    name = "find_symbol",
                    arguments = new { targetPath = aPath, namePatterns = new[] { "Callee.Read" }, kind = "method", includeReferences = true, maxResults = 5, operationToken },
                }, timeout.Token);
                found = await ReadResponseAsync(process, requestId, timeout.Token);
            }
            Assert.False(found.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(found));
            var seedHandoff = ExtractHandoff(GetFirstText(found));

            await SendRequestAsync(process, 9, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = aPath, symbolIdentifier = seedHandoff, direction = "incoming", depth = 1, topN = 250, includeReferences = true, maxResponseBytes = 65536 },
            }, timeout.Token);
            var callTree = await ReadResponseAsync(process, 9, timeout.Token);
            var text = GetFirstText(callTree);
            Assert.False(callTree.GetProperty("result").GetProperty("isError").GetBoolean(), text);
            Assert.Contains("completeness=truncated", text, StringComparison.Ordinal);
            Assert.Contains("more calls", text, StringComparison.Ordinal);
            Assert.Contains("Some reachable owner symbols could not be mapped or expanded within the bounded reference closure", text, StringComparison.Ordinal);
            var visibleHandoffCount = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.StartsWith("- [n", StringComparison.Ordinal) && line.Contains("`h:", StringComparison.Ordinal));
            Assert.Equal(250, visibleHandoffCount);

            await SendRequestAsync(process, 10, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = aPath, namePatterns = new[] { "Bridge.Start" }, kind = "method", includeReferences = true, maxResults = 5 },
            }, timeout.Token);
            var startFound = await ReadResponseAsync(process, 10, timeout.Token);
            for (var requestId = 11; GetFirstText(startFound).Contains("operation=running", StringComparison.Ordinal) && requestId < 16; requestId++)
            {
                var operationToken = ReadStringLine(GetFirstText(startFound), "operationToken");
                await SendRequestAsync(process, requestId, "tools/call", new
                {
                    name = "find_symbol",
                    arguments = new { targetPath = aPath, namePatterns = new[] { "Bridge.Start" }, kind = "method", includeReferences = true, maxResults = 5, operationToken },
                }, timeout.Token);
                startFound = await ReadResponseAsync(process, requestId, timeout.Token);
            }
            Assert.False(startFound.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(startFound));
            var startHandoff = ExtractHandoff(GetFirstText(startFound));
            await SendRequestAsync(process, 17, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = aPath, symbolIdentifier = startHandoff, direction = "outgoing", depth = 2, topN = 250, includeReferences = true, maxResponseBytes = 65536 },
            }, timeout.Token);
            var edgeCappedTree = await ReadResponseAsync(process, 17, timeout.Token);
            var edgeCappedText = GetFirstText(edgeCappedTree);
            Assert.False(edgeCappedTree.GetProperty("result").GetProperty("isError").GetBoolean(), edgeCappedText);
            Assert.Contains("completeness=truncated", edgeCappedText, StringComparison.Ordinal);
            Assert.Contains("more calls", edgeCappedText, StringComparison.Ordinal);
            var edgeCappedHandoffs = edgeCappedText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.StartsWith("- [n", StringComparison.Ordinal) && line.Contains("`h:", StringComparison.Ordinal));
            Assert.Equal(132, edgeCappedHandoffs);

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

            var concreteReadBetaHandle = contractMethodImplementation.GetProperty("handoffId").GetString()!;
            await SendRequestAsync(process, 61, "tools/call", new
            {
                name = "find_references",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = concreteReadBetaHandle, depth = 2, maxResults = 10, includeReferences = false },
            }, timeout.Token);
            var assemblyReferences = await ReadResponseAsync(process, 61, timeout.Token);
            var assemblyReferencesText = GetFirstText(assemblyReferences);
            Assert.False(assemblyReferences.GetProperty("result").GetProperty("isError").GetBoolean(), assemblyReferencesText);
            var betaReference = ParsePayload(assemblyReferencesText).GetProperty("references").EnumerateArray()
                .Single(item => item.GetProperty("snippet").GetString()!.Contains("ReadBeta", StringComparison.Ordinal));
            Assert.Contains("BetaInvoker.Invoke", betaReference.GetProperty("enclosingSymbolName").GetString(), StringComparison.Ordinal);
            var referenceCallerHandle = betaReference.GetProperty("enclosingSymbolHandoffId").GetString();
            Assert.StartsWith("h:", referenceCallerHandle, StringComparison.Ordinal);
            await SendRequestAsync(process, 62, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { referenceCallerHandle } },
            }, timeout.Token);
            var referenceCallerBody = await ReadResponseAsync(process, 62, timeout.Token);
            Assert.False(referenceCallerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(referenceCallerBody));
            Assert.Contains("Invoke", GetFirstText(referenceCallerBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 63, "tools/call", new
            {
                name = "find_references",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = concreteReadBetaHandle, depth = 2, maxResults = 10, includeReferences = true },
            }, timeout.Token);
            var incompleteAssemblyReferences = await ReadResponseAsync(process, 63, timeout.Token);
            var completeAssemblyReferencesText = GetFirstText(incompleteAssemblyReferences);
            Assert.False(incompleteAssemblyReferences.GetProperty("result").GetProperty("isError").GetBoolean(), completeAssemblyReferencesText);
            Assert.Contains("completeness=complete", completeAssemblyReferencesText, StringComparison.Ordinal);
            var completeAssemblyReference = ParsePayload(completeAssemblyReferencesText).GetProperty("references").EnumerateArray()
                .Single(item => item.GetProperty("snippet").GetString()!.Contains("ReadBeta", StringComparison.Ordinal));
            Assert.Contains("BetaInvoker.Invoke", completeAssemblyReference.GetProperty("enclosingSymbolName").GetString(), StringComparison.Ordinal);
            Assert.Equal(Path.GetFullPath(assemblyPath), completeAssemblyReference.GetProperty("ownerTargetPath").GetString(), StringComparer.OrdinalIgnoreCase);

            await SendRequestAsync(process, 71, "tools/call", new
            {
                name = "get_impact",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = concreteReadBetaHandle, depth = 2, maxResults = 10, includeReferences = false },
            }, timeout.Token);
            var assemblyImpact = await ReadResponseAsync(process, 71, timeout.Token);
            var assemblyImpactText = GetFirstText(assemblyImpact);
            Assert.False(assemblyImpact.GetProperty("result").GetProperty("isError").GetBoolean(), assemblyImpactText);
            var impactCallSite = ParsePayload(assemblyImpactText).GetProperty("callSites").EnumerateArray()
                .Single(item => item.GetProperty("callingMember").GetString() == "BetaInvoker.Invoke");
            Assert.True(impactCallSite.TryGetProperty("callingMemberHandoffId", out var impactCallerHandoff),
                "The assembly impact caller must include an owner-bound handoff.");
            var impactCallerHandle = impactCallerHandoff.GetString();
            Assert.StartsWith("h:", impactCallerHandle, StringComparison.Ordinal);
            await SendRequestAsync(process, 72, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { impactCallerHandle } },
            }, timeout.Token);
            var impactCallerBody = await ReadResponseAsync(process, 72, timeout.Token);
            Assert.False(impactCallerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(impactCallerBody));
            Assert.Contains("Invoke", GetFirstText(impactCallerBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 73, "tools/call", new
            {
                name = "get_impact",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = concreteReadBetaHandle, includeReferences = true },
            }, timeout.Token);
            var incompleteAssemblyImpact = await ReadResponseAsync(process, 73, timeout.Token);
            Assert.False(incompleteAssemblyImpact.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(incompleteAssemblyImpact));
            var closureImpactText = GetFirstText(incompleteAssemblyImpact);
            Assert.Contains("completeness=complete", closureImpactText, StringComparison.Ordinal);
            var closureImpactCaller = ParsePayload(closureImpactText).GetProperty("callSites").EnumerateArray()
                .Single(item => item.GetProperty("callingMember").GetString() == "BetaInvoker.Invoke");
            Assert.Equal(Path.GetFullPath(assemblyPath), Path.GetFullPath(closureImpactCaller.GetProperty("ownerTargetPath").GetString()!), StringComparer.OrdinalIgnoreCase);
            var closureImpactCallerHandle = closureImpactCaller.GetProperty("callingMemberHandoffId").GetString();
            Assert.StartsWith("h:", closureImpactCallerHandle, StringComparison.Ordinal);
            await SendRequestAsync(process, 83, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { closureImpactCallerHandle } },
            }, timeout.Token);
            var closureImpactCallerBody = await ReadResponseAsync(process, 83, timeout.Token);
            Assert.False(closureImpactCallerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(closureImpactCallerBody));
            Assert.Contains("Invoke", GetFirstText(closureImpactCallerBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 76, "tools/call", new
            {
                name = "dependency_graph",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = referenceCallerHandle, direction = "outgoing", depth = 1, maxResults = 20 },
            }, timeout.Token);
            var assemblyDependencyGraph = await ReadResponseAsync(process, 76, timeout.Token);
            var assemblyDependencyText = GetFirstText(assemblyDependencyGraph);
            Assert.False(assemblyDependencyGraph.GetProperty("result").GetProperty("isError").GetBoolean(), assemblyDependencyText);
            var dependencyEdges = ParsePayload(assemblyDependencyText).GetProperty("typeDependencies").EnumerateArray().ToArray();
            Assert.True(dependencyEdges.Length > 0, assemblyDependencyText);
            var betaDependency = dependencyEdges.Single(edge => edge.GetProperty("fromTypeName").GetString()!.Contains("BetaInvoker", StringComparison.Ordinal)
                && edge.GetProperty("toTypeName").GetString()!.Contains("PagedBeta", StringComparison.Ordinal));
            var dependencySourceHandle = betaDependency.GetProperty("fromHandoffId").GetString();
            var dependencyTargetHandle = betaDependency.GetProperty("toHandoffId").GetString();
            Assert.StartsWith("h:", dependencySourceHandle, StringComparison.Ordinal);
            Assert.StartsWith("h:", dependencyTargetHandle, StringComparison.Ordinal);
            await SendRequestAsync(process, 79, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { dependencySourceHandle } },
            }, timeout.Token);
            var dependencySourceBody = await ReadResponseAsync(process, 79, timeout.Token);
            Assert.False(dependencySourceBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(dependencySourceBody));
            Assert.Contains("Invoke", GetFirstText(dependencySourceBody), StringComparison.Ordinal);
            await SendRequestAsync(process, 77, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { dependencyTargetHandle } },
            }, timeout.Token);
            var dependencyTargetBody = await ReadResponseAsync(process, 77, timeout.Token);
            Assert.False(dependencyTargetBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(dependencyTargetBody));
            Assert.Contains("PagedBeta", GetFirstText(dependencyTargetBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 78, "tools/call", new
            {
                name = "dependency_graph",
                arguments = new { targetPath = assemblyPath, filePath = betaDependency.GetProperty("fromFile").GetString(), direction = "outgoing", depth = 1, maxResults = 20 },
            }, timeout.Token);
            var assemblyFileDependencyGraph = await ReadResponseAsync(process, 78, timeout.Token);
            var assemblyFileDependencyText = GetFirstText(assemblyFileDependencyGraph);
            Assert.False(assemblyFileDependencyGraph.GetProperty("result").GetProperty("isError").GetBoolean(), assemblyFileDependencyText);
            var fileDependencyEdges = ParsePayload(assemblyFileDependencyText).GetProperty("typeDependencies").EnumerateArray().ToArray();
            Assert.Contains(fileDependencyEdges, edge => edge.GetProperty("fromTypeName").GetString() == betaDependency.GetProperty("fromTypeName").GetString()
                && edge.GetProperty("toTypeName").GetString() == betaDependency.GetProperty("toTypeName").GetString());

            await SendRequestAsync(process, 81, "tools/call", new
            {
                name = "dependency_graph",
                arguments = new { targetPath = hostAssemblyPath, symbolIdentifier = concreteReadBetaHandle, direction = "outgoing" },
            }, timeout.Token);
            var foreignTargetDependency = await ReadResponseAsync(process, 81, timeout.Token);
            Assert.True(foreignTargetDependency.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(foreignTargetDependency));
            Assert.Contains("TARGET_MISMATCH", GetFirstText(foreignTargetDependency), StringComparison.Ordinal);

            await SendRequestAsync(process, 64, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = concreteReadBetaHandle, direction = "incoming", depth = 2, topN = 10, includeReferences = false },
            }, timeout.Token);
            var assemblyCallTree = await ReadResponseAsync(process, 64, timeout.Token);
            var assemblyCallTreeText = GetFirstText(assemblyCallTree);
            Assert.False(assemblyCallTree.GetProperty("result").GetProperty("isError").GetBoolean(), assemblyCallTreeText);
            Assert.Contains("BetaInvoker.Invoke", assemblyCallTreeText, StringComparison.Ordinal);
            var callerHandoffLine = assemblyCallTreeText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Single(line => line.Contains("(BetaInvoker.Invoke)", StringComparison.Ordinal));
            var callTreeCallerHandle = ExtractBacktickHandoff(callerHandoffLine);
            await SendRequestAsync(process, 65, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { callTreeCallerHandle } },
            }, timeout.Token);
            var callTreeCallerBody = await ReadResponseAsync(process, 65, timeout.Token);
            Assert.False(callTreeCallerBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(callTreeCallerBody));
            Assert.Contains("Invoke", GetFirstText(callTreeCallerBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 67, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = referenceCallerHandle, direction = "outgoing", depth = 2, topN = 10, includeReferences = false },
            }, timeout.Token);
            var outgoingCallTree = await ReadResponseAsync(process, 67, timeout.Token);
            var outgoingCallTreeText = GetFirstText(outgoingCallTree);
            Assert.False(outgoingCallTree.GetProperty("result").GetProperty("isError").GetBoolean(), outgoingCallTreeText);
            Assert.Contains("PagedBeta.ReadBeta", outgoingCallTreeText, StringComparison.Ordinal);
            var outgoingCallHandoffLine = outgoingCallTreeText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Single(line => line.Contains("(PagedBeta.ReadBeta)", StringComparison.Ordinal));
            var outgoingCallHandle = ExtractBacktickHandoff(outgoingCallHandoffLine);
            await SendRequestAsync(process, 68, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = assemblyPath, symbolIdentifiers = new[] { outgoingCallHandle } },
            }, timeout.Token);
            var outgoingCallBody = await ReadResponseAsync(process, 68, timeout.Token);
            Assert.False(outgoingCallBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(outgoingCallBody));
            Assert.Contains("ReadBeta", GetFirstText(outgoingCallBody), StringComparison.Ordinal);

            await SendRequestAsync(process, 69, "tools/call", new
            {
                name = "find_references",
                arguments = new { targetPath = hostAssemblyPath, symbolIdentifier = concreteReadBetaHandle, maxResults = 10 },
            }, timeout.Token);
            var foreignAssemblyReferences = await ReadResponseAsync(process, 69, timeout.Token);
            Assert.True(foreignAssemblyReferences.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(foreignAssemblyReferences));
            Assert.Contains("TARGET_MISMATCH", GetFirstText(foreignAssemblyReferences), StringComparison.Ordinal);
            await SendRequestAsync(process, 70, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = "h:unknown", direction = "incoming" },
            }, timeout.Token);
            var unknownCallTreeHandle = await ReadResponseAsync(process, 70, timeout.Token);
            Assert.True(unknownCallTreeHandle.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(unknownCallTreeHandle));
            Assert.Contains("HANDOFF_UNKNOWN", GetFirstText(unknownCallTreeHandle), StringComparison.Ordinal);

            var (sourceSolutionPath, _) = await CreateNavigationFixtureAsync(Path.Combine(fixture.DirectoryPath, "source-owner"));
            await SendRequestAsync(process, 74, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = sourceSolutionPath, pattern = "CounterConsumer", maxResults = 5 },
            }, timeout.Token);
            var sourceSymbol = await ReadResponseAsync(process, 74, timeout.Token);
            var sourceSymbolText = GetFirstText(sourceSymbol);
            Assert.False(sourceSymbol.GetProperty("result").GetProperty("isError").GetBoolean(), sourceSymbolText);
            var sourceHandle = ExtractHandoff(sourceSymbolText);
            await SendRequestAsync(process, 82, "tools/call", new
            {
                name = "dependency_graph",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = sourceHandle, direction = "outgoing" },
            }, timeout.Token);
            var sourceHandleDependency = await ReadResponseAsync(process, 82, timeout.Token);
            Assert.True(sourceHandleDependency.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(sourceHandleDependency));
            Assert.Contains("TARGET_MISMATCH", GetFirstText(sourceHandleDependency), StringComparison.Ordinal);
            await SendRequestAsync(process, 75, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = sourceHandle },
            }, timeout.Token);
            var sourceHandleOnAssembly = await ReadResponseAsync(process, 75, timeout.Token);
            var sourceHandleOnAssemblyText = GetFirstText(sourceHandleOnAssembly);
            Assert.True(sourceHandleOnAssembly.GetProperty("result").GetProperty("isError").GetBoolean(), sourceHandleOnAssemblyText);
            Assert.Contains("TARGET_MISMATCH", sourceHandleOnAssemblyText, StringComparison.Ordinal);

            await SendRequestAsync(process, 66, "tools/call", new
            {
                name = "get_call_tree",
                arguments = new { targetPath = assemblyPath, symbolIdentifier = concreteReadBetaHandle, direction = "incoming", depth = 2, topN = 10, includeReferences = true },
            }, timeout.Token);
            var incompleteAssemblyCallTree = await ReadResponseAsync(process, 66, timeout.Token);
            var completeAssemblyCallTreeText = GetFirstText(incompleteAssemblyCallTree);
            Assert.False(incompleteAssemblyCallTree.GetProperty("result").GetProperty("isError").GetBoolean(), completeAssemblyCallTreeText);
            Assert.Contains("completeness=complete", completeAssemblyCallTreeText, StringComparison.Ordinal);
            Assert.Contains("BetaInvoker.Invoke", completeAssemblyCallTreeText, StringComparison.Ordinal);

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

            await SendRequestAsync(process, 20, "tools/call", new
            {
                name = "get_file_skeleton",
                arguments = new { targetPath = assemblyPath, filePaths = new[] { betaHandle, "MissingNavigationFixture.cs" } },
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
            var ownerEdge = ParsePayload(graphText).GetProperty("typeDependencies").EnumerateArray()
                .Single(edge => edge.GetProperty("fromTypeName").GetString()!.Contains("Consumer", StringComparison.Ordinal)
                    && edge.GetProperty("toTypeName").GetString()!.Contains("LeafFirst", StringComparison.Ordinal));
            var sourceDependencyHandle = ownerEdge.GetProperty("toHandoffId").GetString();
            Assert.StartsWith("h:", sourceDependencyHandle, StringComparison.Ordinal);
            await SendRequestAsync(process, 9, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = solutionPath, symbolIdentifiers = new[] { sourceDependencyHandle } },
            }, timeout.Token);
            var sourceDependencyBody = await ReadResponseAsync(process, 9, timeout.Token);
            Assert.False(sourceDependencyBody.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(sourceDependencyBody));
            Assert.Contains("LeafFirst", GetFirstText(sourceDependencyBody), StringComparison.Ordinal);

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
    public async Task ColdSolutionsWithSameProjectNameRemainIsolatedAndWriteNothingToEitherWorkspace()
    {
        var repositoryRoot = SolutionRootLocator.Find();
        var hostAssemblyPath = GetHostAssemblyPath(repositoryRoot);
        var fixtureRoot = Directory.CreateTempSubdirectory("ainet-cold-pair-").FullName;
        var firstRoot = Path.Combine(fixtureRoot, "First");
        var secondRoot = Path.Combine(fixtureRoot, "Second");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        var firstSolution = Path.Combine(firstRoot, "First.slnx");
        var secondSolution = Path.Combine(secondRoot, "Second.slnx");
        var firstProject = Path.Combine(firstRoot, "Shared.csproj");
        var secondProject = Path.Combine(secondRoot, "Shared.csproj");
        var configPath = Path.Combine(Path.GetTempPath(), "ainet-cold-pair-config-" + Guid.NewGuid().ToString("N") + ".json");
        var logDirectory = Path.Combine(Path.GetTempPath(), "ainet-cold-pair-logs-" + Guid.NewGuid().ToString("N"));
        Process? process = null;

        await File.WriteAllTextAsync(firstSolution, "<Solution><Project Path=\"Shared.csproj\" /></Solution>");
        await File.WriteAllTextAsync(secondSolution, "<Solution><Project Path=\"Shared.csproj\" /></Solution>");
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>";
        await File.WriteAllTextAsync(firstProject, project);
        await File.WriteAllTextAsync(secondProject, project);
        await File.WriteAllTextAsync(Path.Combine(firstRoot, "FirstOnly.cs"),
            "namespace ColdPair.FirstApi; public sealed class FirstOnlyMarker { public string Origin() => \"first\"; }");
        await File.WriteAllTextAsync(Path.Combine(secondRoot, "SecondOnly.cs"),
            "namespace ColdPair.SecondApi; public sealed class SecondOnlyMarker { public string Origin() => \"second\"; }");
        await File.WriteAllTextAsync(configPath, "{\"minimumLogLevel\":\"Information\"}");

        try
        {
            await RestoreProjectAsync(firstProject, firstRoot);
            await RestoreProjectAsync(secondProject, secondRoot);
            Assert.False(Directory.Exists(Path.Combine(firstRoot, "bin")));
            Assert.False(Directory.Exists(Path.Combine(secondRoot, "bin")));
            var beforeFirst = CaptureWorkspaceSnapshot(firstRoot);
            var beforeSecond = CaptureWorkspaceSnapshot(secondRoot);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            process = await StartInitializedHostAsync(repositoryRoot, hostAssemblyPath, configPath, timeout.Token, logDirectory);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

            await SendRequestAsync(process, 2, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = firstSolution, pattern = "FirstOnlyMarker", maxResults = 10 },
            }, timeout.Token);
            var firstSearch = await ReadResponseAsync(process, 2, timeout.Token);
            var firstSearchText = GetFirstText(firstSearch);
            Assert.False(firstSearch.GetProperty("result").GetProperty("isError").GetBoolean(), firstSearchText);
            Assert.Contains("FirstOnlyMarker", firstSearchText, StringComparison.Ordinal);
            Assert.Contains("(Shared)", firstSearchText, StringComparison.Ordinal);
            var firstHandle = ExtractHandoff(firstSearchText);

            await SendRequestAsync(process, 3, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = secondSolution, pattern = "SecondOnlyMarker", maxResults = 10 },
            }, timeout.Token);
            var secondSearch = await ReadResponseAsync(process, 3, timeout.Token);
            var secondSearchText = GetFirstText(secondSearch);
            Assert.False(secondSearch.GetProperty("result").GetProperty("isError").GetBoolean(), secondSearchText);
            Assert.Contains("SecondOnlyMarker", secondSearchText, StringComparison.Ordinal);
            Assert.Contains("(Shared)", secondSearchText, StringComparison.Ordinal);
            var secondHandle = ExtractHandoff(secondSearchText);

            foreach (var (requestId, target, handoff, expected, unexpected) in new[]
            {
                (4, firstSolution, firstHandle, "first", "SecondOnlyMarker"),
                (5, secondSolution, secondHandle, "second", "FirstOnlyMarker"),
            })
            {
                await SendRequestAsync(process, requestId, "tools/call", new
                {
                    name = "get_symbol_body",
                    arguments = new { targetPath = target, symbolIdentifiers = new[] { handoff } },
                }, timeout.Token);
                var body = await ReadResponseAsync(process, requestId, timeout.Token);
                var bodyText = GetFirstText(body);
                Assert.False(body.GetProperty("result").GetProperty("isError").GetBoolean(), bodyText);
                Assert.Contains($"\"{expected}\"", bodyText, StringComparison.Ordinal);
                Assert.DoesNotContain(unexpected, bodyText, StringComparison.Ordinal);
            }

            await SendRequestAsync(process, 6, "tools/call", new
            {
                name = "get_symbol_body",
                arguments = new { targetPath = secondSolution, symbolIdentifiers = new[] { firstHandle } },
            }, timeout.Token);
            var crossedHandle = await ReadResponseAsync(process, 6, timeout.Token);
            Assert.True(crossedHandle.GetProperty("result").GetProperty("isError").GetBoolean(), GetFirstText(crossedHandle));
            Assert.Contains("TARGET_MISMATCH", GetFirstText(crossedHandle), StringComparison.Ordinal);

            await SendRequestAsync(process, 7, "tools/call", new
            {
                name = "find_symbol",
                arguments = new { targetPath = secondSolution, pattern = "FirstOnlyMarker", maxResults = 10 },
            }, timeout.Token);
            var isolatedSearch = await ReadResponseAsync(process, 7, timeout.Token);
            var isolatedSearchText = GetFirstText(isolatedSearch);
            Assert.Contains("No matches for", isolatedSearchText, StringComparison.Ordinal);
            Assert.DoesNotContain("[handoff:", isolatedSearchText, StringComparison.Ordinal);

            AssertWorkspaceUnchanged(beforeFirst, CaptureWorkspaceSnapshot(firstRoot));
            AssertWorkspaceUnchanged(beforeSecond, CaptureWorkspaceSnapshot(secondRoot));
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await process.StandardOutput.ReadToEndAsync(timeout.Token));
            _ = await stderrTask;
            AssertWorkspaceUnchanged(beforeFirst, CaptureWorkspaceSnapshot(firstRoot));
            AssertWorkspaceUnchanged(beforeSecond, CaptureWorkspaceSnapshot(secondRoot));
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
            var resolvedFixtureRoot = Path.GetFullPath(fixtureRoot);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Assert.StartsWith(tempRoot, resolvedFixtureRoot, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("ainet-cold-pair-", Path.GetFileName(resolvedFixtureRoot), StringComparison.Ordinal);
            if (Directory.Exists(resolvedFixtureRoot)) Directory.Delete(resolvedFixtureRoot, recursive: true);
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
        Directory.CreateDirectory(repositoryPath);
        var solutionPath = Path.Combine(repositoryPath, "NavigationFixture.slnx");
        var projectPath = Path.Combine(repositoryPath, "NavigationFixture.csproj");
        var linkedConsumerProject = Path.Combine(repositoryPath, "LinkedConsumer.csproj");
        var sharedDirectory = Path.Combine(fixtureRoot, "Shared");
        Directory.CreateDirectory(sharedDirectory);
        await File.WriteAllTextAsync(Path.Combine(sharedDirectory, "LinkedFixture.cs"),
            "namespace NavigationFixture; public sealed class LinkedFeature { public int Value() => 42; }");
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"NavigationFixture.csproj\" /><Project Path=\"LinkedConsumer.csproj\" /></Solution>");
        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><Compile Include=\"..\\Shared\\LinkedFixture.cs\" Link=\"LinkedFixture.cs\" /></ItemGroup></Project>");
        await File.WriteAllTextAsync(linkedConsumerProject,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include=\"..\\Shared\\LinkedFixture.cs\" Link=\"LinkedFixture.cs\" /></ItemGroup></Project>");
        const string source = "namespace NavigationFixture;\npublic interface ICounter { int Read(); }\npublic sealed class Counter : ICounter\n{\n    private readonly int firstValue = 1, muchLongerValue = 2;\n    public int Read() => firstValue;\n    public int StableSeparator() => 7;\n    public int BothFields() => firstValue + muchLongerValue;\n}\npublic sealed class CounterConsumer { public int Run(ICounter counter) => counter.Read(); }\npublic static class CounterExtensions { public static int Double(this Counter counter) => counter.Read() * 2; }\n";
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "NavigationFixture.cs"), source);
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
        return (solutionPath, Path.Combine(repositoryPath, "bin", "Debug", "net10.0", "NavigationFixture.dll"));
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
        var fixtureName = Path.GetFileName(fullRoot);
        Assert.True(fixtureName.StartsWith("ainet-contract-fixture-", StringComparison.Ordinal)
            || fixtureName.StartsWith("ainet-public-error-matrix-", StringComparison.Ordinal),
            "Read-only cleanup is restricted to owned navigation integration fixtures.");
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

    private static async Task<JsonElement> CallAndDrainAsync(Process process, string toolName, Dictionary<string, object?> arguments,
        int initialRequestId, CancellationToken cancellationToken)
    {
        var requestId = initialRequestId;
        await SendRequestAsync(process, requestId, "tools/call", new { name = toolName, arguments }, cancellationToken);
        var response = await ReadResponseAsync(process, requestId++, cancellationToken);
        for (var poll = 0; poll < 120 && response.TryGetProperty("result", out _) && GetFirstText(response).Contains("operation=running", StringComparison.Ordinal); poll++)
        {
            var operationToken = ReadStringLine(GetFirstText(response), "operationToken");
            var pollArguments = new Dictionary<string, object?>(arguments, StringComparer.Ordinal) { ["operationToken"] = operationToken };
            await SendRequestAsync(process, requestId, "tools/call", new { name = toolName, arguments = pollArguments }, cancellationToken);
            response = await ReadResponseAsync(process, requestId++, cancellationToken);
            if (GetFirstText(response).Contains("operation=running", StringComparison.Ordinal))
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
        return response;
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

    private static void AssertPropertyDescriptionContains(JsonElement tool, string propertyName, params string[] expectedFragments)
    {
        var properties = tool.GetProperty("inputSchema").GetProperty("properties");
        Assert.True(properties.TryGetProperty(propertyName, out var property), $"{tool.GetProperty("name").GetString()} omitted {propertyName}.");
        Assert.True(property.TryGetProperty("description", out var description),
            $"{tool.GetProperty("name").GetString()}.{propertyName} has no parameter description.");
        var text = description.GetString() ?? string.Empty;
        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, text, StringComparison.OrdinalIgnoreCase);
        }
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

    private static string ExtractBodyHandoff(string text)
    {
        const string marker = "Handoff: ";
        var markerStart = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerStart >= 0, $"The body result did not include a reusable opaque handoff. Response: {text}");
        var valueStart = markerStart + marker.Length;
        var valueEnd = text.IndexOfAny(['\r', '\n'], valueStart);
        if (valueEnd < 0) valueEnd = text.Length;
        var handoff = text[valueStart..valueEnd].Trim();
        Assert.StartsWith("h:", handoff, StringComparison.Ordinal);
        return handoff;
    }

    private static string ExtractOwnerCandidateHandoff(string text, string ownerPath)
    {
        var ownerMarker = $"targetPath: '{ownerPath}', handoffId: `";
        var ownerStart = text.IndexOf(ownerMarker, StringComparison.OrdinalIgnoreCase);
        Assert.True(ownerStart >= 0, $"No ambiguity candidate was offered for owner '{ownerPath}'. Response: {text}");
        var valueStart = ownerStart + ownerMarker.Length;
        var valueEnd = text.IndexOf('`', valueStart);
        Assert.True(valueEnd > valueStart, $"The owner candidate did not include a reusable h: handoff. Response: {text}");
        var handoff = text[valueStart..valueEnd];
        Assert.StartsWith("h:", handoff, StringComparison.Ordinal);
        return handoff;
    }

    private static string ExtractBacktickHandoff(string text)
    {
        const string marker = "`h:";
        var markerStart = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerStart >= 0, "The rendered call graph did not include an owner handoff for the selected node.");
        var valueStart = markerStart + 1;
        var valueEnd = text.IndexOf('`', valueStart);
        Assert.True(valueEnd > valueStart, "The rendered call graph included a malformed handoff.");
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
