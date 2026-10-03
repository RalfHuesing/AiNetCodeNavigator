using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using AiNetCodeNavigator.Core.Common;
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
    internal Action? BeforeAssemblyContextBodyLoadForTesting { get; set; }

    [McpServerTool(Name = "get_assembly_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Combine assembly metadata, body, structure, callers, and impact sections for a selected target or symbol.")]
    public Task<CallToolResult> GetAssemblyContext([Required, System.ComponentModel.Description("Absolute path to an existing managed .dll or .exe target.")] string targetPath, [System.ComponentModel.Description("Optional type or member identifier; omit to return only the assembly overview.")] string? symbolIdentifier = null,
        [System.ComponentModel.Description("Include resolved reference metadata and, for assembly queries, search reference owners for raw symbols and caller/impact traversal.")] bool includeReferences = false, [System.ComponentModel.Description("Include callers for the selected symbol.")] bool includeCallers = false, [System.ComponentModel.Description("Include impact analysis for the selected symbol.")] bool includeImpact = false, [System.ComponentModel.Description("Include the selected symbol's source or decompiled body.")] bool includeBody = false,
        [System.ComponentModel.Description("Include the selected symbol's containing type structure.")] bool includeClassStructure = false, [System.ComponentModel.Description("Maximum overview entries; zero uses the default of 100.")] [Range(0, 1000)] int maxResults = 100, [Range(1, 1000), System.ComponentModel.Description("Maximum body lines to include when includeBody is true.")] int maxBodyLines = 80,
        [Range(1, 200), System.ComponentModel.Description("Maximum callers to include when caller or impact sections are requested.")] int maxCallers = 10, [Range(1, 3), System.ComponentModel.Description("Maximum caller or impact traversal depth.")] int depth = 1, [Range(1, 200), System.ComponentModel.Description("Maximum neighboring call nodes when call details are requested.")] int topN = 10,
        [System.ComponentModel.Description("Default response detail: compact, standard (default), or full. An explicit positive maxResponseBytes takes precedence.")] string detailLevel = "standard", [Range(0, 65536), System.ComponentModel.Description("Optional response byte cap; zero uses the detail-level default, and a positive value overrides it.")] int? maxResponseBytes = null,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null, [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        maxResults = maxResults == 0 ? 100 : maxResults;
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
                var effectiveSymbolIdentifier = symbolIdentifier;
                var normalizedIdentifier = InputNormalizer.NormalizeSymbolIdentifier(symbolIdentifier);
                if (!InputNormalizer.HasOpaqueHandoffPrefix(normalizedIdentifier)
                    && !normalizedIdentifier.StartsWith("i:", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = includeReferences
                        ? await AssemblyReferenceClosureSession.ResolveRawAcrossReferencesAsync(target.CanonicalPath, normalizedIdentifier, ct).ConfigureAwait(false)
                        : await ResolveRawInTargetAsync(target.CanonicalPath, normalizedIdentifier, ct).ConfigureAwait(false);
                    if (!raw.IsSuccess) return NavigationToolSupport.Failure(raw.Error!.Value, effectiveResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    effectiveSymbolIdentifier = raw.HandoffId!;
                }
                var resolved = await AssemblySymbolHandoffResolver.ResolveAsync(effectiveSymbolIdentifier, ct).ConfigureAwait(false);
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
                    if (!AssemblyHandoffFormatting.HasSourceDeclaration(symbol, access.Solution, access.DecompiledProjectPaths?.DecompiledSourceRoot)) return null;
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
                    BeforeAssemblyContextBodyLoadForTesting?.Invoke();
                    var result = await AssemblySymbolBodyScanner.GetAsync(effectiveSymbolIdentifier, maxBodyLines, 1, ct, ownerPath).ConfigureAwait(false);
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
                        var closure = await AssemblyReferencesClosureScanner.ScanAsync(target.CanonicalPath, effectiveSymbolIdentifier,
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
                        var closure = await AssemblyImpactClosureScanner.ScanAsync(target.CanonicalPath, effectiveSymbolIdentifier,
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

    private static async Task<AssemblySymbolInputResolution> ResolveRawInTargetAsync(
        string targetPath,
        string identifier,
        CancellationToken cancellationToken)
    {
        var opened = await AssemblyNavigationSessionScope.OpenAsync(targetPath, cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess)
            return new(null, null, Array.Empty<AssemblySymbolInputCandidate>(), opened.Error!.Value);
        await using var scope = opened.Value!;
        return await AssemblySymbolInputResolver.ResolveAsync(scope, identifier, cancellationToken).ConfigureAwait(false);
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
            output.AppendLine($"- {site.FilePath}:{site.Line}:{site.Column} [{site.EvidenceKind}]: {site.CallingMember} (depth {site.Depth}){handoff}{owner}");
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
            output.AppendLine($"- {reference.FilePath}:{reference.Line}:{reference.Column} [{reference.EvidenceKind}]: {reference.EnclosingSymbolName}{handoff}{owner}");
            if (!string.IsNullOrWhiteSpace(reference.Snippet)) output.AppendLine($"  {reference.Snippet}");
        }
        return output.ToString().TrimEnd();
    }

    [McpServerTool(Name = "inspect_assembly", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Inspect types and members in a managed assembly, optionally including referenced assemblies.")]
    public Task<CallToolResult> InspectAssembly([Required, System.ComponentModel.Description("Absolute path to an existing managed .dll or .exe target.")] string targetPath, [System.ComponentModel.Description("Optional namespace name used to limit inspected types.")] string? @namespace = null,
        [System.ComponentModel.Description("Optional type name used to limit inspected types.")] string? typeName = null, [System.ComponentModel.Description("Optional member name used to limit inspected members.")] string? memberName = null, [System.ComponentModel.Description("Return public API members only.")] bool publicOnly = true, [System.ComponentModel.Description("Match typeName exactly instead of as a name filter.")] bool exactTypeName = false,
        [System.ComponentModel.Description("Optional member names to inspect within the selected type.")] string[]? memberNames = null,
        [System.ComponentModel.Description("Maximum types to return; zero uses the default of 100.")] [Range(0, 1000)] int maxResults = 100,
        [System.ComponentModel.Description("Include reference inventory details. Defaults to false, independently of type/member filters.")] bool includeReferences = false,
        [System.ComponentModel.Description("Include detailed navigation and reference diagnostics. Defaults to false.")] bool includeDiagnostics = false,
        [System.ComponentModel.Description("Optional byte cap; zero uses this tool's 24,576-byte default.")] [Range(0, 65536)] int maxResponseBytes = 24576,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null, [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque cursor returned for the next page of assembly results; use after reading all outer response pages.")] string? resultCursor = null, CancellationToken cancellationToken = default)
    {
        var effectiveResponseBytes = maxResponseBytes == 0 ? 24_576 : maxResponseBytes;
        var effectiveMaxResults = maxResults == 0 ? 100 : maxResults;
        return NavigationToolSupport.RouteAsync(runtime, "inspect_assembly", targetPath,
            new { @namespace, typeName, memberName, publicOnly, exactTypeName, memberNames, maxResults = effectiveMaxResults, includeReferences, includeDiagnostics },
            operationToken, continuationToken, effectiveResponseBytes, maxResponseTokens,
            async (target, coreCursor, ct) =>
            {
                var result = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(target.CanonicalPath, @namespace,
                    typeName, memberName, publicOnly, effectiveMaxResults, exactTypeName, memberNames, includeReferences,
                    Cursor: coreCursor), ct).ConfigureAwait(false);
                return result.IsSuccess ? NavigationToolSupport.SuccessCompact(ProjectInspect(result.Value!, includeDiagnostics), result.Value!.Truncated,
                    result.Value.ResultCursor is null ? null : "Repeat the query with the returned resultCursor.")
                    : NavigationToolSupport.Failure(result.Error!.Value, effectiveResponseBytes, maxResponseTokens, "$.targetPath");
            }, AnalysisTargetType.Assembly, cancellationToken, resultCursor, "assembly.inspect.types");
    }

    [McpServerTool(Name = "search_assembly", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Search decompiled assembly declarations or text with optional regex, kind, file, and result filters.")]
    public Task<CallToolResult> SearchAssembly([Required, System.ComponentModel.Description("Absolute path to an existing managed .dll or .exe target.")] string targetPath,
        [System.ComponentModel.Description("Literal text or a regular-expression pattern to find.")] string? pattern = null, [System.ComponentModel.Description("When true, interpret pattern as a regular expression; false (default) searches literally.")] bool isRegex = false, [System.ComponentModel.Description("Match text with case sensitivity.")] bool caseSensitive = false, [System.ComponentModel.Description("Restrict text results to declarations.")] bool declarationOnly = false,
        [System.ComponentModel.Description("Declaration kind filter: method, type, or property.")] string? kind = null, [System.ComponentModel.Description("Optional decompiled source file path filter.")] string? fileFilter = null, [Range(0, 5), System.ComponentModel.Description("Number of surrounding source lines to include around each match.")] int contextLines = 0,
        [System.ComponentModel.Description("Maximum matches to return; zero uses the default limit of 50.")] [Range(0, 1000)] int maxResults = 50,
        [System.ComponentModel.Description("Maximum matching files to search; zero (default) means no matching-file limit. Positive values are capped at 2000.")] [Range(0, 2000)] int maxFiles = 0,
        [System.ComponentModel.Description("Include detailed navigation and reference diagnostics. Defaults to false.")] bool includeDiagnostics = false,
        [System.ComponentModel.Description("Optional byte cap; zero uses this tool's 24,576-byte default.")] [Range(0, 65536)] int maxResponseBytes = 24576,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null, [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque cursor returned for the next page of search results; use after reading all outer response pages.")] string? resultCursor = null, CancellationToken cancellationToken = default)
    {
        var effectiveResponseBytes = maxResponseBytes == 0 ? 24_576 : maxResponseBytes;
        return NavigationToolSupport.RouteAsync(runtime, "search_assembly", targetPath,
            new { pattern, isRegex, caseSensitive, declarationOnly, kind, fileFilter, contextLines, maxResults, maxFiles, includeDiagnostics },
            operationToken, continuationToken, effectiveResponseBytes, maxResponseTokens,
            async (target, coreCursor, ct) =>
            {
                if (kind is not (null or "method" or "type" or "property")) return Invalid("kind", "Use method, type, or property.");
                var result = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(target.CanonicalPath, pattern,
                    caseSensitive, isRegex, fileFilter, declarationOnly, contextLines, maxResults == 0 ? 50 : maxResults, maxFiles, kind,
                    Cursor: coreCursor), ct).ConfigureAwait(false);
                return result.IsSuccess ? NavigationToolSupport.SuccessCompact(ProjectSearch(result.Value!, includeDiagnostics), result.Value!.Truncated,
                    result.Value.ResultCursor is not null
                        ? "Repeat the same query with the returned resultCursor; increase maxFiles to include additional matching files."
                        : result.Value.TruncatedBy?.Contains("incompleteRelationships", StringComparer.Ordinal) == true
                            ? "Restore missing assembly references, then retry."
                        : result.Value.TruncatedBy?.Contains("maxFiles", StringComparer.Ordinal) == true
                            ? "Increase maxFiles and repeat the same query."
                            : "Increase maxResults and repeat the same query.")
                    : NavigationToolSupport.Failure(result.Error!.Value, effectiveResponseBytes, maxResponseTokens, "$.pattern");
            }, AnalysisTargetType.Assembly, cancellationToken, resultCursor, "assembly.search.matches");
    }

    [McpServerTool(Name = "find_assembly_extensions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Find extension methods in a managed assembly by receiver type, extension name, or namespace.")]
    public Task<CallToolResult> FindAssemblyExtensions([Required, System.ComponentModel.Description("Absolute path to an existing managed .dll or .exe target.")] string targetPath, [System.ComponentModel.Description("Optional extension receiver type name.")] string? receiverType = null,
        [System.ComponentModel.Description("Optional extension method name filter.")] string? extensionName = null, [System.ComponentModel.Description("Optional namespace filter for extension methods.")] string? @namespace = null, [System.ComponentModel.Description("Search resolved referenced assemblies in addition to the target.")] bool includeReferences = false,
        [System.ComponentModel.Description("Maximum extensions per page; zero uses the default of 100.")] [Range(0, 1000)] int maxResults = 100,
        [System.ComponentModel.Description("Include detailed navigation and reference diagnostics. Defaults to false.")] bool includeDiagnostics = false,
        [System.ComponentModel.Description("Optional byte cap; zero uses this tool's 16,384-byte default.")] [Range(0, 65536)] int maxResponseBytes = 16384, [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null, [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque cursor returned for the next extension result page; use after reading all outer response pages.")] string? resultCursor = null, CancellationToken cancellationToken = default)
    {
        var effectiveResponseBytes = maxResponseBytes == 0 ? 16_384 : maxResponseBytes;
        return NavigationToolSupport.RouteAsync(runtime, "find_assembly_extensions", targetPath,
            new { receiverType, extensionName, @namespace, includeReferences, maxResults, includeDiagnostics }, operationToken,
            continuationToken, effectiveResponseBytes, maxResponseTokens,
            async (target, coreCursor, ct) =>
            {
                var effectiveMaxResults = maxResults == 0 ? 100 : maxResults;
                var result = await FindAssemblyExtensionsScanner.FindAsync(new FindAssemblyExtensionsRequest(target.CanonicalPath,
                    receiverType, extensionName, @namespace, includeReferences, effectiveMaxResults, coreCursor), ct).ConfigureAwait(false);
                return result.IsSuccess ? NavigationToolSupport.SuccessCompact(ProjectExtensions(result.Value!, includeDiagnostics), result.Value!.Truncated,
                    result.Value.ResultCursor is not null ? "Repeat the query with the returned resultCursor."
                    : result.Value.Analysis?.OmissionReasons.Contains("incompleteRelationships", StringComparer.Ordinal) == true
                        ? "Restore missing assembly references, then retry."
                        : "Increase maxResults and repeat the same query.")
                    : NavigationToolSupport.Failure(result.Error!.Value, effectiveResponseBytes, maxResponseTokens, "$.targetPath");
            }, AnalysisTargetType.Assembly, cancellationToken, resultCursor, "assembly.extensions.results");
    }

    private static InspectAssemblyPayload ProjectInspect(InspectAssemblyPayload payload, bool includeDiagnostics) => payload with
    {
        Diagnostics = ProjectDiagnostics(payload.Diagnostics, includeDiagnostics),
        Types = payload.Types.Select(type => type with
        {
            HandoffId = type.Id is null ? null : type.HandoffId,
            OwnerTargetPath = type.Id is null ? null : type.OwnerTargetPath,
            Members = type.Members.Select(member => member with
            {
                HandoffId = member.Id is null ? null : member.HandoffId,
                OwnerTargetPath = member.Id is null ? null : member.OwnerTargetPath,
            }).ToArray(),
        }).ToArray(),
    };

    private static AssemblySearchPayload ProjectSearch(AssemblySearchPayload payload, bool includeDiagnostics) => payload with
    {
        Diagnostics = ProjectDiagnostics(payload.Diagnostics, includeDiagnostics),
    };

    private static FindAssemblyExtensionsPayload ProjectExtensions(FindAssemblyExtensionsPayload payload, bool includeDiagnostics) => payload with
    {
        Diagnostics = ProjectDiagnostics(payload.Diagnostics, includeDiagnostics),
    };

    private static IReadOnlyList<string> ProjectDiagnostics(IReadOnlyList<string> diagnostics, bool includeDiagnostics)
    {
        if (includeDiagnostics || diagnostics.Count == 0) return diagnostics;
        return diagnostics
            .GroupBy(DiagnosticCategory, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}: {group.Count()}")
            .ToArray();
    }

    private static string DiagnosticCategory(string diagnostic)
    {
        var value = diagnostic.ToLowerInvariant();
        if (value.Contains("incompleterelationships", StringComparison.Ordinal)
            || value.Contains("incomplete relationship", StringComparison.Ordinal)
            || value.Contains("relationship closure", StringComparison.Ordinal)) return "incompleteRelationships";
        if (value.Contains("version_mismatch", StringComparison.Ordinal)
            || value.Contains("version mismatch", StringComparison.Ordinal)
            || value.Contains("version conflict", StringComparison.Ordinal)) return "versionConflict";
        if (value.Contains("closure", StringComparison.Ordinal) || value.Contains("limit", StringComparison.Ordinal)) return "analysisLimit";
        if (value.Contains("reference", StringComparison.Ordinal) || value.Contains("resolve", StringComparison.Ordinal)) return "unresolvedReference";
        if (value.Contains("decompil", StringComparison.Ordinal)) return "decompilation";
        return "navigation";
    }

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
