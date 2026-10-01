using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Diagnostics;
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
    [McpServerTool(Name = "get_call_tree", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Trace incoming, outgoing, or combined call relationships from a source or assembly symbol.")]
    public async Task<CallToolResult> GetCallTree([Required] string targetPath, [Required] string symbolIdentifier,
        [System.ComponentModel.Description("Traversal direction: incoming (default), outgoing, or both.")] string direction = "incoming", [Range(1, 5)] int depth = 2, [Range(1, 250)] int topN = 10,
        [System.ComponentModel.Description("Rendering: ascii (default) or mermaid.")] string format = "ascii", bool includeBcl = false, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", bool includeGenerated = false,
        bool includeReferences = false, [System.ComponentModel.Description("For assembly targets, include navigation/decompilation and graph-expansion diagnostics. Default false; source targets do not add this section.")] bool includeDiagnostics = false,
        [Range(512, 65536)] int maxResponseBytes = 32768,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default)
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
                    var body = format == "mermaid" ? CallTreeMermaidRenderer.RenderMermaid(graph) : CallGraphTextRenderer.RenderAscii(graph);
                    var diagnosticScope = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
                    if (!diagnosticScope.IsSuccess)
                        return NavigationToolSupport.Failure(diagnosticScope.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
                    IReadOnlyList<string> sessionDiagnostics;
                    await using (var scopeAccess = diagnosticScope.Value!) sessionDiagnostics = scopeAccess.Context.Diagnostics;
                    body = AppendCallTreeDiagnostics(body, graph, sessionDiagnostics, includeDiagnostics);
                    return NavigationToolSupport.SuccessText(body, graph.Truncated,
                        graph.Truncated ? "Increase depth or topN and repeat the query." : null);
                }
                return await WithSource(target, async solution =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(solution, symbol.Symbol!, depth, topN, parsedDirection, includeBcl, scope, includeGenerated), ct).ConfigureAwait(false);
                var body = format == "mermaid" ? CallTreeMermaidRenderer.RenderMermaid(graph) : CallGraphTextRenderer.RenderAscii(graph);
                return NavigationToolSupport.SuccessText(body, graph.Truncated,
                    graph.Truncated ? "Increase depth or topN and repeat the query." : null);
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "find_references", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Find source locations that reference a symbol, optionally traversing bounded assembly references.")]
    public async Task<CallToolResult> FindReferences([Required] string targetPath, [Required] string symbolIdentifier,
        [Range(1, 3)] int depth = 1, [Range(1, 50)] int maxResults = 50, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all",
        bool includeGenerated = false, bool includeReferences = false, [Range(512, 65536)] int maxResponseBytes = 16384,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default)
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
                        return NavigationToolSupport.Success(closure.References!, closure.IsTruncated, closure.NextAction);
                    }
                    var accessResult = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                    if (!accessResult.IsSuccess) return NavigationToolSupport.Failure(accessResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var access = accessResult.Value!;
                    var result = await FindReferencesResolver.FindReferencesAsync(access.Symbol, access.Solution,
                        maxResults, depth, ct, scope: scope, includeGenerated: includeGenerated,
                        handoffFormatter: CreateAssemblyHandoffFormatter(access), ownerTargetPath: access.Origin.CanonicalPath).ConfigureAwait(false);
                    return NavigationToolSupport.Success(result,
                        result.IsTruncated || result.IsTruncatedByNodeLimit || result.IsDepthClamped,
                        "Increase depth or maxResults and repeat the query.");
                }
                return await WithSource(target, async solution =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var result = await FindReferencesResolver.FindReferencesAsync(symbol.Symbol!, solution, maxResults, depth, ct,
                    scope: scope, includeGenerated: includeGenerated).ConfigureAwait(false);
                return NavigationToolSupport.Success(result, result.IsTruncated || result.IsTruncatedByNodeLimit || result.IsDepthClamped,
                    "Increase depth or maxResults and repeat the query.");
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "get_type_hierarchy", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Show base types, interfaces, and derived types for a selected type.")]
    public async Task<CallToolResult> GetTypeHierarchy([Required] string targetPath, [Required] string symbolIdentifier,
        [Range(1, 1000)] int maxResults = 50, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", bool includeGenerated = false,
        [Range(512, 65536)] int maxResponseBytes = 16384, [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        string? operationToken = null, string? continuationToken = null, CancellationToken cancellationToken = default)
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
                    return NavigationToolSupport.Success(assemblyResult, assemblyResult.IsTruncated,
                        "Increase maxResults and repeat the query.");
                }
                return await WithSource(target, async solution =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                if (symbol.Symbol is not INamedTypeSymbol named) return Invalid("symbolIdentifier", "Resolve a named class, interface, or struct.");
                var result = await TypeHierarchyScanner.ScanAsync(named, solution, maxResults, ct, scope, includeGenerated).ConfigureAwait(false);
                if (!result.IsSuccess) return McpToolResults.InvalidArgument(result.ErrorMessage!, "$.symbolIdentifier", "Choose a supported named type.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                return NavigationToolSupport.Success(result, result.IsTruncated, "Increase maxResults and repeat the query.");
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "find_implementations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Find concrete type or member implementations of a selected contract or virtual member.")]
    public async Task<CallToolResult> FindImplementations([Required] string targetPath, [Required] string symbolIdentifier,
        [Range(1, 1000)] int maxResults = 50, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", bool includeGenerated = false,
        [Range(512, 65536)] int maxResponseBytes = 16384, [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        string? operationToken = null, string? continuationToken = null, CancellationToken cancellationToken = default)
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
                    return NavigationToolSupport.Success(assemblyResult, assemblyResult.IsTruncated,
                        "Increase maxResults and repeat the query.");
                }
                return await WithSource(target, async solution =>
                {
                var symbol = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var result = await FindReferencesResolver.FindImplementationsAsync(symbol.Symbol!, solution, maxResults, ct, scope, includeGenerated).ConfigureAwait(false);
                if (result.ErrorMessage is not null) return McpToolResults.InvalidArgument(result.ErrorMessage, "$.symbolIdentifier", "Use an interface, abstract/virtual member, or overridable class.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                return NavigationToolSupport.Success(result, result.IsTruncated, "Increase maxResults and repeat the query.");
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "get_impact", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Summarize callers affected by a symbol or map changed declarations from a Git revision/worktree.")]
    public async Task<CallToolResult> GetImpact([Required] string targetPath, string? symbolIdentifier = null,
        string? gitRef = null, [System.ComponentModel.Description("Impact view: callers (default) or change-context for declarations changed in Git.")] string detailLevel = "callers", [Range(1, 3)] int depth = 1,
        [Range(1, 1000)] int maxResults = 50, [System.ComponentModel.Description("Maximum changed declarations; zero uses the default of 20.")] [Range(0, 100)] int maxChangedSymbols = 20,
        [System.ComponentModel.Description("Maximum test candidates per changed declaration; zero uses the default of 10.")] [Range(0, 50)] int maxTestsPerSymbol = 10, bool includeReferences = false,
        [Range(512, 65536)] int maxResponseBytes = 16384, [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        string? operationToken = null, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        maxChangedSymbols = maxChangedSymbols == 0 ? 20 : maxChangedSymbols;
        maxTestsPerSymbol = maxTestsPerSymbol == 0 ? 10 : maxTestsPerSymbol;
        if (detailLevel is not ("callers" or "change-context")) return Invalid("detailLevel", "Use callers or change-context.");
        if (symbolIdentifier is not null && gitRef is not null) return Invalid("symbolIdentifier", "Specify symbolIdentifier or gitRef, not both.");
        return await NavigationToolSupport.RouteAsync(runtime, "get_impact", targetPath,
            new { symbolIdentifier, gitRef, detailLevel, depth, maxResults, maxChangedSymbols, maxTestsPerSymbol, includeReferences },
            operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    if (symbolIdentifier is null || gitRef is not null || detailLevel == "change-context") return Invalid("symbolIdentifier", "Assembly impact requires a symbolIdentifier and does not accept gitRef or change-context.");
                    if (includeReferences)
                    {
                        var closureImpact = await AssemblyImpactClosureScanner.ScanAsync(target.CanonicalPath,
                            symbolIdentifier, depth, maxResults, ct).ConfigureAwait(false);
                        if (closureImpact.Error is { } error)
                            return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, closureImpact.ErrorField);
                        return NavigationToolSupport.Success(closureImpact.Impact!, closureImpact.IsTruncated, closureImpact.NextAction);
                    }
                    var access = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                    if (!access.IsSuccess) return NavigationToolSupport.Failure(access.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var lease = access.Value!;
                    if (!string.Equals(Path.GetFullPath(lease.Origin.CanonicalPath), target.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                        return Invalid("symbolIdentifier", "Use a handoff produced by this targetPath.");
                    var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(lease.Symbol, lease.Solution, depth, maxResults, ct,
                        handoffFormatter: CreateAssemblyHandoffFormatter(access.Value!)).ConfigureAwait(false);
                    var incomplete = impact.IsTruncated || impact.IsTruncatedByNodeLimit || impact.IsDepthClamped;
                    return NavigationToolSupport.Success(impact, incomplete, "Increase depth or maxResults and repeat the query.");
                }
                if (symbolIdentifier is not null)
                    return await WithSource(target, async solution =>
                    {
                        var symbol = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
                        if (symbol.Error is not null) return NavigationToolSupport.Failure(symbol.Error.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                        var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(symbol.Symbol!, solution, depth, maxResults, ct).ConfigureAwait(false);
                        return NavigationToolSupport.Success(impact, impact.IsTruncated || impact.IsTruncatedByNodeLimit || impact.IsDepthClamped, "Increase depth or maxResults and repeat the query.");
                    }, maxResponseBytes, maxResponseTokens, ct);
                return await BuildGitImpactAsync(target, gitRef, detailLevel, maxResults, maxChangedSymbols, maxTestsPerSymbol, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
    }

    [McpServerTool(Name = "dependency_graph", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Trace dependencies from exactly one file path or symbol identifier in the selected target.")]
    public async Task<CallToolResult> DependencyGraph([Required] string targetPath,
        string? filePath = null,
        string? symbolIdentifier = null, [System.ComponentModel.Description("Traversal direction: both (default), incoming, or outgoing.")] string direction = "both", [Range(1, 3)] int depth = 1,
        [Range(1, 500)] int maxResults = 50, [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", bool includeGenerated = false,
        [Range(512, 65536)] int maxResponseBytes = 24576, [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        string? operationToken = null, string? continuationToken = null, CancellationToken cancellationToken = default)
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
                        return NavigationToolSupport.Success(scan, scan.IsTruncated,
                            "Increase maxResults, depth, or document coverage and repeat the query.");
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
                    return NavigationToolSupport.Success(fileScan, fileScan.IsTruncated,
                        "Increase maxResults, depth, or document coverage and repeat the query.");
                }

                return await WithSource(target, async solution =>
                {
                string? typeName = null;
                string? typeId = null;
                IReadOnlyCollection<string>? fileTypeIds = null;
                if (symbolIdentifier is not null)
                {
                    var resolved = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
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
                var identity = await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
                var scan = await DependencyGraphScanner.ScanSolutionAsync(solution, ct,
                    new DependencyGraphScanOptions(PageSize: maxResults, TargetFilePath: filePath, TargetTypeName: typeName,
                        TargetTypeId: typeId, TargetTypeIds: fileTypeIds, Direction: parsedDirection, Depth: depth,
                        ScopeType: parsedScope, IncludeGenerated: includeGenerated), CreateSourceHandoffFormatter(solution, identity)).ConfigureAwait(false);
                return NavigationToolSupport.Success(scan, scan.IsTruncated, "Increase maxResults, depth, or document coverage and repeat the query.");
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
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
    public async Task<CallToolResult> ResolveTypeOrigin([Required] string targetPath, string? symbolIdentifier = null,
        [System.ComponentModel.Description("Exactly one of this type name or symbolIdentifier is required.")] string? typeName = null, [Range(512, 65536)] int maxResponseBytes = 16384,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default)
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
                    if (symbolIdentifier is not null)
                    {
                        var access = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
                        if (!access.IsSuccess) return NavigationToolSupport.Failure(access.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                        await using var lease = access.Value!;
                        input = lease.Symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                    }
                    var result = await ResolveTypeOriginScanner.ResolveAsync(new ResolveTypeOriginRequest(target.CanonicalPath, input), ct).ConfigureAwait(false);
                    return result.IsSuccess ? NavigationToolSupport.Success(result.Value!) : NavigationToolSupport.Failure(result.Error!.Value, maxResponseBytes, maxResponseTokens,
                        symbolIdentifier is null ? "$.typeName" : "$.symbolIdentifier");
                }
                return await WithSource(target, async solution =>
                {
                    var identifier = symbolIdentifier ?? typeName!;
                    var resolved = await Resolve(solution, identifier, ct).ConfigureAwait(false);
                    if (resolved.Error is { } resolutionError && resolutionError.Code != NavigationErrorCodes.SymbolNotFound)
                        return NavigationToolSupport.Failure(resolutionError, maxResponseBytes, maxResponseTokens,
                            symbolIdentifier is null ? "$.typeName" : "$.symbolIdentifier");
                    var result = await SourceTypeOriginScanner.ResolveAsync(solution, target.CanonicalPath,
                        resolved.Error is null ? resolved.Symbol : null,
                        resolved.Error is null ? null : identifier, ct).ConfigureAwait(false);
                    return result.IsSuccess
                        ? NavigationToolSupport.Success(result.Value!)
                        : NavigationToolSupport.Failure(result.Error!.Value, maxResponseBytes, maxResponseTokens,
                            symbolIdentifier is null ? "$.typeName" : "$.symbolIdentifier");
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
    }

    [McpServerTool(Name = "get_feature_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Summarize source callers and tests associated with a feature symbol.")]
    public async Task<CallToolResult> GetFeatureContext([Required] string targetPath, [Required] string symbolIdentifier,
        [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", bool includeGenerated = false, [Range(1, 50)] int maxCallers = 10,
        [Range(1, 50)] int maxTests = 10, [Range(512, 65536)] int maxResponseBytes = 24576,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolIdentifier))
            return McpToolResults.InvalidArgument("symbolIdentifier must be a non-empty symbol identifier.", "$.symbolIdentifier",
                "Provide a source symbol name, documentation ID, position, or current handoff ID.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (!TryScope(scopeType, out var scope)) return McpToolResults.InvalidArgument("scopeType is unsupported.", "$.scopeType", "Use all, production, or tests.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        return await NavigationToolSupport.RouteAsync(runtime, "get_feature_context", targetPath,
            new { symbolIdentifier, scopeType, includeGenerated, maxCallers, maxTests }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, ct) => await WithSource(target, async solution =>
            {
                var identity = await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
                var payload = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(solution, symbolIdentifier, maxCallers, maxTests, scope, identity, includeGenerated), ct).ConfigureAwait(false);
                if (payload?.Error is { } error) return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                return NavigationToolSupport.Success(payload!, payload!.CallersTruncated || payload.TestsTruncated,
                    "Increase maxCallers or maxTests and repeat the query.");
            }, maxResponseBytes, maxResponseTokens, ct), AnalysisTargetType.Project, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "get_test_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Find source tests and related context for a selected symbol.")]
    public async Task<CallToolResult> GetTestContext([Required] string targetPath, [Required] string symbolIdentifier,
        [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all", bool includeGenerated = false, [Range(1, 100)] int maxResults = 30,
        [Range(512, 65536)] int maxResponseBytes = 16384, [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        string? operationToken = null, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolIdentifier))
            return McpToolResults.InvalidArgument("symbolIdentifier must be a non-empty symbol identifier.", "$.symbolIdentifier",
                "Provide a source symbol name, documentation ID, position, or current handoff ID.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (!TryScope(scopeType, out var scope)) return McpToolResults.InvalidArgument("scopeType is unsupported.", "$.scopeType", "Use all, production, or tests.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        return await NavigationToolSupport.RouteAsync(runtime, "get_test_context", targetPath,
            new { symbolIdentifier, scopeType, includeGenerated, maxResults }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, ct) => await WithSource(target, async solution =>
            {
                var resolved = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
                if (resolved.Error is not null) return NavigationToolSupport.Failure(resolved.Error.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                var payload = await TestRecommendationBuilder.BuildAsync(resolved.Symbol!, solution, ct, includeGenerated, scope).ConfigureAwait(false);
                var fixtures = payload.TestFixtures.Take(maxResults).ToArray();
                var shown = payload with { TestFixtures = fixtures };
                var truncated = fixtures.Length < payload.TestFixtures.Count;
                return NavigationToolSupport.Success(shown, truncated, "Increase maxResults and repeat the query.");
            }, maxResponseBytes, maxResponseTokens, ct), AnalysisTargetType.Project, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CallToolResult> WithSource(AnalysisTarget target, Func<Solution, Task<CallToolResult>> operation,
        int bytes, int? tokens, CancellationToken ct) => await NavigationToolSupport.WithSourceSolutionAsync(runtime, target,
        (solution, _) => operation(solution), bytes, tokens, ct).ConfigureAwait(false);

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

    private sealed record AssemblyCallTreeOwner(string TargetPath, AssemblyNavigationSessionScope Scope, CallGraphPayload Graph, string RootKey);
    private sealed record AssemblyCallTreeSourceOwner(string TargetPath, AssemblyNavigationSessionScope Scope);


    private static async Task<CallToolResult> BuildAssemblyCallTreeWithClosureAsync(
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
        var opened = await AssemblyReferenceClosureSession.OpenAsync(target.CanonicalPath, identifier, ct).ConfigureAwait(false);
        if (opened.Error is { } openError)
            return NavigationToolSupport.Failure(openError, maxResponseBytes, maxResponseTokens, opened.ErrorField);
        await using var session = opened.Session!;

        var handoffOwnerPath = session.HandoffOwnerPath;
        var declarationId = session.DeclarationCommentId;
        var handoffOwnerScope = session.Owners.Single(owner => string.Equals(owner.TargetPath, handoffOwnerPath,
            StringComparison.OrdinalIgnoreCase)).Scope;
        var internalRootHandoff = CreateAssemblyInternalHandoffFormatter(handoffOwnerScope.Solution,
            handoffOwnerScope.Context)(session.HandoffSymbol);
        if (internalRootHandoff is null)
            return NavigationToolSupport.Failure(new ResultError(NavigationErrorCodes.StaleSnapshot,
                "The selected assembly declaration no longer resolves to source in its owner assembly.",
                "Repeat find_symbol for the current owner target and retry."), maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
        var ownerLimitReached = session.OwnerLimitReached;
        var unresolvedReferences = session.HasUnresolvedReferences;
        var failedOwners = session.HasFailedOwners;
        var sourceOwners = session.Owners.Select(owner => new AssemblyCallTreeSourceOwner(owner.TargetPath, owner.Scope)).ToList();
        var owners = new List<AssemblyCallTreeOwner>();
        var rootKey = AssemblyCallTreeNodeKey(handoffOwnerPath, declarationId);
            var frontierQueue = new Queue<(string OwnerPath, AssemblyIdentityDto Identity, string DeclarationId, int Depth)>();
            var visitedFrontiers = new HashSet<string>(StringComparer.Ordinal)
            {
                AssemblyCallTreeNodeKey(handoffOwnerPath, declarationId)
            };
            frontierQueue.Enqueue((handoffOwnerPath, session.HandoffIdentity, declarationId, 0));
            var traversalLimited = false;
            while (frontierQueue.TryDequeue(out var frontier))
            {
                ct.ThrowIfCancellationRequested();
                if (frontier.Depth >= Math.Clamp(depth, 1, 3)) continue;
                var frontierKey = AssemblyCallTreeNodeKey(frontier.OwnerPath, frontier.DeclarationId);
                foreach (var scanOwner in sourceOwners)
                {
                    ct.ThrowIfCancellationRequested();
                    var targetSymbol = string.Equals(scanOwner.TargetPath, frontier.OwnerPath, StringComparison.OrdinalIgnoreCase)
                        && AssemblyIdentityDtoMatches(scanOwner.Scope.Context.Identity, frontier.Identity)
                        ? ResolveAssemblySourceSymbolInOwner(frontier.DeclarationId, scanOwner.Scope)
                        : ResolveAssemblySymbolInCompilation(frontier.DeclarationId, frontier.Identity, scanOwner.Scope.Context.Compilation);
                    if (targetSymbol is null) continue;

                    CallGraphPayload graph;
                    try
                    {
                        graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
                            scanOwner.Scope.Solution, targetSymbol, 1, topN, direction, includeBcl, scope, includeGenerated,
                            CreateAssemblyInternalHandoffFormatter(scanOwner.Scope.Solution, scanOwner.Scope.Context)), ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException)
                    {
                        traversalLimited = true;
                        continue;
                    }
                    owners.Add(new(scanOwner.TargetPath, scanOwner.Scope, graph, frontierKey));

                    foreach (var node in graph.Nodes)
                    {
                        if (string.Equals(node.NodeId, graph.RootNodeId, StringComparison.Ordinal)) continue;
                        var resolvedOwner = ResolveAssemblyCallTreeNodeOwner(node, sourceOwners);
                        if (resolvedOwner is null)
                        {
                            if (frontier.Depth + 1 < Math.Clamp(depth, 1, 3)
                                && node.ContainingAssemblyIdentity is { } unresolvedIdentity
                                && sourceOwners.Any(candidate => AssemblyIdentityDtoMatches(candidate.Scope.Context.Identity, unresolvedIdentity)))
                                traversalLimited = true;
                            continue;
                        }
                        var nextKey = AssemblyCallTreeNodeKey(resolvedOwner.TargetPath, resolvedOwner.DeclarationId);
                        if (!visitedFrontiers.Add(nextKey)) continue;
                        if (visitedFrontiers.Count >= CallTreeBuilder.MaxCallTreeNodes)
                        {
                            traversalLimited = true;
                            continue;
                        }
                        frontierQueue.Enqueue((resolvedOwner.TargetPath, resolvedOwner.Scope.Context.Identity!,
                            resolvedOwner.DeclarationId, frontier.Depth + 1));
                    }
                }
            }

            var nodeMap = new Dictionary<string, CallGraphNode>(StringComparer.Ordinal);
            var nodeOrder = new List<string>();
            var partKeys = new List<Dictionary<string, string>>();
            CallGraphMethodHint[] methodHints = [];
            var hiddenEdges = 0;
            var localTruncated = false;
            foreach (var (ownerIndex, owner) in owners.Select((owner, index) => (index, owner)))
            {
                var keys = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var node in owner.Graph.Nodes)
                {
                    var isRoot = string.Equals(node.NodeId, owner.Graph.RootNodeId, StringComparison.Ordinal);
                    var resolvedNodeOwner = isRoot ? null : ResolveAssemblyCallTreeNodeOwner(node, sourceOwners);
                    var nodeHandoff = isRoot ? internalRootHandoff : resolvedNodeOwner?.HandoffId;
                    var nodeOwnerPath = isRoot ? handoffOwnerPath : resolvedNodeOwner?.TargetPath;
                    var declaration = isRoot ? declarationId : resolvedNodeOwner?.DeclarationId;
                    var key = isRoot ? owner.RootKey : declaration is not null && nodeOwnerPath is not null
                        ? AssemblyCallTreeNodeKey(nodeOwnerPath, declaration)
                        : owner.TargetPath + "|" + node.SymbolId + "|" + node.NodeId;
                    keys[node.NodeId] = key;
                    var visibleNode = node with
                    {
                        OwnerTargetPath = nodeOwnerPath,
                        HandoffId = nodeHandoff,
                    };
                    if (!nodeMap.TryGetValue(key, out var prior))
                    {
                        nodeMap.Add(key, visibleNode);
                        nodeOrder.Add(key);
                    }
                    else if (string.IsNullOrWhiteSpace(prior.DisplayLine) && !string.IsNullOrWhiteSpace(visibleNode.DisplayLine)
                        || prior.HandoffId is null && visibleNode.HandoffId is not null)
                    {
                        nodeMap[key] = prior with
                        {
                            DisplayLine = string.IsNullOrWhiteSpace(prior.DisplayLine) ? visibleNode.DisplayLine : prior.DisplayLine,
                            HandoffId = prior.HandoffId ?? visibleNode.HandoffId,
                            OwnerTargetPath = prior.OwnerTargetPath ?? visibleNode.OwnerTargetPath,
                        };
                    }
                }
                partKeys.Add(keys);
                hiddenEdges += owner.Graph.HiddenEdgeCount;
                localTruncated |= owner.Graph.Truncated;
                if (methodHints.Length == 0 && owner.Graph.MethodHints is { Count: > 0 }) methodHints = owner.Graph.MethodHints.ToArray();
            }

            if (nodeMap.TryGetValue(rootKey, out var rootNode))
                nodeMap[rootKey] = rootNode with { HandoffId = internalRootHandoff, OwnerTargetPath = handoffOwnerPath };
            var mergedEdgesByKey = new List<(string FromKey, string ToKey, CallGraphEdge Edge)>();
            foreach (var (partIndex, owner) in owners.Select((owner, index) => (index, owner)))
            {
                var keys = partKeys[partIndex];
                foreach (var edge in owner.Graph.Edges)
                {
                    if (!keys.TryGetValue(edge.FromNodeId, out var fromKey) || !keys.TryGetValue(edge.ToNodeId, out var toKey))
                    {
                        hiddenEdges++;
                        continue;
                    }
                    var existingIndex = mergedEdgesByKey.FindIndex(item => item.FromKey == fromKey && item.ToKey == toKey
                        && string.Equals(item.Edge.DispatchKind, edge.DispatchKind, StringComparison.Ordinal));
                    if (existingIndex < 0) mergedEdgesByKey.Add((fromKey, toKey, edge));
                    else
                    {
                        var existing = mergedEdgesByKey[existingIndex];
                        mergedEdgesByKey[existingIndex] = (fromKey, toKey, existing.Edge with
                        {
                            CallSites = existing.Edge.CallSites.Concat(edge.CallSites).Distinct().ToArray()
                        });
                    }
                }
            }

            var cappedFanout = ApplyGlobalProjection(mergedEdgesByKey, rootKey, direction, depth, topN);
            mergedEdgesByKey = cappedFanout.Edges;
            hiddenEdges += cappedFanout.HiddenCount;
            if (mergedEdgesByKey.Count > CallTreeBuilder.MaxCallTreeNodes)
            {
                hiddenEdges += mergedEdgesByKey.Count - CallTreeBuilder.MaxCallTreeNodes;
                mergedEdgesByKey = mergedEdgesByKey.Take(CallTreeBuilder.MaxCallTreeNodes).ToList();
            }

            var visibleNodeKeys = mergedEdgesByKey.SelectMany(edge => new[] { edge.FromKey, edge.ToKey })
                .Append(rootKey).ToHashSet(StringComparer.Ordinal);
            var projectedNodeKeys = nodeOrder.Where(visibleNodeKeys.Contains).ToArray();
            var selectedNodeKeys = projectedNodeKeys.Take(CallTreeBuilder.MaxCallTreeNodes).ToArray();
            if (projectedNodeKeys.Length > selectedNodeKeys.Length)
            {
                hiddenEdges += mergedEdgesByKey.Count(edge => !selectedNodeKeys.Contains(edge.FromKey, StringComparer.Ordinal)
                    || !selectedNodeKeys.Contains(edge.ToKey, StringComparer.Ordinal));
                mergedEdgesByKey = mergedEdgesByKey.Where(edge => selectedNodeKeys.Contains(edge.FromKey, StringComparer.Ordinal)
                    && selectedNodeKeys.Contains(edge.ToKey, StringComparer.Ordinal)).ToList();
            }
            var selectedNodeIds = selectedNodeKeys.Select((key, index) => (key, id: $"n{index + 1}"))
                .ToDictionary(item => item.key, item => item.id, StringComparer.Ordinal);
            var outputNodes = selectedNodeKeys.Select(key =>
            {
                var node = nodeMap[key];
                return node with { NodeId = selectedNodeIds[key], HandoffId = ExternalizeInternalAssemblyHandoff(node.HandoffId) };
            }).ToArray();
            var mergedEdges = mergedEdgesByKey.Where(edge => selectedNodeIds.ContainsKey(edge.FromKey) && selectedNodeIds.ContainsKey(edge.ToKey))
                .Select(edge => edge.Edge with { FromNodeId = selectedNodeIds[edge.FromKey], ToNodeId = selectedNodeIds[edge.ToKey] })
                .ToArray();

            var crossOwnerDepthNotTraversed = traversalLimited;
            var truncated = localTruncated || ownerLimitReached || failedOwners || unresolvedReferences
                || crossOwnerDepthNotTraversed || nodeOrder.Count > selectedNodeKeys.Length || hiddenEdges > 0;
            var graphResult = new CallGraphPayload(selectedNodeKeys.Length > 0 ? selectedNodeIds[selectedNodeKeys[0]] : string.Empty,
                outputNodes, mergedEdges, methodHints, truncated, hiddenEdges, owners.Sum(owner => owner.Graph.PendingNodeCount));
            var body = format == "mermaid" ? CallTreeMermaidRenderer.RenderMermaid(graphResult) : CallGraphTextRenderer.RenderAscii(graphResult);
            body = AppendCallTreeDiagnostics(body, graphResult, owners.SelectMany(owner => owner.Scope.Context.Diagnostics), includeDiagnostics);
            var nextAction = crossOwnerDepthNotTraversed
                ? "Some reachable owner symbols could not be mapped or expanded within the bounded reference closure; inspect unresolved or unsupported references, then repeat the query."
                : failedOwners || unresolvedReferences || ownerLimitReached
                    ? "The bounded reference-source closure is incomplete; inspect unresolved or unsupported references, then repeat the query."
                    : truncated ? "Increase topN or reduce the graph scope and repeat the query." : null;
            return NavigationToolSupport.SuccessText(body, truncated, nextAction);
    }

    private static (List<(string FromKey, string ToKey, CallGraphEdge Edge)> Edges, int HiddenCount) ApplyGlobalProjection(
        List<(string FromKey, string ToKey, CallGraphEdge Edge)> edges, string rootKey, CallTreeDirection direction, int depth, int topN)
    {
        var kept = new HashSet<int>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { rootKey };
        var pending = new Queue<(string NodeKey, int Level)>();
        pending.Enqueue((rootKey, 0));
        var hidden = 0;
        while (pending.TryDequeue(out var current))
        {
            if (current.Level >= depth) continue;
            var incoming = direction is CallTreeDirection.Incoming or CallTreeDirection.Both
                ? edges.Select((edge, index) => (edge, index)).Where(item => item.edge.ToKey == current.NodeKey && !kept.Contains(item.index)).ToArray()
                : [];
            var remaining = topN;
            if (direction is CallTreeDirection.Incoming or CallTreeDirection.Both)
            {
                foreach (var item in incoming.Take(remaining))
                {
                    kept.Add(item.index);
                    remaining--;
                    EnqueueOther(current.NodeKey, current.Level, item.edge);
                }
                hidden += Math.Max(0, incoming.Length - topN);
            }
            if (direction is CallTreeDirection.Outgoing or CallTreeDirection.Both)
            {
                var outgoing = edges.Select((edge, index) => (edge, index))
                    .Where(item => item.edge.FromKey == current.NodeKey && !kept.Contains(item.index)).ToArray();
                foreach (var item in outgoing.Take(remaining))
                {
                    if (!kept.Add(item.index)) continue;
                    EnqueueOther(current.NodeKey, current.Level, item.edge);
                }
                hidden += Math.Max(0, outgoing.Length - remaining);
            }
        }
        return (edges.Where((_, index) => kept.Contains(index)).ToList(), hidden);

        void EnqueueOther(string currentNodeKey, int currentLevel, (string FromKey, string ToKey, CallGraphEdge Edge) edge)
        {
            var other = edge.FromKey == currentNodeKey ? edge.ToKey : edge.FromKey;
            if (visited.Add(other)) pending.Enqueue((other, currentLevel + 1));
        }
    }

    private static ISymbol? ResolveAssemblySymbolInCompilation(string declarationId,
        AssemblyIdentityDto sourceIdentity, Compilation compilation)
    {
        var matches = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, compilation)
            .Where(symbol => symbol.ContainingAssembly is { } assembly && AssemblyIdentityMatches(assembly.Identity, sourceIdentity))
            .Distinct(SymbolEqualityComparer.Default)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static ISymbol? ResolveAssemblySourceSymbolInOwner(string declarationId, AssemblyNavigationSessionScope ownerScope)
    {
        var matches = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, ownerScope.Context.Compilation)
            .Where(symbol => SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, ownerScope.Context.Assembly)
                && HasAssemblySourceDeclaration(symbol, ownerScope.Solution,
                    ownerScope.Context.DecompiledProjectPaths?.DecompiledSourceRoot))
            .Distinct(SymbolEqualityComparer.Default)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private sealed record AssemblyCallTreeResolvedNodeOwner(
        string TargetPath,
        AssemblyNavigationSessionScope Scope,
        string DeclarationId,
        ISymbol Symbol,
        string HandoffId);

    private static AssemblyCallTreeResolvedNodeOwner? ResolveAssemblyCallTreeNodeOwner(
        CallGraphNode node, IReadOnlyList<AssemblyCallTreeSourceOwner> sourceOwners)
    {
        if (string.IsNullOrWhiteSpace(node.SymbolId)) return null;
        SymbolHandoffIdentifier? handoff = SymbolHandoffIdentifier.TryParse(node.HandoffId ?? string.Empty, out var parsed)
            && parsed.Origin is SymbolHandoffOrigin.Assembly ? parsed : null;
        var declarationId = handoff?.DocumentationCommentId ?? node.SymbolId;
        var matches = new List<AssemblyCallTreeResolvedNodeOwner>();
        foreach (var candidate in sourceOwners)
        {
            if (handoff is null && (node.ContainingAssemblyIdentity is not { } containingIdentity
                || !AssemblyIdentityDtoMatches(candidate.Scope.Context.Identity, containingIdentity))) continue;
            var symbol = ResolveAssemblySourceSymbolInOwner(declarationId, candidate.Scope);
            if (symbol is null) continue;
            var formatter = CreateAssemblyInternalHandoffFormatter(candidate.Scope.Solution, candidate.Scope.Context);
            var internalHandoff = formatter(symbol);
            if (internalHandoff is null || handoff is not null
                && !string.Equals(internalHandoff, node.HandoffId, StringComparison.Ordinal)) continue;
            matches.Add(new(candidate.TargetPath, candidate.Scope, declarationId, symbol, internalHandoff));
            if (matches.Count > 1) return null;
        }
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string AssemblyCallTreeNodeKey(string ownerPath, string declarationId) =>
        Path.GetFullPath(ownerPath).ToUpperInvariant() + "\0" + declarationId;

    private static bool AssemblyIdentityMatches(AssemblyIdentity actual, AssemblyIdentityDto expected) =>
        string.Equals(actual.Name, expected.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(actual.Version?.ToString(), expected.Version, StringComparison.Ordinal)
        && string.Equals(string.IsNullOrWhiteSpace(actual.CultureName) ? "neutral" : actual.CultureName,
            string.IsNullOrWhiteSpace(expected.Culture) ? "neutral" : expected.Culture, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Convert.ToHexString(actual.PublicKeyToken.ToArray()), expected.PublicKeyToken, StringComparison.OrdinalIgnoreCase);

    private static bool AssemblyIdentityDtoMatches(AssemblyIdentityDto? left, AssemblyIdentityDto right) =>
        left is not null
        && string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Version, right.Version, StringComparison.Ordinal)
        && string.Equals(string.IsNullOrWhiteSpace(left.Culture) ? "neutral" : left.Culture,
            string.IsNullOrWhiteSpace(right.Culture) ? "neutral" : right.Culture, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.PublicKeyToken, right.PublicKeyToken, StringComparison.OrdinalIgnoreCase);

    private static string? ExternalizeInternalAssemblyHandoff(string? internalHandoff) =>
        string.IsNullOrWhiteSpace(internalHandoff)
            ? null
            : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalHandoff);

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
        var internalFormatter = CreateAssemblyInternalHandoffFormatter(solution, canonicalPath, contentHash,
            generation, referenceSnapshotHash, sourceRoot, assembly, compilation);
        return symbol => ExternalizeInternalAssemblyHandoff(internalFormatter(symbol));
    }

    private static Func<ISymbol, string?> CreateAssemblyInternalHandoffFormatter(Solution solution, AssemblyContext context)
        => CreateAssemblyInternalHandoffFormatter(solution, context.Origin.CanonicalPath, context.Origin.ContentHash,
            context.Generation, context.ReferenceSnapshotHash, context.DecompiledProjectPaths?.DecompiledSourceRoot,
            context.Assembly, context.Compilation);

    private static Func<ISymbol, string?> CreateAssemblyInternalHandoffFormatter(Solution solution, string canonicalPath,
        string contentHash, long generation, string referenceSnapshotHash, string? sourceRoot,
        IAssemblySymbol assembly, Compilation compilation)
    {
        var identity = AnalysisSymbolIdentity.ForAssembly(canonicalPath, contentHash, generation, referenceSnapshotHash);
        return symbol =>
        {
            if (!HasAssemblySourceDeclaration(symbol, solution, sourceRoot)) return null;
            var declarationId = DocumentationCommentId.CreateDeclarationId(symbol);
            if (string.IsNullOrWhiteSpace(declarationId)) return null;
            var owned = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, compilation)
                .Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, assembly))
                .Distinct(SymbolEqualityComparer.Default)
                .Take(2)
                .ToArray();
            if (owned.Length != 1) return null;
            return identity.FormatHandoff(owned[0]);
        };
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

    private static bool HasAssemblySourceDeclaration(ISymbol symbol, Solution solution, string? sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot)) return false;
        var root = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var location in symbol.Locations.Where(item => item.IsInSource && item.SourceTree is not null))
        {
            var document = solution.GetDocument(location.SourceTree!);
            if (document?.FilePath is not { } filePath) continue;
            var fullPath = Path.GetFullPath(filePath);
            if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static async Task<(ISymbol? Symbol, ResultError? Error)> Resolve(Solution solution, string identifier, CancellationToken ct)
    {
        var identity = await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
        var result = await SourceSymbolResolver.ResolveAsync(solution, identifier, identity, ct).ConfigureAwait(false);
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

    private async Task<CallToolResult> BuildGitImpactAsync(AnalysisTarget target, string? gitRef, string detailLevel,
        int maxResults, int maxChangedSymbols, int maxTestsPerSymbol, int maxResponseBytes, int? maxResponseTokens, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(target.CanonicalPath)!;
        var rootResult = await RunGitAsync(directory, ["rev-parse", "--show-toplevel"], ct).ConfigureAwait(false);
        if (rootResult.ExitCode != 0)
            return NavigationToolSupport.Success(new { impactStatus = "not_git_repository", completeness = "not_applicable", totalChangedFiles = 0, totalUnresolvedFiles = 0, totalChangedSymbols = 0, changedFiles = Array.Empty<string>(), unresolvedFiles = Array.Empty<string>(), analyzedSymbols = Array.Empty<object>() });
        var gitRoot = Path.GetFullPath(rootResult.StandardOutput.Trim());
        var reference = gitRef ?? "HEAD";
        var verification = await RunGitAsync(gitRoot, ["rev-parse", "--verify", "--end-of-options", reference + "^{commit}"], ct).ConfigureAwait(false);
        if (verification.ExitCode != 0)
            return McpToolResults.Recoverable(NavigationErrorCodes.InvalidArgument, "gitRef could not be resolved.",
                "Provide a commit, branch, or tag that exists in this repository.", fieldPath: "$.gitRef",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);

        var resolvedCommit = verification.StandardOutput.Trim();
        var diff = await RunGitAsync(gitRoot, ["diff", "--no-ext-diff", "--no-textconv", "--name-only", "-z", "--diff-filter=ACMRD", resolvedCommit, "--"], ct).ConfigureAwait(false);
        if (diff.ExitCode != 0)
            return McpToolResults.Recoverable(NavigationErrorCodes.InvalidArgument, "Git could not compute the requested change set.", "Check gitRef and repository availability, then retry.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        var untracked = gitRef is null
            ? await RunGitAsync(gitRoot, ["ls-files", "--others", "--exclude-standard", "-z"], ct).ConfigureAwait(false)
            : (0, string.Empty, string.Empty);
        if (gitRef is null && untracked.Item1 != 0)
            return McpToolResults.Recoverable(NavigationErrorCodes.TargetUnreadable, "Git could not enumerate untracked files.", "Check repository permissions and retry.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var untrackedPaths = ReadNulDelimitedGitPaths(untracked.Item2).ToHashSet(pathComparer);
        var changedPaths = ReadNulDelimitedGitPaths(diff.StandardOutput).Concat(untrackedPaths)
            .Distinct(pathComparer).OrderBy(path => path, pathComparer).ToArray();
        if (changedPaths.Length == 0)
            return NavigationToolSupport.Success(new { impactStatus = "clean_worktree", completeness = "complete", totalChangedFiles = 0, totalUnresolvedFiles = 0, totalChangedSymbols = 0, changedFiles = Array.Empty<string>(), unresolvedFiles = Array.Empty<string>(), analyzedSymbols = Array.Empty<object>() });
        return await WithSource(target, async solution =>
        {
            var byPath = solution.Projects.SelectMany(project => project.Documents)
                .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
                .GroupBy(document => Path.GetFullPath(document.FilePath!), pathComparer)
                .ToDictionary(group => group.Key, group => group.OrderBy(document => document.Project.FilePath, pathComparer).ToArray(), pathComparer);
            var changedSymbols = new List<GitChangedSymbol>();
            var seenSymbols = new HashSet<(ProjectId ProjectId, string DeclarationId)>();
            var unresolvedFiles = new List<string>();
            var limit = detailLevel == "change-context" ? Math.Clamp(maxChangedSymbols, 1, 100) : 1000;
            var moreSymbolsExist = false;
            foreach (var relative in changedPaths)
            {
                ct.ThrowIfCancellationRequested();
                string fullPath;
                try { fullPath = Path.GetFullPath(Path.Combine(gitRoot, relative)); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    unresolvedFiles.Add(relative);
                    continue;
                }
                if (!IsWithin(gitRoot, fullPath) || !byPath.TryGetValue(fullPath, out var documents))
                {
                    unresolvedFiles.Add(relative);
                    continue;
                }

                IReadOnlyList<ChangedLineRange> ranges;
                var hasDeletionOnlyHunk = false;
                if (untrackedPaths.Contains(relative))
                {
                    ranges = [];
                    foreach (var document in documents)
                    {
                        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                        if (root is null) continue;
                        var lastLine = root.SyntaxTree.GetLineSpan(root.FullSpan).EndLinePosition.Line + 1;
                        if (lastLine > 0) ranges = ranges.Append(new ChangedLineRange(1, lastLine)).ToArray();
                    }
                }
                else
                {
                    var fileDiff = await RunGitAsync(gitRoot,
                        ["diff", "--no-ext-diff", "--no-textconv", "--no-color", "--unified=0", resolvedCommit, "--", ":(literal)" + relative], ct).ConfigureAwait(false);
                    if (fileDiff.ExitCode != 0)
                    {
                        unresolvedFiles.Add(relative);
                        continue;
                    }
                    var parsedDiff = ParseGitNewLineRanges(fileDiff.StandardOutput);
                    ranges = parsedDiff.Ranges;
                    hasDeletionOnlyHunk = parsedDiff.HasDeletionOnlyHunk;
                }

                if (hasDeletionOnlyHunk) unresolvedFiles.Add(relative);

                if (ranges.Count == 0)
                {
                    // Pure deletions and non-text changes have no current source span to attribute safely.
                    unresolvedFiles.Add(relative);
                    continue;
                }

                var fileHadMappedChange = false;
                foreach (var document in documents)
                {
                    var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                    var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                    if (root is null || model is null) { unresolvedFiles.Add(relative); continue; }
                    var declarations = GetImpactDeclarationNodes(root)
                        .Select(node => (Node: node, Symbol: model.GetDeclaredSymbol(node, ct)))
                        .Where(candidate => candidate.Symbol is not null
                            && (detailLevel == "change-context" || IsCallerImpactSymbol(candidate.Symbol)))
                        .ToArray();
                    foreach (var range in ranges)
                    {
                        for (var line = range.StartLine; line <= range.EndLine; line++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var matching = declarations.Where(candidate => ContainsLine(candidate.Node, line)).ToArray();
                            if (matching.Length == 0)
                            {
                                unresolvedFiles.Add(relative);
                                continue;
                            }
                            fileHadMappedChange = true;
                            var innermost = matching.Where(candidate => !matching.Any(other =>
                                    other.Node != candidate.Node && candidate.Node.Span.Length > other.Node.Span.Length
                                    && candidate.Node.Span.Contains(other.Node.Span)))
                                .OrderBy(candidate => candidate.Node.SpanStart).ToArray();
                            foreach (var candidate in innermost)
                            {
                                var symbol = candidate.Symbol!;
                                var declarationId = DocumentationCommentId.CreateDeclarationId(symbol)
                                    ?? $"{symbol.Kind}:{symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}:{candidate.Node.SpanStart}";
                                if (!seenSymbols.Add((document.Project.Id, declarationId))) continue;
                                if (changedSymbols.Count < limit)
                                    changedSymbols.Add(new(symbol, document.Project.Id, document.Project.FilePath ?? string.Empty,
                                        document.FilePath ?? fullPath, line, declarationId));
                                else moreSymbolsExist = true;
                            }
                        }
                    }
                }
                if (!fileHadMappedChange) unresolvedFiles.Add(relative);
            }
            var entries = new List<object>();
            var directCallerCount = 0;
            var analysisIncomplete = false;
            var testsIncomplete = false;
            foreach (var change in changedSymbols)
            {
                ct.ThrowIfCancellationRequested();
                var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(change.Symbol, solution, maxDepth: 1, maxResults: maxResults, ct: ct).ConfigureAwait(false);
                directCallerCount += impact.DirectCallersCount;
                analysisIncomplete |= impact.IsTruncated || impact.IsTruncatedByNodeLimit || impact.IsDepthClamped;
                if (detailLevel == "change-context")
                {
                    var tests = await TestRecommendationBuilder.BuildAsync(change.Symbol, solution, ct).ConfigureAwait(false);
                    var testLimit = Math.Clamp(maxTestsPerSymbol, 1, 50);
                    testsIncomplete |= tests.TestFixtures.Count > testLimit;
                    entries.Add(new { symbol = change.Symbol.ToDisplayString(), ownerProjectPath = change.ProjectPath,
                        changedFile = change.FilePath, changedLine = change.Line, impact,
                        testCandidates = tests.TestFixtures.Take(testLimit).ToArray(), testEvidence = tests.EvidenceMode });
                }
                else entries.Add(new { symbol = change.Symbol.ToDisplayString(), ownerProjectPath = change.ProjectPath,
                    changedFile = change.FilePath, changedLine = change.Line, impact });
            }
            var incomplete = changedPaths.Length > maxResults || moreSymbolsExist || unresolvedFiles.Count > 0 || analysisIncomplete || testsIncomplete;
            var distinctUnresolved = unresolvedFiles.Distinct(pathComparer).ToArray();
            var status = detailLevel == "callers" && directCallerCount == 0 && !incomplete ? "diff_without_callsite_impact" : "changes_found";
            return NavigationToolSupport.Success(new { impactStatus = status, completeness = incomplete ? "partial" : "complete",
                totalChangedFiles = changedPaths.Length, totalUnresolvedFiles = distinctUnresolved.Length, totalChangedSymbols = seenSymbols.Count,
                changedFiles = changedPaths.Take(maxResults).ToArray(), unresolvedFiles = distinctUnresolved, analyzedSymbols = entries }, incomplete,
                "Increase maxResults or maxChangedSymbols and repeat the query; inspect listed unresolved files before treating an empty symbol list as complete.");
        }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
    }

    private static IEnumerable<Microsoft.CodeAnalysis.SyntaxNode> GetImpactDeclarationNodes(Microsoft.CodeAnalysis.SyntaxNode root)
    {
        foreach (var node in root.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax field:
                    foreach (var variable in field.Declaration.Variables) yield return variable;
                    break;
                case Microsoft.CodeAnalysis.CSharp.Syntax.EventFieldDeclarationSyntax eventField:
                    foreach (var variable in eventField.Declaration.Variables) yield return variable;
                    break;
                case Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax:
                case Microsoft.CodeAnalysis.CSharp.Syntax.DelegateDeclarationSyntax:
                case Microsoft.CodeAnalysis.CSharp.Syntax.BaseMethodDeclarationSyntax:
                case Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax:
                case Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax:
                case Microsoft.CodeAnalysis.CSharp.Syntax.IndexerDeclarationSyntax:
                case Microsoft.CodeAnalysis.CSharp.Syntax.EventDeclarationSyntax:
                    yield return node;
                    break;
            }
        }
    }

    private static bool ContainsLine(Microsoft.CodeAnalysis.SyntaxNode node, int oneBasedLine)
    {
        var lineSpan = node.SyntaxTree.GetLineSpan(node.Span);
        return lineSpan.StartLinePosition.Line + 1 <= oneBasedLine && lineSpan.EndLinePosition.Line + 1 >= oneBasedLine;
    }

    private static bool IsCallerImpactSymbol(ISymbol? symbol) => symbol is IMethodSymbol method
        && method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.StaticConstructor
        && method.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.Protected
            or Accessibility.ProtectedOrInternal or Accessibility.ProtectedAndInternal;

    private static IReadOnlyList<string> ReadNulDelimitedGitPaths(string output) =>
        output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static (IReadOnlyList<ChangedLineRange> Ranges, bool HasDeletionOnlyHunk) ParseGitNewLineRanges(string diff)
    {
        var ranges = new List<ChangedLineRange>();
        var hasDeletionOnlyHunk = false;
        foreach (var line in diff.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("@@ ", StringComparison.Ordinal)) continue;
            var plus = line.IndexOf('+');
            var end = plus < 0 ? -1 : line.IndexOf(' ', plus);
            var minus = line.IndexOf('-');
            if (plus < 0 || end < 0 || minus < 0) continue;
            var oldEnd = line.IndexOf(' ', minus);
            if (oldEnd < 0) continue;
            var oldCoordinates = line.AsSpan(minus + 1, oldEnd - minus - 1);
            var oldComma = oldCoordinates.IndexOf(',');
            var oldCount = 1;
            if (oldComma >= 0 && !int.TryParse(oldCoordinates[(oldComma + 1)..], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out oldCount)) continue;
            var coordinates = line.AsSpan(plus + 1, end - plus - 1);
            var comma = coordinates.IndexOf(',');
            var startSpan = comma < 0 ? coordinates : coordinates[..comma];
            if (!int.TryParse(startSpan, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var startLine)) continue;
            var count = 1;
            if (comma >= 0 && !int.TryParse(coordinates[(comma + 1)..], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out count)) continue;
            if (count > 0) ranges.Add(new ChangedLineRange(startLine, startLine + count - 1));
            else if (oldCount > 0) hasDeletionOnlyHunk = true;
        }
        return (ranges, hasDeletionOnlyHunk);
    }

    private sealed record GitChangedSymbol(ISymbol Symbol, ProjectId ProjectId, string ProjectPath, string FilePath, int Line, string DeclarationId);
    private readonly record struct ChangedLineRange(int StartLine, int EndLine);

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Git.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            throw;
        }
        return (process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
