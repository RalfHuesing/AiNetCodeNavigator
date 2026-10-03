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
    internal Action<string>? BeforeContextSectionForTesting { get; set; }
    internal Action<string>? BeforeAssemblyContextOwnerOpenForTesting { get; set; }

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
        [Range(1, 3), System.ComponentModel.Description("Maximum reference traversal depth.")] int depth = 1, [Range(1, int.MaxValue), System.ComponentModel.Description("Page size for matching references.")] int maxResults = 50, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all",
        [System.ComponentModel.Description("Include matches from generated source files.")] bool includeGenerated = false, [System.ComponentModel.Description("Traverse into referenced assemblies when supported.")] bool includeReferences = false, [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16384,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null, [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque cursor for the next page of the complete reference list.")] string? resultCursor = null, CancellationToken cancellationToken = default)
    {
        if (!TryScope(scopeType, out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "find_references", targetPath,
            new { symbolIdentifier, depth, scopeType, includeGenerated, includeReferences, maxResults }, operationToken,
            continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, coreCursor, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    if (includeReferences)
                    {
                        var closure = await AssemblyReferencesClosureScanner.ScanAsync(target.CanonicalPath,
                            symbolIdentifier, int.MaxValue, depth, scope, includeGenerated, ct).ConfigureAwait(false);
                        if (closure.Error is { } error)
                            return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, closure.ErrorField);
                        var closureIdentity = closure.AnalysisIdentity!;
                        var closureBinding = BoundResultCursor.CreateBinding(target.CanonicalPath,
                            closureIdentity.ContentHash,
                            "find_references.references", symbolIdentifier.Trim(), depth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            scope.ToString(), includeGenerated.ToString(), "includeReferences=true",
                            maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        var closurePage = NavigationToolSupport.PageResults(closure.References!.References, maxResults, coreCursor,
                            closureBinding, maxResponseBytes, maxResponseTokens);
                        if (closurePage.Error is not null) return closurePage.Error;
                        var closureResponse = NavigationToolSupport.Success(new { References = closurePage.Items,
                            TotalCount = closure.References!.TotalCount, ReturnedCount = closurePage.Items.Length,
                            closure.References.RequestedDepth, closure.References.EffectiveDepth,
                            closure.References.VisitedSymbolCount, closure.References.IsTruncatedByNodeLimit,
                            closure.References.IsDepthClamped, closure.References.EffectiveNodeLimit,
                            ResultCursor = closurePage.NextCursor }, closure.IsTruncated, closure.NextAction);
                        return NavigationToolSupport.WithAssemblyMetadata(closureResponse, closure.AnalysisIdentity!,
                            $"findReferences(symbol={symbolIdentifier.Trim()}, requestedDepth={closure.References.RequestedDepth}, effectiveDepth={closure.References.EffectiveDepth}, visitedSymbols={closure.References.VisitedSymbolCount}, nodeLimit={closure.References.EffectiveNodeLimit}, pageSize={maxResults}, scope={scope}, includeGenerated={includeGenerated}, includeReferences=true)",
                            (closure.OmissionReasons ?? []).Where(reason => reason != "maxResults").ToArray(), closurePage.NextCursor is not null);
                    }
                    var accessResult = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                    if (!accessResult.IsSuccess) return NavigationToolSupport.Failure(accessResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var access = accessResult.Value!;
                    var result = await FindReferencesResolver.FindReferencesAsync(access.Symbol, access.Solution,
                        int.MaxValue, depth, ct, scope: scope, includeGenerated: includeGenerated,
                        handoffFormatter: CreateAssemblyHandoffFormatter(access), ownerTargetPath: access.Origin.CanonicalPath).ConfigureAwait(false);
                    var identity = AnalysisSymbolIdentity.ForAssembly(access.Origin.CanonicalPath, access.Origin.ContentHash,
                        access.Generation, access.ReferenceSnapshotHash);
                    var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, identity.ContentHash + "|" + access.ReferenceSnapshotHash,
                        "find_references.references", symbolIdentifier.Trim(), depth.ToString(System.Globalization.CultureInfo.InvariantCulture), scope.ToString(), includeGenerated.ToString(),
                        maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var page = NavigationToolSupport.PageResults(result.References, maxResults, coreCursor, binding,
                        maxResponseBytes, maxResponseTokens);
                    if (page.Error is not null) return page.Error;
                    var response = NavigationToolSupport.Success(new { result.TargetSymbolName, result.TargetKind, References = page.Items,
                        result.TotalCount, ReturnedCount = page.Items.Length, result.RequestedDepth, result.EffectiveDepth,
                        result.VisitedSymbolCount, result.IsTruncatedByNodeLimit, result.IsDepthClamped, result.EffectiveNodeLimit,
                        ResultCursor = page.NextCursor }, result.IsTruncatedByNodeLimit || result.IsDepthClamped,
                        result.IsTruncatedByNodeLimit
                            ? "Narrow the source scope or choose a supported traversal depth; the symbol-visit budget is fixed."
                            : result.IsDepthClamped ? "Choose a supported traversal depth up to three and repeat the query." : null);
                    var omissions = new List<string>();
                    if (result.IsTruncatedByNodeLimit) omissions.Add("nodeLimit");
                    if (result.IsDepthClamped) omissions.Add("depthLimit");
                    return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                        $"findReferences(symbol={symbolIdentifier.Trim()}, requestedDepth={result.RequestedDepth}, effectiveDepth={result.EffectiveDepth}, visitedSymbols={result.VisitedSymbolCount}, nodeLimit={result.EffectiveNodeLimit}, pageSize={maxResults}, scope={scope}, includeGenerated={includeGenerated}, includeReferences=false)", omissions, page.NextCursor is not null);
                }
                return await WithSource(target, async (solution, source) =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, source.Identity, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var result = await FindReferencesResolver.FindReferencesAsync(symbol.Symbol!, solution, int.MaxValue, depth, ct,
                    scope: scope, includeGenerated: includeGenerated).ConfigureAwait(false);
                var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, source.Identity.ContentHash,
                    "find_references.references", symbolIdentifier.Trim(), depth.ToString(System.Globalization.CultureInfo.InvariantCulture), scope.ToString(), includeGenerated.ToString(),
                    maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var page = NavigationToolSupport.PageResults(result.References, maxResults, coreCursor, binding,
                    maxResponseBytes, maxResponseTokens);
                if (page.Error is not null) return page.Error;
                var response = NavigationToolSupport.Success(new { result.TargetSymbolName, result.TargetKind, References = page.Items,
                    result.TotalCount, ReturnedCount = page.Items.Length, result.RequestedDepth, result.EffectiveDepth,
                    result.VisitedSymbolCount, result.IsTruncatedByNodeLimit, result.IsDepthClamped, result.EffectiveNodeLimit,
                    ResultCursor = page.NextCursor }, result.IsTruncatedByNodeLimit || result.IsDepthClamped,
                    result.IsTruncatedByNodeLimit
                        ? "Narrow the source scope or choose a supported traversal depth; the symbol-visit budget is fixed."
                        : result.IsDepthClamped ? "Choose a supported traversal depth up to three and repeat the query." : null);
                var omissions = new List<string>();
                if (result.IsTruncatedByNodeLimit) omissions.Add("nodeLimit");
                if (result.IsDepthClamped) omissions.Add("depthLimit");
                return source.WithMetadata(response,
                    $"findReferences(symbol={symbolIdentifier.Trim()}, requestedDepth={result.RequestedDepth}, effectiveDepth={result.EffectiveDepth}, visitedSymbols={result.VisitedSymbolCount}, nodeLimit={result.EffectiveNodeLimit}, pageSize={maxResults}, scope={scope}, includeGenerated={includeGenerated})",
                    omissions.ToArray(), page.NextCursor is not null);
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken, resultCursor, "find_references.references");

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "get_type_hierarchy", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Show base types, interfaces, and derived types for a selected type.")]
    public async Task<CallToolResult> GetTypeHierarchy([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [Required, System.ComponentModel.Description("Type name, documentation ID, or current type handoff whose hierarchy should be shown.")] string symbolIdentifier,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Page size for derived types.")] int maxResults = 50, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", [System.ComponentModel.Description("Include derived types declared in generated source.")] bool includeGenerated = false,
        [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16384, [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null, [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque cursor for the next page of derived types.")] string? resultCursor = null, CancellationToken cancellationToken = default)
    {
        if (!TryScope(scopeType, out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "get_type_hierarchy", targetPath,
            new { symbolIdentifier, scopeType, includeGenerated, maxResults }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, coreCursor, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    var accessResult = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                    if (!accessResult.IsSuccess) return NavigationToolSupport.Failure(accessResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var access = accessResult.Value!;
                    if (access.Symbol is not INamedTypeSymbol assemblyNamed)
                        return Invalid("symbolIdentifier", "Resolve a named class, interface, or struct.");
                    var formatter = CreateAssemblyHandoffFormatter(access);
                    var assemblyResult = await TypeHierarchyScanner.ScanAsync(assemblyNamed, access.Solution, int.MaxValue, ct,
                        scope, includeGenerated, formatter).ConfigureAwait(false);
                    if (!assemblyResult.IsSuccess)
                        return McpToolResults.InvalidArgument(assemblyResult.ErrorMessage!, "$.symbolIdentifier", "Choose a supported named type.",
                            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    var identity = AnalysisSymbolIdentity.ForAssembly(access.Origin.CanonicalPath, access.Origin.ContentHash,
                        access.Generation, access.ReferenceSnapshotHash);
                    var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, identity.ContentHash + "|" + access.ReferenceSnapshotHash,
                        "get_type_hierarchy.subtypes", symbolIdentifier.Trim(), scope.ToString(), includeGenerated.ToString(),
                        maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var page = NavigationToolSupport.PageResults(assemblyResult.Subtypes, maxResults, coreCursor, binding,
                        maxResponseBytes, maxResponseTokens);
                    if (page.Error is not null) return page.Error;
                    var response = NavigationToolSupport.Success(new { assemblyResult.TypeName, assemblyResult.BaseTypes,
                        assemblyResult.Interfaces, assemblyResult.SubtypesHeading, Subtypes = page.Items,
                        assemblyResult.TotalSubtypes, ReturnedSubtypes = page.Items.Length,
                        IsTruncated = assemblyResult.IsTruncated, ResultCursor = page.NextCursor }, assemblyResult.IsTruncated,
                        assemblyResult.IsTruncated ? "Increase traversal coverage and repeat the query." : null);
                    return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                        $"typeHierarchy(symbol={symbolIdentifier.Trim()}, pageSize={maxResults}, scope={scope}, includeGenerated={includeGenerated})",
                        [], page.NextCursor is not null);
                }
                return await WithSource(target, async (solution, source) =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, source.Identity, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                if (symbol.Symbol is not INamedTypeSymbol named) return Invalid("symbolIdentifier", "Resolve a named class, interface, or struct.");
                var result = await TypeHierarchyScanner.ScanAsync(named, solution, int.MaxValue, ct, scope, includeGenerated).ConfigureAwait(false);
                if (!result.IsSuccess) return McpToolResults.InvalidArgument(result.ErrorMessage!, "$.symbolIdentifier", "Choose a supported named type.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, source.Identity.ContentHash,
                    "get_type_hierarchy.subtypes", symbolIdentifier.Trim(), scope.ToString(), includeGenerated.ToString(),
                    maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var page = NavigationToolSupport.PageResults(result.Subtypes, maxResults, coreCursor, binding,
                    maxResponseBytes, maxResponseTokens);
                if (page.Error is not null) return page.Error;
                var response = NavigationToolSupport.Success(new { result.TypeName, result.BaseTypes, result.Interfaces,
                    result.SubtypesHeading, Subtypes = page.Items, result.TotalSubtypes, ReturnedSubtypes = page.Items.Length,
                    IsTruncated = result.IsTruncated, ResultCursor = page.NextCursor }, result.IsTruncated,
                    result.IsTruncated ? "Increase traversal coverage and repeat the query." : null);
                return source.WithMetadata(response,
                    $"typeHierarchy(symbol={symbolIdentifier.Trim()}, pageSize={maxResults}, scope={scope}, includeGenerated={includeGenerated})",
                    [], page.NextCursor is not null);
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken, resultCursor, "get_type_hierarchy.subtypes");

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "find_implementations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Find concrete type or member implementations of a selected contract or virtual member.")]
    public async Task<CallToolResult> FindImplementations([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [Required, System.ComponentModel.Description("Type, member, documentation ID, or current symbol handoff whose implementations should be found.")] string symbolIdentifier,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Page size for matching implementations.")] int maxResults = 50, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", [System.ComponentModel.Description("Include implementations declared in generated source.")] bool includeGenerated = false,
        [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16384, [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null, [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque cursor for the next page of the complete implementation list.")] string? resultCursor = null, CancellationToken cancellationToken = default)
    {
        if (!TryScope(scopeType, out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "find_implementations", targetPath,
            new { symbolIdentifier, scopeType, includeGenerated, maxResults }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, coreCursor, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    var accessResult = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                    if (!accessResult.IsSuccess) return NavigationToolSupport.Failure(accessResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var access = accessResult.Value!;
                    var formatter = CreateAssemblyHandoffFormatter(access);
                    var assemblyResult = await FindReferencesResolver.FindImplementationsAsync(access.Symbol, access.Solution,
                        int.MaxValue, ct, scope, includeGenerated, formatter).ConfigureAwait(false);
                    if (assemblyResult.ErrorMessage is not null)
                        return McpToolResults.InvalidArgument(assemblyResult.ErrorMessage, "$.symbolIdentifier",
                            "Use an interface, abstract/virtual member, or overridable class.",
                            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    var identity = AnalysisSymbolIdentity.ForAssembly(access.Origin.CanonicalPath, access.Origin.ContentHash,
                        access.Generation, access.ReferenceSnapshotHash);
                    var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, identity.ContentHash + "|" + access.ReferenceSnapshotHash,
                        "find_implementations.implementations", symbolIdentifier.Trim(), scope.ToString(), includeGenerated.ToString(),
                        maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var page = NavigationToolSupport.PageResults(assemblyResult.Implementations, maxResults, coreCursor, binding,
                        maxResponseBytes, maxResponseTokens);
                    if (page.Error is not null) return page.Error;
                    var response = NavigationToolSupport.Success(new { assemblyResult.TargetSymbolName, assemblyResult.TargetKind,
                        Implementations = page.Items, assemblyResult.TotalCount, ReturnedCount = page.Items.Length,
                        ResultCursor = page.NextCursor });
                    return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                        $"findImplementations(symbol={symbolIdentifier.Trim()}, pageSize={maxResults}, scope={scope}, includeGenerated={includeGenerated})",
                        [], page.NextCursor is not null);
                }
                return await WithSource(target, async (solution, source) =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, source.Identity, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var result = await FindReferencesResolver.FindImplementationsAsync(symbol.Symbol!, solution, int.MaxValue, ct, scope, includeGenerated).ConfigureAwait(false);
                if (result.ErrorMessage is not null) return McpToolResults.InvalidArgument(result.ErrorMessage, "$.symbolIdentifier", "Use an interface, abstract/virtual member, or overridable class.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, source.Identity.ContentHash,
                    "find_implementations.implementations", symbolIdentifier.Trim(), scope.ToString(), includeGenerated.ToString(),
                    maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var page = NavigationToolSupport.PageResults(result.Implementations, maxResults, coreCursor, binding,
                    maxResponseBytes, maxResponseTokens);
                if (page.Error is not null) return page.Error;
                var response = NavigationToolSupport.Success(new { result.TargetSymbolName, result.TargetKind,
                    Implementations = page.Items, result.TotalCount, ReturnedCount = page.Items.Length,
                    ResultCursor = page.NextCursor });
                return source.WithMetadata(response,
                    $"findImplementations(symbol={symbolIdentifier.Trim()}, pageSize={maxResults}, scope={scope}, includeGenerated={includeGenerated})",
                    [], page.NextCursor is not null);
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken, resultCursor, "find_implementations.implementations");

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "get_impact", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Summarize callers affected by a source or assembly symbol.")]
    public async Task<CallToolResult> GetImpact([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [Required, System.ComponentModel.Description("Type, member, documentation ID, or current symbol handoff whose callers and effects should be summarized.")] string symbolIdentifier,
        [Range(1, 3), System.ComponentModel.Description("Maximum impact traversal depth.")] int depth = 1, [Range(1, int.MaxValue), System.ComponentModel.Description("Page size for impact call sites.")] int maxResults = 50, [System.ComponentModel.Description("Traverse into referenced assemblies when supported.")] bool includeReferences = false,
        [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16384, [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null, [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque cursor for the next page of impact call sites.")] string? resultCursor = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolIdentifier)) return Invalid("symbolIdentifier", "Provide a non-empty source or assembly symbol identifier.");
        return await NavigationToolSupport.RouteAsync(runtime, "get_impact", targetPath,
            new { symbolIdentifier, depth, includeReferences, maxResults },
            operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, coreCursor, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    if (includeReferences)
                    {
                        var closureImpact = await AssemblyImpactClosureScanner.ScanAsync(target.CanonicalPath,
                            symbolIdentifier, depth, int.MaxValue, ct).ConfigureAwait(false);
                        if (closureImpact.Error is { } error)
                            return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, closureImpact.ErrorField);
                        var closureIdentity = closureImpact.AnalysisIdentity!;
                        var closureBinding = BoundResultCursor.CreateBinding(target.CanonicalPath,
                            closureIdentity.ContentHash, "get_impact.callSites", symbolIdentifier.Trim(),
                            depth.ToString(System.Globalization.CultureInfo.InvariantCulture), "includeReferences=true",
                            maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        var closurePage = NavigationToolSupport.PageResults(closureImpact.Impact!.CallSites, maxResults,
                            coreCursor, closureBinding, maxResponseBytes, maxResponseTokens);
                        if (closurePage.Error is not null) return closurePage.Error;
                        var impactPayload = closureImpact.Impact;
                        var closureNextAction = impactPayload.IsTruncatedByNodeLimit
                            ? "Narrow the reference traversal scope; the symbol-visit budget is fixed."
                            : impactPayload.IsDepthClamped ? "Choose a supported impact depth up to three and repeat the query." : closureImpact.NextAction;
                        var closureResponse = NavigationToolSupport.Success(new { impactPayload.TargetSymbol,
                            impactPayload.TargetKind, impactPayload.DirectCallersCount, impactPayload.TransitiveImpactCount,
                            impactPayload.MaxDepthReached, CallSites = closurePage.Items, impactPayload.AffectedProjects,
                            impactPayload.AffectedFiles, impactPayload.IsTruncatedByNodeLimit, impactPayload.IsDepthClamped,
                            impactPayload.RequestedDepth, impactPayload.EffectiveDepth, impactPayload.VisitedSymbolCount,
                            impactPayload.EffectiveNodeLimit, impactPayload.TransitiveCallSitesCount,
                            ResultCursor = closurePage.NextCursor }, closureImpact.IsTruncated, closureNextAction);
                        return NavigationToolSupport.WithAssemblyMetadata(closureResponse, closureImpact.AnalysisIdentity!,
                            $"impact(symbol={closureImpact.Impact!.TargetSymbol}, requestedDepth={impactPayload.RequestedDepth}, effectiveDepth={impactPayload.EffectiveDepth}, visitedSymbols={impactPayload.VisitedSymbolCount}, nodeLimit={impactPayload.EffectiveNodeLimit}, pageSize={maxResults}, includeReferences=true)",
                            (closureImpact.OmissionReasons ?? []).Where(reason => reason != "maxResults").ToArray(), closurePage.NextCursor is not null);
                    }
                    var access = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                    if (!access.IsSuccess) return NavigationToolSupport.Failure(access.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var lease = access.Value!;
                    if (!string.Equals(Path.GetFullPath(lease.Origin.CanonicalPath), target.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                        return Invalid("symbolIdentifier", "Use a handoff produced by this targetPath.");
                    var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(lease.Symbol, lease.Solution, depth, int.MaxValue, ct,
                        handoffFormatter: CreateAssemblyHandoffFormatter(access.Value!)).ConfigureAwait(false);
                    var identity = AnalysisSymbolIdentity.ForAssembly(lease.Origin.CanonicalPath, lease.Origin.ContentHash,
                        lease.Generation, lease.ReferenceSnapshotHash);
                    var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, identity.ContentHash + "|" + lease.ReferenceSnapshotHash,
                        "get_impact.callSites", symbolIdentifier.Trim(), depth.ToString(System.Globalization.CultureInfo.InvariantCulture), includeReferences.ToString(),
                        maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var page = NavigationToolSupport.PageResults(impact.CallSites, maxResults, coreCursor, binding,
                        maxResponseBytes, maxResponseTokens);
                    if (page.Error is not null) return page.Error;
                    var incomplete = impact.IsTruncatedByNodeLimit || impact.IsDepthClamped;
                    var nextAction = impact.IsTruncatedByNodeLimit
                        ? "Narrow the source scope or choose a supported impact depth; the symbol-visit budget is fixed."
                        : impact.IsDepthClamped ? "Choose a supported impact depth up to three and repeat the query." : null;
                    var response = NavigationToolSupport.Success(new { impact.TargetSymbol, impact.TargetKind, impact.DirectCallersCount,
                        impact.TransitiveImpactCount, impact.MaxDepthReached, CallSites = page.Items,
                        impact.AffectedProjects, impact.AffectedFiles, impact.RequestedDepth, impact.EffectiveDepth,
                        impact.VisitedSymbolCount, impact.IsTruncatedByNodeLimit, impact.IsDepthClamped,
                        impact.EffectiveNodeLimit, impact.TransitiveCallSitesCount, ResultCursor = page.NextCursor }, incomplete,
                        nextAction);
                    var omissions = new List<string>();
                    if (impact.IsTruncatedByNodeLimit) omissions.Add("nodeLimit");
                    if (impact.IsDepthClamped) omissions.Add("depthLimit");
                    return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                        $"impact(symbol={lease.Symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}, requestedDepth={impact.RequestedDepth}, effectiveDepth={impact.EffectiveDepth}, visitedSymbols={impact.VisitedSymbolCount}, nodeLimit={impact.EffectiveNodeLimit}, pageSize={maxResults}, includeReferences=false)", omissions, page.NextCursor is not null);
                }
                return await WithSource(target, async (solution, source) =>
                {
                    var symbol = await Resolve(solution, symbolIdentifier, source.Identity, ct).ConfigureAwait(false);
                    if (symbol.Error is not null) return NavigationToolSupport.Failure(symbol.Error.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(symbol.Symbol!, solution, depth, int.MaxValue, ct).ConfigureAwait(false);
                    var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, source.Identity.ContentHash,
                        "get_impact.callSites", symbolIdentifier.Trim(), depth.ToString(System.Globalization.CultureInfo.InvariantCulture), includeReferences.ToString(),
                        maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var page = NavigationToolSupport.PageResults(impact.CallSites, maxResults, coreCursor, binding,
                        maxResponseBytes, maxResponseTokens);
                    if (page.Error is not null) return page.Error;
                    var response = NavigationToolSupport.Success(new { impact.TargetSymbol, impact.TargetKind, impact.DirectCallersCount,
                        impact.TransitiveImpactCount, impact.MaxDepthReached, CallSites = page.Items,
                        impact.AffectedProjects, impact.AffectedFiles, impact.RequestedDepth, impact.EffectiveDepth,
                        impact.VisitedSymbolCount, impact.IsTruncatedByNodeLimit, impact.IsDepthClamped,
                        impact.EffectiveNodeLimit, impact.TransitiveCallSitesCount, ResultCursor = page.NextCursor },
                        impact.IsTruncatedByNodeLimit || impact.IsDepthClamped,
                        impact.IsTruncatedByNodeLimit
                            ? "Narrow the source scope or choose a supported impact depth; the symbol-visit budget is fixed."
                            : impact.IsDepthClamped ? "Choose a supported impact depth up to three and repeat the query." : null);
                    var omissions = new List<string>();
                    if (impact.IsTruncatedByNodeLimit) omissions.Add("nodeLimit");
                    if (impact.IsDepthClamped) omissions.Add("depthLimit");
                    return source.WithMetadata(response,
                        $"impact(symbol={symbolIdentifier.Trim()}, requestedDepth={impact.RequestedDepth}, effectiveDepth={impact.EffectiveDepth}, visitedSymbols={impact.VisitedSymbolCount}, nodeLimit={impact.EffectiveNodeLimit}, pageSize={maxResults})", omissions.ToArray(), page.NextCursor is not null);
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken, resultCursor, "get_impact.callSites");

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

    [McpServerTool(Name = "get_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Read selected body, direct members, direct callers, or static test candidates for one source or assembly symbol.")]
    public Task<CallToolResult> GetContext(
        [Required, System.ComponentModel.Description("Absolute path to an existing source .sln/.slnx solution or managed .dll/.exe assembly.")] string targetPath,
        [Required, System.ComponentModel.Description("Type or member identifier for the selected context target.")] string symbolIdentifier,
        [Required, System.ComponentModel.Description("Non-empty, duplicate-free selection from body, members, callers, and tests.")] string[] sections,
        [System.ComponentModel.Description("Source caller scope: all (default), production, or tests. Applies only to callers.")] string? callerScope = null,
        [System.ComponentModel.Description("Include generated source declarations; defaults to false.")] bool? includeGenerated = null,
        [System.ComponentModel.Description("Include referenced assembly owners in the callers section; defaults to false.")] bool? includeReferences = null,
        [Range(1, 100), System.ComponentModel.Description("Page size for each selected list section (1–100; default 10). Body window size is controlled separately.")] int? maxResults = null,
        [Range(1, 1000), System.ComponentModel.Description("Maximum declaration lines in a body window; defaults to 80.")] int? maxBodyLines = null,
        [Range(1, 1000000), System.ComponentModel.Description("One-based body window start line; omit for the first window.")] int? startLine = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes.")] int maxResponseBytes = 24576,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque cursor for exactly one selected section. Read all outer response pages before continuing it.")] string? resultCursor = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateContextArguments(targetPath, symbolIdentifier, sections, callerScope, includeGenerated,
            includeReferences, maxResults, maxBodyLines, startLine, resultCursor, maxResponseBytes, maxResponseTokens);
        if (validation is not null) return Task.FromResult(validation);

        var selected = sections.Select(value => value.Trim().ToLowerInvariant()).ToArray();
        var scope = TryScope(callerScope ?? "all", out var parsedScope) ? parsedScope : SymbolScopeType.All;
        var generated = includeGenerated ?? false;
        var references = includeReferences ?? false;
        var effectivePageSize = maxResults ?? 10;
        var bodyLines = maxBodyLines ?? 80;
        var bodyStart = startLine ?? 1;
        var args = new { symbolIdentifier, sections, callerScope, includeGenerated, includeReferences, maxResults, maxBodyLines, startLine };
        var requestBinding = System.Text.Json.JsonSerializer.Serialize(new { callerScope, includeGenerated, includeReferences, maxResults, maxBodyLines, startLine });

        return NavigationToolSupport.RouteAsync(runtime, "get_context", targetPath, args, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, coreCursor, ct) =>
            {
                var continuation = ParseContextCursor(coreCursor, selected);
                if (continuation.Error is not null) return McpToolResults.InvalidArgument(continuation.Error, "$.resultCursor",
                    "Use a resultCursor returned for this exact get_context selection.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                var activeSections = continuation.Section is null ? selected : [continuation.Section];
                if (target.TargetType == AnalysisTargetType.Project)
                    return await NavigationToolSupport.WithSourceSolutionAsync(runtime, target,
                        async (solution, source, token) => await BuildSourceContextAsync(target, solution, source, symbolIdentifier,
                            selected, activeSections, requestBinding, scope, generated, effectivePageSize, bodyLines, bodyStart, coreCursor,
                            continuation.Section, maxResponseBytes, maxResponseTokens, token).ConfigureAwait(false),
                        maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
                return await BuildAssemblyContextAsync(target, symbolIdentifier, selected, activeSections, requestBinding, generated, references,
                    effectivePageSize, bodyLines, bodyStart, coreCursor, continuation.Section, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
            }, requiredType: null, cancellationToken, resultCursor, "get_context");
    }

    private CallToolResult? ValidateContextArguments(string? targetPath, string? symbolIdentifier, string[]? sections,
        string? callerScope, bool? includeGenerated, bool? includeReferences, int? maxResults, int? maxBodyLines,
        int? startLine, string? resultCursor, int maxResponseBytes, int? maxResponseTokens)
    {
        if (string.IsNullOrWhiteSpace(symbolIdentifier))
            return McpToolResults.InvalidArgument("symbolIdentifier must be a non-empty symbol identifier.", "$.symbolIdentifier",
                "Provide a declaration name, documentation ID, source position, or current handoff ID.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (symbolIdentifier.Trim().StartsWith("i:", StringComparison.OrdinalIgnoreCase))
            return McpToolResults.InvalidArgument("Internal symbol identifiers are not accepted by get_context.", "$.symbolIdentifier",
                "Provide a declaration name, documentation ID, source position, or public handoff ID.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (sections is null || sections.Length == 0 || sections.Any(string.IsNullOrWhiteSpace))
            return McpToolResults.InvalidArgument("sections must contain at least one supported section.", "$.sections",
                "Choose one or more of body, members, callers, and tests.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        var normalized = sections.Select(value => value.Trim().ToLowerInvariant()).ToArray();
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            return McpToolResults.InvalidArgument("sections cannot contain duplicates.", "$.sections",
                "List each requested section once.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (normalized.Any(value => value is not ("body" or "members" or "callers" or "tests"))
            || sections.Where((value, index) => !string.Equals(value, normalized[index], StringComparison.Ordinal)).Any())
            return McpToolResults.InvalidArgument("sections contains an unsupported section.", "$.sections",
                "Choose from body, members, callers, and tests.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (maxResults is < 1 or > 100) return McpToolResults.InvalidArgument("maxResults must be from 1 to 100.", "$.maxResults",
            "Use a positive list page size no greater than 100.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (maxResults is not null && !normalized.Intersect(["members", "callers", "tests"], StringComparer.Ordinal).Any())
            return McpToolResults.InvalidArgument("maxResults applies only to list sections.", "$.maxResults",
                "Select members, callers, or tests, or omit maxResults.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (maxBodyLines is < 1 or > 1000) return McpToolResults.InvalidArgument("maxBodyLines must be from 1 to 1000.", "$.maxBodyLines",
            "Use a positive body window size.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (startLine is < 1) return McpToolResults.InvalidArgument("startLine must be positive.", "$.startLine",
            "Use a one-based body window position.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        var type = Path.GetExtension(targetPath ?? string.Empty).ToLowerInvariant() is ".dll" or ".exe"
            ? AnalysisTargetType.Assembly : AnalysisTargetType.Project;
        if (type == AnalysisTargetType.Project)
        {
            if (includeReferences is not null) return McpToolResults.InvalidArgument("includeReferences applies only to assembly callers.", "$.includeReferences",
                "Omit this argument for source targets.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            if (callerScope is not null && !normalized.Contains("callers"))
                return McpToolResults.InvalidArgument("callerScope requires the callers section.", "$.callerScope",
                    "Select callers or omit callerScope.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            if (callerScope is not null && !TryScope(callerScope, out _))
                return McpToolResults.InvalidArgument("callerScope is unsupported.", "$.callerScope", "Use all, production, or tests.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        }
        else
        {
            if (normalized.Contains("tests")) return McpToolResults.InvalidArgument("The tests section supports source solutions only.", "$.sections",
                "Use a source solution target for static test candidates.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            if (callerScope is not null) return McpToolResults.InvalidArgument("callerScope applies only to source callers.", "$.callerScope",
                "Omit this argument for assembly targets.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            if (includeGenerated is not null) return McpToolResults.InvalidArgument("includeGenerated applies only to source targets.", "$.includeGenerated",
                "Omit this argument for assembly targets.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            if (includeReferences is not null && !normalized.Contains("callers"))
                return McpToolResults.InvalidArgument("includeReferences requires the callers section.", "$.includeReferences",
                    "Select callers or omit includeReferences.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        }
        if (!normalized.Contains("body") && (maxBodyLines is not null || startLine is not null))
            return McpToolResults.InvalidArgument("Body window arguments require the body section.", maxBodyLines is not null ? "$.maxBodyLines" : "$.startLine",
                "Select body or omit body window arguments.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (maxResponseBytes is < McpResponseBudgetLimits.MinimumBytes or > McpResponseBudgetLimits.MaximumBytes)
            return McpToolResults.InvalidArgument("maxResponseBytes is outside the supported range.", "$.maxResponseBytes",
                "Use the supported response byte range.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        return null;
    }

    private async Task<CallToolResult> BuildSourceContextAsync(AnalysisTarget target, Solution solution,
        NavigationToolSupport.SourceAnalysisContext source, string identifier, string[] selected, string[] active, string requestBinding,
        SymbolScopeType callerScope, bool includeGenerated, int pageSize, int bodyLines, int startLine,
        string? internalCursor, string? continuationSection, int bytes, int? tokens, CancellationToken ct)
    {
        var resolved = await SourceSymbolResolver.ResolveAsync(solution, identifier, source.Identity, ct).ConfigureAwait(false);
        if (!resolved.IsSuccess) return NavigationToolSupport.Failure(resolved.Error!.Value, bytes, tokens, "$.symbolIdentifier");
        var symbol = resolved.Symbol!;
        if (active.Contains("members", StringComparer.Ordinal) && symbol is not INamedTypeSymbol)
            return McpToolResults.InvalidArgument("members requires a type target.", "$.sections",
                "Select a type declaration or remove members.", maxResponseBytes: bytes, maxResponseTokens: tokens);
        var targetDeclarations = symbol.DeclaringSyntaxReferences;
        SyntaxReference? selectedDeclaration = includeGenerated ? targetDeclarations.FirstOrDefault() : null;
        foreach (var candidate in targetDeclarations)
        {
            if (selectedDeclaration is not null) break;
            var document = solution.GetDocument(candidate.SyntaxTree);
            if (document is null || !await GeneratedDocumentDetector.IsGeneratedDocumentAsync(document, ct).ConfigureAwait(false))
                selectedDeclaration = candidate;
        }
        if (!includeGenerated && targetDeclarations.Length > 0 && selectedDeclaration is null)
            return McpToolResults.InvalidArgument("The selected declaration is generated source and excluded by includeGenerated=false.", "$.includeGenerated",
                "Set includeGenerated=true to inspect generated declarations.", maxResponseBytes: bytes, maxResponseTokens: tokens);
        var declaration = BuildContextDeclaration(symbol, solution, source.Identity, target.CanonicalPath, "source", selectedDeclaration);
        var sections = new List<object>();
        var omissions = new List<string>();
        string? currentSection = null;
        var sectionFailed = false;
        try
        {
            foreach (var section in active)
            {
                currentSection = section;
                BeforeContextSectionForTesting?.Invoke(section);
                if (section == "body")
                {
                var body = selectedDeclaration is not null && symbol is INamedTypeSymbol
                    ? SourceSymbolBodyResolver.ResolveWithDeclaration(symbol, selectedDeclaration, bodyLines, startLine,
                        handoffIdentity: source.Identity, solution: solution)
                    : SourceSymbolBodyResolver.Resolve(symbol, bodyLines, startLine, handoffIdentity: source.Identity, solution: solution);
                var reasons = body.HasMore ? new[] { "maxBodyLines" } : Array.Empty<string>();
                    sections.Add(new ContextSection("body", body.HasMore ? "partial" : "complete", "selected source declaration",
                    reasons, 1, new { body.Body, body.DisplayedStart, body.DisplayedEnd, body.TotalLines, body.HasMore,
                        Availability = body.Availability, body.Hint }, null,
                        body.HasMore ? body.DisplayedEnd + 1 : null, null,
                        body.HasMore ? $"Continue with startLine={body.DisplayedEnd + 1} and the same maxBodyLines." : null,
                        AnalysisComplete: true));
                omissions.AddRange(reasons);
            }
                else if (section == "members")
                {
                if (symbol is not INamedTypeSymbol type) return McpToolResults.InvalidArgument("members requires a type target.", "$.sections",
                    "Select a type declaration or remove members.", maxResponseBytes: bytes, maxResponseTokens: tokens);
                var sourceMembers = new List<ISymbol>();
                foreach (var member in type.GetMembers().Where(member => !member.IsImplicitlyDeclared))
                {
                    if (!includeGenerated && member.DeclaringSyntaxReferences.Length > 0)
                    {
                        var hasVisibleDeclaration = false;
                        foreach (var memberDeclaration in member.DeclaringSyntaxReferences)
                        {
                            var memberDocument = solution.GetDocument(memberDeclaration.SyntaxTree);
                            if (memberDocument is null || !await GeneratedDocumentDetector.IsGeneratedDocumentAsync(memberDocument, ct).ConfigureAwait(false))
                            {
                                hasVisibleDeclaration = true;
                                break;
                            }
                        }
                        if (!hasVisibleDeclaration) continue;
                    }
                    sourceMembers.Add(member);
                }
                var all = sourceMembers.Select(member =>
                    {
                        var location = member.Locations.FirstOrDefault(item => item.IsInSource);
                        var line = location?.GetLineSpan().StartLinePosition.Line + 1 ?? 0;
                        var handoff = source.Identity.FormatHandoff(member, solution);
                        return new { Kind = member.Kind.ToString().ToLowerInvariant(), member.Name,
                            Signature = member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                            FilePath = location?.SourceTree?.FilePath ?? string.Empty, Line = line,
                            SpanStart = location?.SourceSpan.Start ?? int.MaxValue,
                            HandoffId = handoff is null ? null : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(handoff) };
                    }).OrderBy(member => member.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(member => member.Line).ThenBy(member => member.SpanStart).ToArray();
                var page = PageContextList(all, target.CanonicalPath, source.Identity.ContentHash, selected, section,
                    pageSize, internalCursor, identifier, callerScope, null, includeGenerated, null, null, requestBinding, bytes, tokens);
                if (page.Error is not null) return page.Error;
                sections.Add(new ContextSection(section, page.NextCursor is null ? "complete" : "partial", "direct declared members", [], all.Length, page.Items!, page.NextCursor,
                    null, null, page.NextCursor is null ? null : "Continue this section with its resultCursor.",
                    AnalysisComplete: true, ResultContinuationAvailable: page.NextCursor is not null));
            }
                else if (section == "callers")
                {
                var refs = await FindReferencesResolver.FindReferencesAsync(symbol, solution, int.MaxValue, 1, ct,
                    scope: callerScope, includeGenerated: includeGenerated,
                    handoffFormatter: CreateSourceHandoffFormatter(solution, source.Identity)).ConfigureAwait(false);
                var page = PageContextList(refs.References, target.CanonicalPath, source.Identity.ContentHash, selected, section,
                    pageSize, internalCursor, identifier, callerScope, null, includeGenerated, null, null, requestBinding, bytes, tokens);
                if (page.Error is not null) return page.Error;
                if (!refs.IsComplete) omissions.Add("callerAnalysisLimit");
                sections.Add(new ContextSection(section, !refs.IsComplete ? "partial" : page.NextCursor is null ? "complete" : "partial", "direct incoming source references",
                    refs.IsComplete ? [] : ["callerAnalysisLimit"], refs.TotalCount, page.Items!, page.NextCursor, null, null,
                    page.NextCursor is null ? null : "Continue this section with its resultCursor.",
                    AnalysisComplete: refs.IsComplete, ResultContinuationAvailable: page.NextCursor is not null));
            }
                else
                {
                var tests = await TestRecommendationBuilder.BuildAsync(symbol, solution, source.Identity, ct,
                    includeGenerated, SymbolScopeType.All).ConfigureAwait(false);
                var fixtures = tests.TestFixtures;
                var page = PageContextList(fixtures, target.CanonicalPath, source.Identity.ContentHash, selected, section,
                    pageSize, internalCursor, identifier, callerScope, null, includeGenerated, null, null, requestBinding, bytes, tokens);
                if (page.Error is not null) return page.Error;
                var limited = tests.ImplementationExpansionLimitReached || tests.CandidateExpansionLimitReached || tests.ReferenceInspectionLimitReached;
                var reasons = new List<string>();
                if (tests.ImplementationExpansionLimitReached) reasons.Add("implementationExpansionLimit");
                if (tests.CandidateExpansionLimitReached) reasons.Add("candidateExpansionLimit");
                if (tests.ReferenceInspectionLimitReached) reasons.Add("referenceInspectionLimit");
                sections.Add(new ContextSection(section, limited || page.NextCursor is not null ? "partial" : "complete", "recognized source test projects and files",
                    reasons, tests.TotalTestFixtures, page.Items!, page.NextCursor, null, null,
                    limited ? "Select a narrower symbol to inspect beyond the bounded test-candidate analysis." :
                        page.NextCursor is null ? null : "Continue this section with its resultCursor.",
                    AnalysisComplete: !limited, ResultContinuationAvailable: page.NextCursor is not null,
                    Analysis: new { tests.EvidenceMode, tests.ExpandedImplementationCount, tests.ImplementationExpansionLimitReached,
                        tests.CandidateExpansionLimitReached, tests.ReferenceInspectionLimitReached }));
                omissions.AddRange(reasons);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            sectionFailed = true;
            sections = sections.Select(item => item is ContextSection section ? section with { Status = "partial" } : item).ToList();
            omissions.Add("sectionError");
            sections.Add(new ContextSection(currentSection ?? "unknown", "error", "analysis failed", ["sectionError"], 0,
                Array.Empty<object>(), null, null, new ContextSectionError("CONTEXT_SECTION_FAILED",
                    $"{exception.GetType().Name}: {exception.Message}"), AnalysisComplete: false));
            var failedIndex = Array.IndexOf(active, currentSection);
            foreach (var notRun in active.Skip(failedIndex + 1))
                sections.Add(new ContextSection(notRun, "notAnalyzed", "not analyzed after an earlier section error",
                    ["blockedBySectionError"], 0, Array.Empty<object>(), null, null,
                    new ContextSectionError("SECTION_NOT_ANALYZED", "An earlier selected section failed."), AnalysisComplete: false));
        }
        var snapshotId = NavigationAnalysisMetadata.CreateSnapshotId("source", source.Identity.ContentHash);
        var hasPartialSection = sections.Any(item => item is ContextSection { Status: "partial" or "notAnalyzed" });
        var responsePayload = new ContextResult(sectionFailed ? "error" : omissions.Count == 0 && !hasPartialSection ? "complete" : "partial", declaration with { SnapshotId = snapshotId }, sections,
            continuationSection, omissions.Distinct(StringComparer.Ordinal).ToArray());
        var response = NavigationToolSupport.Success(responsePayload, !sectionFailed && sections.Any(item => item is ContextSection { ResultCursor: not null }),
            "Continue the selected result section with its resultCursor.");
        if (sectionFailed)
        {
            response.IsError = true;
            return response;
        }
        return source.WithMetadata(response, $"get_context(symbol={identifier.Trim()}, sections={string.Join('|', selected)}, callerScope={callerScope}, includeGenerated={includeGenerated}, maxResults={pageSize}, bodyStart={startLine}, bodyLines={bodyLines})",
            omissions.ToArray(), sections.Any(item => item is ContextSection { ResultCursor: not null }));
    }

    private async Task<CallToolResult> BuildAssemblyContextAsync(AnalysisTarget target, string identifier, string[] selected,
        string[] active, string requestBinding, bool includeGenerated, bool includeReferences, int pageSize, int bodyLines, int startLine,
        string? internalCursor, string? continuationSection, int bytes, int? tokens, CancellationToken ct)
    {
        if (includeReferences && selected.Contains("callers", StringComparer.Ordinal))
            return await BuildAssemblyContextWithReferencesAsync(target, identifier, selected, active, requestBinding, pageSize,
                bodyLines, startLine, internalCursor, continuationSection, bytes, tokens, ct).ConfigureAwait(false);
        var normalizedIdentifier = InputNormalizer.NormalizeSymbolIdentifier(identifier);
        var opened = InputNormalizer.HasOpaqueHandoffPrefix(normalizedIdentifier)
            ? await AssemblyNavigationSessionScope.OpenResidentAsync(target.CanonicalPath, ct).ConfigureAwait(false)
            : await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
        if (!opened.IsSuccess) return NavigationToolSupport.Failure(opened.Error!.Value, bytes, tokens, "$.targetPath");
        await using var scope = opened.Value!;
        ISymbol symbol;
        string symbolHandoff;
        if (InputNormalizer.HasOpaqueHandoffPrefix(normalizedIdentifier))
        {
            var resolvedHandoff = AssemblySymbolHandoffResolver.ResolveWithinScope(normalizedIdentifier, scope);
            if (!resolvedHandoff.IsSuccess) return NavigationToolSupport.Failure(resolvedHandoff.Error!.Value, bytes, tokens, "$.symbolIdentifier");
            symbol = resolvedHandoff.Value!;
            var internalHandoff = AnalysisSymbolIdentity.ForAssembly(scope.Context.Origin.CanonicalPath, scope.Context.Origin.ContentHash,
                scope.Context.Generation, scope.Context.ReferenceSnapshotHash).FormatHandoff(symbol);
            if (internalHandoff is null) return McpToolResults.Recoverable(NavigationErrorCodes.StaleSnapshot,
                "The selected assembly symbol no longer resolves in this snapshot.", "Repeat symbol discovery and retry.",
                fieldPath: "$.symbolIdentifier", maxResponseBytes: bytes, maxResponseTokens: tokens);
            symbolHandoff = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalHandoff);
        }
        else
        {
            var symbolResult = await AssemblySymbolInputResolver.ResolveAsync(scope, normalizedIdentifier, ct).ConfigureAwait(false);
            if (!symbolResult.IsSuccess) return NavigationToolSupport.Failure(symbolResult.Error!.Value, bytes, tokens, "$.symbolIdentifier");
            symbol = symbolResult.Symbol!;
            symbolHandoff = symbolResult.HandoffId!;
        }
        var identity = AnalysisSymbolIdentity.ForAssembly(scope.Context.Origin.CanonicalPath, scope.Context.Origin.ContentHash,
            scope.Context.Generation, scope.Context.ReferenceSnapshotHash);
        if (selected.Contains("members", StringComparer.Ordinal) && symbol is not INamedTypeSymbol)
            return McpToolResults.InvalidArgument("members requires a type target.", "$.sections",
                "Select a type declaration or remove members.", maxResponseBytes: bytes, maxResponseTokens: tokens);
        var ownerFormatter = CreateAssemblyHandoffFormatter(scope.Solution, scope.Context);
        var declaration = BuildContextDeclaration(symbol, scope.Solution, identity, scope.Context.Origin.CanonicalPath, "assembly");
        var sections = new List<object>();
        var omissions = new List<string>();
        string? currentSection = null;
        var sectionFailed = false;
        try
        {
        foreach (var section in active)
        {
            currentSection = section;
            BeforeContextSectionForTesting?.Invoke(section);
            if (section == "body")
            {
                var body = SourceSymbolBodyResolver.Resolve(symbol, bodyLines, startLine, handoffId: symbolHandoff);
                var reasons = body.HasMore ? new[] { "maxBodyLines" } : Array.Empty<string>();
                sections.Add(new ContextSection(section, body.HasMore ? "partial" : "complete", "selected assembly owner declaration",
                    reasons, 1, new { body.Body, body.DisplayedStart, body.DisplayedEnd, body.TotalLines, body.HasMore,
                        Availability = body.Availability, body.Hint }, null,
                    body.HasMore ? body.DisplayedEnd + 1 : null, null,
                    body.HasMore ? $"Continue with startLine={body.DisplayedEnd + 1} and the same maxBodyLines." : null,
                    AnalysisComplete: true));
                omissions.AddRange(reasons);
            }
            else if (section == "members")
            {
                var type = (INamedTypeSymbol)symbol;
                var all = type.GetMembers().Where(member => !member.IsImplicitlyDeclared)
                    .Select(member => new { Kind = member.Kind.ToString().ToLowerInvariant(), member.Name,
                        Signature = member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        FilePath = member.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath ?? string.Empty,
                        Line = member.Locations.FirstOrDefault(location => location.IsInSource)?.GetLineSpan().StartLinePosition.Line + 1 ?? 0,
                        SpanStart = member.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start ?? int.MaxValue,
                        HandoffId = ownerFormatter(member) })
                    .OrderBy(member => member.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(member => member.Line).ThenBy(member => member.SpanStart).ToArray();
                var page = PageContextList(all, target.CanonicalPath, identity.ContentHash + "|" + scope.Context.ReferenceSnapshotHash, selected, section,
                    pageSize, internalCursor, identifier, SymbolScopeType.All, includeReferences, false, null, null, requestBinding, bytes, tokens);
                if (page.Error is not null) return page.Error;
                sections.Add(new ContextSection(section, page.NextCursor is null ? "complete" : "partial", "direct declared members", [], all.Length, page.Items!, page.NextCursor, null, null,
                    page.NextCursor is null ? null : "Continue this section with its resultCursor.",
                    AnalysisComplete: true, ResultContinuationAvailable: page.NextCursor is not null));
            }
            else
            {
                var refs = await FindReferencesResolver.FindReferencesAsync(symbol, scope.Solution, int.MaxValue, 1, ct,
                    scope: SymbolScopeType.All, includeGenerated: false, handoffFormatter: ownerFormatter,
                    ownerTargetPath: scope.Context.Origin.CanonicalPath).ConfigureAwait(false);
                var page = PageContextList(refs.References, target.CanonicalPath, identity.ContentHash + "|" + scope.Context.ReferenceSnapshotHash, selected, section,
                    pageSize, internalCursor, identifier, SymbolScopeType.All, includeReferences, false, null, null, requestBinding, bytes, tokens);
                if (page.Error is not null) return page.Error;
                var incompleteClosure = includeReferences && scope.Context.References.Any(reference => !reference.Resolved);
                var incompleteOwner = scope.Context.Status != AssemblySessionStatus.Complete;
                var reasons = refs.IsComplete && !incompleteClosure && !incompleteOwner ? Array.Empty<string>()
                    : new[] { incompleteClosure ? "referenceClosureIncomplete" : incompleteOwner ? "assemblyOwnerIncomplete" : "callerAnalysisLimit" };
                sections.Add(new ContextSection(section, reasons.Length > 0 || page.NextCursor is not null ? "partial" : "complete",
                    includeReferences ? "direct callers in selected assembly reference scope" : "direct callers in selected assembly owner",
                    reasons, refs.TotalCount, page.Items!, page.NextCursor, null, null,
                    page.NextCursor is null ? null : "Continue this section with its resultCursor.",
                    AnalysisComplete: refs.IsComplete && !incompleteClosure && !incompleteOwner,
                    ResultContinuationAvailable: page.NextCursor is not null));
                omissions.AddRange(reasons);
            }
        }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            sectionFailed = true;
            sections = sections.Select(item => item is ContextSection section ? section with { Status = "partial" } : item).ToList();
            omissions.Add("sectionError");
            sections.Add(new ContextSection(currentSection ?? "unknown", "error", "analysis failed", ["sectionError"], 0,
                Array.Empty<object>(), null, null, new ContextSectionError("CONTEXT_SECTION_FAILED",
                    $"{exception.GetType().Name}: {exception.Message}"), AnalysisComplete: false));
            var failedIndex = Array.IndexOf(active, currentSection);
            foreach (var notRun in active.Skip(failedIndex + 1))
                sections.Add(new ContextSection(notRun, "notAnalyzed", "not analyzed after an earlier section error",
                    ["blockedBySectionError"], 0, Array.Empty<object>(), null, null,
                    new ContextSectionError("SECTION_NOT_ANALYZED", "An earlier selected section failed."), AnalysisComplete: false));
        }
        var snapshotId = NavigationAnalysisMetadata.CreateSnapshotId("assembly", identity.ContentHash + scope.Context.ReferenceSnapshotHash);
        declaration = declaration with { SnapshotId = snapshotId };
        var hasPartialSection = sections.Any(item => item is ContextSection { Status: "partial" or "notAnalyzed" });
        var result = new ContextResult(sectionFailed ? "error" : omissions.Count == 0 && !hasPartialSection ? "complete" : "partial", declaration, sections, continuationSection, omissions.Distinct(StringComparer.Ordinal).ToArray());
        var response = NavigationToolSupport.Success(result, !sectionFailed && sections.Any(item => item is ContextSection { ResultCursor: not null }),
            "Continue the selected result section with its resultCursor.");
        if (sectionFailed) { response.IsError = true; return response; }
        return NavigationToolSupport.WithAssemblyMetadata(response, identity,
            $"get_context(symbol={identifier.Trim()}, sections={string.Join('|', selected)}, includeReferences={includeReferences}, maxResults={pageSize}, bodyStart={startLine}, bodyLines={bodyLines})",
            omissions.ToArray(), sections.Any(item => item is ContextSection { ResultCursor: not null }));
    }

    private async Task<CallToolResult> BuildAssemblyContextWithReferencesAsync(AnalysisTarget target, string identifier,
        string[] selected, string[] active, string requestBinding, int pageSize, int bodyLines, int startLine, string? internalCursor,
        string? continuationSection, int bytes, int? tokens, CancellationToken ct)
    {
        var opened = await AssemblyReferenceClosureSession.OpenAsync(target.CanonicalPath, identifier, ct,
            afterRawDiscovery: afterAssemblyClosureRawDiscovery,
            afterRootScopeOpened: afterAssemblyClosureRootScopeOpened,
            afterHandoffResolved: afterAssemblyClosureHandoffResolved,
            beforeOwnerScopeOpen: BeforeAssemblyContextOwnerOpenForTesting).ConfigureAwait(false);
        if (opened.Error is { } openError) return NavigationToolSupport.Failure(openError, bytes, tokens, opened.ErrorField);
        await using var session = opened.Session!;
        var owner = session.Owners.Single(item => string.Equals(item.TargetPath, session.HandoffOwnerPath, StringComparison.OrdinalIgnoreCase));
        var symbol = session.HandoffSymbol;
        var identity = owner.HandoffIdentity;
        var formatter = session.CreateInternalFormatter(owner);
        var internalHandoff = formatter(symbol);
        var handoff = AssemblyReferenceClosureSession.Externalize(internalHandoff);
        var declaration = BuildContextDeclaration(symbol, owner.Scope.Solution, identity, owner.TargetPath, "assembly");
        var sections = new List<object>();
        var omissions = new List<string>();
        string? currentSection = null;
        var sectionFailed = false;
        try
        {
        if (selected.Contains("members", StringComparer.Ordinal) && symbol is not INamedTypeSymbol)
            return McpToolResults.InvalidArgument("members requires a type target.", "$.sections",
                "Select a type declaration or remove members.", maxResponseBytes: bytes, maxResponseTokens: tokens);
        foreach (var section in active)
        {
            currentSection = section;
            BeforeContextSectionForTesting?.Invoke(section);
            if (section == "body")
            {
                var body = SourceSymbolBodyResolver.Resolve(symbol, bodyLines, startLine, handoffId: handoff);
                var reasons = body.HasMore ? new[] { "maxBodyLines" } : Array.Empty<string>();
                sections.Add(new ContextSection(section, body.HasMore ? "partial" : "complete", "selected assembly owner declaration", reasons, 1,
                    new { body.Body, body.DisplayedStart, body.DisplayedEnd, body.TotalLines, body.HasMore, Availability = body.Availability, body.Hint }, null,
                    body.HasMore ? body.DisplayedEnd + 1 : null, null,
                    body.HasMore ? $"Continue with startLine={body.DisplayedEnd + 1} and the same maxBodyLines." : null,
                    AnalysisComplete: true));
                omissions.AddRange(reasons);
            }
            else if (section == "members")
            {
                var type = (INamedTypeSymbol)symbol;
                var all = type.GetMembers().Where(member => !member.IsImplicitlyDeclared)
                    .Select(member => new { Kind = member.Kind.ToString().ToLowerInvariant(), member.Name,
                        Signature = member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        FilePath = member.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath ?? string.Empty,
                        Line = member.Locations.FirstOrDefault(location => location.IsInSource)?.GetLineSpan().StartLinePosition.Line + 1 ?? 0,
                        SpanStart = member.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start ?? int.MaxValue,
                        HandoffId = AssemblyReferenceClosureSession.Externalize(formatter(member)) })
                    .OrderBy(member => member.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(member => member.Line).ThenBy(member => member.SpanStart).ToArray();
                var page = PageContextList(all, target.CanonicalPath, session.RootAnalysisIdentity.ContentHash + "|" + session.RootReferenceSnapshotHash + "|" + session.RootAnalysisIdentity.Generation,
                    selected, section, pageSize, internalCursor, identifier, SymbolScopeType.All, true, false, null, null, requestBinding, bytes, tokens);
                if (page.Error is not null) return page.Error;
                sections.Add(new ContextSection(section, page.NextCursor is null ? "complete" : "partial", "direct declared members", [], all.Length, page.Items!, page.NextCursor, null, null,
                    page.NextCursor is null ? null : "Continue this section with its resultCursor.",
                    AnalysisComplete: true, ResultContinuationAvailable: page.NextCursor is not null));
            }
            else if (section == "callers")
            {
                var locations = new List<ReferenceLocationEntry>();
                var total = 0;
                var limited = session.OwnerLimitReached || session.HasFailedOwners || session.HasUnresolvedReferences;
                foreach (var candidateOwner in session.Owners)
                {
                    ct.ThrowIfCancellationRequested();
                    var ownerSymbol = session.ResolveDeclaration(candidateOwner, session.HandoffOwnerPath,
                        session.DeclarationCommentId, session.HandoffIdentity);
                    if (ownerSymbol is null) { limited = true; continue; }
                    var result = await FindReferencesResolver.FindReferencesAsync(ownerSymbol, candidateOwner.Scope.Solution,
                        int.MaxValue, 1, ct, scope: SymbolScopeType.All, includeGenerated: false,
                        handoffFormatter: session.CreateInternalFormatter(candidateOwner), ownerTargetPath: candidateOwner.TargetPath).ConfigureAwait(false);
                    total += result.TotalCount;
                    limited |= !result.IsComplete;
                    locations.AddRange(result.References);
                }
                var ordered = locations.DistinctBy(item => (item.OwnerTargetPath, item.FilePath, item.Line, item.Column, item.EnclosingSymbolHandoffId))
                    .OrderBy(item => item.OwnerTargetPath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Line).ThenBy(item => item.Column).ToArray();
                var page = PageContextList(ordered, target.CanonicalPath, session.RootAnalysisIdentity.ContentHash + "|" + session.RootReferenceSnapshotHash + "|" + session.RootAnalysisIdentity.Generation,
                    selected, section, pageSize, internalCursor, identifier, SymbolScopeType.All, true, false, null, null, requestBinding, bytes, tokens);
                if (page.Error is not null) return page.Error;
                var reasons = limited ? new[] { "referenceClosureIncomplete" } : Array.Empty<string>();
                sections.Add(new ContextSection(section, limited || page.NextCursor is not null ? "partial" : "complete", "direct incoming references in the selected reference closure",
                    reasons, total, page.Items!, page.NextCursor, null, null,
                    page.NextCursor is null ? null : "Continue this section with its resultCursor.",
                    AnalysisComplete: !limited, ResultContinuationAvailable: page.NextCursor is not null));
                omissions.AddRange(reasons);
            }
        }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            sectionFailed = true;
            sections = sections.Select(item => item is ContextSection section ? section with { Status = "partial" } : item).ToList();
            omissions.Add("sectionError");
            sections.Add(new ContextSection(currentSection ?? "unknown", "error", "analysis failed", ["sectionError"], 0,
                Array.Empty<object>(), null, null, new ContextSectionError("CONTEXT_SECTION_FAILED",
                    $"{exception.GetType().Name}: {exception.Message}"), AnalysisComplete: false));
            var failedIndex = Array.IndexOf(active, currentSection);
            foreach (var notRun in active.Skip(failedIndex + 1))
                sections.Add(new ContextSection(notRun, "notAnalyzed", "not analyzed after an earlier section error",
                    ["blockedBySectionError"], 0, Array.Empty<object>(), null, null,
                    new ContextSectionError("SECTION_NOT_ANALYZED", "An earlier selected section failed."), AnalysisComplete: false));
        }
        var closureSnapshot = session.RootAnalysisIdentity.ContentHash + "|" + session.RootReferenceSnapshotHash;
        var snapshotId = NavigationAnalysisMetadata.CreateSnapshotId("assembly", closureSnapshot);
        declaration = declaration with { SnapshotId = snapshotId };
        var hasPartialSection = sections.Any(item => item is ContextSection { Status: "partial" or "notAnalyzed" });
        var resultPayload = new ContextResult(sectionFailed ? "error" : omissions.Count == 0 && !hasPartialSection ? "complete" : "partial", declaration, sections,
            continuationSection, omissions.Distinct(StringComparer.Ordinal).ToArray());
        var hasCursor = !sectionFailed && sections.Any(item => item is ContextSection { ResultCursor: not null });
        var response = NavigationToolSupport.Success(resultPayload, hasCursor, "Continue the selected result section with its resultCursor.");
        if (sectionFailed) { response.IsError = true; return response; }
        return NavigationToolSupport.WithAssemblyMetadata(response, session.RootAnalysisIdentity,
            $"get_context(symbol={identifier.Trim()}, sections={string.Join('|', selected)}, includeReferences=true, maxResults={pageSize}, bodyStart={startLine}, bodyLines={bodyLines})",
            omissions.ToArray(), hasCursor);
    }

    private (object[]? Items, string? NextCursor, CallToolResult? Error) PageContextList<T>(IReadOnlyList<T> items,
        string target, string snapshot, string[] selected, string section, int pageSize, string? internalCursor,
        string identifier, SymbolScopeType callerScope, bool? includeReferences, bool includeGenerated,
        int? bodyLines, int? startLine, string requestBinding, int bytes, int? tokens)
    {
        var binding = BoundResultCursor.CreateBinding(target, snapshot, "get_context." + section,
            identifier.Trim(), string.Join("\0", selected), callerScope.ToString(), includeReferences?.ToString(),
            includeGenerated.ToString(), pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bodyLines?.ToString(System.Globalization.CultureInfo.InvariantCulture), startLine?.ToString(System.Globalization.CultureInfo.InvariantCulture), requestBinding);
        var cursor = UnwrapContextCursor(internalCursor, section, out var cursorError);
        if (cursorError is not null)
            return (null, null, McpToolResults.InvalidArgument(cursorError, "$.resultCursor", "Use the returned cursor for this section.", maxResponseBytes: bytes, maxResponseTokens: tokens));
        var page = NavigationToolSupport.PageResults(items, pageSize, cursor, binding, bytes, tokens);
        return (page.Items?.Cast<object>().ToArray(), page.NextCursor is null ? null : "ctx1:" + section + ":" + page.NextCursor, page.Error);
    }

    private static string? UnwrapContextCursor(string? cursor, string section, out string? error)
    {
        error = null;
        if (cursor is null) return null;
        var prefix = "ctx1:" + section + ":";
        if (!cursor.StartsWith(prefix, StringComparison.Ordinal))
        {
            error = "The resultCursor does not identify the requested context section.";
            return null;
        }
        return cursor[prefix.Length..];
    }

    private static (string? Section, string? Error) ParseContextCursor(string? cursor, string[] selected)
    {
        if (cursor is null) return (null, null);
        if (!cursor.StartsWith("ctx1:", StringComparison.Ordinal)) return (null, "The resultCursor is malformed.");
        var end = cursor.IndexOf(':', 5);
        if (end < 0) return (null, "The resultCursor is malformed.");
        var section = cursor[5..end];
        if (!selected.Contains(section, StringComparer.Ordinal)) return (null, "The resultCursor section is outside the original sections selection.");
        return (section, null);
    }

    private static ContextDeclaration BuildContextDeclaration(ISymbol symbol, Solution solution, AnalysisSymbolIdentity identity,
        string ownerPath, string targetKind, SyntaxReference? preferredDeclaration = null)
    {
        var location = preferredDeclaration is null
            ? symbol.Locations.FirstOrDefault(item => item.IsInSource)
            : Location.Create(preferredDeclaration.SyntaxTree, preferredDeclaration.Span);
        var line = location?.GetLineSpan().StartLinePosition.Line + 1 ?? 0;
        var internalId = targetKind == "source" ? identity.FormatHandoff(symbol, solution) : identity.FormatHandoff(symbol);
        var handoff = internalId is null ? null : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalId);
        return new ContextDeclaration(symbol.Name, symbol.Kind.ToString().ToLowerInvariant(),
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), SymbolVisibilityResolver.ResolveVisibility(symbol),
            location?.SourceTree?.FilePath ?? string.Empty, line, handoff, ownerPath, string.Empty);
    }

    private sealed record ContextDeclaration(string Name, string Kind, string Signature, string Visibility,
        string FilePath, int Line, string? HandoffId, string OwnerTargetPath, string SnapshotId);
    private sealed record ContextSectionError(string Code, string Message);
    private sealed record ContextSection(string Name, string Status, string AnalyzedScope, IReadOnlyList<string> Omissions,
        int TotalCount, object Items, string? ResultCursor, int? NextStartLine, ContextSectionError? Error,
        string? NextAction = null, bool AnalysisComplete = true, bool ResultContinuationAvailable = false, object? Analysis = null);
    private sealed record ContextResult(string Status, ContextDeclaration Target, IReadOnlyList<object> Sections,
        string? ContinuationSection, IReadOnlyList<string> Omissions);

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
