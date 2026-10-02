using System.ComponentModel.DataAnnotations;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AiNetCodeNavigator.Mcp.Tools.Symbols;

[McpServerToolType]
public sealed class SymbolTools(NavigatorHostRuntime runtime)
{
    [McpServerTool(Name = "find_symbol", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Find C# types or members by one or more name patterns and return source locations with navigable symbol handles.")]
    public Task<CallToolResult> FindSymbol(
        [Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath,
        [System.ComponentModel.Description("Specify exactly one of this field or pattern. This field accepts one to ten non-empty name patterns.")] string[]? namePatterns = null,
        [System.ComponentModel.Description("A single non-empty name pattern. Specify this or namePatterns, but not both.")]
        string? pattern = null,
        [System.ComponentModel.Description("Optional C# symbol kind filter: class, interface, record, record class, record struct, struct, enum, delegate, method, property, or field.")]
        string? kind = null,
        [System.ComponentModel.Description("Source scope: all (default), production, or tests.")]
        string scopeType = "all",
        [System.ComponentModel.Description("Include declarations from generated source files.")] bool includeGenerated = false,
        [Range(1, 1000), System.ComponentModel.Description("Maximum matching symbols to return per pattern.")] int maxResults = 50,
        [System.ComponentModel.Description("For assembly targets, include matches from resolved referenced assemblies; source searches ignore this option.")] bool includeReferences = false,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16 * 1024,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        CancellationToken cancellationToken = default)
    {
        if ((namePatterns is null) == string.IsNullOrWhiteSpace(pattern))
        {
            return Task.FromResult(McpToolResults.InvalidArgument(
                "Specify exactly one of namePatterns or pattern.", "$.namePatterns",
                "Provide a non-empty pattern or a non-empty namePatterns array, but not both.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        }
        var patterns = namePatterns ?? [pattern!];
        if (patterns.Length is < 1 or > 10 || patterns.Any(string.IsNullOrWhiteSpace))
        {
            return Task.FromResult(McpToolResults.InvalidArgument(
                "namePatterns must contain between 1 and 10 non-empty patterns.", "$.namePatterns",
                "Provide up to 10 non-empty patterns.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        }
        if (!TryScope(scopeType, out var scope))
        {
            return Task.FromResult(McpToolResults.InvalidArgument(
                "scopeType must be all, production, or tests.", "$.scopeType",
                "Choose one of the supported scope values.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        }
        if (!TryKind(kind, out var symbolKind))
        {
            return Task.FromResult(McpToolResults.InvalidArgument(
                "kind is not a supported C# symbol kind.", "$.kind",
                "Use a supported type or member kind.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        }

        var effectivePatterns = patterns.Distinct(StringComparer.Ordinal).ToArray();
        var arguments = new { patterns = effectivePatterns, kind = symbolKind, scope, includeGenerated, maxResults, includeReferences };
        return NavigationToolSupport.RouteAsync(runtime, "find_symbol", targetPath, arguments,
            operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                var results = new List<FindSymbolScanResult>();
                foreach (var searchPattern in effectivePatterns)
                {
                    ct.ThrowIfCancellationRequested();
                    FindSymbolScanResult result;
                    if (target.TargetType == AnalysisTargetType.Assembly)
                    {
                        result = await AssemblyFindSymbolScanner.FindAsync(target.CanonicalPath, searchPattern,
                            symbolKind, scope, maxResults, includeReferences, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        var scanned = await ScanSourceAsync(target, searchPattern, symbolKind, scope,
                            includeGenerated, maxResults, ct, maxResponseBytes, maxResponseTokens).ConfigureAwait(false);
                        if (scanned.Result is null) return scanned.Response!;
                        result = scanned.Result;
                    }
                    if (result.Error is { } error)
                        return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, "$.targetPath");
                    results.Add(result);
                }

                var truncated = results.Any(result => result.IsTruncated);
                var text = string.Join("\n\n", results.Select((result, index) =>
                {
                    var resultText = target.TargetType == AnalysisTargetType.Assembly
                        ? FormatAssemblyFindResult(result)
                        : result.Text;
                    return results.Count == 1 ? resultText : $"Pattern: {effectivePatterns[index]}\n{resultText}";
                }));
                return NavigationToolSupport.SuccessText(text, truncated,
                    truncated ? "Increase maxResults up to 1000 and repeat the same pattern query." : null);
            }, null, cancellationToken);
    }

    [McpServerTool(Name = "get_symbol_body", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Read source or decompiled text for one or more current symbol handoffs, with line-bounded output.")]
    public Task<CallToolResult> GetSymbolBody(
        [Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath,
        [Required, System.ComponentModel.Description("One or more symbol names, documentation IDs, source locations, or current h: handoff identifiers.")] string[] symbolIdentifiers,
        [Range(1, 1000), System.ComponentModel.Description("Maximum source or decompiled lines to return for each symbol.")] int maxBodyLines = 80,
        [Range(1, int.MaxValue), System.ComponentModel.Description("First line to include, using one-based numbering.")] int startLine = 1,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional inclusive final line to include.")] int? endLine = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 32768).") ] int maxResponseBytes = 32 * 1024,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        CancellationToken cancellationToken = default)
    {
        if (symbolIdentifiers.Length == 0 || symbolIdentifiers.Any(string.IsNullOrWhiteSpace))
            return Task.FromResult(McpToolResults.InvalidArgument(
                "symbolIdentifiers must contain one or more non-empty identifiers.", "$.symbolIdentifiers",
                "Provide symbol names, documentation IDs, source positions, or h: handoffs.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        if (endLine is { } end && end < startLine)
            return Task.FromResult(McpToolResults.InvalidArgument(
                "endLine must be greater than or equal to startLine.", "$.endLine",
                "Choose an inclusive endLine that is not before startLine.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));

        var effectiveLines = endLine is { } inclusiveEnd ? inclusiveEnd - startLine + 1 : maxBodyLines;
        var arguments = new { symbolIdentifiers, maxBodyLines, startLine, endLine };
        return NavigationToolSupport.RouteAsync(runtime, "get_symbol_body", targetPath, arguments,
            operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                var items = new List<object>();
                var hasDomainGaps = false;
                var suggestedStartLine = startLine;
                if (target.TargetType == AnalysisTargetType.Project)
                {
                    var response = await NavigationToolSupport.WithSourceSolutionAsync(runtime, target, async (solution, token) =>
                    {
                        var identity = await AnalysisSymbolIdentity.ForSourceAsync(solution, token).ConfigureAwait(false);
                        ResultError? firstSourceResolutionError = null;
                        var resolvedSourceBodies = 0;
                        foreach (var identifier in symbolIdentifiers)
                        {
                            token.ThrowIfCancellationRequested();
                            var resolved = await SourceSymbolBodyResolver.ResolveAsync(
                                solution, identifier, effectiveLines, startLine, identity, token).ConfigureAwait(false);
                            hasDomainGaps |= resolved.Error is not null || resolved.Body?.HasMore == true;
                            if (resolved.Body?.HasMore == true)
                                suggestedStartLine = Math.Max(suggestedStartLine, resolved.Body.DisplayedEnd + 1);
                            if (resolved.Error is { } error)
                            {
                                firstSourceResolutionError ??= error;
                                var candidates = resolved.ResolutionCandidates.Count == 0 ? string.Empty
                                    : $" Candidates: {string.Join(", ", resolved.ResolutionCandidates.Select(FormatResolutionCandidate))}.";
                                items.Add($"Could not resolve {identifier}: {error.Code}: {error.Message}{candidates}");
                            }
                            else if (resolved.Body is { } body)
                            {
                                resolvedSourceBodies++;
                                items.Add(FormatBody(identifier, body, target.CanonicalPath));
                            }
                        }
                        if (resolvedSourceBodies == 0 && firstSourceResolutionError is { } resolutionError)
                            return NavigationToolSupport.Failure(resolutionError, maxResponseBytes, maxResponseTokens, "$.symbolIdentifiers");

                        return NavigationToolSupport.SuccessText(string.Join("\n\n", items), hasDomainGaps,
                            hasDomainGaps ? $"Resolve item errors and repeat; for a body with more lines set startLine to {suggestedStartLine}." : null);
                    }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
                    return response;
                }

                ResultError? firstAssemblyResolutionError = null;
                var resolvedAssemblyBodies = 0;
                foreach (var identifier in symbolIdentifiers)
                {
                    ct.ThrowIfCancellationRequested();
                    var resolved = await AssemblySymbolBodyScanner.GetAsync(
                        identifier, effectiveLines, startLine, ct, expectedTargetPath: target.CanonicalPath).ConfigureAwait(false);
                    hasDomainGaps |= resolved.Error is not null || resolved.Body?.HasMore == true;
                    if (resolved.Body?.HasMore == true)
                        suggestedStartLine = Math.Max(suggestedStartLine, resolved.Body.DisplayedEnd + 1);
                    if (resolved.Error is { } error)
                    {
                        firstAssemblyResolutionError ??= error;
                        var candidates = resolved.ResolutionCandidates.Count == 0 ? string.Empty
                            : $" Candidates: {string.Join(", ", resolved.ResolutionCandidates.Select(FormatResolutionCandidate))}.";
                        items.Add($"Could not resolve {identifier}: {error.Code}: {error.Message}{candidates}");
                    }
                    else if (resolved.Body is { } body)
                    {
                        resolvedAssemblyBodies++;
                        items.Add(FormatBody(identifier, body, target.CanonicalPath));
                    }
                }
                if (resolvedAssemblyBodies == 0 && firstAssemblyResolutionError is { } resolutionError)
                    return NavigationToolSupport.Failure(resolutionError, maxResponseBytes, maxResponseTokens, "$.symbolIdentifiers");

                return NavigationToolSupport.SuccessText(string.Join("\n\n", items), hasDomainGaps,
                    hasDomainGaps ? $"Resolve item errors and repeat; for a body with more lines set startLine to {suggestedStartLine}." : null);
            }, null, cancellationToken);
    }

    private async Task<(FindSymbolScanResult? Result, CallToolResult? Response)> ScanSourceAsync(
        AnalysisTarget target,
        string pattern,
        SymbolKindFilter kind,
        SymbolScopeType scope,
        bool includeGenerated,
        int maxResults,
        CancellationToken cancellationToken,
        int maxResponseBytes,
        int? maxResponseTokens)
    {
        FindSymbolScanResult? result = null;
        var response = await NavigationToolSupport.WithSourceSolutionAsync(runtime, target, async (solution, token) =>
        {
            var identity = await AnalysisSymbolIdentity.ForSourceAsync(solution, token).ConfigureAwait(false);
            result = await FindSymbolScanner.FindMatchesWithDetailsAsync(
                new FindSymbolScanRequest(solution, pattern, kind, scope, maxResults,
                    SourceIdentity: identity, IncludeGenerated: includeGenerated), token).ConfigureAwait(false);
            return NavigationToolSupport.Success(result);
        }, maxResponseBytes, maxResponseTokens, cancellationToken).ConfigureAwait(false);
        return (result, response);
    }

    private static bool TryScope(string value, out SymbolScopeType scope)
    {
        scope = value.ToLowerInvariant() switch
        {
            "all" => SymbolScopeType.All,
            "production" => SymbolScopeType.Production,
            "tests" => SymbolScopeType.Tests,
            _ => (SymbolScopeType)(-1),
        };
        return Enum.IsDefined(scope);
    }

    private static string FormatResolutionCandidate(SymbolResolutionCandidate candidate) =>
        candidate.OwnerTargetPath is { Length: > 0 } ownerPath && candidate.HandoffId is { Length: > 0 } handoffId
            ? $"{candidate.Signature} at {candidate.FilePath}:{candidate.Line} [targetPath: `{ownerPath}`, handoffId: `{handoffId}`]"
            : candidate.Name;

    private static string FormatBody(string identifier, SymbolBodyResult body, string targetPath)
    {
        var status = body.HasMore ? ", more lines available" : ", complete";
        var handoff = body.HandoffId is null ? string.Empty : $"\nHandoff: {body.HandoffId}\nOwner targetPath: {targetPath}";
        return $"Symbol: {identifier}\nContent mode: {body.ContentMode}{handoff}\nLines: {body.DisplayedStart}-{body.DisplayedEnd} of {body.TotalLines}{status}\n{body.Body}";
    }

    private static string FormatAssemblyFindResult(FindSymbolScanResult result)
    {
        if (result.Entries.Count == 0)
            return result.IsTruncated
                ? $"{result.Text}\nSearch incomplete: {string.Join(", ", result.TruncatedBy)}."
                : result.Text;
        var lines = result.Entries.Select(entry =>
        {
            var handoff = entry.HandoffId is null ? string.Empty : $" [handoff: {entry.HandoffId}]";
            var owner = string.IsNullOrWhiteSpace(entry.OwnerTargetPath) ? string.Empty : $" [targetPath: {entry.OwnerTargetPath}]";
            return $"- {entry.Kind} {entry.Name} in {entry.FilePath}:{entry.Line} ({entry.ProjectName}) {entry.Signature}{owner}{handoff}";
        });
        var summary = $"Found {result.TotalMatches} matching assembly symbol(s); returned {result.ReturnedMatches}.";
        var incomplete = result.IsTruncated ? $"\nSearch incomplete: {string.Join(", ", result.TruncatedBy)}." : string.Empty;
        return summary + "\n" + string.Join("\n", lines) + incomplete;
    }

    private static bool TryKind(string? value, out SymbolKindFilter kind)
    {
        kind = value?.Trim().ToLowerInvariant() switch
        {
            null or "all" => SymbolKindFilter.All,
            "class" => SymbolKindFilter.Class,
            "record" => SymbolKindFilter.Record,
            "record class" => SymbolKindFilter.RecordClass,
            "record struct" => SymbolKindFilter.RecordStruct,
            "struct" => SymbolKindFilter.Struct,
            "interface" => SymbolKindFilter.Interface,
            "enum" => SymbolKindFilter.Enum,
            "delegate" => SymbolKindFilter.Delegate,
            "method" => SymbolKindFilter.Method,
            "property" => SymbolKindFilter.Property,
            "field" => SymbolKindFilter.Field,
            "event" => SymbolKindFilter.Event,
            _ => (SymbolKindFilter)(-1),
        };
        return Enum.IsDefined(kind);
    }
}
