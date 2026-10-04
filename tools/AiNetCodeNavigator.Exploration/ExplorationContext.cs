using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.Mcp.Validation;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AiNetCodeNavigator.Exploration;

/// <summary>Invokes real local handlers and records their visible MCP output for manual inspection.</summary>
internal sealed class ExplorationContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Dictionary<string, McpServerTool> _tools = new(StringComparer.Ordinal);
    private readonly CancellationToken _cancellationToken;
    private int _callNumber;

    internal ExplorationContext(NavigatorHostRuntime runtime, string repositoryRoot, string outputDirectory, CancellationToken cancellationToken)
    {
        RepositorySolution = Path.Combine(repositoryRoot, "AiNetCodeNavigator.slnx");
        OutputDirectory = outputDirectory;
        _cancellationToken = cancellationToken;
        // Discover the same attributed tool classes used by the production host. No scenario-specific dispatch table.
        foreach (var type in typeof(SymbolTools).Assembly.GetTypes()
                     .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null))
        {
            var instance = Activator.CreateInstance(type, runtime)
                ?? throw new InvalidOperationException($"Could not create {type.FullName}.");
            foreach (var method in type.GetMethods())
            {
                var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attribute is null) continue;
                var signature = method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType).ToArray();
                var handler = method.CreateDelegate(Expression.GetDelegateType(signature), instance);
                var tool = McpServerTool.Create(handler, new McpServerToolCreateOptions { Name = attribute.Name });
                _tools.Add(tool.ProtocolTool.Name, tool);
            }
        }
    }

    internal string RepositorySolution { get; }
    internal string OutputDirectory { get; }

    internal async Task<ExplorationResult> CallAsync(string toolName, object parameters, string? expectedErrorCode = null)
    {
        var directory = Path.Combine(OutputDirectory, $"{++_callNumber:D2}-{toolName}");
        Directory.CreateDirectory(directory);
        var arguments = JsonSerializer.SerializeToElement(parameters, JsonOptions).EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        var request = new CallToolRequestParams { Name = toolName, Arguments = arguments };
        await WriteJsonAsync(Path.Combine(directory, "request.json"), request).ConfigureAwait(false);
        try
        {
            if (!_tools.TryGetValue(toolName, out var tool))
                throw new InvalidOperationException($"Unknown MCP tool '{toolName}'.");

            var visible = new StringBuilder();
            var payload = new StringBuilder();
            var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
            for (var attempt = 1; ; attempt++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var attemptDirectory = Path.Combine(directory, "attempts", attempt.ToString("D3", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(attemptDirectory);
                await WriteJsonAsync(Path.Combine(attemptDirectory, "request.json"), request).ConfigureAwait(false);
                var result = await InvokeAsync(tool, arguments).ConfigureAwait(false);
                var text = string.Join('\n', result.Content.OfType<TextContentBlock>().Select(block => block.Text));
                await WriteJsonAsync(Path.Combine(attemptDirectory, "response.json"), result).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(attemptDirectory, "response.txt"), text, _cancellationToken).ConfigureAwait(false);
                // Keep the latest complete raw response even when a subsequent call fails.
                await WriteJsonAsync(Path.Combine(directory, "response.json"), result).ConfigureAwait(false);
                visible.Append(text).Append('\n');
                await File.WriteAllTextAsync(Path.Combine(directory, "response.txt"), visible.ToString(), _cancellationToken).ConfigureAwait(false);
                if (result.IsError == true)
                {
                    if (expectedErrorCode is not null && text.Contains(expectedErrorCode, StringComparison.Ordinal))
                    {
                        await File.WriteAllTextAsync(Path.Combine(directory, "payload.txt"), ResponseBody(text), _cancellationToken).ConfigureAwait(false);
                        return new ExplorationResult(visible.ToString(), ResponseBody(text), result);
                    }
                    throw new InvalidOperationException($"MCP tool '{toolName}' returned IsError=true. Inspect response.txt.");
                }

                if (text.StartsWith("Status: operation=running", StringComparison.Ordinal)
                    || text.StartsWith("Status: operation=retry", StringComparison.Ordinal))
                {
                    arguments.Remove("continuationToken");
                    if (Header(text, "operationToken=") is { } operationToken)
                        arguments["operationToken"] = JsonSerializer.SerializeToElement(operationToken);
                    else
                        arguments.Remove("operationToken");
                    var delay = int.TryParse(Header(text, "retryAfterMilliseconds: "), CultureInfo.InvariantCulture, out var milliseconds)
                        ? Math.Max(1000, milliseconds) : 1000;
                    await Task.Delay(delay, _cancellationToken).ConfigureAwait(false);
                    continue;
                }

                payload.Append(ResponseBody(text));
                if (Header(text, "continuationToken=") is { } continuationToken)
                {
                    if (!seenContinuations.Add(continuationToken))
                        throw new InvalidOperationException("The outer response repeated a continuation token.");
                    arguments.Remove("operationToken");
                    arguments.Remove("resultCursor");
                    arguments["continuationToken"] = JsonSerializer.SerializeToElement(continuationToken);
                    continue;
                }

                if (expectedErrorCode is not null)
                    throw new InvalidOperationException($"Expected {expectedErrorCode}, but MCP tool '{toolName}' succeeded.");
                await File.WriteAllTextAsync(Path.Combine(directory, "payload.txt"), payload.ToString(), _cancellationToken).ConfigureAwait(false);
                return new ExplorationResult(visible.ToString(), payload.ToString(), result);
            }
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "error.txt"), exception.ToString(), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<CallToolResult> InvokeAsync(McpServerTool tool, Dictionary<string, JsonElement> arguments)
    {
        var error = await McpArgumentValidationFilter.ValidateArgumentsAsync(tool, arguments, _cancellationToken).ConfigureAwait(false);
        if (error is not null) return error;
        // SDK InvokeAsync requires a live McpServer RequestContext. Its generated AIFunction is the same
        // transport-free binder exercised by IndexScopeContractTests; keep SDK-specific reflection here.
        var property = tool.GetType().GetProperty("AIFunction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var function = property?.GetValue(tool) as AIFunction
            ?? throw new InvalidOperationException("The MCP SDK tool binder is unavailable.");
        var boundArguments = new AIFunctionArguments(arguments.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal));
        return await function.InvokeAsync(boundArguments, _cancellationToken).ConfigureAwait(false) as CallToolResult
            ?? throw new InvalidOperationException("The MCP handler returned no CallToolResult.");
    }

    private Task WriteJsonAsync<T>(string path, T value) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), _cancellationToken);

    private static string? Header(string text, string prefix) =>
        text.Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].TrimEnd('\r');

    private static string ResponseBody(string text)
    {
        var lines = text.Split('\n');
        var first = lines.Length > 0 && lines[0].StartsWith("Status:", StringComparison.Ordinal) ? 1 : 0;
        string[] prefixes = ["snapshotId=", "analyzedScope=", "analysisCompleteness=", "resultContinuation=", "omissions=", "nextAction: ", "continuationToken="];
        while (first < lines.Length && prefixes.Any(prefix => lines[first].StartsWith(prefix, StringComparison.Ordinal))) first++;
        return string.Join('\n', lines.Skip(first));
    }
}

internal sealed record ExplorationResult(string Text, string Payload, CallToolResult Response);
