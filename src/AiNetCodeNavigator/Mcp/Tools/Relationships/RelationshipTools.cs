using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.CallTree;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Core.Hierarchy;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.Tools;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AiNetCodeNavigator.Mcp.Tools.Relationships;

[McpServerToolType]
public sealed class RelationshipTools(NavigatorHostRuntime runtime)
{
    private Func<CancellationToken, Task>? afterAssemblyCallTreeGraphBuilt;
    private Func<AssemblyNavigationSessionScope, CancellationToken, Task>? afterAssemblyClosureRootScopeOpened;
    private Func<AssemblySymbolHandoffAccess, CancellationToken, Task>? afterAssemblyClosureHandoffResolved;
    private Func<CancellationToken, Task>? afterAssemblyClosureRawDiscovery;

    internal RelationshipTools(
        NavigatorHostRuntime runtime,
        Func<CancellationToken, Task>? afterAssemblyCallTreeGraphBuilt,
        Func<AssemblyNavigationSessionScope, CancellationToken, Task>? afterAssemblyClosureRootScopeOpened,
        Func<AssemblySymbolHandoffAccess, CancellationToken, Task>? afterAssemblyClosureHandoffResolved = null,
        Func<CancellationToken, Task>? afterAssemblyClosureRawDiscovery = null)
        : this(runtime)
    {
        this.afterAssemblyCallTreeGraphBuilt = afterAssemblyCallTreeGraphBuilt;
        this.afterAssemblyClosureRootScopeOpened = afterAssemblyClosureRootScopeOpened;
        this.afterAssemblyClosureHandoffResolved = afterAssemblyClosureHandoffResolved;
        this.afterAssemblyClosureRawDiscovery = afterAssemblyClosureRawDiscovery;
    }

    [McpServerTool(Name = "get_call_tree", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Trace incoming, outgoing, or combined call relationships from a source or assembly symbol.")]
    public async Task<CallToolResult> GetCallTree([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [Required, System.ComponentModel.Description("Type, member, documentation ID, or current symbol handoff to trace.")] string symbolIdentifier,
        [System.ComponentModel.Description("Traversal direction: incoming (default), outgoing, or both.")] string direction = "incoming", [Range(1, 5), System.ComponentModel.Description("Maximum call-graph traversal depth.")] int depth = 2, [Range(1, 250), System.ComponentModel.Description("Maximum neighboring call nodes to include.")] int topN = 10,
        [System.ComponentModel.Description("Rendering: ascii (default) or mermaid.")] string format = "ascii", [System.ComponentModel.Description("Include calls to or from base class library symbols.")] bool includeBcl = false, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", [System.ComponentModel.Description("Include symbols from generated source files.")] bool includeGenerated = false,
        [System.ComponentModel.Description("Traverse references across owned assemblies when supported.")] bool includeReferences = false, [System.ComponentModel.Description("For assembly targets, include navigation/decompilation and graph-expansion diagnostics. Default false; source targets do not add this section.")] bool includeDiagnostics = false,
        [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 32768).") ] int maxResponseBytes = 32768,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null, [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (!TryDirection(direction, out var parsedDirection)) return Invalid("direction", "Use incoming, outgoing, or both.");
        if (format is not ("ascii" or "mermaid")) return Invalid("format", "Use ascii or mermaid.");
        if (!TryScope(scopeType, out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "get_call_tree", targetPath,
            new { symbolIdentifier, direction, depth, topN, format, includeBcl, scopeType, includeGenerated, includeReferences, includeDiagnostics },
            operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    if (includeReferences)
                        return await BuildAssemblyCallTreeWithClosureAsync(target, symbolIdentifier, depth, topN,
                            parsedDirection, includeBcl, scope, includeGenerated, format, includeDiagnostics, maxResponseBytes,
                            maxResponseTokens, ct).ConfigureAwait(false);
                    var accessResult = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                    if (!accessResult.IsSuccess) return NavigationToolSupport.Failure(accessResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var access = accessResult.Value!;
                    var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(access.Solution, access.Symbol,
                        depth, topN, parsedDirection, includeBcl, scope, includeGenerated, CreateAssemblyHandoffFormatter(access)), ct)
                        .ConfigureAwait(false);
                    graph = graph with
                    {
                        Nodes = graph.Nodes.Select(node => node.HandoffId is null
                            ? node
                            : node with { OwnerTargetPath = access.Origin.CanonicalPath }).ToArray(),
                    };
                    if (afterAssemblyCallTreeGraphBuilt is not null)
                        await afterAssemblyCallTreeGraphBuilt(ct).ConfigureAwait(false);
                    var body = format == "mermaid" ? CallTreeMermaidRenderer.RenderMermaid(graph) : CallGraphTextRenderer.RenderAscii(graph);
                    var identity = AnalysisSymbolIdentity.ForAssembly(access.Origin.CanonicalPath,
                        access.Origin.ContentHash, access.Generation, access.ReferenceSnapshotHash);
                    body = AppendCallTreeDiagnostics(body, graph, access.Diagnostics, includeDiagnostics);
                    var response = NavigationToolSupport.SuccessText(body, graph.Truncated,
                        graph.Truncated ? "Increase depth or topN and repeat the query." : null);
                    return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                        $"callTree(symbol={symbolIdentifier.Trim()}, depth={depth}, topN={topN}, direction={parsedDirection}, includeBcl={includeBcl}, scope={scope}, includeGenerated={includeGenerated}, format={format}, includeReferences=false)",
                        graph.Truncated ? ["graphLimit"] : []);
                }
                return await WithSource(target, async (solution, source) =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, source.Identity, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(solution, symbol.Symbol!, depth, topN, parsedDirection, includeBcl, scope, includeGenerated), ct).ConfigureAwait(false);
                var body = format == "mermaid" ? CallTreeMermaidRenderer.RenderMermaid(graph) : CallGraphTextRenderer.RenderAscii(graph);
                var response = NavigationToolSupport.SuccessText(body, graph.Truncated,
                    graph.Truncated ? "Increase depth or topN and repeat the query." : null);
                return source.WithMetadata(response,
                    $"callTree(symbol={symbolIdentifier.Trim()}, depth={depth}, topN={topN}, direction={parsedDirection}, includeBcl={includeBcl}, scope={scope}, includeGenerated={includeGenerated}, format={format})",
                    graph.Truncated ? ["depthOrTopN"] : []);
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "find_references", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Find source locations that reference a symbol, optionally traversing bounded assembly references.")]
    public async Task<CallToolResult> FindReferences([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [Required, System.ComponentModel.Description("Type, member, documentation ID, or current symbol handoff whose references should be found.")] string symbolIdentifier,
        [Range(1, 3), System.ComponentModel.Description("Maximum reference traversal depth.")] int depth = 1, [Range(1, 50), System.ComponentModel.Description("Maximum references to return.")] int maxResults = 50, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all",
        [System.ComponentModel.Description("Include matches from generated source files.")] bool includeGenerated = false, [System.ComponentModel.Description("Traverse into referenced assemblies when supported.")] bool includeReferences = false, [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16384,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null, [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (!TryScope(scopeType, out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "find_references", targetPath,
            new { symbolIdentifier, depth, maxResults, scopeType, includeGenerated, includeReferences }, operationToken,
            continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    if (includeReferences)
                    {
                        var closure = await AssemblyReferencesClosureScanner.ScanAsync(target.CanonicalPath,
                            symbolIdentifier, maxResults, depth, scope, includeGenerated, ct).ConfigureAwait(false);
                        if (closure.Error is { } error)
                            return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, closure.ErrorField);
                        var closureResponse = NavigationToolSupport.Success(closure.References!, closure.IsTruncated, closure.NextAction);
                        return NavigationToolSupport.WithAssemblyMetadata(closureResponse, closure.AnalysisIdentity!,
                            $"findReferences(symbol={symbolIdentifier.Trim()}, depth={depth}, maxResults={maxResults}, scope={scope}, includeGenerated={includeGenerated}, includeReferences=true)",
                            closure.OmissionReasons);
                    }
                    var accessResult = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                    if (!accessResult.IsSuccess) return NavigationToolSupport.Failure(accessResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var access = accessResult.Value!;
                    var result = await FindReferencesResolver.FindReferencesAsync(access.Symbol, access.Solution,
                        maxResults, depth, ct, scope: scope, includeGenerated: includeGenerated,
                        handoffFormatter: CreateAssemblyHandoffFormatter(access), ownerTargetPath: access.Origin.CanonicalPath).ConfigureAwait(false);
                    var response = NavigationToolSupport.Success(result,
                        result.IsTruncated || result.IsTruncatedByNodeLimit || result.IsDepthClamped,
                        "Increase depth or maxResults and repeat the query.");
                    var omissions = new List<string>();
                    if (result.IsTruncated) omissions.Add("maxResults");
                    if (result.IsTruncatedByNodeLimit) omissions.Add("nodeLimit");
                    if (result.IsDepthClamped) omissions.Add("depthLimit");
                    var identity = AnalysisSymbolIdentity.ForAssembly(access.Origin.CanonicalPath, access.Origin.ContentHash,
                        access.Generation, access.ReferenceSnapshotHash);
                    return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                        $"findReferences(symbol={symbolIdentifier.Trim()}, depth={depth}, maxResults={maxResults}, scope={scope}, includeGenerated={includeGenerated}, includeReferences=false)", omissions);
                }
                return await WithSource(target, async (solution, source) =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, source.Identity, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var result = await FindReferencesResolver.FindReferencesAsync(symbol.Symbol!, solution, maxResults, depth, ct,
                    scope: scope, includeGenerated: includeGenerated).ConfigureAwait(false);
                var response = NavigationToolSupport.Success(result, result.IsTruncated || result.IsTruncatedByNodeLimit || result.IsDepthClamped,
                    "Increase depth or maxResults and repeat the query.");
                var omissions = new List<string>();
                if (result.IsTruncated) omissions.Add("maxResults");
                if (result.IsTruncatedByNodeLimit) omissions.Add("nodeLimit");
                if (result.IsDepthClamped) omissions.Add("depthLimit");
                return source.WithMetadata(response,
                    $"findReferences(symbol={symbolIdentifier.Trim()}, depth={depth}, maxResults={maxResults}, scope={scope}, includeGenerated={includeGenerated})",
                    omissions.ToArray());
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "get_type_hierarchy", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Show base types, interfaces, and derived types for a selected type.")]
    public async Task<CallToolResult> GetTypeHierarchy([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [Required, System.ComponentModel.Description("Type name, documentation ID, or current type handoff whose hierarchy should be shown.")] string symbolIdentifier,
        [Range(1, 1000), System.ComponentModel.Description("Maximum hierarchy entries to return.")] int maxResults = 50, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", [System.ComponentModel.Description("Include derived types declared in generated source.")] bool includeGenerated = false,
        [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16384, [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null, [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (!TryScope(scopeType, out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "get_type_hierarchy", targetPath,
            new { symbolIdentifier, maxResults, scopeType, includeGenerated }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    var accessResult = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                    if (!accessResult.IsSuccess) return NavigationToolSupport.Failure(accessResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var access = accessResult.Value!;
                    if (access.Symbol is not INamedTypeSymbol assemblyNamed)
                        return Invalid("symbolIdentifier", "Resolve a named class, interface, or struct.");
                    var formatter = CreateAssemblyHandoffFormatter(access);
                    var assemblyResult = await TypeHierarchyScanner.ScanAsync(assemblyNamed, access.Solution, maxResults, ct,
                        scope, includeGenerated, formatter).ConfigureAwait(false);
                    if (!assemblyResult.IsSuccess)
                        return McpToolResults.InvalidArgument(assemblyResult.ErrorMessage!, "$.symbolIdentifier", "Choose a supported named type.",
                            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    var response = NavigationToolSupport.Success(assemblyResult, assemblyResult.IsTruncated,
                        "Increase maxResults and repeat the query.");
                    var identity = AnalysisSymbolIdentity.ForAssembly(access.Origin.CanonicalPath, access.Origin.ContentHash,
                        access.Generation, access.ReferenceSnapshotHash);
                    return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                        $"typeHierarchy(symbol={symbolIdentifier.Trim()}, maxResults={maxResults}, scope={scope}, includeGenerated={includeGenerated})",
                        assemblyResult.IsTruncated ? ["maxResults"] : []);
                }
                return await WithSource(target, async (solution, source) =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, source.Identity, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                if (symbol.Symbol is not INamedTypeSymbol named) return Invalid("symbolIdentifier", "Resolve a named class, interface, or struct.");
                var result = await TypeHierarchyScanner.ScanAsync(named, solution, maxResults, ct, scope, includeGenerated).ConfigureAwait(false);
                if (!result.IsSuccess) return McpToolResults.InvalidArgument(result.ErrorMessage!, "$.symbolIdentifier", "Choose a supported named type.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                var response = NavigationToolSupport.Success(result, result.IsTruncated, "Increase maxResults and repeat the query.");
                return source.WithMetadata(response,
                    $"typeHierarchy(symbol={symbolIdentifier.Trim()}, maxResults={maxResults}, scope={scope}, includeGenerated={includeGenerated})",
                    result.IsTruncated ? ["maxResults"] : []);
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "find_implementations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Find concrete type or member implementations of a selected contract or virtual member.")]
    public async Task<CallToolResult> FindImplementations([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [Required, System.ComponentModel.Description("Type, member, documentation ID, or current symbol handoff whose implementations should be found.")] string symbolIdentifier,
        [Range(1, 1000), System.ComponentModel.Description("Maximum implementations to return.")] int maxResults = 50, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", [System.ComponentModel.Description("Include implementations declared in generated source.")] bool includeGenerated = false,
        [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16384, [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null, [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (!TryScope(scopeType, out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "find_implementations", targetPath,
            new { symbolIdentifier, maxResults, scopeType, includeGenerated }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    var accessResult = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                    if (!accessResult.IsSuccess) return NavigationToolSupport.Failure(accessResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var access = accessResult.Value!;
                    var formatter = CreateAssemblyHandoffFormatter(access);
                    var assemblyResult = await FindReferencesResolver.FindImplementationsAsync(access.Symbol, access.Solution,
                        maxResults, ct, scope, includeGenerated, formatter).ConfigureAwait(false);
                    if (assemblyResult.ErrorMessage is not null)
                        return McpToolResults.InvalidArgument(assemblyResult.ErrorMessage, "$.symbolIdentifier",
                            "Use an interface, abstract/virtual member, or overridable class.",
                            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    var response = NavigationToolSupport.Success(assemblyResult, assemblyResult.IsTruncated,
                        "Increase maxResults and repeat the query.");
                    var identity = AnalysisSymbolIdentity.ForAssembly(access.Origin.CanonicalPath, access.Origin.ContentHash,
                        access.Generation, access.ReferenceSnapshotHash);
                    return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                        $"findImplementations(symbol={symbolIdentifier.Trim()}, maxResults={maxResults}, scope={scope}, includeGenerated={includeGenerated})",
                        assemblyResult.IsTruncated ? ["maxResults"] : []);
                }
                return await WithSource(target, async (solution, source) =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, source.Identity, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var result = await FindReferencesResolver.FindImplementationsAsync(symbol.Symbol!, solution, maxResults, ct, scope, includeGenerated).ConfigureAwait(false);
                if (result.ErrorMessage is not null) return McpToolResults.InvalidArgument(result.ErrorMessage, "$.symbolIdentifier", "Use an interface, abstract/virtual member, or overridable class.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                var response = NavigationToolSupport.Success(result, result.IsTruncated, "Increase maxResults and repeat the query.");
                return source.WithMetadata(response,
                    $"findImplementations(symbol={symbolIdentifier.Trim()}, maxResults={maxResults}, scope={scope}, includeGenerated={includeGenerated})",
                    result.IsTruncated ? ["maxResults"] : []);
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "get_impact", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Summarize callers affected by a source or assembly symbol.")]
    public async Task<CallToolResult> GetImpact([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [Required, System.ComponentModel.Description("Type, member, documentation ID, or current symbol handoff whose callers and effects should be summarized.")] string symbolIdentifier,
        [Range(1, 3), System.ComponentModel.Description("Maximum impact traversal depth.")] int depth = 1, [Range(1, 1000), System.ComponentModel.Description("Maximum impact entries to return.")] int maxResults = 50, [System.ComponentModel.Description("Traverse into referenced assemblies when supported.")] bool includeReferences = false,
        [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16384, [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null, [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolIdentifier)) return Invalid("symbolIdentifier", "Provide a non-empty source or assembly symbol identifier.");
        return await NavigationToolSupport.RouteAsync(runtime, "get_impact", targetPath,
            new { symbolIdentifier, depth, maxResults, includeReferences },
            operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    if (includeReferences)
                    {
                        var closureImpact = await AssemblyImpactClosureScanner.ScanAsync(target.CanonicalPath,
                            symbolIdentifier, depth, maxResults, ct).ConfigureAwait(false);
                        if (closureImpact.Error is { } error)
                            return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, closureImpact.ErrorField);
                        var closureResponse = NavigationToolSupport.Success(closureImpact.Impact!, closureImpact.IsTruncated, closureImpact.NextAction);
                        return NavigationToolSupport.WithAssemblyMetadata(closureResponse, closureImpact.AnalysisIdentity!,
                            $"impact(symbol={closureImpact.Impact!.TargetSymbol}, depth={depth}, maxResults={maxResults}, includeReferences=true)",
                            closureImpact.OmissionReasons);
                    }
                    var access = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                    if (!access.IsSuccess) return NavigationToolSupport.Failure(access.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var lease = access.Value!;
                    if (!string.Equals(Path.GetFullPath(lease.Origin.CanonicalPath), target.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                        return Invalid("symbolIdentifier", "Use a handoff produced by this targetPath.");
                    var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(lease.Symbol, lease.Solution, depth, maxResults, ct,
                        handoffFormatter: CreateAssemblyHandoffFormatter(access.Value!)).ConfigureAwait(false);
                    var incomplete = impact.IsTruncated || impact.IsTruncatedByNodeLimit || impact.IsDepthClamped;
                    var response = NavigationToolSupport.Success(impact, incomplete, "Increase depth or maxResults and repeat the query.");
                    var identity = AnalysisSymbolIdentity.ForAssembly(lease.Origin.CanonicalPath, lease.Origin.ContentHash,
                        lease.Generation, lease.ReferenceSnapshotHash);
                    var omissions = new List<string>();
                    if (impact.IsTruncated) omissions.Add("maxResults");
                    if (impact.IsTruncatedByNodeLimit) omissions.Add("nodeLimit");
                    if (impact.IsDepthClamped) omissions.Add("depthLimit");
                    return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                        $"impact(symbol={lease.Symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}, depth={depth}, maxResults={maxResults}, includeReferences=false)", omissions);
                }
                return await WithSource(target, async (solution, source) =>
                {
                    var symbol = await Resolve(solution, symbolIdentifier, source.Identity, ct).ConfigureAwait(false);
                    if (symbol.Error is not null) return NavigationToolSupport.Failure(symbol.Error.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(symbol.Symbol!, solution, depth, maxResults, ct).ConfigureAwait(false);
                    var response = NavigationToolSupport.Success(impact, impact.IsTruncated || impact.IsTruncatedByNodeLimit || impact.IsDepthClamped, "Increase depth or maxResults and repeat the query.");
                    var omissions = new List<string>();
                    if (impact.IsTruncated) omissions.Add("maxResults");
                    if (impact.IsTruncatedByNodeLimit) omissions.Add("nodeLimit");
                    if (impact.IsDepthClamped) omissions.Add("depthLimit");
                    return source.WithMetadata(response,
                        $"impact(symbol={symbolIdentifier.Trim()}, depth={depth}, maxResults={maxResults})", omissions.ToArray());
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
    }

    [McpServerTool(Name = "dependency_graph", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Trace dependencies from exactly one file path or symbol identifier in the selected target.")]
    public async Task<CallToolResult> DependencyGraph([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath,
        [System.ComponentModel.Description("Indexed source file path used as the dependency graph root; specify this or symbolIdentifier.")] string? filePath = null,
        [System.ComponentModel.Description("Type or member identifier used as the dependency graph root; specify this or filePath.")] string? symbolIdentifier = null, [System.ComponentModel.Description("Traversal direction: both (default), incoming, or outgoing.")] string direction = "both", [Range(1, 3), System.ComponentModel.Description("Maximum dependency traversal depth.")] int depth = 1,
        [Range(1, 500), System.ComponentModel.Description("Maximum dependency entries to return.")] int maxResults = 50, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", [System.ComponentModel.Description("Include dependencies from generated source files.")] bool includeGenerated = false,
        [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 24576).") ] int maxResponseBytes = 24576, [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null, [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if ((filePath is null) == (symbolIdentifier is null)) return Invalid("filePath", "Specify exactly one of filePath or symbolIdentifier.");
        if (!TryDependencyDirection(direction, out var parsedDirection)) return Invalid("direction", "Use incoming, outgoing, or both.");
        if (!TryScope(scopeType, out var parsedScope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "dependency_graph", targetPath,
            new { filePath, symbolIdentifier, direction, depth, maxResults, scopeType, includeGenerated }, operationToken,
            continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    if (symbolIdentifier is not null)
                    {
                        var accessResult = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                        if (!accessResult.IsSuccess)
                            return NavigationToolSupport.Failure(accessResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                        await using var access = accessResult.Value!;
                        var targetSymbol = access.Symbol as INamedTypeSymbol ?? access.Symbol.ContainingType;
                        if (targetSymbol is null || !targetSymbol.Locations.Any(location => location.IsInSource))
                            return McpToolResults.InvalidArgument("The handoff has no source type in this assembly.", "$.symbolIdentifier",
                                "Use a handoff for a type or member declared in the selected assembly source.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                        string targetTypeId;
                        try { targetTypeId = DependencyGraphScanner.GetSourceTypeId(access.Solution, targetSymbol); }
                        catch (ArgumentException)
                        {
                            return McpToolResults.InvalidArgument("The handoff has no source type in this assembly.", "$.symbolIdentifier",
                                "Use a handoff for a type or member declared in the selected assembly source.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                        }
                        var scan = await DependencyGraphScanner.ScanSolutionAsync(access.Solution, ct,
                            new DependencyGraphScanOptions(PageSize: maxResults, TargetTypeName: targetSymbol.ToDisplayString(),
                                TargetTypeId: targetTypeId, Direction: parsedDirection, Depth: depth, ScopeType: parsedScope,
                                IncludeGenerated: includeGenerated), CreateAssemblyHandoffFormatter(access)).ConfigureAwait(false);
                        var response = NavigationToolSupport.Success(scan, scan.IsTruncated,
                            "Increase maxResults, depth, or document coverage and repeat the query.");
                        var identity = AnalysisSymbolIdentity.ForAssembly(access.Origin.CanonicalPath, access.Origin.ContentHash,
                            access.Generation, access.ReferenceSnapshotHash);
                        return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                            $"dependencyGraph(symbol={targetSymbol.ToDisplayString()}, direction={parsedDirection}, depth={depth}, maxResults={maxResults}, scope={parsedScope}, includeGenerated={includeGenerated})",
                            DependencyGraphOmissions(scan));
                    }

                    var opened = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
                    if (!opened.IsSuccess) return NavigationToolSupport.Failure(opened.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
                    await using var scope = opened.Value!;
                    var sourceRoot = scope.Context.DecompiledProjectPaths?.DecompiledSourceRoot;
                    if (string.IsNullOrWhiteSpace(sourceRoot))
                        return McpToolResults.Recoverable(NavigationErrorCodes.AssemblyTargetUnsupported,
                            "The assembly has no materialized decompiled source tree.", "Use inspect_assembly for metadata-only navigation.",
                            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    var selectedDocument = ResolveDependencyDocument(scope.Solution, filePath!);
                    if (selectedDocument.Document is null)
                        return McpToolResults.InvalidArgument("The requested assembly source file could not be selected.", "$.filePath",
                            selectedDocument.Error!, maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    if (!IsWithinDirectory(sourceRoot, selectedDocument.Document.FilePath))
                        return McpToolResults.InvalidArgument("The requested source file is outside this assembly's decompiled source.", "$.filePath",
                            "Choose a file emitted by this assembly's source tree.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    var fileTypeIds = await DependencyGraphScanner.GetDocumentTypeIdsAsync(selectedDocument.Document, ct).ConfigureAwait(false);
                    var fileScan = await DependencyGraphScanner.ScanSolutionAsync(scope.Solution, ct,
                        new DependencyGraphScanOptions(PageSize: maxResults, TargetFilePath: filePath,
                            TargetTypeIds: fileTypeIds, Direction: parsedDirection, Depth: depth, ScopeType: parsedScope,
                            IncludeGenerated: includeGenerated), CreateAssemblyHandoffFormatter(scope.Solution, scope.Context)).ConfigureAwait(false);
                    var fileResponse = NavigationToolSupport.Success(fileScan, fileScan.IsTruncated,
                        "Increase maxResults, depth, or document coverage and repeat the query.");
                    var fileIdentity = AnalysisSymbolIdentity.ForAssembly(scope.Context.Origin.CanonicalPath,
                        scope.Context.Origin.ContentHash, scope.Context.Generation, scope.Context.ReferenceSnapshotHash);
                    return NavigationToolSupport.WithAssemblyMetadata(fileResponse, fileIdentity,
                        $"dependencyGraph(file={selectedDocument.Document.FilePath}, direction={parsedDirection}, depth={depth}, maxResults={maxResults}, scope={parsedScope}, includeGenerated={includeGenerated})",
                        DependencyGraphOmissions(fileScan));
                }

                return await WithSource(target, async (solution, source) =>
                {
                string? typeName = null;
                string? typeId = null;
                IReadOnlyCollection<string>? fileTypeIds = null;
                if (symbolIdentifier is not null)
                {
                    var resolved = await Resolve(solution, symbolIdentifier, source.Identity, ct).ConfigureAwait(false);
                    if (resolved.Error is not null) return NavigationToolSupport.Failure(resolved.Error.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    var type = resolved.Symbol is INamedTypeSymbol namedType ? namedType : resolved.Symbol!.ContainingType;
                    typeName = type?.ToDisplayString();
                    if (type is null) return McpToolResults.InvalidArgument("The symbol has no containing type.", "$.symbolIdentifier", "Choose a type or member declared in a type.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    typeId = DependencyGraphScanner.GetSourceTypeId(solution, type);
                }
                else if (filePath is not null)
                {
                    var selectedDocument = ResolveDependencyDocument(solution, filePath);
                    if (selectedDocument.Document is null)
                        return McpToolResults.InvalidArgument("The requested source file could not be selected.", "$.filePath", selectedDocument.Error!, maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    fileTypeIds = await DependencyGraphScanner.GetDocumentTypeIdsAsync(selectedDocument.Document, ct).ConfigureAwait(false);
                }
                var identity = source.Identity;
                var scan = await ScanSourceDependencyGraphAcrossDocumentsAsync(solution,
                    new DependencyGraphTraversalOptions(TargetFilePath: filePath,
                        TargetTypeName: typeName, Direction: parsedDirection, Depth: depth,
                        PageSize: maxResults, TargetTypeId: typeId, TargetTypeIds: fileTypeIds),
                    new DependencyGraphScanOptions(ScopeType: parsedScope, IncludeGenerated: includeGenerated),
                    CreateSourceHandoffFormatter(solution, identity), ct).ConfigureAwait(false);
                var response = NavigationToolSupport.Success(scan, scan.IsTruncated, "Increase maxResults, depth, or document coverage and repeat the query.");
                var omissions = new List<string>();
                if (scan.IsDepthClamped) omissions.Add("depthLimit");
                if (scan.NodeLimitReached) omissions.Add("nodeLimit");
                if (scan.DocumentLimitReached) omissions.Add("documentLimit");
                if (scan.ContinuationInputIncomplete) omissions.Add("continuationInputIncomplete");
                if (scan.Errors is { Count: > 0 }) omissions.Add("scannerErrors");
                return source.WithMetadata(response,
                    $"dependencyGraph(file={filePath?.Trim() ?? "*"}, symbol={symbolIdentifier?.Trim() ?? "*"}, direction={parsedDirection}, depth={depth}, maxResults={maxResults}, scope={parsedScope}, includeGenerated={includeGenerated})",
                    omissions.ToArray());
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
    }

    private static async Task<DependencyGraphPayload> ScanSourceDependencyGraphAcrossDocumentsAsync(
        Solution solution,
        DependencyGraphTraversalOptions traversalOptions,
        DependencyGraphScanOptions scanOptions,
        Func<ISymbol, string?> handoffFormatter,
        CancellationToken cancellationToken)
    {
        var pages = new List<DependencyGraphPayload>();
        var documentOffset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relationshipOffset = 0;
            DependencyGraphPayload? firstPage = null;
            while (true)
            {
                var page = await DependencyGraphScanner.ScanSolutionAsync(solution, cancellationToken,
                    scanOptions with
                    {
                        Offset = relationshipOffset,
                        PageSize = DependencyGraphScanner.MaximumPageSize,
                        MaxDocuments = DependencyGraphScanner.MaximumDocuments,
                        DocumentOffset = documentOffset,
                        TargetFilePath = null,
                        TargetTypeName = null,
                        TargetProject = null,
                        TargetTypeId = null,
                        TargetTypeIds = null,
                    }).ConfigureAwait(false);
                pages.Add(page);
                firstPage ??= page;
                var relationshipTotal = Math.Max(page.TotalProjectDependencyCount,
                    Math.Max(page.TotalNamespaceDependencyCount,
                        Math.Max(page.TotalFileDependencyCount, page.TotalTypeDependencyCount)));
                if (relationshipOffset + page.PageSize >= relationshipTotal) break;
                relationshipOffset += page.PageSize;
            }

            if (firstPage!.NextDocumentOffset is not int nextDocumentOffset) break;
            documentOffset = nextDocumentOffset;
        }

        var merged = DependencyGraphTraversal.MergeAndTraverse(pages, traversalOptions);
        var visibleEdges = merged.TypeDependencies ?? [];
        if (visibleEdges.Count == 0) return merged;

        var requestedTypeIds = visibleEdges.SelectMany(edge => new[] { edge.FromTypeId, edge.ToTypeId })
            .ToHashSet(StringComparer.Ordinal);
        var symbolsByTypeId = await ResolveSourceDependencyTypesAsync(solution, requestedTypeIds, cancellationToken).ConfigureAwait(false);
        return merged with
        {
            TypeDependencies = visibleEdges.Select(edge => edge with
            {
                FromHandoffId = symbolsByTypeId.TryGetValue(edge.FromTypeId, out var fromSymbol) ? handoffFormatter(fromSymbol) : null,
                ToHandoffId = symbolsByTypeId.TryGetValue(edge.ToTypeId, out var toSymbol) ? handoffFormatter(toSymbol) : null,
            }).ToArray(),
        };
    }

    private static async Task<IReadOnlyDictionary<string, INamedTypeSymbol>> ResolveSourceDependencyTypesAsync(
        Solution solution,
        HashSet<string> requestedTypeIds,
        CancellationToken cancellationToken)
    {
        var symbolsByTypeId = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var project in solution.Projects.OrderBy(project => project.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null) continue;
            VisitNamespace(compilation.Assembly.GlobalNamespace);
            if (symbolsByTypeId.Count == requestedTypeIds.Count) break;
        }
        return symbolsByTypeId;

        void VisitNamespace(INamespaceSymbol namespaceSymbol)
        {
            if (symbolsByTypeId.Count == requestedTypeIds.Count) return;
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                VisitNamespace(childNamespace);
                if (symbolsByTypeId.Count == requestedTypeIds.Count) return;
            }
            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                VisitType(type);
                if (symbolsByTypeId.Count == requestedTypeIds.Count) return;
            }
        }

        void VisitType(INamedTypeSymbol type)
        {
            if (symbolsByTypeId.Count == requestedTypeIds.Count) return;
            cancellationToken.ThrowIfCancellationRequested();
            if (type.Locations.Any(location => location.IsInSource))
            {
                var typeId = DependencyGraphScanner.GetSourceTypeId(solution, type);
                if (requestedTypeIds.Contains(typeId)) symbolsByTypeId.TryAdd(typeId, type.OriginalDefinition);
            }
            foreach (var nestedType in type.GetTypeMembers())
            {
                VisitType(nestedType);
                if (symbolsByTypeId.Count == requestedTypeIds.Count) return;
            }
        }
    }

    private static (Document? Document, string? Error) ResolveDependencyDocument(Solution solution, string filePath)
    {
        var solutionDirectory = Path.GetDirectoryName(solution.FilePath) ?? Environment.CurrentDirectory;
        string requestedPath;
        try
        {
            requestedPath = Path.GetFullPath(Path.IsPathRooted(filePath) ? filePath : Path.Combine(solutionDirectory, filePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, "Provide a valid path relative to the solution or an absolute document path.");
        }

        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var exactMatches = solution.Projects.SelectMany(project => project.Documents)
            .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
            .Where(document =>
            {
                try { return pathComparer.Equals(Path.GetFullPath(document.FilePath!), requestedPath); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
            })
            .ToList();
        if (exactMatches.Count == 1) return (exactMatches[0], null);
        if (exactMatches.Count > 1) return (null, "The path is linked into multiple projects; use a symbolIdentifier or a unique source path.");

        var suffix = filePath.Replace('\\', '/').TrimStart('.', '/');
        var suffixMatches = solution.Projects.SelectMany(project => project.Documents)
            .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
            .Where(document => document.FilePath!.Replace('\\', '/').EndsWith("/" + suffix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(document.FilePath), suffix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return suffixMatches.Count switch
        {
            1 => (suffixMatches[0], null),
            > 1 => (null, "The relative path matches multiple documents; pass a longer solution-relative path or an absolute document path."),
            _ => (null, "The path does not identify a source document in the loaded solution."),
        };
    }

    [McpServerTool(Name = "resolve_type_origin", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Resolve a type name or symbol identifier to its source or metadata assembly origin.")]
    public async Task<CallToolResult> ResolveTypeOrigin([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [System.ComponentModel.Description("Symbol identifier for the type; specify this or typeName.")] string? symbolIdentifier = null,
        [System.ComponentModel.Description("Exactly one of this type name or symbolIdentifier is required.")] string? typeName = null, [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16384,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null, [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolIdentifier) == string.IsNullOrWhiteSpace(typeName))
            return Invalid("symbolIdentifier", "Specify exactly one non-empty symbolIdentifier or typeName.");
        return await NavigationToolSupport.RouteAsync(runtime, "resolve_type_origin", targetPath,
            new { symbolIdentifier, typeName }, operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    var input = symbolIdentifier ?? typeName!;
                    AnalysisSymbolIdentity? assemblyIdentity = null;
                    if (symbolIdentifier is not null)
                    {
                        var access = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                        if (!access.IsSuccess) return NavigationToolSupport.Failure(access.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                        await using var lease = access.Value!;
                        assemblyIdentity = AnalysisSymbolIdentity.ForAssembly(lease.Origin.CanonicalPath, lease.Origin.ContentHash,
                            lease.Generation, lease.ReferenceSnapshotHash);
                        input = lease.Symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                    }
                    var result = await ResolveTypeOriginScanner.ResolveAsync(new ResolveTypeOriginRequest(target.CanonicalPath, input), ct).ConfigureAwait(false);
                    if (!result.IsSuccess) return NavigationToolSupport.Failure(result.Error!.Value, maxResponseBytes, maxResponseTokens,
                        symbolIdentifier is null ? "$.typeName" : "$.symbolIdentifier");
                    if (assemblyIdentity is null) return NavigationToolSupport.Success(result.Value!);
                    return NavigationToolSupport.WithAssemblyMetadata(NavigationToolSupport.Success(result.Value!), assemblyIdentity,
                        $"resolveTypeOrigin(input={input.Trim()})");
                }
                return await WithSource(target, async (solution, source) =>
                {
                    var identifier = symbolIdentifier ?? typeName!;
                    var resolved = await Resolve(solution, identifier, source.Identity, ct).ConfigureAwait(false);
                    if (resolved.Error is { } resolutionError && resolutionError.Code != NavigationErrorCodes.SymbolNotFound)
                        return NavigationToolSupport.Failure(resolutionError, maxResponseBytes, maxResponseTokens,
                            symbolIdentifier is null ? "$.typeName" : "$.symbolIdentifier");
                    var result = await SourceTypeOriginScanner.ResolveAsync(solution, target.CanonicalPath,
                        resolved.Error is null ? resolved.Symbol : null,
                        resolved.Error is null ? null : identifier, ct).ConfigureAwait(false);
                    return result.IsSuccess
                        ? source.WithMetadata(NavigationToolSupport.Success(result.Value!),
                            $"resolveTypeOrigin(input={identifier.Trim()})")
                        : NavigationToolSupport.Failure(result.Error!.Value, maxResponseBytes, maxResponseTokens,
                            symbolIdentifier is null ? "$.typeName" : "$.symbolIdentifier");
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
    }

    [McpServerTool(Name = "get_feature_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Summarize source callers and tests associated with a feature symbol.")]
    public async Task<CallToolResult> GetFeatureContext([Required, System.ComponentModel.Description("Absolute path to an existing source solution.")] string targetPath, [Required, System.ComponentModel.Description("Source type or member identifier whose callers and related tests should be summarized.")] string symbolIdentifier,
        [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", [System.ComponentModel.Description("Include declarations from generated source files.")] bool includeGenerated = false, [Range(1, 50), System.ComponentModel.Description("Maximum callers to return.")] int maxCallers = 10,
        [Range(1, 50), System.ComponentModel.Description("Maximum related tests to return.")] int maxTests = 10, [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 24576).") ] int maxResponseBytes = 24576,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null, [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolIdentifier))
            return McpToolResults.InvalidArgument("symbolIdentifier must be a non-empty symbol identifier.", "$.symbolIdentifier",
                "Provide a source symbol name, documentation ID, position, or current handoff ID.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (!TryScope(scopeType, out var scope)) return McpToolResults.InvalidArgument("scopeType is unsupported.", "$.scopeType", "Use all, production, or tests.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        return await NavigationToolSupport.RouteAsync(runtime, "get_feature_context", targetPath,
            new { symbolIdentifier, scopeType, includeGenerated, maxCallers, maxTests }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, ct) => await WithSource(target, async (solution, source) =>
            {
                var identity = source.Identity;
                var payload = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(solution, symbolIdentifier, maxCallers, maxTests, scope, identity, includeGenerated), ct).ConfigureAwait(false);
                if (payload?.Error is { } error) return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                var response = NavigationToolSupport.Success(payload!, payload!.CallersTruncated || payload.TestsTruncated,
                    "Increase maxCallers or maxTests and repeat the query.");
                var omissions = new List<string>();
                if (payload.CallersTruncated) omissions.Add("maxCallers");
                if (payload.TestsTruncated) omissions.Add("maxTests");
                return source.WithMetadata(response,
                    $"featureContext(symbol={symbolIdentifier.Trim()}, scope={scope}, includeGenerated={includeGenerated}, maxCallers={maxCallers}, maxTests={maxTests})",
                    omissions.ToArray());
            }, maxResponseBytes, maxResponseTokens, ct), AnalysisTargetType.Project, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "get_test_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Find source tests and related context for a selected symbol.")]
    public async Task<CallToolResult> GetTestContext([Required, System.ComponentModel.Description("Absolute path to an existing source solution.")] string targetPath, [Required, System.ComponentModel.Description("Source type or member identifier used to locate related tests.")] string symbolIdentifier,
        [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", [System.ComponentModel.Description("Include declarations from generated source files.")] bool includeGenerated = false, [Range(1, 100), System.ComponentModel.Description("Maximum related test entries to return.")] int maxResults = 30,
        [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16384, [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null, [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolIdentifier))
            return McpToolResults.InvalidArgument("symbolIdentifier must be a non-empty symbol identifier.", "$.symbolIdentifier",
                "Provide a source symbol name, documentation ID, position, or current handoff ID.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (!TryScope(scopeType, out var scope)) return McpToolResults.InvalidArgument("scopeType is unsupported.", "$.scopeType", "Use all, production, or tests.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        return await NavigationToolSupport.RouteAsync(runtime, "get_test_context", targetPath,
            new { symbolIdentifier, scopeType, includeGenerated, maxResults }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, ct) => await WithSource(target, async (solution, source) =>
            {
                var resolved = await Resolve(solution, symbolIdentifier, source.Identity, ct).ConfigureAwait(false);
                if (resolved.Error is not null) return NavigationToolSupport.Failure(resolved.Error.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                var payload = await TestRecommendationBuilder.BuildAsync(resolved.Symbol!, solution, ct, includeGenerated, scope).ConfigureAwait(false);
                var fixtures = payload.TestFixtures.Take(maxResults).ToArray();
                var shown = payload with { TestFixtures = fixtures };
                var truncated = fixtures.Length < payload.TestFixtures.Count;
                var response = NavigationToolSupport.Success(shown, truncated, "Increase maxResults and repeat the query.");
                return source.WithMetadata(response,
                    $"testContext(symbol={symbolIdentifier.Trim()}, scope={scope}, includeGenerated={includeGenerated}, maxResults={maxResults})",
                    truncated ? ["maxResults"] : []);
            }, maxResponseBytes, maxResponseTokens, ct), AnalysisTargetType.Project, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CallToolResult> WithSource(AnalysisTarget target,
        Func<Solution, NavigationToolSupport.SourceAnalysisContext, Task<CallToolResult>> operation,
        int bytes, int? tokens, CancellationToken ct) => await NavigationToolSupport.WithSourceSolutionAsync(runtime, target,
        (solution, source, _) => operation(solution, source), bytes, tokens, ct).ConfigureAwait(false);

    private static async Task<Result<AssemblySymbolHandoffAccess>> ResolveAssemblySymbolAsync(
        AnalysisTarget target, string identifier, CancellationToken ct)
    {
        var normalizedIdentifier = InputNormalizer.NormalizeSymbolIdentifier(identifier);
        if (!InputNormalizer.HasOpaqueHandoffPrefix(normalizedIdentifier)
            && !normalizedIdentifier.StartsWith("i:", StringComparison.OrdinalIgnoreCase))
        {
            var opened = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
            if (!opened.IsSuccess) return Result<AssemblySymbolHandoffAccess>.Failure(opened.Error);
            string? handoff;
            await using (var scope = opened.Value!)
            {
                var raw = await AssemblySymbolInputResolver.ResolveAsync(scope, normalizedIdentifier, ct).ConfigureAwait(false);
                if (!raw.IsSuccess) return Result<AssemblySymbolHandoffAccess>.Failure(raw.Error!);
                handoff = raw.HandoffId;
            }

            if (string.IsNullOrWhiteSpace(handoff))
                return Result<AssemblySymbolHandoffAccess>.Failure(NavigationErrorCodes.SymbolNotFound,
                    "The raw identifier did not produce an owner-bound assembly handoff.");
            identifier = handoff;
        }

        var resolved = await AssemblySymbolHandoffResolver.ResolveAsync(identifier, ct).ConfigureAwait(false);
        if (!resolved.IsSuccess) return resolved;
        if (!string.Equals(Path.GetFullPath(resolved.Value!.Origin.CanonicalPath), target.CanonicalPath, StringComparison.OrdinalIgnoreCase))
        {
            await resolved.Value.DisposeAsync().ConfigureAwait(false);
            return Result<AssemblySymbolHandoffAccess>.Failure(
                NavigationErrorCodes.TargetMismatch,
                "The symbol handoff belongs to another assembly.",
                "Use a handoff returned for this targetPath.");
        }
        return resolved;
    }

    private async Task<CallToolResult> BuildAssemblyCallTreeWithClosureAsync(
        AnalysisTarget target,
        string identifier,
        int depth,
        int topN,
        CallTreeDirection direction,
        bool includeBcl,
        SymbolScopeType scope,
        bool includeGenerated,
        string format,
        bool includeDiagnostics,
        int maxResponseBytes,
        int? maxResponseTokens,
        CancellationToken ct)
    {
        var opened = await AssemblyReferenceClosureSession.OpenAsync(target.CanonicalPath, identifier, ct,
            afterRawDiscovery: afterAssemblyClosureRawDiscovery,
            afterRootScopeOpened: afterAssemblyClosureRootScopeOpened,
            afterHandoffResolved: afterAssemblyClosureHandoffResolved).ConfigureAwait(false);
        if (opened.Error is { } openError)
            return NavigationToolSupport.Failure(openError, maxResponseBytes, maxResponseTokens, opened.ErrorField);
        await using var session = opened.Session!;
        var handoffOwnerScope = session.Owners.Single(owner => string.Equals(owner.TargetPath, session.HandoffOwnerPath,
            StringComparison.OrdinalIgnoreCase)).Scope;
        var internalRootHandoff = AssemblyHandoffFormatting.CreateInternal(handoffOwnerScope)(session.HandoffSymbol);
        if (internalRootHandoff is null)
            return NavigationToolSupport.Failure(new ResultError(NavigationErrorCodes.StaleSnapshot,
                "The selected assembly declaration no longer resolves to source in its owner assembly.",
                "Repeat find_symbol for the current owner target and retry."), maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
        var build = await AssemblyCallTreeClosureBuilder.BuildAsync(session, internalRootHandoff, depth, topN,
            direction, includeBcl, scope, includeGenerated, ct).ConfigureAwait(false);
        var body = format == "mermaid" ? CallTreeMermaidRenderer.RenderMermaid(build.Graph) : CallGraphTextRenderer.RenderAscii(build.Graph);
        body = AppendCallTreeDiagnostics(body, build.Graph, build.Diagnostics, includeDiagnostics);
        var nextAction = build.TraversalLimited
                ? "Some reachable owner symbols could not be mapped or expanded within the bounded reference closure; inspect unresolved or unsupported references, then repeat the query."
                : build.ClosureIncomplete
                    ? "The bounded reference-source closure is incomplete; inspect unresolved or unsupported references, then repeat the query."
                    : build.Graph.Truncated ? "Increase topN or reduce the graph scope and repeat the query." : null;
        var response = NavigationToolSupport.SuccessText(body, build.Graph.Truncated, nextAction);
        var omissions = new List<string>();
        if (build.TraversalLimited) omissions.Add("traversalLimit");
        if (build.ClosureIncomplete) omissions.Add("referenceClosureIncomplete");
        if (build.Graph.Truncated && omissions.Count == 0) omissions.Add("graphLimit");
        return NavigationToolSupport.WithAssemblyMetadata(response, session.RootAnalysisIdentity,
            $"callTree(symbol={identifier.Trim()}, depth={depth}, topN={topN}, direction={direction}, includeBcl={includeBcl}, scope={scope}, includeGenerated={includeGenerated}, format={format}, includeReferences=true)",
            omissions);
    }

    private static IReadOnlyList<string> DependencyGraphOmissions(DependencyGraphPayload payload)
    {
        var omissions = new List<string>();
        if (payload.IsDepthClamped) omissions.Add("depthLimit");
        if (payload.NodeLimitReached) omissions.Add("nodeLimit");
        if (payload.DocumentLimitReached) omissions.Add("documentLimit");
        if (payload.ContinuationInputIncomplete) omissions.Add("continuationInputIncomplete");
        if (payload.Errors is { Count: > 0 }) omissions.Add("scannerErrors");
        if (payload.HasMoreProjectDependencies || payload.HasMoreNamespaceDependencies
            || payload.HasMoreFileDependencies || payload.HasMoreTypeDependencies) omissions.Add("maxResults");
        return omissions;
    }

    private static Func<ISymbol, string?> CreateAssemblyHandoffFormatter(AssemblySymbolHandoffAccess access)
        => CreateAssemblyHandoffFormatter(access.Solution, access.Origin.CanonicalPath, access.Origin.ContentHash,
            access.Generation, access.ReferenceSnapshotHash, access.DecompiledProjectPaths?.DecompiledSourceRoot,
            access.Assembly, access.Compilation);

    private static Func<ISymbol, string?> CreateSourceHandoffFormatter(Solution solution, AnalysisSymbolIdentity? identity)
        => symbol =>
        {
            var internalId = identity?.FormatHandoff(symbol, solution);
            return internalId is null ? null : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalId);
        };

    private static Func<ISymbol, string?> CreateAssemblyHandoffFormatter(Solution solution, AssemblyContext context)
        => CreateAssemblyHandoffFormatter(solution, context.Origin.CanonicalPath, context.Origin.ContentHash,
            context.Generation, context.ReferenceSnapshotHash, context.DecompiledProjectPaths?.DecompiledSourceRoot,
            context.Assembly, context.Compilation);

    private static Func<ISymbol, string?> CreateAssemblyHandoffFormatter(Solution solution, string canonicalPath,
        string contentHash, long generation, string referenceSnapshotHash, string? sourceRoot,
        IAssemblySymbol assembly, Compilation compilation)
    {
        var internalFormatter = AssemblyHandoffFormatting.CreateInternal(solution, canonicalPath, contentHash,
            generation, referenceSnapshotHash, sourceRoot, assembly, compilation);
        return symbol => AssemblyHandoffFormatting.Externalize(internalFormatter(symbol));
    }

    private static bool IsWithinDirectory(string rootDirectory, string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath)) return false;
        try
        {
            var root = Path.GetFullPath(rootDirectory);
            var candidate = Path.GetFullPath(candidatePath);
            var relative = Path.GetRelativePath(root, candidate);
            return !Path.IsPathRooted(relative)
                && !string.Equals(relative, "..", StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static async Task<(ISymbol? Symbol, ResultError? Error)> Resolve(Solution solution, string identifier,
        AnalysisSymbolIdentity sourceIdentity, CancellationToken ct)
    {
        var result = await SourceSymbolResolver.ResolveAsync(solution, identifier, sourceIdentity, ct).ConfigureAwait(false);
        return result.IsSuccess ? (result.Symbol, null) : (null, result.Error);
    }

    private static string AppendCallTreeDiagnostics(string body, CallGraphPayload graph,
        IEnumerable<string> sessionDiagnostics, bool includeDiagnostics)
    {
        var diagnostics = sessionDiagnostics.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).ToList();
        if (graph.HiddenEdgeCount > 0)
            diagnostics.Add($"The graph cap omitted {graph.HiddenEdgeCount} call edge(s).");
        if (graph.PendingNodeCount > 0)
            diagnostics.Add($"{graph.PendingNodeCount} call node(s) remain unexplored.");
        if (graph.Truncated && diagnostics.Count == 0)
            diagnostics.Add("Call graph expansion is incomplete.");

        var output = new StringBuilder(body).AppendLine().AppendLine().AppendLine($"Diagnostics count: {diagnostics.Count}");
        if (!includeDiagnostics) return output.ToString().TrimEnd();

        output.AppendLine().AppendLine("## Diagnostics");
        if (diagnostics.Count == 0) output.AppendLine("- No navigation or assembly-resolution diagnostics.");
        else foreach (var diagnostic in diagnostics) output.AppendLine($"- {diagnostic}");
        return output.ToString().TrimEnd();
    }

    private static bool TryScope(string value, out SymbolScopeType scope)
    {
        scope = value switch { "all" => SymbolScopeType.All, "production" => SymbolScopeType.Production, "tests" => SymbolScopeType.Tests, _ => (SymbolScopeType)(-1) };
        return Enum.IsDefined(scope);
    }

    private static bool TryDirection(string value, out CallTreeDirection direction)
    {
        direction = value switch { "incoming" => CallTreeDirection.Incoming, "outgoing" => CallTreeDirection.Outgoing, "both" => CallTreeDirection.Both, _ => (CallTreeDirection)(-1) };
        return Enum.IsDefined(direction);
    }

    private static bool TryDependencyDirection(string value, out DependencyGraphDirection direction)
    {
        direction = value switch { "incoming" => DependencyGraphDirection.Incoming, "outgoing" => DependencyGraphDirection.Outgoing, "both" => DependencyGraphDirection.Both, _ => (DependencyGraphDirection)(-1) };
        return Enum.IsDefined(direction);
    }


}
