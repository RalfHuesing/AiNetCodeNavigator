using System.Text.Json;
using System.Text;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.Tools.Assemblies;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.Mcp.Tools;
using AiNetCodeNavigator.Mcp.Validation;
using AiNetCodeNavigator.TestKit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class IndexScopeContractTests
{
    [Fact]
    public async Task BrowseTarget_SdkDefinitionPublishesSharedRoutingParameters()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>(),
            operationResponseWindow: TimeSpan.FromMilliseconds(1));
        var tools = new StructureTools(runtime);
        AssertNavigationSdkToolCatalog(runtime);
        var sdkTool = McpServerTool.Create(typeof(StructureTools).GetMethod(nameof(StructureTools.BrowseTarget))!, tools,
            new McpServerToolCreateOptions { Name = "browse_target" });

        var schema = sdkTool.ProtocolTool.InputSchema;
        var properties = schema.GetProperty("properties");
        var required = schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Equal(new[] { "targetPath", "view" }, required);
        Assert.Contains("maxResponseBytes", properties.EnumerateObject().Select(property => property.Name));
        Assert.Contains("maxResponseTokens", properties.EnumerateObject().Select(property => property.Name));
        Assert.Contains("operationToken", properties.EnumerateObject().Select(property => property.Name));
        Assert.Contains("continuationToken", properties.EnumerateObject().Select(property => property.Name));
        Assert.Contains("outer response page", properties.GetProperty("continuationToken").GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("background work", properties.GetProperty("operationToken").GetProperty("description").GetString(), StringComparison.Ordinal);

        using var validDocument = JsonDocument.Parse("""{"view":"scope","targetPath":"C:\\source.slnx","maxResponseBytes":512,"maxResponseTokens":120,"operationToken":"op","continuationToken":"1"}""");
        var validArguments = validDocument.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        Assert.Null(await McpArgumentValidationFilter.ValidateArgumentsAsync(sdkTool, validArguments));

        foreach (var invalidJson in new[]
        {
            """{"view":"scope","targetPath":null}""",
            """{"view":"scope","targetPath":["C:\\source.slnx"]}""",
            """{"view":"scope","targetPath":"C:\\source.slnx","maxResponseBytes":512.5}""",
            """{"view":"scope","targetPath":"C:\\source.slnx","maxResponseBytes":65537}""",
            """{"view":"scope","targetPath":"C:\\source.slnx","maxResponseTokens":1.5}""",
            """{"view":"scope","targetPath":"C:\\source.slnx","operationToken":["op"]}""",
        })
        {
            var invalid = await ValidateJsonArgumentsAsync(sdkTool, invalidJson);
            Assert.NotNull(invalid);
            Assert.True(invalid!.IsError, TextOf(invalid));
        }

        using var invalidBudgetDocument = JsonDocument.Parse("""{"view":"scope","targetPath":"C:\\source.slnx","maxResponseBytes":511}""");
        var invalidBudgetArguments = invalidBudgetDocument.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        var invalidBudget = await McpArgumentValidationFilter.ValidateArgumentsAsync(sdkTool, invalidBudgetArguments);
        Assert.NotNull(invalidBudget);
        Assert.True(invalidBudget!.IsError);
        Assert.Contains("maxResponseBytes", TextOf(invalidBudget), StringComparison.Ordinal);

        using var unknownDocument = JsonDocument.Parse("""{"view":"scope","targetPath":"C:\\source.slnx","unexpected":true}""");
        var unknownArguments = unknownDocument.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        var unknownField = await McpArgumentValidationFilter.ValidateArgumentsAsync(sdkTool, unknownArguments);
        Assert.NotNull(unknownField);
        Assert.True(unknownField!.IsError);
        Assert.Contains("unexpected", TextOf(unknownField), StringComparison.Ordinal);

        Assert.Contains("$.view", TextOf(await tools.BrowseTarget(@"C:\missing.slnx", "unknown")), StringComparison.Ordinal);
        Assert.Contains("$.view", TextOf(await tools.BrowseTarget(@"C:\missing.dll", "scope")), StringComparison.Ordinal);
        Assert.Contains("$.project", TextOf(await tools.BrowseTarget(@"C:\missing.slnx", "scope", project: "")), StringComparison.Ordinal);
        Assert.Contains("$.namespacePrefix", TextOf(await tools.BrowseTarget(@"C:\missing.slnx", "scope", namespacePrefix: "")), StringComparison.Ordinal);
        Assert.Contains("$.depth", TextOf(await tools.BrowseTarget(@"C:\missing.slnx", "scope", depth: 1)), StringComparison.Ordinal);
        Assert.Contains("$.includeTypes", TextOf(await tools.BrowseTarget(@"C:\missing.slnx", "scope", includeTypes: true)), StringComparison.Ordinal);
        Assert.Contains("$.kind", TextOf(await tools.BrowseTarget(@"C:\missing.slnx", "scope", kind: "all")), StringComparison.Ordinal);
        Assert.Contains("$.includeGenerated", TextOf(await tools.BrowseTarget(@"C:\missing.slnx", "scope", includeGenerated: false)), StringComparison.Ordinal);
        Assert.Contains("$.maxResults", TextOf(await tools.BrowseTarget(@"C:\missing.slnx", "scope", maxResults: 129)), StringComparison.Ordinal);
        Assert.NotNull(await ValidateJsonArgumentsAsync(sdkTool, """{"targetPath":"missing.slnx"}"""));

        using var fixture = TestTempDirectory.Create("ainet-index-scope-route-");
        var solutionPath = await CreateSourceFixtureAsync(fixture.DirectoryPath);
        var firstPageTask = tools.BrowseTarget(solutionPath, "scope", maxResponseBytes: 512, maxResponseTokens: 120);
        await WaitUntilAsync(() => runtime.ProjectRegistry.ActiveLoadCount > 0, TimeSpan.FromSeconds(10));
        var firstPage = await firstPageTask;
        Assert.StartsWith(McpToolResults.RunningStatusPrefix, TextOf(firstPage), StringComparison.Ordinal);
        var sdkBound = await InvokeSdkBinderAsync(sdkTool, new AIFunctionArguments
        {
            ["targetPath"] = solutionPath,
            ["view"] = "scope",
            ["maxResponseBytes"] = 65536,
            ["maxResponseTokens"] = 4096,
        });
        Assert.IsType<ModelContextProtocol.Protocol.CallToolResult>(sdkBound);
        Assert.False(((ModelContextProtocol.Protocol.CallToolResult)sdkBound!).IsError ?? false, TextOf((ModelContextProtocol.Protocol.CallToolResult)sdkBound));
        var expected = await tools.BrowseTarget(solutionPath, "scope", maxResponseBytes: 65536, maxResponseTokens: 4096);
        if (TextOf(expected).StartsWith(McpToolResults.RunningStatusPrefix, StringComparison.Ordinal))
        {
            var expectedOperation = ReadOperationToken(TextOf(expected));
            for (var poll = 0; poll < 100; poll++)
            {
                await Task.Delay(20);
                expected = await tools.BrowseTarget(solutionPath, "scope", maxResponseBytes: 65536, maxResponseTokens: 4096,
                    operationToken: expectedOperation);
                if (!TextOf(expected).StartsWith(McpToolResults.RunningStatusPrefix, StringComparison.Ordinal)) break;
            }
        }
        Assert.False(expected.IsError ?? false, TextOf(expected));
        await AssertIndexScopePagesReconstructAsync(tools, solutionPath, BodyOf(TextOf(expected)), firstPage);

        var inventory = new List<string>();
        var projectIdentities = new List<string>();
        string? inventoryCursor = null;
        string? firstInventoryCursor = null;
        var inventoryPages = 0;
        do
        {
            string? operation = null;
            ModelContextProtocol.Protocol.CallToolResult page = new();
            for (var poll = 0; poll < 100; poll++)
            {
                page = await tools.BrowseTarget(solutionPath, "scope", maxResults: 2,
                    maxResponseBytes: 16384, maxResponseTokens: 2048,
                    operationToken: operation, resultCursor: inventoryCursor);
                if (!TextOf(page).StartsWith(McpToolResults.RunningStatusPrefix, StringComparison.Ordinal)) break;
                operation = ReadOperationToken(TextOf(page));
                await Task.Delay(50);
            }
            Assert.False(page.IsError ?? false, TextOf(page));
            using var inventoryDocument = JsonDocument.Parse(JsonPayload(TextOf(page)));
            var rootElement = inventoryDocument.RootElement;
            Assert.Equal(8, rootElement.GetProperty("totalDocumentCount").GetInt32());
            Assert.Equal(4, rootElement.GetProperty("generatedDocumentCount").GetInt32());
            Assert.Equal(5, rootElement.GetProperty("totalItems").GetInt32());
            foreach (var item in rootElement.GetProperty("items").EnumerateArray())
            {
                var itemKind = item.GetProperty("kind").GetString();
                if (itemKind == "project")
                {
                    var projectIdentity = item.GetProperty("projectIdentity").GetString();
                    Assert.False(string.IsNullOrWhiteSpace(projectIdentity));
                    Assert.StartsWith(Path.GetFullPath(solutionPath[..solutionPath.LastIndexOf(Path.DirectorySeparatorChar)]).Replace('\\', '/'),
                        projectIdentity, StringComparison.OrdinalIgnoreCase);
                    Assert.Equal("net10.0", item.GetProperty("loadedFrameworkContext").GetString());
                    Assert.True(Path.IsPathFullyQualified(Assert.IsType<string>(item.GetProperty("projectPath").GetString())));
                    Assert.True(File.Exists(item.GetProperty("projectPath").GetString()));
                    Assert.Equal(2, item.GetProperty("documentCount").GetInt32());
                    Assert.Equal(2, item.GetProperty("cSharpDocumentCount").GetInt32());
                    Assert.True(item.GetProperty("configuredFrameworksKnown").GetBoolean());
                    Assert.Contains("generatedSourceExcludedFromDefaultSymbolSearch",
                        item.GetProperty("exclusions").EnumerateArray().Select(value => value.GetString()));
                    Assert.Equal(new[] { "net9.0" }, item.GetProperty("configuredFrameworksNotAnalyzed")
                        .EnumerateArray().Select(value => value.GetString()).ToArray());
                    inventory.Add($"project:{projectIdentity}");
                    projectIdentities.Add(projectIdentity!);
                }
                else inventory.Add($"fileType:{item.GetProperty("extension").GetString()}");
            }
            inventoryCursor = rootElement.TryGetProperty("resultCursor", out var cursorValue)
                && cursorValue.ValueKind == JsonValueKind.String ? cursorValue.GetString() : null;
            firstInventoryCursor ??= inventoryCursor;
            inventoryPages++;
            Assert.InRange(inventoryPages, 1, 10);
        } while (inventoryCursor is not null);
        Assert.Equal(3, inventoryPages);
        Assert.Equal(5, inventory.Count);
        Assert.Equal(5, inventory.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(projectIdentities.Select(identity => $"project:{identity}").Append("fileType:.cs"), inventory);

        Assert.NotNull(firstInventoryCursor);
        var changedPageSize = await GetIndexScopeUntilCompleteAsync(tools, solutionPath, 3, firstInventoryCursor);
        Assert.True(changedPageSize.IsError, TextOf(changedPageSize));
        Assert.Contains("RESULT_CURSOR_ARGUMENT_MISMATCH", TextOf(changedPageSize), StringComparison.Ordinal);

        var alternateSolutionPath = Path.Combine(fixture.DirectoryPath, "AlternateIndexScope.slnx");
        File.Copy(solutionPath, alternateSolutionPath);
        var changedTarget = await GetIndexScopeUntilCompleteAsync(tools, alternateSolutionPath, 2, firstInventoryCursor);
        Assert.True(changedTarget.IsError, TextOf(changedTarget));
        Assert.Contains("RESULT_CURSOR_ARGUMENT_MISMATCH", TextOf(changedTarget), StringComparison.Ordinal);

        await using (var reloadedRuntime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>()))
        {
            var reloadedTools = new StructureTools(reloadedRuntime);
            var reloaded = await reloadedTools.BrowseTarget(solutionPath, "scope", maxResponseBytes: 65536, maxResponseTokens: 4096);
            for (var poll = 0; poll < 100 && TextOf(reloaded).StartsWith(McpToolResults.RunningStatusPrefix, StringComparison.Ordinal); poll++)
            {
                var operation = ReadOperationToken(TextOf(reloaded));
                await Task.Delay(50);
                reloaded = await reloadedTools.BrowseTarget(solutionPath, "scope", maxResponseBytes: 65536, maxResponseTokens: 4096,
                    operationToken: operation);
            }
            Assert.False(reloaded.IsError ?? false, TextOf(reloaded));
            using var reloadedDocument = JsonDocument.Parse(JsonPayload(TextOf(reloaded)));
            var reloadedProjectItems = reloadedDocument.RootElement.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("kind").GetString() == "project")
                .Select(item =>
                {
                    Assert.Equal("net10.0", item.GetProperty("loadedFrameworkContext").GetString());
                    Assert.True(Path.IsPathFullyQualified(Assert.IsType<string>(item.GetProperty("projectPath").GetString())));
                    Assert.True(File.Exists(item.GetProperty("projectPath").GetString()));
                    Assert.True(item.GetProperty("configuredFrameworksKnown").GetBoolean());
                    Assert.Equal(new[] { "net9.0" }, item.GetProperty("configuredFrameworksNotAnalyzed")
                        .EnumerateArray().Select(value => value.GetString()).ToArray());
                    return item.GetProperty("projectIdentity").GetString()!;
                }).ToArray();
            Assert.Equal(projectIdentities.Order(StringComparer.Ordinal), reloadedProjectItems.Order(StringComparer.Ordinal));
        }

        var firstProjectName = "IndexScopeProject00" + new string('P', 65);
        var firstProjectFile = Path.Combine(fixture.DirectoryPath, "p0", firstProjectName + ".csproj");
        await File.WriteAllTextAsync(firstProjectFile,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net10.0;net8.0</TargetFrameworks><ImplicitUsings>disable</ImplicitUsings><Nullable>enable</Nullable><GenerateAssemblyInfo>false</GenerateAssemblyInfo><GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute></PropertyGroup></Project>");
        var updatedConfiguration = await GetIndexScopeUntilCompleteAsync(tools, solutionPath, 100, null);
        Assert.False(updatedConfiguration.IsError ?? false, TextOf(updatedConfiguration));
        using (var updatedDocument = JsonDocument.Parse(JsonPayload(TextOf(updatedConfiguration))))
        {
            var updatedProject = updatedDocument.RootElement.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("kind").GetString() == "project"
                    && item.GetProperty("name").GetString() == firstProjectName);
            Assert.Equal("net10.0", updatedProject.GetProperty("loadedFrameworkContext").GetString());
            Assert.Equal(new[] { "net8.0" }, updatedProject.GetProperty("configuredFrameworksNotAnalyzed")
                .EnumerateArray().Select(value => value.GetString()).ToArray());
        }
        var changedFrameworkConfiguration = await GetIndexScopeUntilCompleteAsync(tools, solutionPath, 2, firstInventoryCursor);
        Assert.True(changedFrameworkConfiguration.IsError, TextOf(changedFrameworkConfiguration));
        Assert.Contains("STALE_SNAPSHOT", TextOf(changedFrameworkConfiguration), StringComparison.Ordinal);

        var freshPageAfterConfigurationChange = await GetIndexScopeUntilCompleteAsync(tools, solutionPath, 2, null);
        Assert.False(freshPageAfterConfigurationChange.IsError ?? false, TextOf(freshPageAfterConfigurationChange));
        using var freshPageDocument = JsonDocument.Parse(JsonPayload(TextOf(freshPageAfterConfigurationChange)));
        var freshSourceCursor = freshPageDocument.RootElement.GetProperty("resultCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(freshSourceCursor));
        await File.WriteAllTextAsync(Path.Combine(fixture.DirectoryPath, "p0", firstProjectName + ".cs"),
            "namespace Updated; public sealed class Probe { public int Value => 2; }");
        var changedSnapshot = await GetIndexScopeUntilCompleteAsync(tools, solutionPath, 2, freshSourceCursor);
        Assert.True(changedSnapshot.IsError, TextOf(changedSnapshot));
        Assert.Contains("STALE_SNAPSHOT", TextOf(changedSnapshot), StringComparison.Ordinal);

        var assemblyTools = new AssemblyTools(runtime);
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "IndexScopeAssemblyProbe",
            "namespace IndexScopeAssemblyProbe; public sealed class Probe { public int Read() => 1; }");
        var assemblyPendingTask = assemblyTools.InspectAssembly(assemblyPath, maxResponseBytes: 65536, maxResponseTokens: 4096);
        await WaitUntilAsync(() => runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath) > 0,
            TimeSpan.FromSeconds(10));
        var assemblyPending = await assemblyPendingTask;
        Assert.StartsWith(McpToolResults.RunningStatusPrefix, TextOf(assemblyPending), StringComparison.Ordinal);
        var assemblyToken = ReadOperationToken(TextOf(assemblyPending));
        ModelContextProtocol.Protocol.CallToolResult assemblyResult = assemblyPending;
        var assemblyPollCompleted = false;
        for (var poll = 0; poll < 100; poll++)
        {
            await Task.Delay(50);
            assemblyResult = await assemblyTools.InspectAssembly(assemblyPath, operationToken: assemblyToken,
                maxResponseBytes: 65536, maxResponseTokens: 4096);
            var assemblyText = TextOf(assemblyResult);
            if (assemblyText.StartsWith(McpToolResults.RunningStatusPrefix, StringComparison.Ordinal)) continue;
            if (assemblyText.StartsWith(McpToolResults.LoadingStatusPrefix, StringComparison.Ordinal)) continue;
            Assert.False(assemblyResult.IsError ?? false, assemblyText);
            Assert.StartsWith("Status: operation=ok,", assemblyText, StringComparison.Ordinal);
            assemblyPollCompleted = true;
            break;
        }
        Assert.True(assemblyPollCompleted, $"The assembly operation did not reach an owner result: {TextOf(assemblyResult)}");

        var assemblyPayloadText = new StringBuilder();
        var assemblyPagesCompleted = false;
        for (var page = 0; page < 100; page++)
        {
            var assemblyText = TextOf(assemblyResult);
            assemblyPayloadText.Append(BodyOf(assemblyText));
            if (!TryReadToken(assemblyText, "continuationToken", out var assemblyCursor))
            {
                assemblyPagesCompleted = true;
                break;
            }
            assemblyResult = await assemblyTools.InspectAssembly(assemblyPath, continuationToken: assemblyCursor,
                maxResponseBytes: 65536, maxResponseTokens: 4096);
            Assert.False(assemblyResult.IsError ?? false, TextOf(assemblyResult));
        }
        Assert.True(assemblyPagesCompleted, "The inspect_assembly response did not finish within 100 outer pages.");
        using (var completeAssemblyPayload = JsonDocument.Parse(JsonPayload(assemblyPayloadText.ToString())))
        {
            var assemblyOwner = completeAssemblyPayload.RootElement;
            Assert.Equal(Path.GetFullPath(assemblyPath), Path.GetFullPath(assemblyOwner.GetProperty("assemblyPath").GetString()!));
            Assert.True(assemblyOwner.GetProperty("totalTypes").GetInt32() > 0, assemblyOwner.ToString());
            Assert.Equal(assemblyOwner.GetProperty("types").GetArrayLength(), assemblyOwner.GetProperty("shownCount").GetInt32());
            Assert.Contains(assemblyOwner.GetProperty("completeness").GetString(), new[] { "complete", "truncated" });
        }

        await AssertSourceAndAssemblyCancellationUsesOwnerRoutesAsync(host.Services.GetRequiredService<IHostApplicationLifetime>(),
            solutionPath, typeof(IndexScopeContractTests).Assembly.Location);

        var expiredOperation = await tools.BrowseTarget(solutionPath, "scope", maxResponseBytes: 512, maxResponseTokens: 512, operationToken: "unknown-operation");
        Assert.True(expiredOperation.IsError);
        Assert.Contains("OPERATION_EXPIRED", TextOf(expiredOperation), StringComparison.Ordinal);
        AssertBudget(TextOf(expiredOperation), 512, 512);

        var expiredContinuation = await tools.BrowseTarget(solutionPath, "scope", maxResponseBytes: 512, maxResponseTokens: 512, continuationToken: "unknown-continuation");
        Assert.True(expiredContinuation.IsError);
        Assert.Contains("CONTINUATION_EXPIRED", TextOf(expiredContinuation), StringComparison.Ordinal);
        AssertBudget(TextOf(expiredContinuation), 512, 512);

        var mixedTokens = await tools.BrowseTarget(solutionPath, "scope", maxResponseBytes: 512, maxResponseTokens: 512,
            operationToken: "unknown-operation", continuationToken: "unknown-continuation");
        Assert.True(mixedTokens.IsError);
        Assert.Contains("INVALID_ARGUMENT", TextOf(mixedTokens), StringComparison.Ordinal);
    }

    private static string TextOf(ModelContextProtocol.Protocol.CallToolResult result) =>
        Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(result.Content)).Text;

    private static async Task<ModelContextProtocol.Protocol.CallToolResult> GetIndexScopeUntilCompleteAsync(
        StructureTools tools,
        string solutionPath,
        int maxResults,
        string? resultCursor)
    {
        string? operation = null;
        ModelContextProtocol.Protocol.CallToolResult result = new();
        for (var poll = 0; poll < 100; poll++)
        {
            result = await tools.BrowseTarget(solutionPath, "scope", maxResults: maxResults,
                maxResponseBytes: 16384, maxResponseTokens: 2048, operationToken: operation,
                resultCursor: resultCursor);
            if (!TextOf(result).StartsWith(McpToolResults.RunningStatusPrefix, StringComparison.Ordinal))
                return result;
            operation = ReadOperationToken(TextOf(result));
            await Task.Delay(20);
        }

        return result;
    }

    private static string JsonPayload(string text)
    {
        var start = text.IndexOf('{');
        Assert.True(start >= 0, text);
        return text[start..];
    }

    private static async Task<ModelContextProtocol.Protocol.CallToolResult?> ValidateJsonArgumentsAsync(
        ModelContextProtocol.Server.McpServerTool sdkTool,
        string json)
    {
        using var document = JsonDocument.Parse(json);
        var arguments = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        return await McpArgumentValidationFilter.ValidateArgumentsAsync(sdkTool, arguments);
    }

    private static async Task<object?> InvokeSdkBinderAsync(McpServerTool tool, AIFunctionArguments arguments)
    {
        // The public MCP tool InvokeAsync requires a RequestContext that itself requires McpServer/JsonRpcRequest;
        // invoke the SDK-created AIFunction's public binder directly to keep this contract test transport- and server-free.
        var property = tool.GetType().GetProperty("AIFunction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var function = Assert.IsAssignableFrom<AIFunction>(property?.GetValue(tool));
        return await function.InvokeAsync(arguments, CancellationToken.None);
    }

    private static void AssertNavigationSdkToolCatalog(NavigatorHostRuntime runtime)
    {
        var toolTypes = typeof(SymbolTools).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var instances = toolTypes.ToDictionary(type => type, type =>
            Activator.CreateInstance(type, runtime)
            ?? throw new InvalidOperationException($"Could not create MCP tool type {type.FullName}."));
        var registered = new List<(ModelContextProtocol.Server.McpServerTool Tool, MethodInfo Method)>();
        foreach (var (type, instance) in instances)
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attribute is null) continue;

                var delegateSignature = method.GetParameters().Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType).ToArray();
                var handler = method.CreateDelegate(Expression.GetDelegateType(delegateSignature), method.IsStatic ? null : instance);
                registered.Add((ModelContextProtocol.Server.McpServerTool.Create(handler,
                    new McpServerToolCreateOptions { Name = attribute.Name }), method));
            }
        }

        var names = registered.Select(item => item.Tool.ProtocolTool.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(13, names.Length);
        Assert.Equal(new[]
        {
            "browse_target", "dependency_graph", "find_assembly_extensions", "find_references", "find_symbol",
            "get_call_tree", "get_context", "get_file_skeleton",
            "get_symbol_body",
            "get_type_relations", "inspect_assembly", "resolve_type_origin", "search_assembly",
        }, names);
        Assert.DoesNotContain("get_server_health", names);
        Assert.DoesNotContain("reload_config", names);
        Assert.DoesNotContain("get_file_tree", names);

        var missingDescriptions = new List<string>();
        foreach (var (tool, method) in registered)
        {
            if (string.IsNullOrWhiteSpace(tool.ProtocolTool.Description)) missingDescriptions.Add(tool.ProtocolTool.Name + ": purpose");
            var annotations = tool.ProtocolTool.GetType().GetProperty("Annotations")!.GetValue(tool.ProtocolTool)!;
            Assert.Equal(true, annotations.GetType().GetProperty("ReadOnlyHint")!.GetValue(annotations));
            Assert.Equal(false, annotations.GetType().GetProperty("DestructiveHint")!.GetValue(annotations));
            Assert.Equal(true, annotations.GetType().GetProperty("IdempotentHint")!.GetValue(annotations));
            Assert.Equal(false, annotations.GetType().GetProperty("OpenWorldHint")!.GetValue(annotations));
            var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
            Assert.Equal("object", tool.ProtocolTool.InputSchema.GetProperty("type").GetString());
            var sdkParameterNames = method.GetParameters()
                .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
                .Select(GetWireParameterName)
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(sdkParameterNames, properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
            var required = tool.ProtocolTool.InputSchema.TryGetProperty("required", out var requiredSchema)
                ? requiredSchema.EnumerateArray().Select(value => value.GetString()).ToArray()
                : [];
            Assert.All(required, name => Assert.Contains(name, sdkParameterNames));
            foreach (var parameter in tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject())
            {
                if (!parameter.Value.TryGetProperty("description", out var description)
                    || string.IsNullOrWhiteSpace(description.GetString()))
                {
                    missingDescriptions.Add(tool.ProtocolTool.Name + "." + parameter.Name);
                }
                Assert.True(parameter.Value.TryGetProperty("type", out _) || parameter.Value.TryGetProperty("anyOf", out _)
                    || parameter.Value.TryGetProperty("$ref", out _), $"{tool.ProtocolTool.Name}.{parameter.Name} must publish a JSON schema.");

                if (parameter.Name is "symbolIdentifier" or "symbolIdentifiers" or "filePaths")
                {
                    var routeDescription = parameter.Value.GetProperty("description").GetString()!;
                    Assert.Contains("src:", routeDescription, StringComparison.Ordinal);
                    Assert.Contains("asm:", routeDescription, StringComparison.Ordinal);
                    Assert.DoesNotContain("h:", routeDescription, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("i:", routeDescription, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("opaque", routeDescription, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("handle", routeDescription, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        Assert.True(missingDescriptions.Count == 0, string.Join(", ", missingDescriptions));

        var findSymbolDescription = registered.Single(item => item.Tool.ProtocolTool.Name == "find_symbol")
            .Tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("includeReferences").GetProperty("description").GetString();
        Assert.Contains("assembly targets", findSymbolDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resolved referenced assemblies", findSymbolDescription, StringComparison.Ordinal);
        Assert.Contains("source searches ignore", findSymbolDescription, StringComparison.Ordinal);
        var contextSchema = registered.Single(item => item.Tool.ProtocolTool.Name == "get_context")
            .Tool.ProtocolTool.InputSchema.GetProperty("properties");
        Assert.Contains("members", contextSchema.GetProperty("sections").GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("uses", contextSchema.GetProperty("sections").GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    private static string GetWireParameterName(ParameterInfo parameter)
    {
        var nameAttribute = parameter.GetCustomAttributes()
            .FirstOrDefault(attribute => attribute.GetType().FullName == "Microsoft.Extensions.AI.AIParameterNameAttribute");
        var explicitName = nameAttribute?.GetType().GetProperty("Name")?.GetValue(nameAttribute) as string;
        return string.IsNullOrWhiteSpace(explicitName) ? parameter.Name! : explicitName;
    }

    private static async Task AssertIndexScopePagesReconstructAsync(
        StructureTools tools,
        string solutionPath,
        string expectedBody,
        ModelContextProtocol.Protocol.CallToolResult firstPage)
    {
        var responseBytes = 512;
        var responseTokens = 120;
        var reconstructed = new StringBuilder();
        var pageCount = 0;
        var complete = false;
        string? operationToken = null;
        string? continuationToken = null;
        var result = firstPage;
        for (var request = 0; request < 100; request++)
        {
            var text = TextOf(result);
            if (result.IsError == true && text.Contains("RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
            {
                var minimumBytes = ReadBudget(text, "minimumResponseBytes");
                var minimumTokens = ReadBudget(text, "minimumResponseTokens");
                responseBytes = minimumBytes;
                responseTokens = minimumTokens;
                result = await tools.BrowseTarget(solutionPath, "scope", maxResponseBytes: minimumBytes, maxResponseTokens: minimumTokens,
                    operationToken: operationToken, continuationToken: continuationToken);
                continue;
            }

            if (TryReadToken(text, "operationToken", out var pendingOperation))
            {
                operationToken = pendingOperation;
                await Task.Delay(50);
                result = await tools.BrowseTarget(solutionPath, "scope", maxResponseBytes: responseBytes, maxResponseTokens: responseTokens,
                    operationToken: operationToken, continuationToken: continuationToken);
                continue;
            }

            if (text.Contains("operation=retry", StringComparison.Ordinal))
            {
                await Task.Delay(50);
                result = await tools.BrowseTarget(solutionPath, "scope", maxResponseBytes: responseBytes, maxResponseTokens: responseTokens,
                    operationToken: operationToken, continuationToken: continuationToken);
                continue;
            }

            Assert.False(result.IsError ?? false, text);
            reconstructed.Append(BodyOf(text));
            pageCount++;
            operationToken = null;
            if (!TryReadToken(text, "continuationToken", out var nextContinuation))
            {
                complete = true;
                break;
            }

            continuationToken = nextContinuation;
            result = await tools.BrowseTarget(solutionPath, "scope", maxResponseBytes: responseBytes, maxResponseTokens: responseTokens,
                operationToken: operationToken, continuationToken: continuationToken);
        }

        Assert.True(complete, $"The index scope response should reach its final continuation page. Last page: {TextOf(result)}");
        Assert.True(pageCount > 1, "The index scope report should use the shared continuation store.");
        Assert.Equal(expectedBody, reconstructed.ToString());
    }

    private static string ReadOperationToken(string text)
    {
        Assert.True(TryReadToken(text, "operationToken", out var token), text);
        return token;
    }

    private static async Task AssertSourceAndAssemblyCancellationUsesOwnerRoutesAsync(
        IHostApplicationLifetime lifetime,
        string solutionPath,
        string assemblyPath)
    {
        await using var runtime = new NavigatorHostRuntime(lifetime, operationResponseWindow: TimeSpan.FromSeconds(30));
        var structure = new StructureTools(runtime);
        using (var sourceCancellation = new CancellationTokenSource())
        {
            var sourceCall = structure.BrowseTarget(solutionPath, "scope", cancellationToken: sourceCancellation.Token);
            await WaitUntilAsync(() => runtime.ProjectRegistry.ActiveLoadCount > 0, TimeSpan.FromSeconds(10));
            await sourceCancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceCall);
        }

        using var assemblyCancellation = new CancellationTokenSource();
        var assemblies = new AssemblyTools(runtime);
        var assemblyCall = assemblies.InspectAssembly(assemblyPath, cancellationToken: assemblyCancellation.Token);
        await WaitUntilAsync(() => runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath) > 0,
            TimeSpan.FromSeconds(10));
        await assemblyCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => assemblyCall);
        await WaitUntilAsync(() => runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath) == 0,
            TimeSpan.FromSeconds(10));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(deadline.Elapsed < timeout, "The owner state did not reach the expected state before timeout.");
            await Task.Delay(10);
        }
    }

    private static async Task<string> CreateSourceFixtureAsync(string root)
    {
        const int projectCount = 4;
        var projectNames = Enumerable.Range(0, projectCount)
            .Select(index => $"IndexScopeProject{index:D2}{new string('P', 65)}")
            .ToArray();
        var solutionPath = Path.Combine(root, "IndexScopeContract.slnx");
        var projects = string.Join("", projectNames.Select((name, index) => $"<Project Path=\"p{index}/{name}.csproj\" />"));
        await File.WriteAllTextAsync(solutionPath, $"<Solution>{projects}</Solution>");
        for (var index = 0; index < projectNames.Length; index++)
        {
            var name = projectNames[index];
            var projectDirectory = Path.Combine(root, $"p{index}");
            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, name + ".csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net10.0;net9.0</TargetFrameworks><ImplicitUsings>disable</ImplicitUsings><Nullable>enable</Nullable><GenerateAssemblyInfo>false</GenerateAssemblyInfo><GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, name + ".cs"), $"namespace {name}; public sealed class Probe {{ public int Value => 1; }}");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Generated.g.cs"), $"// <auto-generated />{Environment.NewLine}namespace {name}; public sealed class GeneratedProbe {{ }}");
        }

        var nugetConfigPath = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(nugetConfigPath, "<configuration><packageSources><clear /></packageSources></configuration>");
        await FixtureRestore.RunAsync(solutionPath, root, nugetConfigPath, "Index scope fixture restore");
        return solutionPath;
    }
}
