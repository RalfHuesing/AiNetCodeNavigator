using System.ComponentModel.DataAnnotations;
using System.Text;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.CallTree;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.Tools;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Mcp.Tools.Assemblies;

[McpServerToolType]
public sealed class AssemblyTools(NavigatorHostRuntime runtime)
{
    [McpServerTool(Name = "get_assembly_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public Task<CallToolResult> GetAssemblyContext([Required] string targetPath, string? symbolIdentifier = null,
        bool includeReferences = false, bool includeCallers = false, bool includeImpact = false, bool includeBody = false,
        bool includeClassStructure = false, [Range(1, 1000)] int maxResults = 100, [Range(1, 1000)] int maxBodyLines = 80,
        [Range(1, 200)] int maxCallers = 10, [Range(1, 3)] int depth = 1, [Range(1, 200)] int topN = 10,
        string detailLevel = "standard", [Range(0, 65536)] int? maxResponseBytes = null,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (maxResponseBytes is > 0 and < McpResponseBudgetLimits.MinimumBytes)
            return Task.FromResult(Invalid("maxResponseBytes", $"Use {McpResponseBudgetLimits.MinimumBytes} to {McpResponseBudgetLimits.MaximumBytes} bytes."));
        if (symbolIdentifier is not null && string.IsNullOrWhiteSpace(symbolIdentifier))
            return Task.FromResult(Invalid("symbolIdentifier", "Provide a current symbol handoff or omit this optional argument."));
        if (!TryGetDetailBudget(detailLevel, maxResponseBytes, out var effectiveResponseBytes))
            return Task.FromResult(Invalid("detailLevel", "Use compact, standard, or full."));
        return NavigationToolSupport.RouteAsync(runtime, "get_assembly_context", targetPath,
            new { symbolIdentifier, includeReferences, includeCallers, includeImpact, includeBody, includeClassStructure, maxResults, maxBodyLines, maxCallers, depth, topN, detailLevel },
            operationToken, continuationToken, effectiveResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                var context = await AssemblyContextScanner.GetAsync(new AssemblyContextRequest(target.CanonicalPath, maxResults, includeReferences), ct).ConfigureAwait(false);
                if (!context.IsSuccess) return NavigationToolSupport.Failure(context.Error!.Value, effectiveResponseBytes, maxResponseTokens, "$.targetPath");
                var payload = context.Value!;
                if (symbolIdentifier is null) return NavigationToolSupport.Success(payload, payload.Truncated, "Increase maxResults and repeat the query.");
                var resolved = await AssemblySymbolHandoffResolver.ResolveAsync(symbolIdentifier, ct).ConfigureAwait(false);
                if (!resolved.IsSuccess) return NavigationToolSupport.Failure(resolved.Error!.Value, effectiveResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                await using var access = resolved.Value!;
                var ownerPath = Path.GetFullPath(access.Origin.CanonicalPath);
                var isTargetOwner = string.Equals(ownerPath, target.CanonicalPath, StringComparison.OrdinalIgnoreCase);
                var isResolvedReferenceOwner = includeReferences && payload.References.Any(reference => reference.Resolved
                    && !string.IsNullOrWhiteSpace(reference.ResolvedPath)
                    && string.Equals(Path.GetFullPath(reference.ResolvedPath), ownerPath, StringComparison.OrdinalIgnoreCase));
                if (!isTargetOwner && !isResolvedReferenceOwner)
                    return McpToolResults.InvalidArgument("The symbol handoff belongs to another assembly.", "$.symbolIdentifier", "Use a handoff produced by this targetPath.", maxResponseBytes: effectiveResponseBytes, maxResponseTokens: maxResponseTokens);
                var identity = AnalysisSymbolIdentity.ForAssembly(access.Origin.CanonicalPath, access.Origin.ContentHash,
                    access.Generation, access.ReferenceSnapshotHash);
                string? FormatOwnedHandoff(ISymbol symbol)
                {
                    if (!HasRootSourceDeclaration(symbol, access.Solution, access.DecompiledProjectPaths?.DecompiledSourceRoot)) return null;
                    var declarationId = DocumentationCommentId.CreateDeclarationId(symbol);
                    if (string.IsNullOrWhiteSpace(declarationId))
                        return null;
                    var ownedSymbols = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, access.Compilation)
                        .Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, access.Assembly))
                        .Distinct(SymbolEqualityComparer.Default)
                        .Take(2)
                        .ToArray();
                    if (ownedSymbols.Length != 1) return null;
                    var internalId = identity.FormatHandoff(ownedSymbols[0]);
                    return internalId is null ? null : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalId);
                }

                var sections = new List<string>
                {
                    FormatAssemblyOverview(payload),
                    $"## Symbol\n{access.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}"
                };
                var truncated = payload.Truncated;
                var incompleteActions = new List<string>();
                if (includeBody)
                {
                    var result = await AssemblySymbolBodyScanner.GetAsync(symbolIdentifier, maxBodyLines, 1, ct, ownerPath).ConfigureAwait(false);
                    if (result.Error is { } error) return NavigationToolSupport.Failure(error, effectiveResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    var body = result.Body!;
                    sections.Add($"## Body\n```csharp\n{body.Body.TrimEnd()}\n```");
                    truncated |= body.HasMore;
                }
                if (includeClassStructure)
                {
                    var type = access.Symbol as INamedTypeSymbol ?? access.Symbol.ContainingType;
                    if (type is null) return McpToolResults.InvalidArgument("The symbol has no containing type.", "$.symbolIdentifier",
                        "Use a type or a member declared in a type.", maxResponseBytes: effectiveResponseBytes, maxResponseTokens: maxResponseTokens);
                    var structure = StructureTools.BuildAssemblyClassStructure(type, ownerPath, identity,
                        "lines", Math.Min(maxResults, 200), null, null);
                    sections.Add($"## Class Structure\n{StructureTools.FormatClassStructure(structure)}");
                    truncated |= structure.Truncated;
                }
                var graphLimit = Math.Min(Math.Clamp(maxCallers, 1, 200), Math.Clamp(topN, 1, 200));
                if (includeCallers)
                {
                    if (includeReferences)
                    {
                        var closure = await AssemblyReferencesClosureScanner.ScanAsync(target.CanonicalPath, symbolIdentifier,
                            graphLimit, Math.Clamp(depth, 1, 3), SymbolScopeType.All, includeGenerated: false, ct).ConfigureAwait(false);
                        if (closure.Error is { } error)
                            return NavigationToolSupport.Failure(error, effectiveResponseBytes, maxResponseTokens, closure.ErrorField);
                        sections.Add(FormatReferences(closure.References!));
                        truncated |= closure.IsTruncated;
                        if (closure.IsTruncated && !string.IsNullOrWhiteSpace(closure.NextAction)) incompleteActions.Add(closure.NextAction);
                    }
                    else
                    {
                        var references = await FindReferencesResolver.FindReferencesAsync(access.Symbol, access.Solution,
                            graphLimit, Math.Clamp(depth, 1, 3), ct, handoffFormatter: FormatOwnedHandoff,
                            ownerTargetPath: ownerPath).ConfigureAwait(false);
                        sections.Add(FormatReferences(references));
                        truncated |= references.IsTruncated || references.IsTruncatedByNodeLimit || references.IsDepthClamped;
                    }
                }
                if (includeImpact)
                {
                    if (includeReferences)
                    {
                        var closure = await AssemblyImpactClosureScanner.ScanAsync(target.CanonicalPath, symbolIdentifier,
                            Math.Clamp(depth, 1, 3), graphLimit, ct).ConfigureAwait(false);
                        if (closure.Error is { } error)
                            return NavigationToolSupport.Failure(error, effectiveResponseBytes, maxResponseTokens, closure.ErrorField);
                        sections.Add(FormatImpact(closure.Impact!));
                        truncated |= closure.IsTruncated;
                        if (closure.IsTruncated && !string.IsNullOrWhiteSpace(closure.NextAction)) incompleteActions.Add(closure.NextAction);
                    }
                    else
                    {
                        var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(access.Symbol, access.Solution,
                            maxDepth: Math.Clamp(depth, 1, 3), maxResults: graphLimit, ct: ct, handoffFormatter: FormatOwnedHandoff).ConfigureAwait(false);
                        sections.Add(FormatImpact(impact));
                        truncated |= !impact.IsComplete;
                    }
                }
                var nextAction = incompleteActions.Count == 0
                    ? truncated ? "Increase maxResults, maxBodyLines, maxCallers, depth, or topN and repeat the query." : null
                    : string.Join(" ", incompleteActions.Distinct(StringComparer.Ordinal));
                return NavigationToolSupport.SuccessText(string.Join("\n\n", sections), truncated, nextAction);
            }, AnalysisTargetType.Assembly, cancellationToken);
    }

    private static string FormatAssemblyOverview(AssemblyContextPayload payload)
    {
        var output = new StringBuilder()
            .AppendLine("## Assembly")
            .AppendLine($"- Target: {payload.AssemblyPath}")
            .AppendLine($"- Identity: {payload.Identity?.Name ?? "unknown"}")
            .AppendLine($"- Status: {payload.Status}")
            .AppendLine($"- Types: {payload.TotalTypes}; namespaces: {payload.TotalNamespaces}")
            .AppendLine($"- References: {payload.TotalReferenceCount}");
        foreach (var name in payload.Namespaces) output.AppendLine($"- Namespace: {name}");
        foreach (var name in payload.Types) output.AppendLine($"- Type: {name}");
        foreach (var reference in payload.References)
        {
            var path = reference.ResolvedPath is null ? string.Empty : $" — {reference.ResolvedPath}";
            output.AppendLine($"- Reference: {reference.Name}, {reference.ResolutionState}{path}");
        }
        foreach (var diagnostic in payload.Diagnostics) output.AppendLine($"- Diagnostic: {diagnostic}");
        return output.ToString().TrimEnd();
    }

    private static string FormatImpact(SymbolImpactPayload impact)
    {
        var output = new StringBuilder()
            .AppendLine("## Impact")
            .AppendLine($"- Direct callers: {impact.DirectCallersCount}")
            .AppendLine($"- Transitive call sites: {impact.TransitiveImpactCount}")
            .AppendLine($"- Visited symbols: {impact.VisitedSymbolCount}");
        foreach (var site in impact.CallSites)
        {
            var handoff = site.CallingMemberHandoffId is null ? string.Empty : $" [handoff: {site.CallingMemberHandoffId}]";
            var owner = string.IsNullOrWhiteSpace(site.OwnerTargetPath) ? string.Empty : $" (targetPath: {site.OwnerTargetPath})";
            output.AppendLine($"- {site.FilePath}:{site.Line}: {site.CallingMember} (depth {site.Depth}){handoff}{owner}");
        }
        return output.ToString().TrimEnd();
    }

    private static string FormatReferences(FindReferencesResult references)
    {
        var output = new StringBuilder()
            .AppendLine("## Callers")
            .AppendLine($"- References: {references.TotalCount}")
            .AppendLine($"- Visited symbols: {references.VisitedSymbolCount}");
        foreach (var reference in references.References)
        {
            var handoff = reference.EnclosingSymbolHandoffId is null ? string.Empty : $" [handoff: {reference.EnclosingSymbolHandoffId}]";
            var owner = string.IsNullOrWhiteSpace(reference.OwnerTargetPath) ? string.Empty : $" (targetPath: {reference.OwnerTargetPath})";
            output.AppendLine($"- {reference.FilePath}:{reference.Line}: {reference.EnclosingSymbolName}{handoff}{owner}");
            if (!string.IsNullOrWhiteSpace(reference.Snippet)) output.AppendLine($"  {reference.Snippet}");
        }
        return output.ToString().TrimEnd();
    }

    private static bool HasRootSourceDeclaration(ISymbol symbol, Solution solution, string? decompiledSourceRoot)
    {
        if (string.IsNullOrWhiteSpace(decompiledSourceRoot)) return false;
        var root = Path.GetFullPath(decompiledSourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var location in symbol.Locations.Where(location => location.IsInSource && location.SourceTree is not null))
        {
            var document = solution.GetDocument(location.SourceTree!);
            if (document?.FilePath is not { } filePath) continue;
            var fullPath = Path.GetFullPath(filePath);
            if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

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
                    typeName, memberName, publicOnly, maxResults, exactTypeName, memberNames, maxMembers, includeReferences,
                    Cursor: continuationToken), ct).ConfigureAwait(false);
                return result.IsSuccess ? NavigationToolSupport.Success(result.Value!, result.Value!.Truncated,
                    result.Value.ContinuationToken is null ? null : "Repeat the query with the returned continuationToken.")
                    : NavigationToolSupport.Failure(result.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
            }, AnalysisTargetType.Assembly, cancellationToken, acceptsDomainCursor: true);

    [McpServerTool(Name = "search_assembly", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public Task<CallToolResult> SearchAssembly([Required] string targetPath, string searchKind = "text",
        string? pattern = null, bool? isRegex = null, bool caseSensitive = false, bool declarationOnly = false,
        string? kind = null, string? fileFilter = null, [Range(0, 5)] int contextLines = 0,
        [Range(0, 1000)] int maxResults = 50, [Range(0, 2000)] int maxFiles = 0,
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
                    searchKind, caseSensitive, isRegex, fileFilter, declarationOnly, contextLines, maxResults == 0 ? 50 : maxResults, maxFiles, kind,
                    Cursor: continuationToken), ct).ConfigureAwait(false);
                return result.IsSuccess ? NavigationToolSupport.Success(result.Value!, result.Value!.Truncated,
                    result.Value.ContinuationToken is not null
                        ? "Repeat the same query with the returned continuationToken; increase maxFiles to include additional matching files."
                        : result.Value.TruncatedBy?.Contains("maxFiles", StringComparer.Ordinal) == true
                            ? "Increase maxFiles and repeat the same query."
                            : "Increase maxResults and repeat the same query.")
                    : NavigationToolSupport.Failure(result.Error!.Value, maxResponseBytes, maxResponseTokens, "$.pattern");
            }, AnalysisTargetType.Assembly, cancellationToken, acceptsDomainCursor: true);

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
    private static bool TryDetail(string value) => TryGetDetailBudget(value, null, out _);

    private static bool TryGetDetailBudget(string value, int? requestedBytes, out int budget)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            budget = McpResponseBudgetLimits.DefaultBytes;
            return false;
        }
        var normalized = value.Trim();
        if (normalized.Equals("compact", StringComparison.OrdinalIgnoreCase))
            budget = 32_768;
        else if (normalized.Equals("standard", StringComparison.OrdinalIgnoreCase))
            budget = 32_768;
        else if (normalized.Equals("full", StringComparison.OrdinalIgnoreCase))
            budget = McpResponseBudgetLimits.MaximumBytes;
        else
        {
            budget = McpResponseBudgetLimits.DefaultBytes;
            return false;
        }

        if (requestedBytes is > 0) budget = requestedBytes.Value;
        return true;
    }
}
