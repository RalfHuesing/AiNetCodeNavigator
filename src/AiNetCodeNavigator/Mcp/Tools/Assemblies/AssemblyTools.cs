using System.ComponentModel.DataAnnotations;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AiNetCodeNavigator.Mcp.Tools.Assemblies;

[McpServerToolType]
public sealed class AssemblyTools(NavigatorHostRuntime runtime)
{
    [McpServerTool(Name = "get_assembly_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public Task<CallToolResult> GetAssemblyContext([Required] string targetPath, string? symbolIdentifier = null,
        bool includeReferences = false, bool includeCallers = false, bool includeImpact = false, bool includeBody = false,
        bool includeClassStructure = false, [Range(1, 1000)] int maxResults = 100, [Range(1, 1000)] int maxBodyLines = 80,
        [Range(1, 200)] int maxCallers = 10, [Range(1, 3)] int depth = 1, [Range(1, 200)] int topN = 10,
        string detailLevel = "standard", [Range(512, 65536)] int maxResponseBytes = 24576,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default) =>
        NavigationToolSupport.RouteAsync(runtime, "get_assembly_context", targetPath,
            new { symbolIdentifier, includeReferences, includeCallers, includeImpact, includeBody, includeClassStructure, maxResults, maxBodyLines, maxCallers, depth, topN, detailLevel },
            operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                var context = await AssemblyContextScanner.GetAsync(new AssemblyContextRequest(target.CanonicalPath, maxResults, includeReferences), ct).ConfigureAwait(false);
                if (!context.IsSuccess) return NavigationToolSupport.Failure(context.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
                var payload = context.Value!;
                if (symbolIdentifier is null) return NavigationToolSupport.Success(payload, payload.Truncated, "Increase maxResults and repeat the query.");
                var resolved = await AssemblySymbolHandoffResolver.ResolveAsync(symbolIdentifier, ct).ConfigureAwait(false);
                if (!resolved.IsSuccess) return NavigationToolSupport.Failure(resolved.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                await using var access = resolved.Value!;
                if (!string.Equals(Path.GetFullPath(access.Origin.CanonicalPath), target.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                    return McpToolResults.InvalidArgument("The symbol handoff belongs to another assembly.", "$.symbolIdentifier", "Use a handoff produced by this targetPath.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                object? body = null;
                if (includeBody)
                {
                    var result = await AssemblySymbolBodyScanner.GetAsync(symbolIdentifier, maxBodyLines, 1, ct).ConfigureAwait(false);
                    if (result.Error is { } error) return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    body = result.Body;
                }
                return NavigationToolSupport.Success(new { assembly = payload, symbol = access.Symbol.ToDisplayString(), body }, payload.Truncated,
                    "Increase maxResults or maxBodyLines and repeat the query.");
            }, AnalysisTargetType.Assembly, cancellationToken);

    [McpServerTool(Name = "inspect_assembly", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public Task<CallToolResult> InspectAssembly([Required] string targetPath, string? @namespace = null,
        string? typeName = null, string? memberName = null, bool publicOnly = true, bool exactTypeName = false,
        string[]? memberNames = null, [Range(1, 1000)] int maxResults = 100, [Range(1, 1000)] int maxMembers = 100,
        bool? includeReferences = null, string detailLevel = "standard", [Range(512, 65536)] int maxResponseBytes = 24576,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default) =>
        NavigationToolSupport.RouteAsync(runtime, "inspect_assembly", targetPath,
            new { @namespace, typeName, memberName, publicOnly, exactTypeName, memberNames, maxResults, maxMembers, includeReferences, detailLevel },
            operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (!TryDetail(detailLevel)) return Invalid("detailLevel", "Use compact, standard, or full.");
                var result = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(target.CanonicalPath, @namespace,
                    typeName, memberName, publicOnly, maxResults, exactTypeName, memberNames, maxMembers, includeReferences), ct).ConfigureAwait(false);
                return result.IsSuccess ? NavigationToolSupport.Success(result.Value!, result.Value!.Truncated,
                    result.Value.ContinuationToken is null ? null : "Repeat the query with the returned continuationToken.")
                    : NavigationToolSupport.Failure(result.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
            }, AnalysisTargetType.Assembly, cancellationToken);

    [McpServerTool(Name = "search_assembly", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public Task<CallToolResult> SearchAssembly([Required] string targetPath, string searchKind = "text",
        string? pattern = null, bool? isRegex = null, bool caseSensitive = false, bool declarationOnly = false,
        string? kind = null, string? fileFilter = null, [Range(0, 5)] int contextLines = 0,
        [Range(1, 1000)] int maxResults = 50, [Range(1, 10000)] int maxFiles = 1000,
        string detailLevel = "standard", [Range(512, 65536)] int maxResponseBytes = 24576,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default) =>
        NavigationToolSupport.RouteAsync(runtime, "search_assembly", targetPath,
            new { searchKind, pattern, isRegex, caseSensitive, declarationOnly, kind, fileFilter, contextLines, maxResults, maxFiles, detailLevel },
            operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (!TryDetail(detailLevel)) return Invalid("detailLevel", "Use compact, standard, or full.");
                if (kind is not (null or "method" or "type" or "property")) return Invalid("kind", "Use method, type, or property.");
                var result = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(target.CanonicalPath, pattern,
                    searchKind, caseSensitive, isRegex ?? false, fileFilter, declarationOnly, contextLines, maxResults), ct).ConfigureAwait(false);
                return result.IsSuccess ? NavigationToolSupport.Success(result.Value!, result.Value!.Truncated,
                    "Increase maxResults and repeat the same query.")
                    : NavigationToolSupport.Failure(result.Error!.Value, maxResponseBytes, maxResponseTokens, "$.pattern");
            }, AnalysisTargetType.Assembly, cancellationToken);

    [McpServerTool(Name = "find_assembly_extensions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public Task<CallToolResult> FindAssemblyExtensions([Required] string targetPath, string? receiverType = null,
        string? extensionName = null, string? @namespace = null, bool includeReferences = false,
        [Range(1, 1000)] int maxResults = 100, string detailLevel = "standard",
        [Range(512, 65536)] int maxResponseBytes = 16384, [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        string? operationToken = null, string? continuationToken = null, CancellationToken cancellationToken = default) =>
        NavigationToolSupport.RouteAsync(runtime, "find_assembly_extensions", targetPath,
            new { receiverType, extensionName, @namespace, includeReferences, maxResults, detailLevel }, operationToken,
            continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (!TryDetail(detailLevel)) return Invalid("detailLevel", "Use compact, standard, or full.");
                var result = await FindAssemblyExtensionsScanner.FindAsync(new FindAssemblyExtensionsRequest(target.CanonicalPath,
                    receiverType, extensionName, @namespace, includeReferences, maxResults), ct).ConfigureAwait(false);
                return result.IsSuccess ? NavigationToolSupport.Success(result.Value!, result.Value!.Truncated,
                    "Increase maxResults and repeat the same query.")
                    : NavigationToolSupport.Failure(result.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
            }, AnalysisTargetType.Assembly, cancellationToken);

    private CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint);
    private static bool TryDetail(string value) => value is "compact" or "standard" or "full";
}
