using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
public sealed partial class RelationshipTools(NavigatorHostRuntime runtime)
{
    private Func<CancellationToken, Task>? afterAssemblyCallTreeGraphBuilt;
    private Func<AssemblyNavigationSessionScope, CancellationToken, Task>? afterAssemblyClosureRootScopeOpened;
    private Func<AssemblySymbolReferenceAccess, CancellationToken, Task>? afterAssemblyClosureHandoffResolved;
    private Func<CancellationToken, Task>? afterAssemblyClosureRawDiscovery;
    internal Func<AssemblyNavigationSessionScope, CancellationToken, Task>? AfterAssemblySymbolScopeOpenedForTesting { get; set; }

    internal RelationshipTools(
        NavigatorHostRuntime runtime,
        Func<CancellationToken, Task>? afterAssemblyCallTreeGraphBuilt,
        Func<AssemblyNavigationSessionScope, CancellationToken, Task>? afterAssemblyClosureRootScopeOpened,
        Func<AssemblySymbolReferenceAccess, CancellationToken, Task>? afterAssemblyClosureHandoffResolved = null,
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
    public async Task<CallToolResult> GetCallTree([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [Required, System.ComponentModel.Description("Type, member, documentation ID, or stable src:/asm: reference to trace.")] string symbolIdentifier,
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
                if (ValidateSourceReferenceInput(symbolIdentifier, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier") is { } referenceRouteError)
                    return referenceRouteError;
                return await WithSource(target, async (solution, source) =>
                {
                    var symbol = await Resolve(solution, symbolIdentifier, source.IdentityRequest, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(solution, symbol.Symbol!, depth, topN, parsedDirection, includeBcl, scope, includeGenerated,
                    symbolValue => source.FormatHandoff(symbolValue, solution)), ct).ConfigureAwait(false);
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
    public async Task<CallToolResult> FindReferences([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [Required, System.ComponentModel.Description("Type, member, documentation ID, or stable src:/asm: reference whose references should be found.")] string symbolIdentifier,
        [Range(1, 3), System.ComponentModel.Description("Maximum reference traversal depth.")] int depth = 1, [Range(1, int.MaxValue), System.ComponentModel.Description("Page size for matching references.")] int maxResults = 50, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all",
        [System.ComponentModel.Description("Include matches from generated source files.")] bool includeGenerated = false, [System.ComponentModel.Description("Traverse into referenced assemblies when supported.")] bool includeReferences = false, [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16384,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null, [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque cursor for the next page of the complete reference list.")] string? resultCursor = null, [System.ComponentModel.Description("Include counts and sorted owner-qualified identities for all discovered reference sites before display paging. Repeat the same summary on every result page; do not sum pages.")] bool includeSummary = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolIdentifier)) return Invalid("symbolIdentifier", "Provide a non-empty source or assembly symbol identifier.");
        if (!TryScope(scopeType, out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "find_references", targetPath,
            new { symbolIdentifier, depth, scopeType, includeGenerated, includeReferences, includeSummary, maxResults }, operationToken,
            continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, coreCursor, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    if (includeReferences)
                    {
                        var closure = await AssemblyReferencesClosureScanner.ScanAsync(target.CanonicalPath,
                            symbolIdentifier, int.MaxValue, depth, scope, includeGenerated, ct, includeSummary: includeSummary).ConfigureAwait(false);
                        if (closure.Error is { } error)
                            return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, closure.ErrorField);
                        var closureIdentity = closure.AnalysisIdentity!;
                        var closureBinding = BoundResultCursor.CreateBinding(target.CanonicalPath,
                            closureIdentity.ContentHash,
                            "find_references.references", symbolIdentifier.Trim(), depth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            scope.ToString(), includeGenerated.ToString(), "includeReferences=true",
                            maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture), includeSummary.ToString());
                        var closurePage = NavigationToolSupport.PageResults(closure.References!.References, maxResults, coreCursor,
                            closureBinding, maxResponseBytes, maxResponseTokens);
                        if (closurePage.Error is not null) return closurePage.Error;
                        var closureResponse = NavigationToolSupport.Success(new { References = closurePage.Items,
                            TotalCount = closure.References!.TotalCount, ReturnedCount = closurePage.Items.Length,
                            closure.References.RequestedDepth, closure.References.EffectiveDepth,
                            closure.References.VisitedSymbolCount, closure.References.IsTruncatedByNodeLimit,
                            closure.References.IsDepthClamped, closure.References.EffectiveNodeLimit,
                            closure.References.Summary, ResultCursor = closurePage.NextCursor }, closure.IsTruncated, closure.NextAction);
                        return NavigationToolSupport.WithAssemblyMetadata(closureResponse, closure.AnalysisIdentity!,
                            $"findReferences(symbol={symbolIdentifier.Trim()}, requestedDepth={closure.References.RequestedDepth}, effectiveDepth={closure.References.EffectiveDepth}, visitedSymbols={closure.References.VisitedSymbolCount}, nodeLimit={closure.References.EffectiveNodeLimit}, pageSize={maxResults}, scope={scope}, includeGenerated={includeGenerated}, includeReferences=true)",
                            (closure.OmissionReasons ?? []).Where(reason => reason != "maxResults").ToArray(), closurePage.NextCursor is not null);
                    }
                    var accessResult = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                    if (!accessResult.IsSuccess) return NavigationToolSupport.Failure(accessResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var access = accessResult.Value!;
                    var result = await FindReferencesResolver.FindReferencesAsync(access.Symbol, access.Solution,
                        int.MaxValue, depth, ct, scope: scope, includeGenerated: includeGenerated,
                        handoffFormatter: CreateAssemblyHandoffFormatter(access), ownerTargetPath: access.Origin.CanonicalPath, includeSummary: includeSummary).ConfigureAwait(false);
                    var identity = AnalysisSymbolIdentity.ForAssembly(access.Origin.CanonicalPath, access.Origin.ContentHash,
                        access.Generation, access.ReferenceSnapshotHash);
                    var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, identity.ContentHash + "|" + access.ReferenceSnapshotHash,
                        "find_references.references", symbolIdentifier.Trim(), depth.ToString(System.Globalization.CultureInfo.InvariantCulture), scope.ToString(), includeGenerated.ToString(),
                        maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture), includeSummary.ToString());
                    var page = NavigationToolSupport.PageResults(result.References, maxResults, coreCursor, binding,
                        maxResponseBytes, maxResponseTokens);
                    if (page.Error is not null) return page.Error;
                    var omissions = new List<string>();
                    if (result.IsTruncatedByNodeLimit) omissions.Add("nodeLimit");
                    if (result.IsDepthClamped) omissions.Add("depthLimit");
                    if (access.ScopeValue.Context.Status != AssemblySessionStatus.Complete) omissions.Add("assemblyOwnerIncomplete");
                    if (result.Summary is not null)
                        result = result with { Summary = result.Summary with { AnalysisComplete = omissions.Count == 0, Omissions = omissions.ToArray() } };
                    var response = NavigationToolSupport.Success(new { result.TargetSymbolName, result.TargetKind, References = page.Items,
                        result.TotalCount, ReturnedCount = page.Items.Length, result.RequestedDepth, result.EffectiveDepth,
                        result.VisitedSymbolCount, result.IsTruncatedByNodeLimit, result.IsDepthClamped, result.EffectiveNodeLimit,
                        result.Summary, ResultCursor = page.NextCursor }, omissions.Count > 0,
                        result.IsTruncatedByNodeLimit
                            ? "Narrow the source scope or choose a supported traversal depth; the symbol-visit budget is fixed."
                            : result.IsDepthClamped ? "Choose a supported traversal depth up to three and repeat the query."
                            : omissions.Count > 0 ? "The selected assembly owner is incomplete; inspect its navigation status and repeat the query when dependencies are available." : null);
                    return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                        $"findReferences(symbol={symbolIdentifier.Trim()}, requestedDepth={result.RequestedDepth}, effectiveDepth={result.EffectiveDepth}, visitedSymbols={result.VisitedSymbolCount}, nodeLimit={result.EffectiveNodeLimit}, pageSize={maxResults}, scope={scope}, includeGenerated={includeGenerated}, includeReferences=false)", omissions, page.NextCursor is not null);
                }
                if (ValidateSourceReferenceInput(symbolIdentifier, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier") is { } referenceRouteError)
                    return referenceRouteError;
                return await WithSource(target, async (solution, source) =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, source.IdentityRequest, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var result = await FindReferencesResolver.FindReferencesAsync(symbol.Symbol!, solution, int.MaxValue, depth, ct,
                    scope: scope, includeGenerated: includeGenerated,
                    handoffFormatter: symbolValue => source.FormatHandoff(symbolValue, solution), includeSummary: includeSummary).ConfigureAwait(false);
                var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, source.Identity.ContentHash,
                    "find_references.references", symbolIdentifier.Trim(), depth.ToString(System.Globalization.CultureInfo.InvariantCulture), scope.ToString(), includeGenerated.ToString(),
                    maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture), includeSummary.ToString());
                var page = NavigationToolSupport.PageResults(result.References, maxResults, coreCursor, binding,
                    maxResponseBytes, maxResponseTokens);
                if (page.Error is not null) return page.Error;
                var response = NavigationToolSupport.Success(new { result.TargetSymbolName, result.TargetKind, References = page.Items,
                    result.TotalCount, ReturnedCount = page.Items.Length, result.RequestedDepth, result.EffectiveDepth,
                    result.VisitedSymbolCount, result.IsTruncatedByNodeLimit, result.IsDepthClamped, result.EffectiveNodeLimit,
                    result.Summary, ResultCursor = page.NextCursor }, result.IsTruncatedByNodeLimit || result.IsDepthClamped,
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
    public async Task<CallToolResult> GetTypeHierarchy([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [Required, System.ComponentModel.Description("Type name, documentation ID, or stable src:/asm: reference whose hierarchy should be shown.")] string symbolIdentifier,
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
                if (ValidateSourceReferenceInput(symbolIdentifier, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier") is { } referenceRouteError)
                    return referenceRouteError;
                return await WithSource(target, async (solution, source) =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, source.IdentityRequest, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                if (symbol.Symbol is not INamedTypeSymbol named) return Invalid("symbolIdentifier", "Resolve a named class, interface, or struct.");
                var result = await TypeHierarchyScanner.ScanAsync(named, solution, int.MaxValue, ct, scope, includeGenerated,
                    symbolValue => source.FormatHandoff(symbolValue, solution)).ConfigureAwait(false);
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
    public async Task<CallToolResult> FindImplementations([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [Required, System.ComponentModel.Description("Type, member, documentation ID, or stable src:/asm: reference whose implementations should be found.")] string symbolIdentifier,
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
                if (ValidateSourceReferenceInput(symbolIdentifier, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier") is { } referenceRouteError)
                    return referenceRouteError;
                return await WithSource(target, async (solution, source) =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, source.IdentityRequest, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var result = await FindReferencesResolver.FindImplementationsAsync(symbol.Symbol!, solution, int.MaxValue, ct, scope, includeGenerated,
                    symbolValue => source.FormatHandoff(symbolValue, solution)).ConfigureAwait(false);
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

    [McpServerTool(Name = "dependency_graph", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Trace dependencies from exactly one file path or symbol identifier in the selected target.")]
    public async Task<CallToolResult> DependencyGraph([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath,
        [System.ComponentModel.Description("Indexed source file path used as the dependency graph root; specify this or symbolIdentifier.")] string? filePath = null,
        [System.ComponentModel.Description("Type or member identifier, including a stable src:/asm: reference, used as the dependency graph root; specify this or filePath.")] string? symbolIdentifier = null, [System.ComponentModel.Description("Traversal direction: both (default), incoming, or outgoing.")] string direction = "both", [Range(1, 3), System.ComponentModel.Description("Maximum dependency traversal depth.")] int depth = 1,
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
                            return McpToolResults.InvalidArgument("The reference has no source type in this assembly.", "$.symbolIdentifier",
                                "Use an asm: reference for a type or member declared in the selected assembly source.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                        string targetTypeId;
                        try { targetTypeId = DependencyGraphScanner.GetSourceTypeId(access.Solution, targetSymbol); }
                        catch (ArgumentException)
                        {
                            return McpToolResults.InvalidArgument("The reference has no source type in this assembly.", "$.symbolIdentifier",
                                "Use an asm: reference for a type or member declared in the selected assembly source.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                        }
                        var scan = await CollectAndProjectDependencyGraphAsync(access.Solution,
                            new DependencyGraphProjectionOptions(PageSize: maxResults, TargetTypeName: targetSymbol.ToDisplayString(),
                                TargetTypeId: targetTypeId, Direction: parsedDirection, Depth: depth),
                            parsedScope, includeGenerated, CreateAssemblyHandoffFormatter(access), ct).ConfigureAwait(false);
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
                    var selectedDocument = await ResolveDependencyDocumentAsync(scope.Solution, filePath!, ct).ConfigureAwait(false);
                    if (selectedDocument.Document is null)
                        return McpToolResults.InvalidArgument("The requested assembly source file could not be selected.", "$.filePath",
                            selectedDocument.Error!, maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    if (!IsWithinDirectory(sourceRoot, selectedDocument.Document.FilePath))
                        return McpToolResults.InvalidArgument("The requested source file is outside this assembly's decompiled source.", "$.filePath",
                            "Choose a file emitted by this assembly's source tree.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    var fileTypeIds = await DependencyGraphScanner.GetDocumentTypeIdsAsync(selectedDocument.Document, ct).ConfigureAwait(false);
                    var fileScan = await CollectAndProjectDependencyGraphAsync(scope.Solution,
                        new DependencyGraphProjectionOptions(PageSize: maxResults, TargetFilePath: filePath,
                            TargetTypeIds: fileTypeIds, Direction: parsedDirection, Depth: depth),
                        parsedScope, includeGenerated, CreateAssemblyHandoffFormatter(scope.Solution, scope.Context), ct).ConfigureAwait(false);
                    var fileResponse = NavigationToolSupport.Success(fileScan, fileScan.IsTruncated,
                        "Increase maxResults, depth, or document coverage and repeat the query.");
                    var fileIdentity = AnalysisSymbolIdentity.ForAssembly(scope.Context.Origin.CanonicalPath,
                        scope.Context.Origin.ContentHash, scope.Context.Generation, scope.Context.ReferenceSnapshotHash);
                    return NavigationToolSupport.WithAssemblyMetadata(fileResponse, fileIdentity,
                        $"dependencyGraph(file={selectedDocument.Document.FilePath}, direction={parsedDirection}, depth={depth}, maxResults={maxResults}, scope={parsedScope}, includeGenerated={includeGenerated})",
                        DependencyGraphOmissions(fileScan));
                }

                if (symbolIdentifier is not null && ValidateSourceReferenceInput(symbolIdentifier, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier") is { } typeRouteError)
                    return typeRouteError;
                return await WithSource(target, async (solution, source) =>
                {
                string? typeName = null;
                string? typeId = null;
                IReadOnlyCollection<string>? fileTypeIds = null;
                IReadOnlyList<INamedTypeSymbol> outgoingRoots = [];
                if (symbolIdentifier is not null)
                {
                    var resolved = await Resolve(solution, symbolIdentifier, source.IdentityRequest, ct).ConfigureAwait(false);
                    if (resolved.Error is not null) return NavigationToolSupport.Failure(resolved.Error.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    var type = resolved.Symbol is INamedTypeSymbol namedType ? namedType : resolved.Symbol!.ContainingType;
                    typeName = type?.ToDisplayString();
                    if (type is null) return McpToolResults.InvalidArgument("The symbol has no containing type.", "$.symbolIdentifier", "Choose a type or member declared in a type.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    IReadOnlyDictionary<SyntaxTree, SourceGeneratedDocument>? generatedDocumentOwners = null;
                    var sourceTree = type.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree;
                    if (sourceTree is not null && solution.GetDocument(sourceTree) is null)
                        generatedDocumentOwners = await ExactSourceSymbolResolver.GetSourceGeneratedDocumentOwnersAsync(solution, ct).ConfigureAwait(false);
                    typeId = DependencyGraphScanner.GetSourceTypeId(solution, type,
                        source.IdentityRequest.OwnerContextFingerprints, generatedDocumentOwners);
                    outgoingRoots = [type.OriginalDefinition];
                }
                else if (filePath is not null)
                {
                    var selectedDocument = await ResolveDependencyDocumentAsync(solution, filePath, ct).ConfigureAwait(false);
                    if (selectedDocument.Document is null)
                        return McpToolResults.InvalidArgument("The requested source file could not be selected.", "$.filePath", selectedDocument.Error!, maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    outgoingRoots = await DependencyGraphScanner.GetDocumentNamedTypesAsync(selectedDocument.Document, ct).ConfigureAwait(false);
                    var generatedOwners = selectedDocument.Document is SourceGeneratedDocument
                        ? await ExactSourceSymbolResolver.GetSourceGeneratedDocumentOwnersAsync(solution, ct).ConfigureAwait(false)
                        : null;
                    fileTypeIds = outgoingRoots.Select(type => DependencyGraphScanner.GetSourceTypeId(solution, type,
                        source.IdentityRequest.OwnerContextFingerprints, generatedOwners)).Distinct(StringComparer.Ordinal).ToArray();
                }
                var scan = await CollectAndProjectSourceDependencyGraphAsync(runtime, target.CanonicalPath, solution,
                    new DependencyGraphProjectionOptions(TargetFilePath: filePath,
                        TargetTypeName: typeName, Direction: parsedDirection, Depth: depth,
                        PageSize: maxResults, TargetTypeId: typeId, TargetTypeIds: fileTypeIds),
                    parsedScope, includeGenerated, outgoingRoots, source.IdentityRequest, ct).ConfigureAwait(false);
                var response = NavigationToolSupport.Success(scan, scan.IsTruncated, scan.ContinuationInputIncomplete
                    ? "Rediscover a unique owner-bound declaration with find_symbol and repeat the query."
                    : "Increase maxResults, depth, or document coverage and repeat the query.");
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

    private static async Task<DependencyGraphPayload> CollectAndProjectDependencyGraphAsync(
        Solution solution,
        DependencyGraphProjectionOptions projectionOptions,
        SymbolScopeType scopeType,
        bool includeGenerated,
        Func<ISymbol, string?>? handoffFormatter,
        CancellationToken cancellationToken)
    {
        var symbolsByTypeId = new Dictionary<string, ISymbol>(StringComparer.Ordinal);
        var collection = await DependencyGraphScanner.CollectAsync(solution,
            new DependencyGraphCollectionOptions(scopeType, includeGenerated), cancellationToken,
            null,
            new DependencyGraphCollectionObserver(SymbolDiscovered: (typeId, symbol) => symbolsByTypeId.TryAdd(typeId, symbol)))
            .ConfigureAwait(false);
        return DependencyGraphScanner.Project(collection, projectionOptions,
            typeId => symbolsByTypeId.GetValueOrDefault(typeId), handoffFormatter);
    }

    private static async Task<DependencyGraphPayload> CollectAndProjectSourceDependencyGraphAsync(
        NavigatorHostRuntime runtime,
        string canonicalTargetPath,
        Solution solution,
        DependencyGraphProjectionOptions projectionOptions,
        SymbolScopeType scopeType,
        bool includeGenerated,
        IReadOnlyList<INamedTypeSymbol> outgoingRoots,
        SourceIdentityRequest sourceIdentityRequest,
        CancellationToken cancellationToken)
    {
        var collectionOptions = new DependencyGraphCollectionOptions(scopeType, includeGenerated);
        var operationProgress = NavigationOperationProgress.Current;
        Action<DependencyGraphProgress>? reportProgress = operationProgress is null ? null : operationProgress.Report;
        var collection = projectionOptions.Direction == DependencyGraphDirection.Outgoing
            ? await DependencyGraphOutgoingCollector.CollectAsync(runtime.DependencyGraphCache, solution,
                collectionOptions, projectionOptions, outgoingRoots, canonicalTargetPath, sourceIdentityRequest.SnapshotTicket,
                sourceIdentityRequest.OwnerContextFingerprints, cancellationToken, reportProgress).ConfigureAwait(false)
            : await runtime.DependencyGraphCache.CollectAsync(solution, collectionOptions, canonicalTargetPath,
                sourceIdentityRequest.SnapshotTicket, cancellationToken,
                sourceIdentityRequest.OwnerContextFingerprints, reportProgress).ConfigureAwait(false);
        operationProgress?.Advance(NavigationAnalysisPhase.Formatting);
        var payload = DependencyGraphScanner.Project(collection, projectionOptions);
        return await DependencyGraphScanner.FormatVisibleHandoffsAsync(collection, payload, solution,
            sourceIdentityRequest.OwnerContextFingerprints,
            symbol => sourceIdentityRequest.FormatHandoff(symbol, solution), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(Document? Document, string? Error)> ResolveDependencyDocumentAsync(
        Solution solution, string filePath, CancellationToken cancellationToken)
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

        var loadedDocuments = new List<Document>();
        foreach (var project in solution.Projects.OrderBy(project => project.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            loadedDocuments.AddRange(project.Documents);
            loadedDocuments.AddRange(await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false));
        }

        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var exactMatches = loadedDocuments
            .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
            .Where(document =>
            {
                try { return pathComparer.Equals(Path.GetFullPath(document.FilePath!), requestedPath); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
            })
            .ToList();
        if (exactMatches.Count == 1) return (exactMatches[0], null);
        if (exactMatches.Count > 1) return (null, AmbiguousDependencyDocumentHint(exactMatches));

        return (null, "The canonical path does not identify a loaded source file. Choose a loaded path relative to the solution or an absolute document path, or use find_symbol and select a unique owner-bound symbol reference.");
    }

    private static string AmbiguousDependencyDocumentHint(IEnumerable<Document> documents)
    {
        var candidates = documents.Select(document => CanonicalDependencyOwnerPath(document.Project.FilePath))
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal);
        return "The file path has multiple loaded owners. Candidate owning project paths: "
            + string.Join(", ", candidates)
            + ". Use find_symbol and select a unique owner-bound symbol reference.";
    }

    private static string? CanonicalDependencyOwnerPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var canonical = Path.GetFullPath(path).Replace('\\', '/');
            return OperatingSystem.IsWindows() ? canonical.ToUpperInvariant() : canonical;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Replace('\\', '/');
        }
    }

    [McpServerTool(Name = "resolve_type_origin", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Resolve a type name or symbol identifier to its source or metadata assembly origin.")]
    public async Task<CallToolResult> ResolveTypeOrigin([Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath, [System.ComponentModel.Description("Symbol identifier for the type, including a stable src:/asm: reference; specify this or typeName.")] string? symbolIdentifier = null,
        [System.ComponentModel.Description("Type name or stable src:/asm: reference; exactly one of this or symbolIdentifier is required.")] string? typeName = null, [Range(512, 65536), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16384,
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
                if (ValidateSourceReferenceInput(symbolIdentifier ?? typeName!, maxResponseBytes, maxResponseTokens,
                    symbolIdentifier is null ? "$.typeName" : "$.symbolIdentifier") is { } typeOriginRouteError)
                    return typeOriginRouteError;
                return await WithSource(target, async (solution, source) =>
                {
                    var identifier = symbolIdentifier ?? typeName!;
                    var resolved = await Resolve(solution, identifier, source.IdentityRequest, ct).ConfigureAwait(false);
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

    private async Task<CallToolResult> WithSource(AnalysisTarget target,
        Func<Solution, NavigationToolSupport.SourceAnalysisContext, Task<CallToolResult>> operation,
        int bytes, int? tokens, CancellationToken ct) => await NavigationToolSupport.WithSourceSolutionAsync(runtime, target,
        (solution, source, _) => operation(solution, source), bytes, tokens, ct).ConfigureAwait(false);

    private static CallToolResult? ValidateSourceReferenceInput(string originalInput, int bytes, int? tokens, string fieldPath)
    {
        var discoveryProbe = InputNormalizer.NormalizeSymbolIdentifier(originalInput);
        if (!StableSymbolReferenceCodec.TryParseReferenceInput(originalInput, discoveryProbe,
            out var reference, out var error)) return null;
        if (error is not null) return NavigationToolSupport.Failure(error.Value, bytes, tokens, fieldPath);
        if (reference is StableSymbolReference.Source) return null;
        return NavigationToolSupport.Failure(new ResultError(NavigationErrorCodes.TargetMismatch,
            "An assembly reference cannot be resolved in a source solution.",
            "Open the assembly owner targetPath and use the asm: reference there."), bytes, tokens, fieldPath);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The successful result transfers ownership of the acquired scope to the returned access; every non-transfer path disposes it in finally.")]
    private async Task<Result<AssemblySymbolReferenceAccess>> ResolveAssemblySymbolAsync(
        AnalysisTarget target, string identifier, CancellationToken ct)
    {
        var normalized = InputNormalizer.NormalizeSymbolIdentifier(identifier);
        if (AssemblySymbolInputResolver.TryRouteIdentifier(identifier, normalized, out var reference, out var routeError))
        {
            if (routeError is not null) return Result<AssemblySymbolReferenceAccess>.Failure(routeError.Value);
            if (reference is not StableSymbolReference.Assembly)
                return Result<AssemblySymbolReferenceAccess>.Failure(NavigationErrorCodes.TargetMismatch,
                    "A source reference cannot be resolved in an assembly target.",
                    "Open the source solution target and use the src: reference there.");
        }
        var opened = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
        if (!opened.IsSuccess) return Result<AssemblySymbolReferenceAccess>.Failure(opened.Error);
        var scope = opened.Value!;
        var ownershipTransferred = false;
        try
        {
            if (AfterAssemblySymbolScopeOpenedForTesting is not null)
                await AfterAssemblySymbolScopeOpenedForTesting(scope, ct).ConfigureAwait(false);
            var resolved = await AssemblySymbolInputResolver.ResolveAsync(scope, identifier, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess) return Result<AssemblySymbolReferenceAccess>.Failure(resolved.Error!);

            var access = AssemblySymbolReferenceAccess.FromResolvedScope(scope, resolved.Symbol!);
            var result = Result<AssemblySymbolReferenceAccess>.Success(access);
            ownershipTransferred = true;
            return result;
        }
        finally
        {
            if (!ownershipTransferred) await scope.DisposeAsync().ConfigureAwait(false);
        }
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
        var internalRootHandoff = AssemblyReferenceFormatting.Create(handoffOwnerScope)(session.HandoffSymbol);
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

    private static Func<ISymbol, string?> CreateAssemblyHandoffFormatter(AssemblySymbolReferenceAccess access)
        => AssemblyReferenceFormatting.Create(access.ScopeValue);

    private static Func<ISymbol, string?> CreateAssemblyHandoffFormatter(Solution solution, AssemblyContext context)
        => AssemblyReferenceFormatting.Create(solution, context);

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
        SourceIdentityRequest sourceIdentity, CancellationToken ct)
    {
        var result = await SourceSymbolResolver.ResolveAsync(solution, identifier, sourceIdentity.Identity, sourceIdentity, ct).ConfigureAwait(false);
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
