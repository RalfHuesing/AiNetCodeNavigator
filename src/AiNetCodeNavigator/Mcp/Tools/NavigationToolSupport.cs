using System.Text.Json;
using System.Text.Json.Serialization;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp.Formatting;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Protocol;

namespace AiNetCodeNavigator.Mcp.Tools;

internal static class NavigationToolSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static CallToolResult Success(object payload, bool domainTruncated = false, string? nextAction = null)
    {
        var text = JsonSerializer.Serialize(payload, JsonOptions);
        return domainTruncated
            ? McpToolResults.DomainTruncated(text, nextAction ?? "Increase a supported result or traversal limit and repeat the query.")
            : McpToolResults.TextResult(text, isError: false);
    }

    internal static CallToolResult SuccessText(string text, bool domainTruncated = false, string? nextAction = null) =>
        domainTruncated
            ? McpToolResults.DomainTruncated(text, nextAction ?? "Increase a supported result or traversal limit and repeat the query.")
            : McpToolResults.TextResult(text, isError: false);

    internal static CallToolResult Failure(
        ResultError error,
        int maxResponseBytes,
        int? maxResponseTokens,
        string? fieldPath = null) =>
        McpToolResults.Recoverable(
            error.Code,
            error.Message,
            error.Hint ?? "Correct the input or repeat the query with a valid navigation target.",
            fieldPath: fieldPath,
            maxResponseBytes: maxResponseBytes,
            maxResponseTokens: maxResponseTokens);

    internal static async Task<CallToolResult> RouteAsync(
        NavigatorHostRuntime runtime,
        string toolName,
        string? targetPath,
        object arguments,
        string? operationToken,
        string? continuationToken,
        int maxResponseBytes,
        int? maxResponseTokens,
        Func<AnalysisTarget, CancellationToken, Task<CallToolResult>> operation,
        AnalysisTargetType? requiredType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(operation);
        var resolved = AnalysisTargetResolver.Resolve(new AnalysisTargetRequest(targetPath));
        if (!resolved.Succeeded)
        {
            var error = resolved.Error!;
            return McpToolResults.Recoverable(error.Code, error.Message, error.Hint ?? "Correct targetPath and retry.",
                context: error.Context, fieldPath: "$.targetPath",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        }

        var target = resolved.Target!;
        if (requiredType is { } expected && target.TargetType != expected)
        {
            var message = expected == AnalysisTargetType.Project
                ? "This tool requires a .sln or .slnx source solution target."
                : "This tool requires a .dll or .exe managed assembly target.";
            return McpToolResults.InvalidArgument(message, "$.targetPath",
                "Use the target type supported by this navigation tool.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        }

        var request = new LongRunningToolCallRequest(
            toolName,
            target.CanonicalPath,
            JsonSerializer.Serialize(arguments, JsonOptions),
            ct => operation(target, ct),
            operationToken,
            continuationToken,
            maxResponseBytes,
            maxResponseTokens);
        return await runtime.Operations.RunAsync(request, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<CallToolResult> WithSourceSolutionAsync(
        NavigatorHostRuntime runtime,
        AnalysisTarget target,
        Func<Solution, CancellationToken, Task<CallToolResult>> operation,
        int maxResponseBytes,
        int? maxResponseTokens,
        CancellationToken cancellationToken)
    {
        var leased = runtime.ProjectRegistry.Lease(target.CanonicalPath);
        if (!leased.Succeeded)
        {
            return McpToolResults.Recoverable(
                leased.ErrorCode ?? NavigationErrorCodes.SolutionNotFound,
                leased.ErrorMessage ?? "The source solution could not be opened.",
                "Check the solution path and repeat the query after correcting workspace loading errors.",
                context: target.CanonicalPath,
                maxResponseBytes: maxResponseBytes,
                maxResponseTokens: maxResponseTokens);
        }

        using var lease = leased.Lease!;
        var snapshot = await lease.ResidentSolution.GetCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Succeeded)
        {
            var error = snapshot.Error;
            if (error?.Retryable == true)
            {
                return McpToolResults.Loading(
                    error.Message,
                    "Wait briefly, then repeat the same targetPath and query.",
                    maxResponseBytes,
                    maxResponseTokens);
            }

            return McpToolResults.Recoverable(
                error?.ErrorCode ?? NavigationErrorCodes.SolutionNotLoaded,
                error?.Message ?? "The source solution is not loaded.",
                "Correct workspace loading errors and repeat the query.",
                context: target.CanonicalPath,
                maxResponseBytes: maxResponseBytes,
                maxResponseTokens: maxResponseTokens);
        }

        return await operation(snapshot.Solution!, cancellationToken).ConfigureAwait(false);
    }
}
