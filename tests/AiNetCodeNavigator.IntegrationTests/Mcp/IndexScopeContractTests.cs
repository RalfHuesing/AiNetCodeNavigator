using System.Text.Json;
using System.Text;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using AiNetCodeNavigator.Configuration;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.Tools.Assemblies;
using AiNetCodeNavigator.Mcp.Tools.Maintenance;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.Mcp.Tools;
using AiNetCodeNavigator.Mcp.Validation;
using AiNetCodeNavigator.TestKit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using Serilog.Core;
using Serilog.Events;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class IndexScopeContractTests
{
    [Fact]
    public async Task GetIndexScope_OriginalSdkDefinitionPublishesSharedRoutingParameters()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var configuration = new NavigatorHostConfiguration(
            Path.Combine(Path.GetTempPath(), "ainet-index-scope-contract-" + Guid.NewGuid().ToString("N") + ".json"),
            isDefaultPath: true,
            new LoggingLevelSwitch(LogEventLevel.Warning));
        Assert.True((await configuration.LoadStartupAsync(CancellationToken.None)).Succeeded);
        await using var runtime = new NavigatorHostRuntime(configuration, host.Services.GetRequiredService<IHostApplicationLifetime>(),
            operationResponseWindow: TimeSpan.FromMilliseconds(1));
        var tools = new StructureTools(runtime);
        AssertOriginalSdkToolCatalog(runtime);
        Func<string, int, int?, string?, string?, CancellationToken, Task<ModelContextProtocol.Protocol.CallToolResult>> handler = tools.GetIndexScope;
        var sdkTool = McpServerTool.Create(handler, new McpServerToolCreateOptions { Name = "get_index_scope" });

        var schema = sdkTool.ProtocolTool.InputSchema;
        var properties = schema.GetProperty("properties");
        var required = schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Equal(new[] { "targetPath" }, required);
        Assert.Contains("maxResponseBytes", properties.EnumerateObject().Select(property => property.Name));
        Assert.Contains("maxResponseTokens", properties.EnumerateObject().Select(property => property.Name));
        Assert.Contains("operationToken", properties.EnumerateObject().Select(property => property.Name));
        Assert.Contains("continuationToken", properties.EnumerateObject().Select(property => property.Name));
        Assert.Contains("outer response page", properties.GetProperty("continuationToken").GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("background work", properties.GetProperty("operationToken").GetProperty("description").GetString(), StringComparison.Ordinal);

        using var validDocument = JsonDocument.Parse("""{"targetPath":"C:\\source.slnx","maxResponseBytes":512,"maxResponseTokens":120,"operationToken":"op","continuationToken":"1"}""");
        var validArguments = validDocument.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        Assert.Null(await McpArgumentValidationFilter.ValidateArgumentsAsync(sdkTool, validArguments));

        foreach (var invalidJson in new[]
        {
            """{"targetPath":null}""",
            """{"targetPath":["C:\\source.slnx"]}""",
            """{"targetPath":"C:\\source.slnx","maxResponseBytes":512.5}""",
            """{"targetPath":"C:\\source.slnx","maxResponseBytes":65537}""",
            """{"targetPath":"C:\\source.slnx","maxResponseTokens":1.5}""",
            """{"targetPath":"C:\\source.slnx","operationToken":["op"]}""",
        })
        {
            var invalid = await ValidateJsonArgumentsAsync(sdkTool, invalidJson);
            Assert.NotNull(invalid);
            Assert.True(invalid!.IsError, TextOf(invalid));
        }

        using var invalidBudgetDocument = JsonDocument.Parse("""{"targetPath":"C:\\source.slnx","maxResponseBytes":511}""");
        var invalidBudgetArguments = invalidBudgetDocument.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        var invalidBudget = await McpArgumentValidationFilter.ValidateArgumentsAsync(sdkTool, invalidBudgetArguments);
        Assert.NotNull(invalidBudget);
        Assert.True(invalidBudget!.IsError);
        Assert.Contains("maxResponseBytes", TextOf(invalidBudget), StringComparison.Ordinal);

        using var unknownDocument = JsonDocument.Parse("""{"targetPath":"C:\\source.slnx","unexpected":true}""");
        var unknownArguments = unknownDocument.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        var unknownField = await McpArgumentValidationFilter.ValidateArgumentsAsync(sdkTool, unknownArguments);
        Assert.NotNull(unknownField);
        Assert.True(unknownField!.IsError);
        Assert.Contains("unexpected", TextOf(unknownField), StringComparison.Ordinal);

        using var fixture = TestTempDirectory.Create("ainet-index-scope-route-");
        var solutionPath = await CreateSourceFixtureAsync(fixture.DirectoryPath);
        var firstPageTask = tools.GetIndexScope(solutionPath, maxResponseBytes: 512, maxResponseTokens: 120);
        await WaitUntilAsync(() => runtime.ProjectRegistry.ActiveLoadCount > 0, TimeSpan.FromSeconds(10));
        var firstPage = await firstPageTask;
        Assert.StartsWith(McpToolResults.RunningStatusPrefix, TextOf(firstPage), StringComparison.Ordinal);
        var sdkBound = await InvokeSdkBinderAsync(sdkTool, new AIFunctionArguments
        {
            ["targetPath"] = solutionPath,
            ["maxResponseBytes"] = 65536,
            ["maxResponseTokens"] = 4096,
        });
        Assert.IsType<ModelContextProtocol.Protocol.CallToolResult>(sdkBound);
        Assert.False(((ModelContextProtocol.Protocol.CallToolResult)sdkBound!).IsError ?? false, TextOf((ModelContextProtocol.Protocol.CallToolResult)sdkBound));
        var expected = await tools.GetIndexScope(solutionPath, maxResponseBytes: 65536, maxResponseTokens: 4096);
        if (TextOf(expected).StartsWith(McpToolResults.RunningStatusPrefix, StringComparison.Ordinal))
        {
            var expectedOperation = ReadOperationToken(TextOf(expected));
            for (var poll = 0; poll < 100; poll++)
            {
                await Task.Delay(20);
                expected = await tools.GetIndexScope(solutionPath, maxResponseBytes: 65536, maxResponseTokens: 4096,
                    operationToken: expectedOperation);
                if (!TextOf(expected).StartsWith(McpToolResults.RunningStatusPrefix, StringComparison.Ordinal)) break;
            }
        }
        Assert.False(expected.IsError ?? false, TextOf(expected));
        await AssertIndexScopePagesReconstructAsync(tools, solutionPath, BodyOf(TextOf(expected)), firstPage);

        var assemblyTools = new AssemblyTools(runtime);
        var assemblyPath = typeof(TestTempDirectory).Assembly.Location;
        var assemblyPendingTask = assemblyTools.GetAssemblyContext(assemblyPath, maxResponseBytes: 65536, maxResponseTokens: 4096);
        await WaitUntilAsync(() => runtime.AssemblyRegistry.GetHealthSnapshot(assemblyPath).Any(snapshot => snapshot.ActiveAccesses > 0),
            TimeSpan.FromSeconds(10));
        var assemblyPending = await assemblyPendingTask;
        Assert.StartsWith(McpToolResults.RunningStatusPrefix, TextOf(assemblyPending), StringComparison.Ordinal);
        var assemblyToken = ReadOperationToken(TextOf(assemblyPending));
        ModelContextProtocol.Protocol.CallToolResult assemblyResult = assemblyPending;
        var assemblyPollCompleted = false;
        for (var poll = 0; poll < 100; poll++)
        {
            await Task.Delay(50);
            assemblyResult = await assemblyTools.GetAssemblyContext(assemblyPath, operationToken: assemblyToken,
                maxResponseBytes: 65536, maxResponseTokens: 4096);
            var assemblyText = TextOf(assemblyResult);
            if (assemblyText.StartsWith(McpToolResults.RunningStatusPrefix, StringComparison.Ordinal)) continue;
            if (assemblyText.StartsWith(McpToolResults.LoadingStatusPrefix, StringComparison.Ordinal)) continue;
            Assert.False(assemblyResult.IsError ?? false, assemblyText);
            Assert.StartsWith("Status: operation=ok,", assemblyText, StringComparison.Ordinal);
            var payloadOffset = assemblyText.IndexOf('\n');
            Assert.True(payloadOffset > 0, assemblyText);
            using var assemblyPayload = JsonDocument.Parse(assemblyText[(payloadOffset + 1)..]);
            var assemblyOwner = assemblyPayload.RootElement;
            Assert.Equal(Path.GetFullPath(assemblyPath), assemblyOwner.GetProperty("assemblyPath").GetString());
            Assert.Contains(assemblyOwner.GetProperty("status").GetString(), new[] { "complete", "partial", "degraded" });
            Assert.True(assemblyOwner.GetProperty("totalTypes").GetInt32() > 0);
            assemblyPollCompleted = true;
            break;
        }
        Assert.True(assemblyPollCompleted, $"The assembly operation did not reach an owner result: {TextOf(assemblyResult)}");

        await AssertSourceAndAssemblyCancellationUsesOwnerRoutesAsync(configuration, host.Services.GetRequiredService<IHostApplicationLifetime>(),
            solutionPath, typeof(IndexScopeContractTests).Assembly.Location);

        var expiredOperation = await tools.GetIndexScope(solutionPath, 512, 512, "unknown-operation");
        Assert.True(expiredOperation.IsError);
        Assert.Contains("OPERATION_EXPIRED", TextOf(expiredOperation), StringComparison.Ordinal);
        AssertWithinBudget(TextOf(expiredOperation), 512, 512);

        var expiredContinuation = await tools.GetIndexScope(solutionPath, 512, 512, continuationToken: "unknown-continuation");
        Assert.True(expiredContinuation.IsError);
        Assert.Contains("CONTINUATION_EXPIRED", TextOf(expiredContinuation), StringComparison.Ordinal);
        AssertWithinBudget(TextOf(expiredContinuation), 512, 512);

        var mixedTokens = await tools.GetIndexScope(solutionPath, 512, 512, "unknown-operation", "unknown-continuation");
        Assert.True(mixedTokens.IsError);
        Assert.Contains("INVALID_ARGUMENT", TextOf(mixedTokens), StringComparison.Ordinal);
    }

    private static string TextOf(ModelContextProtocol.Protocol.CallToolResult result) =>
        Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(result.Content)).Text;

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

    private static void AssertOriginalSdkToolCatalog(NavigatorHostRuntime runtime)
    {
        var instances = new Dictionary<Type, object>
        {
            [typeof(MaintenanceTools)] = new MaintenanceTools(runtime),
            [typeof(SymbolTools)] = new SymbolTools(runtime),
            [typeof(StructureTools)] = new StructureTools(runtime),
            [typeof(RelationshipTools)] = new RelationshipTools(runtime),
            [typeof(AssemblyTools)] = new AssemblyTools(runtime),
        };
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
        Assert.Equal(new[]
        {
            "dependency_graph", "find_assembly_extensions", "find_implementations", "find_references", "find_symbol",
            "get_assembly_context", "get_call_tree", "get_class_structure", "get_feature_context", "get_file_skeleton",
            "get_impact", "get_index_scope", "get_namespace_tree", "get_server_health", "get_symbol_body",
            "get_test_context", "get_type_hierarchy", "inspect_assembly", "reload_config", "resolve_type_origin", "search_assembly",
        }, names);
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
            }
        }
        Assert.True(missingDescriptions.Count == 0, string.Join(", ", missingDescriptions));

        var findSymbolDescription = registered.Single(item => item.Tool.ProtocolTool.Name == "find_symbol")
            .Tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("includeReferences").GetProperty("description").GetString();
        Assert.Contains("assembly targets", findSymbolDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resolved referenced assemblies", findSymbolDescription, StringComparison.Ordinal);
        Assert.Contains("source searches ignore", findSymbolDescription, StringComparison.Ordinal);
        var assemblyContextReferencesDescription = registered.Single(item => item.Tool.ProtocolTool.Name == "get_assembly_context")
            .Tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("includeReferences").GetProperty("description").GetString();
        Assert.Contains("reference metadata", assemblyContextReferencesDescription, StringComparison.Ordinal);
        Assert.Contains("raw symbols", assemblyContextReferencesDescription, StringComparison.Ordinal);
        Assert.Contains("caller/impact traversal", assemblyContextReferencesDescription, StringComparison.Ordinal);
    }

    private static string GetWireParameterName(ParameterInfo parameter)
    {
        var nameAttribute = parameter.GetCustomAttributes()
            .FirstOrDefault(attribute => attribute.GetType().FullName == "Microsoft.Extensions.AI.AIParameterNameAttribute");
        var explicitName = nameAttribute?.GetType().GetProperty("Name")?.GetValue(nameAttribute) as string;
        return string.IsNullOrWhiteSpace(explicitName) ? parameter.Name! : explicitName;
    }

    private static void AssertWithinBudget(string text, int bytes, int tokens)
    {
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(text) <= bytes);
        Assert.True(McpResponseFormatter.CountTokens(text) <= tokens);
    }

    private static async Task AssertIndexScopePagesReconstructAsync(
        StructureTools tools,
        string solutionPath,
        string expectedBody,
        ModelContextProtocol.Protocol.CallToolResult firstPage)
    {
        const int responseBytes = 512;
        const int responseTokens = 120;
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
                result = await tools.GetIndexScope(solutionPath, minimumBytes, minimumTokens, operationToken, continuationToken);
                continue;
            }

            if (TryReadToken(text, "operationToken", out var pendingOperation))
            {
                operationToken = pendingOperation;
                await Task.Delay(50);
                result = await tools.GetIndexScope(solutionPath, responseBytes, responseTokens, operationToken, continuationToken);
                continue;
            }

            if (text.Contains("operation=retry", StringComparison.Ordinal))
            {
                await Task.Delay(50);
                result = await tools.GetIndexScope(solutionPath, responseBytes, responseTokens, operationToken, continuationToken);
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
            result = await tools.GetIndexScope(solutionPath, responseBytes, responseTokens,
                operationToken: operationToken, continuationToken: continuationToken);
        }

        Assert.True(complete, "The index scope response should reach its final continuation page.");
        Assert.True(pageCount > 1, "The index scope report should use the shared continuation store.");
        Assert.Equal(expectedBody, reconstructed.ToString());
    }

    private static string BodyOf(string text)
    {
        var lines = text.Split('\n');
        var firstContentLine = lines.Length > 1 && lines[1].StartsWith("continuationToken=", StringComparison.Ordinal) ? 2 : 1;
        return string.Join("\n", lines.Skip(firstContentLine));
    }

    private static bool TryReadToken(string text, string name, out string token)
    {
        var prefix = name + "=";
        var value = text.Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        token = value is null ? string.Empty : value[prefix.Length..];
        return value is not null;
    }

    private static string ReadOperationToken(string text)
    {
        Assert.True(TryReadToken(text, "operationToken", out var token), text);
        return token;
    }

    private static async Task AssertSourceAndAssemblyCancellationUsesOwnerRoutesAsync(
        NavigatorHostConfiguration configuration,
        IHostApplicationLifetime lifetime,
        string solutionPath,
        string assemblyPath)
    {
        await using var runtime = new NavigatorHostRuntime(configuration, lifetime, operationResponseWindow: TimeSpan.FromSeconds(30));
        var structure = new StructureTools(runtime);
        using (var sourceCancellation = new CancellationTokenSource())
        {
            var sourceCall = structure.GetIndexScope(solutionPath, cancellationToken: sourceCancellation.Token);
            await WaitUntilAsync(() => runtime.ProjectRegistry.ActiveLoadCount > 0, TimeSpan.FromSeconds(10));
            await sourceCancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceCall);
        }

        using var assemblyCancellation = new CancellationTokenSource();
        var assemblies = new AssemblyTools(runtime);
        var assemblyCall = assemblies.GetAssemblyContext(assemblyPath, cancellationToken: assemblyCancellation.Token);
        await WaitUntilAsync(() => runtime.AssemblyRegistry.GetHealthSnapshot(assemblyPath).Any(snapshot => snapshot.ActiveAccesses > 0),
            TimeSpan.FromSeconds(10));
        await assemblyCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => assemblyCall);
        await WaitUntilAsync(() => runtime.AssemblyRegistry.GetHealthSnapshot(assemblyPath).All(snapshot => snapshot.ActiveAccesses == 0),
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

    private static int ReadBudget(string text, string name)
    {
        var prefix = name + ": ";
        var value = text.Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        Assert.True(value is not null, $"Missing {name} in response: {text}");
        return int.Parse(value![prefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
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
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, name + ".cs"), $"namespace {name}; public sealed class Probe {{ public int Value => 1; }}");
        }

        var nugetConfigPath = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(nugetConfigPath, "<configuration><packageSources><clear /></packageSources></configuration>");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(solutionPath);
        startInfo.ArgumentList.Add("--configfile");
        startInfo.ArgumentList.Add(nugetConfigPath);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start index scope fixture restore.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        var processExit = process.WaitForExitAsync();
        try
        {
            await processExit.WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await processExit;
            _ = await standardOutput;
            _ = await standardError;
            throw new TimeoutException("Index scope fixture restore exceeded its two-minute limit and was terminated.");
        }
        Assert.True(process.ExitCode == 0, $"Index scope fixture restore failed: {await standardError}\n{await standardOutput}");
        return solutionPath;
    }
}
