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
    [System.ComponentModel.Description("Inspect a compact managed-assembly type overview; explicitly request member declarations or reference inventory.")]
    public Task<CallToolResult> InspectAssembly([Required, System.ComponentModel.Description("Absolute path to an existing managed .dll or .exe target.")] string targetPath, [System.ComponentModel.Description("Optional namespace name used to limit inspected types.")] string? @namespace = null,
        [System.ComponentModel.Description("Optional type name used to limit inspected types.")] string? typeName = null, [System.ComponentModel.Description("Optional member-name substring; requires includeMembers=true.")] string? memberName = null, [System.ComponentModel.Description("Return public API types and requested members only.")] bool publicOnly = true, [System.ComponentModel.Description("Match typeName exactly instead of as a name filter.")] bool exactTypeName = false,
        [System.ComponentModel.Description("Optional exact member-name alternatives; requires includeMembers=true.")] string[]? memberNames = null,
        [System.ComponentModel.Description("Maximum types to return; zero uses the default of 100.")] [Range(0, 1000)] int maxResults = 100,
        [System.ComponentModel.Description("Include reference inventory details. Defaults to false, independently of type/member filters.")] bool includeReferences = false,
        [System.ComponentModel.Description("Include member declarations within each selected type. Defaults to false; memberName/memberNames require true.")] bool includeMembers = false,
        [System.ComponentModel.Description("Include detailed navigation and reference diagnostics. Defaults to false.")] bool includeDiagnostics = false,
        [System.ComponentModel.Description("Optional byte cap; zero uses this tool's 24,576-byte default.")] [Range(0, 65536)] int maxResponseBytes = 24576,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null, [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque cursor returned for the next page of assembly results; use after reading all outer response pages.")] string? resultCursor = null, CancellationToken cancellationToken = default)
    {
        var effectiveResponseBytes = maxResponseBytes == 0 ? 24_576 : maxResponseBytes;
        if (!includeMembers && (memberName is not null || memberNames is not null))
            return Task.FromResult(McpToolResults.InvalidArgument("Member filters require includeMembers=true.",
                memberName is not null ? "$.memberName" : "$.memberNames", "Set includeMembers=true or omit the member filter.",
                maxResponseBytes: effectiveResponseBytes, maxResponseTokens: maxResponseTokens));
        var effectiveMaxResults = maxResults == 0 ? 100 : maxResults;
        return NavigationToolSupport.RouteAsync(runtime, "inspect_assembly", targetPath,
            new { @namespace, typeName, memberName, publicOnly, exactTypeName, memberNames, maxResults = effectiveMaxResults, includeReferences, includeMembers, includeDiagnostics },
            operationToken, continuationToken, effectiveResponseBytes, maxResponseTokens,
            async (target, coreCursor, ct) =>
            {
                var result = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(target.CanonicalPath, @namespace,
                    typeName, memberName, publicOnly, effectiveMaxResults, exactTypeName, memberNames, includeReferences,
                    Cursor: coreCursor, IncludeMembers: includeMembers), ct).ConfigureAwait(false);
                return result.IsSuccess ? NavigationToolSupport.Success(ProjectInspect(result.Value!, includeDiagnostics), result.Value!.Truncated,
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
                return result.IsSuccess ? NavigationToolSupport.Success(ProjectSearch(result.Value!, includeDiagnostics), result.Value!.Truncated,
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

    private static InspectAssemblyPayload ProjectInspect(InspectAssemblyPayload payload, bool includeDiagnostics) => payload with
    {
        Diagnostics = AssemblyDiagnosticProjection.Project(payload.Diagnostics, includeDiagnostics),
        Types = payload.Types.Select(type => type with
        {
            HandoffId = type.Id is null ? null : type.HandoffId,
            OwnerTargetPath = type.Id is null ? null : type.OwnerTargetPath,
            Members = type.Members?.Select(member => member with
            {
                HandoffId = member.Id is null ? null : member.HandoffId,
                OwnerTargetPath = member.Id is null ? null : member.OwnerTargetPath,
            }).ToArray(),
        }).ToArray(),
    };

    private static AssemblySearchPayload ProjectSearch(AssemblySearchPayload payload, bool includeDiagnostics) => payload with
    {
        Diagnostics = AssemblyDiagnosticProjection.Project(payload.Diagnostics, includeDiagnostics),
    };

    private CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint);
}
