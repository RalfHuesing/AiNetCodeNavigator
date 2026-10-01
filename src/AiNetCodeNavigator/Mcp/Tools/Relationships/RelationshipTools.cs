using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
    public async Task<CallToolResult> GetCallTree([Required] string targetPath, [Required] string symbolIdentifier,
        string direction = "incoming", [Range(1, 5)] int depth = 2, [Range(1, 250)] int topN = 10,
        string format = "ascii", bool includeBcl = false, string scopeType = "all", bool includeGenerated = false,
        bool includeReferences = false, [Range(512, 65536)] int maxResponseBytes = 32768,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (!TryDirection(direction, out var parsedDirection)) return Invalid("direction", "Use incoming, outgoing, or both.");
        if (format is not ("ascii" or "mermaid")) return Invalid("format", "Use ascii or mermaid.");
        if (!TryScope(scopeType, out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "get_call_tree", targetPath,
            new { symbolIdentifier, direction, depth, topN, format, includeBcl, scopeType, includeGenerated, includeReferences },
            operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    if (includeReferences)
                        return await BuildAssemblyCallTreeWithClosureAsync(target, symbolIdentifier, depth, topN,
                            parsedDirection, includeBcl, scope, includeGenerated, format, maxResponseBytes,
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
                    var incomplete = graph.Truncated || includeReferences;
                    return NavigationToolSupport.SuccessText(body, incomplete,
                        includeReferences
                            ? "Referenced assembly source is not included yet; treat this result as root-only and incomplete."
                            : graph.Truncated ? "Increase depth or topN and repeat the query." : null);
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
    public async Task<CallToolResult> FindReferences([Required] string targetPath, [Required] string symbolIdentifier,
        [Range(1, 3)] int depth = 1, [Range(1, 50)] int maxResults = 50, string scopeType = "all",
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
                        return await FindAssemblyReferencesWithClosureAsync(target, symbolIdentifier, maxResults, depth,
                            scope, includeGenerated, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
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
    public async Task<CallToolResult> GetTypeHierarchy([Required] string targetPath, [Required] string symbolIdentifier,
        [Range(1, 1000)] int maxResults = 50, string scopeType = "all", bool includeGenerated = false,
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
    public async Task<CallToolResult> FindImplementations([Required] string targetPath, [Required] string symbolIdentifier,
        [Range(1, 1000)] int maxResults = 50, string scopeType = "all", bool includeGenerated = false,
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
    public async Task<CallToolResult> GetImpact([Required] string targetPath, string? symbolIdentifier = null,
        string? gitRef = null, string detailLevel = "callers", [Range(1, 3)] int depth = 1,
        [Range(1, 1000)] int maxResults = 50, [Range(1, 100)] int maxChangedSymbols = 20,
        [Range(1, 50)] int maxTestsPerSymbol = 10, bool includeReferences = false,
        [Range(512, 65536)] int maxResponseBytes = 16384, [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        string? operationToken = null, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
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
                    var access = await AssemblySymbolHandoffResolver.ResolveAsync(symbolIdentifier, ct).ConfigureAwait(false);
                    if (!access.IsSuccess) return NavigationToolSupport.Failure(access.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var lease = access.Value!;
                    if (!string.Equals(Path.GetFullPath(lease.Origin.CanonicalPath), target.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                        return Invalid("symbolIdentifier", "Use a handoff produced by this targetPath.");
                    var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(lease.Symbol, lease.Solution, depth, maxResults, ct,
                        handoffFormatter: CreateAssemblyHandoffFormatter(access.Value!)).ConfigureAwait(false);
                    var incomplete = impact.IsTruncated || impact.IsTruncatedByNodeLimit || impact.IsDepthClamped || includeReferences;
                    return NavigationToolSupport.Success(impact, incomplete,
                        includeReferences
                            ? "Referenced assembly source is not included yet; treat this result as root-only and incomplete."
                            : "Increase depth or maxResults and repeat the query.");
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
    public async Task<CallToolResult> DependencyGraph([Required] string targetPath,
        string? filePath = null,
        string? symbolIdentifier = null, string direction = "both", [Range(1, 3)] int depth = 1,
        [Range(1, 500)] int maxResults = 50, string scopeType = "all", bool includeGenerated = false,
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
    public async Task<CallToolResult> ResolveTypeOrigin([Required] string targetPath, string? symbolIdentifier = null,
        string? typeName = null, [Range(512, 65536)] int maxResponseBytes = 16384,
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
                        var access = await AssemblySymbolHandoffResolver.ResolveAsync(symbolIdentifier, ct).ConfigureAwait(false);
                        if (!access.IsSuccess) return NavigationToolSupport.Failure(access.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                        await using var lease = access.Value!;
                        if (!string.Equals(Path.GetFullPath(lease.Origin.CanonicalPath), target.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                            return Invalid("symbolIdentifier", "Use a handoff produced by this targetPath.");
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
    public async Task<CallToolResult> GetFeatureContext([Required] string targetPath, [Required] string symbolIdentifier,
        string scopeType = "all", bool includeGenerated = false, [Range(1, 50)] int maxCallers = 10,
        [Range(1, 50)] int maxTests = 10, [Range(512, 65536)] int maxResponseBytes = 24576,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default)
    {
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
    public async Task<CallToolResult> GetTestContext([Required] string targetPath, [Required] string symbolIdentifier,
        string scopeType = "all", bool includeGenerated = false, [Range(1, 100)] int maxResults = 30,
        [Range(512, 65536)] int maxResponseBytes = 16384, [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        string? operationToken = null, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
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

    private const int MaxReferenceClosureAssemblies = 32;

    private sealed record AssemblyCallTreeOwner(string TargetPath, AssemblyNavigationSessionScope Scope, CallGraphPayload Graph);
    private sealed record AssemblyCallTreeSourceOwner(string TargetPath, AssemblyNavigationSessionScope Scope);

    private sealed record AssemblyReferenceOwner(
        string TargetPath,
        AssemblyNavigationSessionScope Scope,
        ISymbol TargetSymbol,
        FindReferencesResult FirstPass);

    private static async Task<CallToolResult> FindAssemblyReferencesWithClosureAsync(
        AnalysisTarget target,
        string identifier,
        int maxResults,
        int depth,
        SymbolScopeType scope,
        bool includeGenerated,
        int maxResponseBytes,
        int? maxResponseTokens,
        CancellationToken ct)
    {
        var rootResult = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
        if (!rootResult.IsSuccess)
            return NavigationToolSupport.Failure(rootResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");

        await using var root = rootResult.Value!;
        var handoffResult = await AssemblySymbolHandoffResolver.ResolveAsync(identifier, ct).ConfigureAwait(false);
        if (!handoffResult.IsSuccess)
            return NavigationToolSupport.Failure(handoffResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");

        await using var handoff = handoffResult.Value!;
        var rootPath = Path.GetFullPath(root.Context.Origin.CanonicalPath);
        var handoffOwnerPath = Path.GetFullPath(handoff.Origin.CanonicalPath);
        var isRootOwner = string.Equals(rootPath, handoffOwnerPath, StringComparison.OrdinalIgnoreCase);
        var resolvedReferences = root.Context.References
            .Where(reference => reference.Resolved && !string.IsNullOrWhiteSpace(reference.ResolvedPath))
            .Select(reference => (Reference: reference, Path: Path.GetFullPath(reference.ResolvedPath!)))
            .ToArray();
        var isMemberOfClosure = resolvedReferences.Any(item =>
            string.Equals(item.Path, handoffOwnerPath, StringComparison.OrdinalIgnoreCase));
        if (!isRootOwner && !isMemberOfClosure)
            return NavigationToolSupport.Failure(new ResultError(NavigationErrorCodes.TargetMismatch,
                    "The symbol handoff is not owned by the selected assembly or its current reference snapshot.",
                    "Use a handoff returned by this assembly's find_symbol(includeReferences=true) result."),
                maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");

        var declarationId = DocumentationCommentId.CreateDeclarationId(handoff.Symbol);
        if (string.IsNullOrWhiteSpace(declarationId) || handoff.Symbol.ContainingAssembly is null)
            return McpToolResults.InvalidArgument("The selected handoff has no stable assembly declaration identity.", "$.symbolIdentifier",
                "Use a declaration returned by find_symbol(includeReferences=true).", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);

        var ownerPaths = new List<string> { rootPath };
        foreach (var item in resolvedReferences
                     .OrderBy(item => item.Reference.Depth)
                     .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (IsFrameworkReferencePath(item.Path)) continue;
            if (!ownerPaths.Contains(item.Path, StringComparer.OrdinalIgnoreCase)) ownerPaths.Add(item.Path);
        }
        var ownerLimitReached = ownerPaths.Count > MaxReferenceClosureAssemblies;
        if (ownerLimitReached)
        {
            ownerPaths = ownerPaths.Take(MaxReferenceClosureAssemblies).ToList();
            if (!ownerPaths.Contains(handoffOwnerPath, StringComparer.OrdinalIgnoreCase))
            {
                ownerPaths[^1] = handoffOwnerPath;
                ownerPaths = ownerPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        var scans = new List<AssemblyReferenceOwner>();
        var ownedScopes = new List<AssemblyNavigationSessionScope>();
        var failedOwners = new List<string>();
        try
        {
            foreach (var ownerPath in ownerPaths)
            {
                ct.ThrowIfCancellationRequested();
                AssemblyNavigationSessionScope ownerScope;
                if (string.Equals(ownerPath, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    ownerScope = root;
                }
                else
                {
                    var opened = await AssemblyNavigationSessionScope.OpenAsync(ownerPath, ct).ConfigureAwait(false);
                    if (!opened.IsSuccess)
                    {
                        failedOwners.Add(ownerPath);
                        continue;
                    }
                    ownerScope = opened.Value!;
                    ownedScopes.Add(ownerScope);
                }

                var isHandoffOwner = string.Equals(ownerPath, handoffOwnerPath, StringComparison.OrdinalIgnoreCase);
                var ownerSymbol = isHandoffOwner
                    ? ResolveAssemblySourceSymbolInOwner(declarationId, ownerScope)
                    : ResolveAssemblySymbolInCompilation(declarationId, handoff.Identity, ownerScope.Context.Compilation);
                if (ownerSymbol is null) continue;
                if (ownerScope.Context.Status is not AssemblySessionStatus.Complete) failedOwners.Add(ownerPath);

                try
                {
                    var firstPass = await FindReferencesResolver.FindReferencesAsync(ownerSymbol, ownerScope.Solution,
                        Math.Max(maxResults, 1), depth, ct, scope: scope, includeGenerated: includeGenerated,
                        handoffFormatter: CreateAssemblyInternalHandoffFormatter(ownerScope.Solution, ownerScope.Context),
                        ownerTargetPath: ownerPath).ConfigureAwait(false);
                    scans.Add(new(ownerPath, ownerScope, ownerSymbol, firstPass));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    failedOwners.Add(ownerPath);
                }
            }

            var allVisibleCandidates = scans
                .SelectMany(scan => scan.FirstPass.References)
                .OrderBy(entry => entry.Depth)
                .ThenBy(entry => entry.OwnerTargetPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.FilePath, StringComparer.Ordinal)
                .ThenBy(entry => entry.Line)
                .ThenBy(entry => entry.EnclosingSymbolName, StringComparer.Ordinal)
                .ThenBy(entry => entry.ReachedFromSymbolName, StringComparer.Ordinal)
                .ThenBy(entry => entry.Column)
                .ToArray();
            var selected = allVisibleCandidates.Take(Math.Max(maxResults, 1)).ToArray();
            var visible = selected
                .OrderBy(entry => entry.Depth)
                .ThenBy(entry => entry.OwnerTargetPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.FilePath, StringComparer.Ordinal)
                .ThenBy(entry => entry.Line)
                .ThenBy(entry => entry.Column)
                .ThenBy(entry => entry.EnclosingSymbolName, StringComparer.Ordinal)
                .Select(entry => entry with
                {
                    EnclosingSymbolHandoffId = ExternalizeInternalAssemblyHandoff(entry.EnclosingSymbolHandoffId),
                    ReachedFromSymbolHandoffId = ExternalizeInternalAssemblyHandoff(entry.ReachedFromSymbolHandoffId),
                })
                .ToList();

            var totalCount = scans.Sum(scan => scan.FirstPass.TotalCount);
            var truncatedByNodeLimit = scans.Any(scan => scan.FirstPass.IsTruncatedByNodeLimit);
            var depthClamped = scans.Any(scan => scan.FirstPass.IsDepthClamped);
            var crossOwnerDepthNotTraversed = depth > 1;
            var isTruncated = crossOwnerDepthNotTraversed || ownerLimitReached || failedOwners.Count > 0
                || root.Context.References.Any(reference => !reference.Resolved)
                || scans.Any(scan => scan.FirstPass.IsTruncated || scan.FirstPass.IsTruncatedByNodeLimit || scan.FirstPass.IsDepthClamped)
                || totalCount > maxResults;
            var result = new FindReferencesResult(
                TargetSymbolName: handoff.Symbol.Name,
                TargetKind: handoff.Symbol.Kind.ToString().ToLowerInvariant(),
                References: visible,
                TotalCount: totalCount,
                IsTruncated: isTruncated,
                RequestedDepth: depth,
                EffectiveDepth: scans.Count == 0 ? depth : scans.Min(scan => scan.FirstPass.EffectiveDepth),
                VisitedSymbolCount: scans.Sum(scan => scan.FirstPass.VisitedSymbolCount),
                IsTruncatedByNodeLimit: truncatedByNodeLimit,
                IsDepthClamped: depthClamped,
                EffectiveNodeLimit: scans.Count == 0 ? FindReferencesResolver.DefaultMaxVisitedSymbols : scans.Min(scan => scan.FirstPass.EffectiveNodeLimit));
            var nextAction = crossOwnerDepthNotTraversed
                ? "Cross-assembly caller chaining beyond each owner solution is not composed yet; this result covers direct owner scans and is incomplete."
                : failedOwners.Count > 0 || ownerLimitReached || root.Context.References.Any(reference => !reference.Resolved)
                    ? "The bounded reference-source closure is incomplete; inspect unresolved or unsupported references, then repeat the query."
                    : "Increase maxResults or depth and repeat the query.";
            return NavigationToolSupport.Success(result, isTruncated, nextAction);
        }
        finally
        {
            foreach (var ownerScope in ownedScopes) await ownerScope.DisposeAsync().ConfigureAwait(false);
        }
    }

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
        int maxResponseBytes,
        int? maxResponseTokens,
        CancellationToken ct)
    {
        var rootResult = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
        if (!rootResult.IsSuccess)
            return NavigationToolSupport.Failure(rootResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
        await using var root = rootResult.Value!;

        var handoffResult = await AssemblySymbolHandoffResolver.ResolveAsync(identifier, ct).ConfigureAwait(false);
        if (!handoffResult.IsSuccess)
            return NavigationToolSupport.Failure(handoffResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
        await using var handoff = handoffResult.Value!;

        var rootPath = Path.GetFullPath(root.Context.Origin.CanonicalPath);
        var handoffOwnerPath = Path.GetFullPath(handoff.Origin.CanonicalPath);
        var referenceEntries = root.Context.References
            .Where(reference => reference.Resolved && !string.IsNullOrWhiteSpace(reference.ResolvedPath))
            .Select(reference => (Reference: reference, Path: Path.GetFullPath(reference.ResolvedPath!)))
            .ToArray();
        var belongsToClosure = string.Equals(rootPath, handoffOwnerPath, StringComparison.OrdinalIgnoreCase)
            || referenceEntries.Any(item => string.Equals(item.Path, handoffOwnerPath, StringComparison.OrdinalIgnoreCase));
        if (!belongsToClosure)
            return NavigationToolSupport.Failure(new ResultError(NavigationErrorCodes.TargetMismatch,
                "The symbol handoff is not owned by the selected assembly or its current reference snapshot.",
                "Use a handoff returned by this assembly's find_symbol(includeReferences=true) result."),
                maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");

        var declarationId = DocumentationCommentId.CreateDeclarationId(handoff.Symbol);
        if (string.IsNullOrWhiteSpace(declarationId))
            return McpToolResults.InvalidArgument("The selected handoff has no stable assembly declaration identity.", "$.symbolIdentifier",
                "Use a declaration returned by find_symbol(includeReferences=true).", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);

        var internalRootHandoff = AnalysisSymbolIdentity.ForAssembly(handoff.Origin.CanonicalPath,
            handoff.Origin.ContentHash, handoff.Generation, handoff.ReferenceSnapshotHash).FormatHandoff(handoff.Symbol);
        var ownerPaths = new List<string> { rootPath };
        foreach (var item in referenceEntries.OrderBy(item => item.Reference.Depth).ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (IsFrameworkReferencePath(item.Path) || ownerPaths.Contains(item.Path, StringComparer.OrdinalIgnoreCase)) continue;
            ownerPaths.Add(item.Path);
        }
        var ownerLimitReached = ownerPaths.Count > MaxReferenceClosureAssemblies;
        if (ownerLimitReached)
        {
            ownerPaths = ownerPaths.Take(MaxReferenceClosureAssemblies).ToList();
            if (!ownerPaths.Contains(handoffOwnerPath, StringComparer.OrdinalIgnoreCase))
            {
                ownerPaths[^1] = handoffOwnerPath;
                ownerPaths = ownerPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        var owners = new List<AssemblyCallTreeOwner>();
        var sourceOwners = new List<AssemblyCallTreeSourceOwner>();
        var ownedScopes = new List<AssemblyNavigationSessionScope>();
        var failedOwners = new List<string>();
        try
        {
            foreach (var ownerPath in ownerPaths)
            {
                ct.ThrowIfCancellationRequested();
                AssemblyNavigationSessionScope ownerScope;
                if (string.Equals(ownerPath, rootPath, StringComparison.OrdinalIgnoreCase)) ownerScope = root;
                else
                {
                    var opened = await AssemblyNavigationSessionScope.OpenAsync(ownerPath, ct).ConfigureAwait(false);
                    if (!opened.IsSuccess) { failedOwners.Add(ownerPath); continue; }
                    ownerScope = opened.Value!;
                    ownedScopes.Add(ownerScope);
                }
                sourceOwners.Add(new(ownerPath, ownerScope));

                var isHandoffOwner = string.Equals(ownerPath, handoffOwnerPath, StringComparison.OrdinalIgnoreCase);
                var seed = isHandoffOwner
                    ? ResolveAssemblySourceSymbolInOwner(declarationId, ownerScope)
                    : ResolveAssemblySymbolInCompilation(declarationId, handoff.Identity, ownerScope.Context.Compilation);
                if (seed is null) continue;
                if (ownerScope.Context.Status is not AssemblySessionStatus.Complete) failedOwners.Add(ownerPath);

                var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
                    ownerScope.Solution, seed, depth, topN, direction, includeBcl, scope, includeGenerated,
                    CreateAssemblyInternalHandoffFormatter(ownerScope.Solution, ownerScope.Context)), ct).ConfigureAwait(false);
                owners.Add(new(ownerPath, ownerScope, graph));
            }

            var rootKey = "target:" + handoffOwnerPath + "|" + declarationId;
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
                    var nodeHandoff = node.HandoffId;
                    var nodeOwnerPath = nodeHandoff is null ? null : owner.TargetPath;
                    if (!isRoot && nodeHandoff is null && node.ContainingAssemblyIdentity is { } containingIdentity)
                    {
                        var actualOwner = sourceOwners
                            .Where(candidate => AssemblyIdentityDtoMatches(candidate.Scope.Context.Identity, containingIdentity))
                            .Select(candidate => (Candidate: candidate, Symbol: ResolveAssemblySourceSymbolInOwner(node.SymbolId, candidate.Scope)))
                            .Where(item => item.Symbol is not null)
                            .Take(2)
                            .ToArray();
                        if (actualOwner.Length == 1)
                        {
                            nodeOwnerPath = actualOwner[0].Candidate.TargetPath;
                            nodeHandoff = CreateAssemblyInternalHandoffFormatter(actualOwner[0].Candidate.Scope.Solution,
                                actualOwner[0].Candidate.Scope.Context)(actualOwner[0].Symbol!);
                        }
                    }
                    var key = isRoot ? rootKey : nodeHandoff is not null
                        ? nodeOwnerPath + "|" + nodeHandoff
                        : owner.TargetPath + "|" + node.SymbolId + "|" + node.NodeId;
                    keys[node.NodeId] = key;
                    var visibleNode = node with
                    {
                        OwnerTargetPath = isRoot ? handoffOwnerPath : nodeOwnerPath,
                        HandoffId = isRoot ? internalRootHandoff : nodeHandoff,
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

            var crossOwnerDepthNotTraversed = depth > 1;
            var unresolvedReferences = root.Context.References.Any(reference => !reference.Resolved);
            var truncated = localTruncated || ownerLimitReached || failedOwners.Count > 0 || unresolvedReferences
                || crossOwnerDepthNotTraversed || nodeOrder.Count > selectedNodeKeys.Length || hiddenEdges > 0;
            var graphResult = new CallGraphPayload(selectedNodeKeys.Length > 0 ? selectedNodeIds[selectedNodeKeys[0]] : string.Empty,
                outputNodes, mergedEdges, methodHints, truncated, hiddenEdges, owners.Sum(owner => owner.Graph.PendingNodeCount));
            var body = format == "mermaid" ? CallTreeMermaidRenderer.RenderMermaid(graphResult) : CallGraphTextRenderer.RenderAscii(graphResult);
            var nextAction = crossOwnerDepthNotTraversed
                ? "Cross-assembly call-chain expansion beyond each owner solution is not composed yet; this result is incomplete."
                : failedOwners.Count > 0 || unresolvedReferences || ownerLimitReached
                    ? "The bounded reference-source closure is incomplete; inspect unresolved or unsupported references, then repeat the query."
                    : truncated ? "Increase topN or reduce the graph scope and repeat the query." : null;
            return NavigationToolSupport.SuccessText(body, truncated, nextAction);
        }
        finally
        {
            foreach (var ownerScope in ownedScopes) await ownerScope.DisposeAsync().ConfigureAwait(false);
        }
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

    private static bool IsFrameworkReferencePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/shared/Microsoft.NETCore.App/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/packs/Microsoft.NETCore.App.Ref/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/packs/NETStandard.Library.Ref/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/Reference Assemblies/Microsoft/Framework/", StringComparison.OrdinalIgnoreCase);
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
            return NavigationToolSupport.Success(new { impactStatus = "not_git_repository", changedFiles = Array.Empty<string>(), symbols = Array.Empty<object>() });
        var gitRoot = Path.GetFullPath(rootResult.StandardOutput.Trim());
        var reference = gitRef ?? "HEAD";
        var verification = await RunGitAsync(gitRoot, ["rev-parse", "--verify", "--end-of-options", reference + "^{commit}"], ct).ConfigureAwait(false);
        if (verification.ExitCode != 0)
            return McpToolResults.Recoverable(NavigationErrorCodes.InvalidArgument, "gitRef could not be resolved.", "Provide a commit, branch, or tag that exists in this repository.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);

        var resolvedCommit = verification.StandardOutput.Trim();
        var diff = await RunGitAsync(gitRoot, ["diff", "--no-ext-diff", "--no-textconv", "--name-only", "--diff-filter=ACMRD", resolvedCommit, "--"], ct).ConfigureAwait(false);
        if (diff.ExitCode != 0)
            return McpToolResults.Recoverable(NavigationErrorCodes.InvalidArgument, "Git could not compute the requested change set.", "Check gitRef and repository availability, then retry.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        var untracked = gitRef is null
            ? await RunGitAsync(gitRoot, ["ls-files", "--others", "--exclude-standard"], ct).ConfigureAwait(false)
            : (0, string.Empty, string.Empty);
        var changedPaths = diff.StandardOutput.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Concat(untracked.Item2.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (changedPaths.Length == 0)
            return NavigationToolSupport.Success(new { impactStatus = "clean_worktree", changedFiles = Array.Empty<string>(), symbols = Array.Empty<object>() });
        return await WithSource(target, async solution =>
        {
            var byPath = solution.Projects.SelectMany(project => project.Documents)
                .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
                .GroupBy(document => Path.GetFullPath(document.FilePath!), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(document => document.Project.FilePath, StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);
            var changedSymbols = new List<ISymbol>();
            foreach (var relative in changedPaths)
            {
                ct.ThrowIfCancellationRequested();
                var fullPath = Path.GetFullPath(Path.Combine(gitRoot, relative));
                if (!IsWithin(gitRoot, fullPath) || !byPath.TryGetValue(fullPath, out var documents)) continue;
                foreach (var document in documents)
                {
                    var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                    var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                    if (root is null || model is null) continue;
                    foreach (var node in root.DescendantNodes().Where(node => node is Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax
                                 or Microsoft.CodeAnalysis.CSharp.Syntax.DelegateDeclarationSyntax or Microsoft.CodeAnalysis.CSharp.Syntax.BaseMethodDeclarationSyntax
                                 or Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax or Microsoft.CodeAnalysis.CSharp.Syntax.EventDeclarationSyntax))
                    {
                        if (model.GetDeclaredSymbol(node, ct) is { } symbol) changedSymbols.Add(symbol);
                        if (changedSymbols.Count >= Math.Clamp(maxChangedSymbols, 1, 100)) break;
                    }
                    if (changedSymbols.Count >= Math.Clamp(maxChangedSymbols, 1, 100)) break;
                }
                if (changedSymbols.Count >= Math.Clamp(maxChangedSymbols, 1, 100)) break;
            }
            var distinctSymbols = changedSymbols.Distinct(SymbolEqualityComparer.Default).ToArray();
            var symbolLimit = detailLevel == "change-context" ? Math.Clamp(maxChangedSymbols, 1, 100) : Math.Clamp(maxResults, 1, 1000);
            var entries = new List<object>();
            var callSiteCount = 0;
            foreach (var symbol in distinctSymbols.Take(symbolLimit))
            {
                ct.ThrowIfCancellationRequested();
                var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(symbol, solution, maxDepth: 1, maxResults: maxResults, ct: ct).ConfigureAwait(false);
                callSiteCount += impact.CallSites.Count;
                if (detailLevel == "change-context")
                {
                    var tests = await TestRecommendationBuilder.BuildAsync(symbol, solution, ct).ConfigureAwait(false);
                    entries.Add(new { symbol = symbol.ToDisplayString(), impact, testCandidates = tests.TestFixtures.Take(Math.Clamp(maxTestsPerSymbol, 1, 50)).ToArray(), testEvidence = tests.EvidenceMode });
                }
                else entries.Add(new { symbol = symbol.ToDisplayString(), impact });
            }
            var incomplete = changedPaths.Length > maxResults || distinctSymbols.Length > symbolLimit ||
                             distinctSymbols.Length < changedPaths.Length || changedPaths.Any(relative => !byPath.ContainsKey(Path.GetFullPath(Path.Combine(gitRoot, relative))));
            var status = detailLevel == "callers" && callSiteCount == 0 && !incomplete ? "diff_without_callsite_impact" : "changes_found";
            return NavigationToolSupport.Success(new { impactStatus = status, completeness = incomplete ? "partial" : "complete",
                changedFiles = changedPaths.Take(maxResults).ToArray(), analyzedSymbols = entries }, incomplete,
                "Increase maxResults or maxChangedSymbols and repeat the query; inspect listed unresolved files before treating an empty symbol list as complete.");
        }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Git.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); throw; }
        return (process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
