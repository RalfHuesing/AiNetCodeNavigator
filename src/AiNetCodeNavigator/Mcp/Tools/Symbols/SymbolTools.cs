using System.ComponentModel.DataAnnotations;
using Microsoft.CodeAnalysis;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Common;
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
    internal Action<int>? BeforeAssemblyBodyBatchItemForTesting { get; set; }

    [McpServerTool(Name = "find_symbol", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Find C# types or members by one or more name patterns; maxResults pages the combined match list with stable src: or asm: declaration references when available.")]
    public Task<CallToolResult> FindSymbol(
        [Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath,
        [System.ComponentModel.Description("Specify exactly one of this field or pattern, or omit both for extensionOnly enumeration. This field accepts one to ten non-empty name patterns.")] string[]? namePatterns = null,
        [System.ComponentModel.Description("A single non-empty name pattern. Specify this or namePatterns, but not both.")]
        string? pattern = null,
        [System.ComponentModel.Description("Optional C# symbol kind filter: class, interface, record, record class, record struct, struct, enum, delegate, method, property, or field.")]
        string? kind = null,
        [System.ComponentModel.Description("Scope: all (default), production, or tests. Source searches classify documents; assembly searches classify each owner assembly.")]
        string scopeType = "all",
        [System.ComponentModel.Description("Include declarations from generated source files.")] bool includeGenerated = false,
        [Range(1, 1000), System.ComponentModel.Description("Maximum matching symbols to return in this result page across all selected patterns.")] int maxResults = 50,
        [System.ComponentModel.Description("For assembly targets, include matches from resolved referenced assemblies; source searches ignore this option.")] bool includeReferences = false,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque cursor returned for the next page of known symbol matches; use after reading all outer response pages.")] string? resultCursor = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16 * 1024,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Source only: exact loaded project name or returned canonical project path; duplicate names require a path.")] string? project = null,
        [System.ComponentModel.Description("Ordinal ignore-case substring of the full declared namespace; whitespace means no filter.")] string? namespaceFilter = null,
        [System.ComponentModel.Description("Ordinal case-sensitive substring of the returned signature; whitespace means no filter.")] string? signatureFilter = null,
        [System.ComponentModel.Description("Search actual declared extension methods only; may omit both pattern inputs to enumerate. No expression applicability is implied.")] bool extensionOnly = false,
        [System.ComponentModel.Description("Declared receiver filter requires extensionOnly; aliases and qualified suffixes match, without assignability checks.")] string? receiverType = null,
        [System.ComponentModel.Description("Include detailed assembly navigation/reference diagnostics; defaults false.")] bool includeDiagnostics = false,
        CancellationToken cancellationToken = default)
    {
        if (project is not null && string.IsNullOrWhiteSpace(project)) return Invalid("project", "Omit project or choose an exact loaded name or canonical path.");
        if (project is not null && Path.GetExtension(targetPath).ToLowerInvariant() is ".dll" or ".exe") return Invalid("project", "Project selection supports source solutions only.");
        namespaceFilter = NormalizeFilter(namespaceFilter);
        signatureFilter = string.IsNullOrWhiteSpace(signatureFilter) ? null : signatureFilter;
        receiverType = NormalizeFilter(receiverType);
        if (receiverType is not null && !extensionOnly) return Invalid("receiverType", "Set extensionOnly=true to filter declared receivers.");
        if (extensionOnly && kind is not null && !string.Equals(kind.Trim(), "method", StringComparison.OrdinalIgnoreCase)) return Invalid("kind", "Omit kind or use method with extensionOnly.");
        var enumerateExtensions = extensionOnly && namePatterns is null && pattern is null;
        if (!enumerateExtensions && (namePatterns is null) == string.IsNullOrWhiteSpace(pattern))
        {
            return Task.FromResult(McpToolResults.InvalidArgument(
                "Specify exactly one of namePatterns or pattern.", "$.namePatterns",
                "Provide a non-empty pattern or a non-empty namePatterns array, but not both.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        }
        var patterns = enumerateExtensions ? new[] { "*" } : namePatterns ?? [pattern!];
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

        if (extensionOnly) symbolKind = SymbolKindFilter.Method;
        var effectivePatterns = patterns.Distinct(StringComparer.Ordinal).ToArray();
        var arguments = new { patterns = effectivePatterns, kind = symbolKind, scope, includeGenerated, includeReferences, maxResults, project = project?.Trim(), namespaceFilter, signatureFilter, extensionOnly, receiverType, includeDiagnostics };
        var discoveryScope = string.Concat(
            project is null ? "" : $", project={project.Trim()}",
            namespaceFilter is null ? "" : $", namespaceFilter={namespaceFilter}",
            signatureFilter is null ? "" : $", signatureFilter={signatureFilter}",
            extensionOnly ? ", extensionOnly=true" : "",
            receiverType is null ? "" : $", receiverType={receiverType}");
        var filterBinding = System.Text.Json.JsonSerializer.Serialize(arguments);
        return NavigationToolSupport.RouteAsync(runtime, "find_symbol", targetPath, arguments,
            operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, coreCursor, ct) =>
            {
                var results = new List<FindSymbolScanResult>();
                if (target.TargetType == AnalysisTargetType.Project)
                {
                    return await NavigationToolSupport.WithSourceSolutionAsync(runtime, target, async (solution, source, token) =>
                    {
                        ProjectId? selectedProject = null;
                        if (project is not null)
                        {
                            var selection = solution.Projects.Where(candidate => string.Equals(candidate.Name, project.Trim(), StringComparison.OrdinalIgnoreCase)
                                || !string.IsNullOrWhiteSpace(candidate.FilePath) && string.Equals(Path.GetFullPath(candidate.FilePath).Replace('\\', '/'), project.Trim().Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)).ToArray();
                            if (selection.Length != 1)
                                return McpToolResults.Recoverable(selection.Length == 0 ? NavigationErrorCodes.InvalidArgument : NavigationErrorCodes.AmbiguousSymbol,
                                    selection.Length == 0 ? "No loaded source project matches the selector." : "The project name matches multiple loaded owners.",
                                    "Choose a returned canonical project path: " + string.Join(", ", solution.Projects.Select(candidate => candidate.FilePath)),
                                    fieldPath: "$.project", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                            selectedProject = selection[0].Id;
                        }
                        foreach (var searchPattern in effectivePatterns)
                        {
                            token.ThrowIfCancellationRequested();
                            results.Add(await FindSymbolScanner.FindMatchesWithDetailsAsync(
                                new FindSymbolScanRequest(solution, searchPattern, symbolKind, scope, int.MaxValue,
                                    SourceIdentity: source.Identity, IncludeGenerated: includeGenerated)
                                {
                                    CurrentIdentityRequest = source.IdentityRequest, ProjectId = selectedProject, NamespaceFilter = namespaceFilter,
                                    SignatureFilter = signatureFilter, ExtensionOnly = extensionOnly, ReceiverType = receiverType,
                                }, token).ConfigureAwait(false));
                        }

                        var queryParts = effectivePatterns.Cast<string?>()
                            .Prepend(effectivePatterns.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .Concat([symbolKind.ToString(), scope.ToString(), includeGenerated.ToString(),
                                maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture)]).ToArray();
                        var binding = BoundResultCursor.CreateBinding(target.CanonicalPath,
                            source.Identity.ContentHash, "find_symbol", queryParts.Append(filterBinding).ToArray());
                        var paged = PageFindResults(results, effectivePatterns, maxResults, coreCursor, binding,
                            maxResponseBytes, maxResponseTokens);
                        if (paged.Error is { } pageError) return NavigationToolSupport.Failure(pageError, maxResponseBytes, maxResponseTokens, "$.resultCursor");
                        var response = FormatFindResults(paged.Results, effectivePatterns, paged.ResultCursor,
                            maxResponseBytes, maxResponseTokens);
                        var omissions = results.SelectMany(static result => result.TruncatedBy).Where(reason => reason != "maxResults").Distinct(StringComparer.Ordinal).ToArray();
                        var scopes = string.Join(";", effectivePatterns.Select(patternValue => $"pattern={patternValue}"));
                        return source.WithMetadata(response,
                            $"findSymbol({scopes}, kind={symbolKind}, scope={scope}, includeGenerated={includeGenerated}, maxResults={maxResults}{discoveryScope})",
                            omissions, paged.ResultCursor is not null);
                    }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
                }

                var openedAssemblyScope = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
                if (!openedAssemblyScope.IsSuccess)
                {
                    return NavigationToolSupport.Failure(openedAssemblyScope.Error!.Value,
                        maxResponseBytes, maxResponseTokens, "$.targetPath");
                }

                await using var assemblyScope = openedAssemblyScope.Value!;
                foreach (var searchPattern in effectivePatterns)
                {
                    ct.ThrowIfCancellationRequested();
                    if (extensionOnly)
                    {
                        var extensions = await FindAssemblyExtensionsScanner.FindAsync(new FindAssemblyExtensionsRequest(target.CanonicalPath,
                            ReceiverType: receiverType, Namespace: namespaceFilter, IncludeReferences: includeReferences)
                            { NamePattern = searchPattern, SignatureFilter = signatureFilter, Scope = scope, CollectAllInventory = true }, ct, assemblyScope).ConfigureAwait(false);
                        if (!extensions.IsSuccess) return NavigationToolSupport.Failure(extensions.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
                        var payload = extensions.Value!;
                        var entries = payload.Extensions.Select(entry => new SymbolLocationEntry(entry.Name, "method", entry.DocCommentId, entry.HandoffId,
                            "", 0, 0, entry.AssemblyName, entry.DiscoverySignature!, OwnerTargetPath: entry.OwnerTargetPath,
                            IsExtension: true, Namespace: entry.Namespace, ContainingType: entry.ContainingType, ReceiverType: entry.ReceiverType, ReturnType: entry.ReturnType)).ToArray();
                        var extensionOmissions = payload.Analysis?.OmissionReasons ?? [];
                        results.Add(new FindSymbolScanResult("Declared extension matches.", entries, payload.TotalCount, entries.Length,
                            extensionOmissions.Count > 0, extensionOmissions, [], Diagnostics: AssemblyDiagnosticProjection.Project(payload.Diagnostics, includeDiagnostics)));
                    }
                    else
                    {
                        var result = await AssemblyFindSymbolScanner.FindAsync(target.CanonicalPath, searchPattern,
                            symbolKind, scope, int.MaxValue, includeReferences, ct, assemblyScope, namespaceFilter, signatureFilter).ConfigureAwait(false);
                        if (result.Error is { } error) return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, "$.targetPath");
                        results.Add(result);
                    }
                }

                var assemblyIdentity = AssemblySymbolInputResolver.CreateIdentity(assemblyScope);
                var assemblyQueryParts = effectivePatterns.Cast<string?>()
                    .Prepend(effectivePatterns.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Concat([assemblyScope.Context.ReferenceSnapshotHash, assemblyIdentity.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        symbolKind.ToString(), scope.ToString(), includeGenerated.ToString(), includeReferences.ToString(),
                        maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture)]).ToArray();
                var binding = BoundResultCursor.CreateBinding(target.CanonicalPath,
                    assemblyIdentity.ContentHash, "find_symbol", assemblyQueryParts.Append(filterBinding).ToArray());
                var paged = PageFindResults(results, effectivePatterns, maxResults, coreCursor, binding,
                    maxResponseBytes, maxResponseTokens);
                if (paged.Error is { } pageError) return NavigationToolSupport.Failure(pageError, maxResponseBytes, maxResponseTokens, "$.resultCursor");
                var response = FormatFindResults(paged.Results, effectivePatterns, paged.ResultCursor,
                    maxResponseBytes, maxResponseTokens);
                var omissions = results.SelectMany(static result => result.TruncatedBy).Where(reason => reason != "maxResults").Distinct(StringComparer.Ordinal).ToArray();
                var analyzedScope = $"findSymbol(patterns={string.Join('|', effectivePatterns)}, kind={symbolKind}, scope={scope}, includeGenerated={includeGenerated}, includeReferences={includeReferences}, maxResults={maxResults}{discoveryScope})";
                return NavigationToolSupport.WithAssemblyMetadata(response, assemblyIdentity, analyzedScope, omissions,
                    resultContinuationAvailable: paged.ResultCursor is not null);
            }, null, cancellationToken, resultCursor, "find_symbol.matches");

        Task<CallToolResult> Invalid(string field, string hint) => Task.FromResult(McpToolResults.InvalidArgument("The selected discovery options are unsupported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        static string? NormalizeFilter(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    }

    [McpServerTool(Name = "get_symbol_body", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Read source or decompiled text for one or more symbols selected by name, documentation ID, source location, or stable src:/asm: reference.")]
    public Task<CallToolResult> GetSymbolBody(
        [Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath,
        [Required, System.ComponentModel.Description("One or more symbol names, documentation IDs, source locations, or stable src:/asm: references.")] string[] symbolIdentifiers,
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
                "Provide symbol names, documentation IDs, source positions, or canonical src:/asm: references.",
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
                var itemFailures = new List<string>();
                var hasDomainGaps = false;
                var hasSourceResolutionGaps = false;
                var hasSourceBodyLimit = false;
                if (target.TargetType == AnalysisTargetType.Project)
                {
                    var sourceRouteErrors = symbolIdentifiers
                        .Select(identifier => GetBodyReferenceRouteError(identifier, sourceTarget: true)).ToArray();
                    if (sourceRouteErrors.All(static error => error is not null))
                    {
                        var failures = symbolIdentifiers.Select((identifier, index) =>
                            FormatResolutionFailure(identifier, sourceRouteErrors[index]!.Value, string.Empty)).ToArray();
                        return NavigationToolSupport.Failure(
                            AggregateResolutionFailures(sourceRouteErrors[0]!.Value, failures),
                            maxResponseBytes, maxResponseTokens, "$.symbolIdentifiers");
                    }
                    var response = await NavigationToolSupport.WithSourceSolutionAsync(runtime, target, async (solution, source, token) =>
                    {
                        ResultError? firstSourceResolutionError = null;
                        var resolvedSourceBodies = 0;
                        for (var index = 0; index < symbolIdentifiers.Length; index++)
                        {
                            token.ThrowIfCancellationRequested();
                            var identifier = symbolIdentifiers[index];
                            if (sourceRouteErrors[index] is { } routeError)
                            {
                                firstSourceResolutionError ??= routeError;
                                var failure = FormatResolutionFailure(identifier, routeError, string.Empty);
                                items.Add(failure);
                                itemFailures.Add(failure);
                                hasDomainGaps = true;
                                hasSourceResolutionGaps = true;
                                continue;
                            }
                            var resolved = await SourceSymbolBodyResolver.ResolveAsync(
                                solution, identifier, effectiveLines, startLine, source.Identity, source.IdentityRequest, token).ConfigureAwait(false);
                            hasDomainGaps |= resolved.Error is not null || resolved.Body?.HasMore == true;
                            hasSourceResolutionGaps |= resolved.Error is not null;
                            hasSourceBodyLimit |= resolved.Body?.HasMore == true;
                            if (resolved.Error is { } error)
                            {
                                firstSourceResolutionError ??= error;
                                var candidates = resolved.ResolutionCandidates.Count == 0 ? string.Empty
                                    : $" Candidates: {string.Join(", ", resolved.ResolutionCandidates.Select(FormatResolutionCandidate))}.";
                                var failure = FormatResolutionFailure(identifier, error, candidates);
                                items.Add(failure);
                                itemFailures.Add(failure);
                            }
                            else if (resolved.Body is { } body)
                            {
                                resolvedSourceBodies++;
                                items.Add(FormatBody(identifier, body, target.CanonicalPath, targetPath, effectiveLines, symbolIdentifiers.Length > 1));
                            }
                        }
                        if (resolvedSourceBodies == 0 && firstSourceResolutionError is { } resolutionError)
                            return NavigationToolSupport.Failure(AggregateResolutionFailures(resolutionError, itemFailures),
                                maxResponseBytes, maxResponseTokens, "$.symbolIdentifiers");

                        var response = NavigationToolSupport.SuccessText(string.Join("\n\n", items), hasDomainGaps,
                            hasDomainGaps ? "Follow each item's next body window or error action." : null);
                        return source.WithMetadata(response,
                            $"symbolBody(identifiers={string.Join('|', symbolIdentifiers)}, lines={startLine}..{(endLine?.ToString() ?? $"+{effectiveLines}")})",
                            new[]
                            {
                                hasSourceResolutionGaps ? "unresolvedSymbol" : null,
                                hasSourceBodyLimit ? "maxBodyLines" : null,
                            }.Where(static reason => reason is not null).Select(static reason => reason!).ToArray());
                    }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
                    return response;
                }

                ResultError? firstAssemblyResolutionError = null;
                var resolvedAssemblyBodies = 0;
                var hasAssemblyResolutionGaps = false;
                var hasAssemblyBodyLimit = false;
                var assemblyRouteErrors = symbolIdentifiers
                    .Select(identifier => GetBodyReferenceRouteError(identifier, sourceTarget: false)).ToArray();
                if (assemblyRouteErrors.All(static error => error is not null))
                {
                    var failures = symbolIdentifiers.Select((identifier, index) =>
                        FormatResolutionFailure(identifier, assemblyRouteErrors[index]!.Value, string.Empty)).ToArray();
                    return NavigationToolSupport.Failure(
                        AggregateResolutionFailures(assemblyRouteErrors[0]!.Value, failures),
                        maxResponseBytes, maxResponseTokens, "$.symbolIdentifiers");
                }
                var openedAssemblyScope = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
                if (!openedAssemblyScope.IsSuccess)
                    return NavigationToolSupport.Failure(openedAssemblyScope.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
                await using var assemblyScope = openedAssemblyScope.Value!;
                for (var index = 0; index < symbolIdentifiers.Length; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    BeforeAssemblyBodyBatchItemForTesting?.Invoke(index);
                    var identifier = symbolIdentifiers[index];
                    if (assemblyRouteErrors[index] is { } routeError)
                    {
                        firstAssemblyResolutionError ??= routeError;
                        var failure = FormatResolutionFailure(identifier, routeError, string.Empty);
                        items.Add(failure);
                        itemFailures.Add(failure);
                        hasDomainGaps = true;
                        hasAssemblyResolutionGaps = true;
                        continue;
                    }
                    var resolved = await AssemblySymbolBodyScanner.GetAsync(
                        identifier, effectiveLines, startLine, ct, expectedTargetPath: target.CanonicalPath, pinnedScope: assemblyScope).ConfigureAwait(false);
                    hasDomainGaps |= resolved.Error is not null || resolved.Body?.HasMore == true;
                    hasAssemblyResolutionGaps |= resolved.Error is not null;
                    hasAssemblyBodyLimit |= resolved.Body?.HasMore == true;
                    if (resolved.Error is { } error)
                    {
                        firstAssemblyResolutionError ??= error;
                        var candidates = resolved.ResolutionCandidates.Count == 0 ? string.Empty
                            : $" Candidates: {string.Join(", ", resolved.ResolutionCandidates.Select(FormatResolutionCandidate))}.";
                        var failure = FormatResolutionFailure(identifier, error, candidates);
                        items.Add(failure);
                        itemFailures.Add(failure);
                    }
                    else if (resolved.Body is { } body)
                    {
                        resolvedAssemblyBodies++;
                        items.Add(FormatBody(identifier, body, target.CanonicalPath, targetPath, effectiveLines, symbolIdentifiers.Length > 1));
                    }
                }
                if (resolvedAssemblyBodies == 0 && firstAssemblyResolutionError is { } resolutionError)
                    return NavigationToolSupport.Failure(AggregateResolutionFailures(resolutionError, itemFailures),
                        maxResponseBytes, maxResponseTokens, "$.symbolIdentifiers");

                var decompiledSourceRoot = assemblyScope.Context.DecompiledProjectPaths?.DecompiledSourceRoot;
                var responseText = string.Join("\n\n", items);
                if (resolvedAssemblyBodies > 0 && !string.IsNullOrWhiteSpace(decompiledSourceRoot))
                    responseText = $"Decompiled source root: {decompiledSourceRoot}\n\n{responseText}";
                var assemblyResponse = NavigationToolSupport.SuccessText(responseText, hasDomainGaps,
                    hasDomainGaps ? "Follow each item's next body window or error action." : null);
                return NavigationToolSupport.WithAssemblyMetadata(assemblyResponse, AssemblySymbolInputResolver.CreateIdentity(assemblyScope),
                    $"symbolBody(identifiers={string.Join('|', symbolIdentifiers)}, lines={startLine}..{(endLine?.ToString() ?? $"+{effectiveLines}")})",
                    new[]
                    {
                        hasAssemblyResolutionGaps ? "unresolvedSymbol" : null,
                        hasAssemblyBodyLimit ? "maxBodyLines" : null,
                    }.Where(static reason => reason is not null).Select(static reason => reason!).ToArray());
            }, null, cancellationToken);
    }

    private static CallToolResult FormatFindResults(
        IReadOnlyList<FindSymbolScanResult> results,
        IReadOnlyList<string> patterns,
        string? resultCursor,
        int maxResponseBytes,
        int? maxResponseTokens)
    {
        if (results.FirstOrDefault(result => result.Error is not null)?.Error is { } error)
        {
            return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, "$.targetPath");
        }

        var truncated = resultCursor is not null || results.Any(result => result.IsTruncated);
        var payload = new FindSymbolBatchResponse(patterns.Select((pattern, index) => new FindSymbolPatternResponse(pattern,
            results[index].Entries.Select(ProjectFindSymbolEntry).ToArray(), results[index].TotalMatches, results[index].ReturnedMatches,
            results[index].TruncatedBy, results[index].KindAlternatives)).ToArray(), resultCursor,
            results.SelectMany(result => result.Diagnostics ?? []).Distinct(StringComparer.Ordinal).ToArray());
        var omissions = results.SelectMany(result => result.TruncatedBy).ToArray();
        var nextAction = resultCursor is not null ? "Use resultCursor after reading all outer response pages."
            : omissions.Contains("incompleteRelationships", StringComparer.Ordinal) ? "Restore missing assembly references, then retry the same query."
            : omissions.Contains("maxResults", StringComparer.Ordinal) ? "Increase maxResults up to 1000 and repeat the same pattern query."
            : "Review the reported omission reasons and restore required owner/reference evidence, then retry the same query.";
        return NavigationToolSupport.Success(payload, truncated, truncated ? nextAction : null);
    }

    private static (IReadOnlyList<FindSymbolScanResult> Results, string? ResultCursor, ResultError? Error) PageFindResults(
        IReadOnlyList<FindSymbolScanResult> results, IReadOnlyList<string> patterns, int pageSize, string? cursor,
        string binding, int maxResponseBytes, int? maxResponseTokens)
    {
        var status = BoundResultCursor.ReadOffset(cursor, binding, out var offset);
        if (status != BoundResultCursor.CursorStatus.Valid)
        {
            var code = status == BoundResultCursor.CursorStatus.InvalidFormat ? NavigationErrorCodes.InvalidArgument : NavigationErrorCodes.StaleSnapshot;
            return (results, null, new ResultError(code,
                status == BoundResultCursor.CursorStatus.InvalidFormat ? "resultCursor is invalid." : "resultCursor is not bound to this target snapshot and symbol query.",
                "Repeat the same symbol query against the same target snapshot using its most recent resultCursor."));
        }

        var flattened = results.SelectMany((result, patternIndex) => result.Entries.Select(entry => (PatternIndex: patternIndex, Entry: entry))).ToArray();
        if (offset > flattened.Length)
            return (results, null, new ResultError(NavigationErrorCodes.InvalidArgument, "resultCursor is beyond the remaining symbol matches.", "Use a resultCursor from a nonfinal result page."));
        var page = BoundResultCursor.Page(flattened, offset, pageSize, binding);
        var nextResults = results.Select((result, index) =>
        {
            var entries = page.Items.Where(item => item.PatternIndex == index).Select(item => item.Entry).ToArray();
            var truncated = page.NextCursor is not null;
            return result with
            {
                Entries = entries,
                ReturnedMatches = entries.Length,
                IsTruncated = truncated || result.TruncatedBy.Any(reason => reason != "maxResults"),
                TruncatedBy = (truncated ? new[] { "maxResults" } : Array.Empty<string>())
                    .Concat(result.TruncatedBy.Where(reason => reason != "maxResults")).Distinct(StringComparer.Ordinal).ToArray(),
                ResultCursor = null,
                Text = string.Join(Environment.NewLine, entries.Select(entry => $"- {entry.Kind} {entry.Name} in {entry.FilePath}:{entry.Line} ({entry.ProjectName})"
                    + (entry.HandoffId is null ? string.Empty : $" [handoff: {entry.HandoffId}]"))),
            };
        }).ToArray();
        return (nextResults, page.NextCursor, null);
    }

    private sealed record FindSymbolBatchResponse(IReadOnlyList<FindSymbolPatternResponse> Results, string? ResultCursor, IReadOnlyList<string> Diagnostics);
    private sealed record FindSymbolPatternResponse(string Pattern, IReadOnlyList<SymbolLocationEntry> Entries, int TotalMatches,
        int ReturnedMatches, IReadOnlyList<string> TruncatedBy, IReadOnlyList<string> KindAlternatives);

    private static SymbolLocationEntry ProjectFindSymbolEntry(SymbolLocationEntry entry)
    {
        if (entry.Locations is not { Count: 1 } locations)
            return entry;

        var primary = locations[0];
        return string.Equals(primary.FilePath, entry.FilePath, StringComparison.Ordinal)
            && primary.Line == entry.Line
            && primary.EndLine == entry.EndLine
            && string.Equals(primary.ProjectName, entry.ProjectName, StringComparison.Ordinal)
                ? entry with { Locations = null }
                : entry;
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
        candidate.OwnerTargetPath is { Length: > 0 } ownerPath
            ? candidate.HandoffId is { Length: > 0 } handoffId
                ? $"{candidate.Signature} at {candidate.FilePath}:{candidate.Line} [targetPath: `{ownerPath}`, handoffId: `{handoffId}`]"
                : $"{candidate.Signature} at {candidate.FilePath}:{candidate.Line} [targetPath: `{ownerPath}`; no stable reference, repeat the raw identifier]"
            : candidate.HandoffId is { Length: > 0 } sourceReference
                ? $"{candidate.Signature} at {candidate.FilePath}:{candidate.Line} [reference: `{sourceReference}`]"
                : $"{candidate.Signature} at {candidate.FilePath}:{candidate.Line} [no stable reference, use this raw source location]";

    private static ResultError? GetBodyReferenceRouteError(string identifier, bool sourceTarget)
    {
        var discoveryProbe = InputNormalizer.NormalizeSymbolIdentifier(identifier);
        if (sourceTarget)
        {
            if (!StableSymbolReferenceCodec.TryParseReferenceInput(identifier, discoveryProbe,
                out var sourceReference, out var sourceError)) return null;
            if (sourceError is { } invalidSourceReference) return invalidSourceReference;
            return sourceReference is StableSymbolReference.Source
                ? null
                : new ResultError(NavigationErrorCodes.TargetMismatch,
                    "An assembly reference cannot be resolved in a source solution.",
                    "Open the assembly owner targetPath and use the asm: reference there.");
        }

        if (!AssemblySymbolInputResolver.TryRouteIdentifier(identifier, discoveryProbe,
            out var assemblyReference, out var assemblyError)) return null;
        if (assemblyError is { } invalidAssemblyReference) return invalidAssemblyReference;
        return assemblyReference is StableSymbolReference.Assembly
            ? null
            : new ResultError(NavigationErrorCodes.TargetMismatch,
                "A source reference cannot be resolved in an assembly target.",
                "Open the source solution target and use the src: reference there.");
    }

    private static string FormatBody(string identifier, SymbolBodyResult body, string targetPath, string requestedTargetPath,
        int windowLineCount, bool includeSelector)
    {
        var handoff = body.HandoffId is null || string.Equals(identifier, body.HandoffId, StringComparison.Ordinal)
            ? string.Empty : $"Handoff: {body.HandoffId}\n";
        var owner = string.Equals(targetPath, requestedTargetPath, StringComparison.OrdinalIgnoreCase)
            ? string.Empty : $"Owner targetPath: {targetPath}\n";
        var symbol = includeSelector ? $"## {identifier}\n" : string.Empty;
        var availability = body.Availability == "available" ? string.Empty : $"Availability: {body.Availability}\n";
        var mode = body.ContentMode == "source" ? string.Empty : $"Content mode: {body.ContentMode}\n";
        var hint = string.IsNullOrWhiteSpace(body.Hint) ? string.Empty : $"\nHint: {body.Hint}";
        var nextAction = body.HasMore
            ? body.HandoffId is not null
                ? $"Next body window: startLine={body.DisplayedEnd + 1}, maxBodyLines={windowLineCount}; omit endLine and continue this item using its handoff."
                : $"Next body window: startLine={body.DisplayedEnd + 1}, maxBodyLines={windowLineCount}; repeat the original symbolIdentifier with the same targetPath."
            : string.Empty;
        var nextActionLine = nextAction.Length == 0 ? string.Empty : $"{nextAction}\n";
        return $"{symbol}{availability}{mode}{handoff}{owner}Lines: {body.DisplayedStart}-{body.DisplayedEnd}/{body.TotalLines}{hint}\n{nextActionLine}{body.Body}";
    }

    private static string FormatResolutionFailure(string identifier, ResultError error, string candidates)
    {
        var nextAction = error.Hint ?? error.Code switch
        {
            NavigationErrorCodes.TargetMismatch => "Open the owning target and use the matching src: or asm: reference.",
            NavigationErrorCodes.StaleSnapshot => "Repeat the original discovery query against the current target and use a reference from that response.",
            _ => "Repeat the discovery query and use a current reference when one is available; otherwise use the raw source location.",
        };
        return $"## {identifier}\nResolution status: failed ({error.Code})\nError: {error.Message}{candidates}\nNext action: {nextAction}";
    }

    private static ResultError AggregateResolutionFailures(ResultError primary, IReadOnlyList<string> itemFailures) =>
        new(primary.Code,
            $"Every requested body item failed resolution. Item results:{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, itemFailures)}",
            "Use the next action listed for each item, then repeat get_symbol_body with the same targetPath and stable reference or raw identifier.");

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
