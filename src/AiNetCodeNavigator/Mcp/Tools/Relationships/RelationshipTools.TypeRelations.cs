using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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

public sealed partial class RelationshipTools
{
    internal Action<string>? BeforeTypeRelationScanForTesting { get; set; }

    [McpServerTool(Name = "get_type_relations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Show one explicit relationship: type hierarchy, or type/member implementations and overrides.")]
    public Task<CallToolResult> GetTypeRelations(
        [Required, System.ComponentModel.Description("Absolute source solution or managed assembly target path.")] string targetPath,
        [Required, System.ComponentModel.Description("Existing source/assembly type or member identifier, documentation ID, or stable src:/asm: reference.")] string symbolIdentifier,
        [Required, System.ComponentModel.Description("Required selection: hierarchy (named type only) or implementations (supported type/interface member/virtual member).")] string relation,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Positive relationship page size; default 50.")] int? maxResults = null,
        [System.ComponentModel.Description("Relationship scope: all (default), production, or tests.")] string? scopeType = null,
        [System.ComponentModel.Description("Include relationships declared in generated source; default false.")] bool? includeGenerated = null,
        [Range(512, 65536), System.ComponentModel.Description("Maximum UTF-8 response bytes; default 16384.")] int maxResponseBytes = 16384,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive response token budget.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque operation token; repeat the original target, relation and options.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque outer page token; repeat the original target, relation and options.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque relationship cursor bound to the original relation, options and snapshot.")] string? resultCursor = null,
        CancellationToken cancellationToken = default)
    {
        if (relation is not ("hierarchy" or "implementations")) return Invalid("relation", "Choose hierarchy or implementations explicitly.");
        if (!TryScope(scopeType ?? "all", out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        if (maxResults is < 1) return Invalid("maxResults", "Use a positive page size.");
        var args = new { symbolIdentifier, relation, maxResults, scopeType, includeGenerated };
        var binding = JsonSerializer.Serialize(args);
        return NavigationToolSupport.RouteAsync(runtime, "get_type_relations", targetPath, args, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens, (target, cursor, ct) => relation == "hierarchy"
                ? BuildHierarchyAsync(target, symbolIdentifier, maxResults ?? 50, scope, includeGenerated ?? false, binding, cursor, maxResponseBytes, maxResponseTokens, ct)
                : BuildImplementationsAsync(target, symbolIdentifier, maxResults ?? 50, scope, includeGenerated ?? false, binding, cursor, maxResponseBytes, maxResponseTokens, ct),
            null, cancellationToken, resultCursor, "get_type_relations." + relation);

        Task<CallToolResult> Invalid(string field, string hint) => Task.FromResult(McpToolResults.InvalidArgument("The requested relationship option is unsupported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
    }

    private async Task<CallToolResult> BuildHierarchyAsync(AnalysisTarget target, string symbolIdentifier, int maxResults, SymbolScopeType scope, bool includeGenerated, string requestBinding, string? coreCursor, int maxResponseBytes, int? maxResponseTokens, CancellationToken ct)
    {
        if (target.TargetType == AnalysisTargetType.Assembly)
        {
            var accessResult = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
            if (!accessResult.IsSuccess) return NavigationToolSupport.Failure(accessResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
            await using var access = accessResult.Value!;
            if (access.Symbol is not INamedTypeSymbol assemblyNamed)
                return Invalid("symbolIdentifier", "Resolve a named class, interface, or struct.");
            var formatter = CreateAssemblyHandoffFormatter(access);
            BeforeTypeRelationScanForTesting?.Invoke("hierarchy");
            var assemblyResult = await TypeHierarchyScanner.ScanAsync(assemblyNamed, access.Solution, int.MaxValue, ct,
                scope, includeGenerated, formatter).ConfigureAwait(false);
            if (!assemblyResult.IsSuccess)
                return McpToolResults.InvalidArgument(assemblyResult.ErrorMessage!, "$.symbolIdentifier", "Choose a supported named type.",
                    maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            var identity = AnalysisSymbolIdentity.ForAssembly(access.Origin.CanonicalPath, access.Origin.ContentHash,
                access.Generation, access.ReferenceSnapshotHash);
            var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, identity.ContentHash + "|" + access.ReferenceSnapshotHash,
                "get_type_relations.hierarchy", requestBinding, symbolIdentifier.Trim(), scope.ToString(), includeGenerated.ToString(),
                maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var page = NavigationToolSupport.PageResults(assemblyResult.Subtypes, maxResults, coreCursor, binding,
                maxResponseBytes, maxResponseTokens);
            if (page.Error is not null) return page.Error;
            var response = NavigationToolSupport.Success(new { Relation = "hierarchy", assemblyResult.TypeName, assemblyResult.BaseTypes,
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
            BeforeTypeRelationScanForTesting?.Invoke("hierarchy");
            var result = await TypeHierarchyScanner.ScanAsync(named, solution, int.MaxValue, ct, scope, includeGenerated,
                symbolValue => source.FormatHandoff(symbolValue, solution)).ConfigureAwait(false);
            if (!result.IsSuccess) return McpToolResults.InvalidArgument(result.ErrorMessage!, "$.symbolIdentifier", "Choose a supported named type.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, source.Identity.ContentHash,
                "get_type_relations.hierarchy", requestBinding, symbolIdentifier.Trim(), scope.ToString(), includeGenerated.ToString(),
                maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var page = NavigationToolSupport.PageResults(result.Subtypes, maxResults, coreCursor, binding,
                maxResponseBytes, maxResponseTokens);
            if (page.Error is not null) return page.Error;
            var response = NavigationToolSupport.Success(new { Relation = "hierarchy", result.TypeName, result.BaseTypes, result.Interfaces,
                result.SubtypesHeading, Subtypes = page.Items, result.TotalSubtypes, ReturnedSubtypes = page.Items.Length,
                IsTruncated = result.IsTruncated, ResultCursor = page.NextCursor }, result.IsTruncated,
                result.IsTruncated ? "Increase traversal coverage and repeat the query." : null);
            return source.WithMetadata(response,
                $"typeHierarchy(symbol={symbolIdentifier.Trim()}, pageSize={maxResults}, scope={scope}, includeGenerated={includeGenerated})",
                [], page.NextCursor is not null);
        }, maxResponseBytes, maxResponseTokens, ct);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    private async Task<CallToolResult> BuildImplementationsAsync(AnalysisTarget target, string symbolIdentifier, int maxResults, SymbolScopeType scope, bool includeGenerated, string requestBinding, string? coreCursor, int maxResponseBytes, int? maxResponseTokens, CancellationToken ct)
    {
        if (target.TargetType == AnalysisTargetType.Assembly)
        {
            var accessResult = await ResolveAssemblySymbolAsync(target, symbolIdentifier, ct).ConfigureAwait(false);
            if (!accessResult.IsSuccess) return NavigationToolSupport.Failure(accessResult.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
            await using var access = accessResult.Value!;
            var formatter = CreateAssemblyHandoffFormatter(access);
            BeforeTypeRelationScanForTesting?.Invoke("implementations");
            var assemblyResult = await FindReferencesResolver.FindImplementationsAsync(access.Symbol, access.Solution,
                int.MaxValue, ct, scope, includeGenerated, formatter).ConfigureAwait(false);
            if (assemblyResult.ErrorMessage is not null)
                return McpToolResults.InvalidArgument(assemblyResult.ErrorMessage, "$.symbolIdentifier",
                    "Use an interface, abstract/virtual member, or overridable class.",
                    maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            var identity = AnalysisSymbolIdentity.ForAssembly(access.Origin.CanonicalPath, access.Origin.ContentHash,
                access.Generation, access.ReferenceSnapshotHash);
            var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, identity.ContentHash + "|" + access.ReferenceSnapshotHash,
                "get_type_relations.implementations", requestBinding, symbolIdentifier.Trim(), scope.ToString(), includeGenerated.ToString(),
                maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var page = NavigationToolSupport.PageResults(assemblyResult.Implementations, maxResults, coreCursor, binding,
                maxResponseBytes, maxResponseTokens);
            if (page.Error is not null) return page.Error;
            var response = NavigationToolSupport.Success(new { Relation = "implementations", assemblyResult.TargetSymbolName, assemblyResult.TargetKind,
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
            BeforeTypeRelationScanForTesting?.Invoke("implementations");
            var result = await FindReferencesResolver.FindImplementationsAsync(symbol.Symbol!, solution, int.MaxValue, ct, scope, includeGenerated,
                symbolValue => source.FormatHandoff(symbolValue, solution)).ConfigureAwait(false);
            if (result.ErrorMessage is not null) return McpToolResults.InvalidArgument(result.ErrorMessage, "$.symbolIdentifier", "Use an interface, abstract/virtual member, or overridable class.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, source.Identity.ContentHash,
                "get_type_relations.implementations", requestBinding, symbolIdentifier.Trim(), scope.ToString(), includeGenerated.ToString(),
                maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var page = NavigationToolSupport.PageResults(result.Implementations, maxResults, coreCursor, binding,
                maxResponseBytes, maxResponseTokens);
            if (page.Error is not null) return page.Error;
            var response = NavigationToolSupport.Success(new { Relation = "implementations", result.TargetSymbolName, result.TargetKind,
                Implementations = page.Items, result.TotalCount, ReturnedCount = page.Items.Length,
                ResultCursor = page.NextCursor });
            return source.WithMetadata(response,
                $"findImplementations(symbol={symbolIdentifier.Trim()}, pageSize={maxResults}, scope={scope}, includeGenerated={includeGenerated})",
                [], page.NextCursor is not null);
        }, maxResponseBytes, maxResponseTokens, ct);

        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }
}
