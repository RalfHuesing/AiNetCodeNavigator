using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
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
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static CallToolResult Success(object payload, bool domainTruncated = false, string? nextAction = null)
    {
        var text = JsonSerializer.Serialize(payload, JsonOptions);
        return domainTruncated
            ? McpToolResults.DomainTruncated(text, nextAction ?? "Increase a supported result or traversal limit and repeat the query.")
            : McpToolResults.TextResult(text, isError: false);
    }

    internal static CallToolResult SuccessCompact(object payload, bool domainTruncated = false, string? nextAction = null)
    {
        var compactJson = JsonSerializer.Serialize(payload, CompactJsonOptions);
        using var document = JsonDocument.Parse(compactJson);
        var text = FormatCompactJson(document.RootElement);
        return domainTruncated
            ? McpToolResults.DomainTruncated(text, nextAction ?? "Increase a supported result or traversal limit and repeat the query.")
            : McpToolResults.TextResult(text, isError: false);
    }

    private static string FormatCompactJson(JsonElement root)
    {
        var output = new StringBuilder();
        WriteCompactJson(root, output);
        return output.ToString();
    }

    private static void WriteCompactJson(JsonElement element, StringBuilder output)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                output.Append('{');
                var firstProperty = true;
                foreach (var property in element.EnumerateObject())
                {
                    if (!firstProperty) output.Append(',');
                    output.Append('\n').Append(JsonSerializer.Serialize(property.Name, CompactJsonOptions)).Append(':');
                    WriteCompactJson(property.Value, output);
                    firstProperty = false;
                }
                if (!firstProperty) output.Append('\n');
                output.Append('}');
                break;
            case JsonValueKind.Array:
                output.Append('[');
                var firstItem = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem) output.Append(',');
                    output.Append('\n');
                    WriteCompactJson(item, output);
                    firstItem = false;
                }
                if (!firstItem) output.Append('\n');
                output.Append(']');
                break;
            default:
                output.Append(element.GetRawText());
                break;
        }
    }

    internal static CallToolResult SuccessText(string text, bool domainTruncated = false, string? nextAction = null) =>
        domainTruncated
            ? McpToolResults.DomainTruncated(text, nextAction ?? "Increase a supported result or traversal limit and repeat the query.")
            : McpToolResults.TextResult(text, isError: false);

    internal static (T[] Items, string? NextCursor, CallToolResult? Error) PageResults<T>(
        IReadOnlyList<T> entries, int pageSize, string? cursor, string binding,
        int maxResponseBytes, int? maxResponseTokens, string fieldPath = "$.resultCursor")
    {
        var status = BoundResultCursor.ReadOffset(cursor, binding, out var offset);
        if (status == BoundResultCursor.CursorStatus.InvalidFormat)
            return ([], null, McpToolResults.InvalidArgument("resultCursor is malformed.", fieldPath,
                "Use the opaque resultCursor returned for this exact target, snapshot, query, and result section.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        if (status == BoundResultCursor.CursorStatus.StaleBinding)
            return ([], null, McpToolResults.Recoverable("STALE_SNAPSHOT",
                "resultCursor belongs to a different target, snapshot, query, or result section.",
                "Repeat the query against the current snapshot to obtain a fresh cursor.", fieldPath: fieldPath,
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        if (offset > entries.Count)
            return ([], null, McpToolResults.InvalidArgument("resultCursor points beyond the end of the result list.", fieldPath,
                "Use a cursor returned by an earlier page for this result section.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        var page = BoundResultCursor.Page(entries, offset, pageSize, binding);
        return (page.Items, page.NextCursor, null);
    }

    internal static CallToolResult WithAssemblyMetadata(CallToolResult response, AnalysisSymbolIdentity identity,
        string analyzedScope, IReadOnlyList<string>? omissionReasons = null, bool resultContinuationAvailable = false)
    {
        if (response.IsError == true) return response;
        if (!identity.IsAssembly) throw new ArgumentException("The analysis identity must belong to an assembly.", nameof(identity));
        var reasons = omissionReasons ?? Array.Empty<string>();
        var metadata = new NavigationAnalysisMetadata(NavigationAnalysisMetadata.CreateSnapshotId("assembly", identity.ContentHash), analyzedScope,
            reasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            reasons.Count == 0 ? "complete" : "partial", resultContinuationAvailable);
        response.Meta ??= new JsonObject();
        if (response.Meta["navigationAnalysis"] is JsonNode existing)
        {
            var owner = existing.Deserialize<NavigationAnalysisMetadata>(JsonOptions);
            if (owner is null || !string.Equals(owner.SnapshotId, metadata.SnapshotId, StringComparison.Ordinal))
                throw new InvalidOperationException("Assembly result metadata does not match the assembly analysis snapshot.");
            return response;
        }
        response.Meta["navigationAnalysis"] = JsonSerializer.SerializeToNode(metadata, JsonOptions);
        return response;
    }

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
        ArgumentNullException.ThrowIfNull(operation);
        return await RouteAsync(runtime, toolName, targetPath, arguments, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens, (target, _, token) => operation(target, token), requiredType, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<CallToolResult> RouteAsync(
        NavigatorHostRuntime runtime,
        string toolName,
        string? targetPath,
        object arguments,
        string? operationToken,
        string? continuationToken,
        int maxResponseBytes,
        int? maxResponseTokens,
        Func<AnalysisTarget, string?, CancellationToken, Task<CallToolResult>> operation,
        AnalysisTargetType? requiredType,
        CancellationToken cancellationToken,
        string? resultCursor = null,
        string? resultSection = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(operation);

        if (continuationToken is not null)
        {
            if (operationToken is not null || resultCursor is not null)
            {
                return McpToolResults.InvalidArgument(
                    "Outer response pages cannot be combined with polling or domain result cursors.",
                    "$.continuationToken",
                    "Read the outer response page by itself, then use resultCursor on the completed payload.",
                    maxResponseBytes: maxResponseBytes,
                    maxResponseTokens: maxResponseTokens);
            }

            var requestedPath = targetPath?.Trim();
            if (string.IsNullOrWhiteSpace(requestedPath) || !Path.IsPathFullyQualified(requestedPath)
                || requestedPath.Contains('*') || requestedPath.Contains('?'))
            {
                return McpToolResults.InvalidArgument("The original absolute target path is required.", "$.targetPath",
                    "Repeat the continuation with the original absolute target path.",
                    maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            }

            string canonicalPath;
            try { canonicalPath = Path.GetFullPath(requestedPath); }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                return McpToolResults.InvalidArgument("The target path is not valid.", "$.targetPath",
                    "Repeat the continuation with the original absolute target path.",
                    maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            }
            if (Path.GetExtension(canonicalPath).ToLowerInvariant() is not (".sln" or ".slnx" or ".dll" or ".exe"))
            {
                return McpToolResults.InvalidArgument("The original target path must identify a supported navigation file.", "$.targetPath",
                    "Repeat the continuation with the original absolute .sln, .slnx, .dll or .exe target path.",
                    maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            }

            return await runtime.Operations.RunAsync(new LongRunningToolCallRequest(
                toolName,
                canonicalPath,
                JsonSerializer.Serialize(arguments, JsonOptions),
                (_, _) => Task.FromResult(McpToolResults.Error("UNUSED_CONTINUATION_OPERATION", "An outer response page cannot start new work.")),
                ContinuationToken: continuationToken,
                MaxResponseBytes: maxResponseBytes,
                MaxResponseTokens: maxResponseTokens,
                ResultSection: resultSection), cancellationToken).ConfigureAwait(false);
        }

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
            (coreCursor, ct) => operation(target, coreCursor, ct),
            operationToken,
            continuationToken,
            maxResponseBytes,
            maxResponseTokens,
            DomainCursor: resultCursor,
            ResultSection: resultSection);
        return await runtime.Operations.RunAsync(request, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<CallToolResult> WithSourceSolutionAsync(
        NavigatorHostRuntime runtime,
        AnalysisTarget target,
        Func<Solution, SourceAnalysisContext, CancellationToken, Task<CallToolResult>> operation,
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

        var sourceSolution = snapshot.Solution!;
        var identity = await AnalysisSymbolIdentity.ForSourceAsync(sourceSolution, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The loaded source solution has no analysis identity.");
        return await operation(sourceSolution,
            new SourceAnalysisContext(identity, snapshot.ConfiguredTargetFrameworks), cancellationToken).ConfigureAwait(false);
    }

    internal sealed class SourceAnalysisContext(AnalysisSymbolIdentity identity,
        IReadOnlyDictionary<string, AiNetCodeNavigator.Core.Workspace.ConfiguredTargetFrameworks>? configuredTargetFrameworks = null)
    {
        internal AnalysisSymbolIdentity Identity { get; } = identity;
        internal IReadOnlyDictionary<string, AiNetCodeNavigator.Core.Workspace.ConfiguredTargetFrameworks>? ConfiguredTargetFrameworks { get; } = configuredTargetFrameworks;

        internal CallToolResult WithMetadata(CallToolResult response, string analyzedScope, string[]? omissionReasons = null,
            bool resultContinuationAvailable = false, string? snapshotContentHash = null)
        {
            if (response.IsError == true) return response;
            var reasons = omissionReasons ?? Array.Empty<string>();
            var metadata = new NavigationAnalysisMetadata(NavigationAnalysisMetadata.CreateSnapshotId("source", snapshotContentHash ?? Identity.ContentHash), analyzedScope,
                reasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                reasons.Length == 0 ? "complete" : "partial", resultContinuationAvailable);
            response.Meta ??= new JsonObject();
            if (response.Meta["navigationAnalysis"] is JsonNode existing)
            {
                var owner = existing.Deserialize<NavigationAnalysisMetadata>(JsonOptions);
                if (owner is null || !string.Equals(owner.SnapshotId, metadata.SnapshotId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Source result metadata does not match the source analysis snapshot.");
                return response;
            }
            response.Meta["navigationAnalysis"] = JsonSerializer.SerializeToNode(metadata, JsonOptions);
            return response;
        }

        internal string CreateIndexScopeSnapshotHash()
        {
            var configured = ConfiguredTargetFrameworks ?? new Dictionary<string, AiNetCodeNavigator.Core.Workspace.ConfiguredTargetFrameworks>();
            var metadata = string.Join("\n", configured.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Key}\0{entry.Value.IsKnown}\0{string.Join(";", entry.Value.Values)}"));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Identity.ContentHash}\0{metadata}")));
        }
    }
}
