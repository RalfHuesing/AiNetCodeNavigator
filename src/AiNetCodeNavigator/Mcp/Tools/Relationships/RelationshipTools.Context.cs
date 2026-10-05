using System.ComponentModel.DataAnnotations;
using System.Globalization;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp.Formatting;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AiNetCodeNavigator.Mcp.Tools.Relationships;

// Selected context sections share the validated declaration, owner, snapshot and lease.
public sealed partial class RelationshipTools
{
    internal int? TestHelperExpansionLimitForTesting { get; set; }
    internal Action<string>? BeforeContextSectionForTesting { get; set; }
    internal Action<string>? BeforeAssemblyContextOwnerOpenForTesting { get; set; }

    [McpServerTool(Name = "get_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Read selected body, direct members, direct uses, or static test candidates for one source or assembly symbol.")]
    public Task<CallToolResult> GetContext(
        [Required, System.ComponentModel.Description("Absolute path to an existing source .sln/.slnx solution or managed .dll/.exe assembly.")] string targetPath,
        [Required, System.ComponentModel.Description("Type or member identifier, including a stable src:/asm: reference, for the selected context target.")] string symbolIdentifier,
        [Required, System.ComponentModel.Description("Non-empty, duplicate-free selection from body, members, uses, and tests.")] string[] sections,
        [System.ComponentModel.Description("Source usage scope: all (default), production, or tests. Applies only to uses.")] string? usageScope = null,
        [System.ComponentModel.Description("Optional case-insensitive member name substring; requires members.")] string? memberNameFilter = null,
        [System.ComponentModel.Description("Optional member kind filter; requires members.")] string? memberKindFilter = null,
        [System.ComponentModel.Description("Member order: lines (default), kind, or name; requires members.")] string? memberSortBy = null,
        [System.ComponentModel.Description("Source member scope: all (default), production, or tests; requires members.")] string? memberScope = null,
        [System.ComponentModel.Description("Include generated source declarations; defaults to false.")] bool? includeGenerated = null,
        [System.ComponentModel.Description("Include referenced assembly owners in the uses section; defaults to false.")] bool? includeReferences = null,
        [Range(0, 2), System.ComponentModel.Description("Intermediate source helper depth for static test candidates (0-2; default 1); requires tests.")] int? testHelperDepth = null,
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
        var validation = ValidateContextArguments(targetPath, symbolIdentifier, sections, usageScope, memberNameFilter, memberKindFilter, memberSortBy, memberScope, includeGenerated,
            includeReferences, testHelperDepth, maxResults, maxBodyLines, startLine, resultCursor, maxResponseBytes, maxResponseTokens);
        if (validation is not null) return Task.FromResult(validation);

        var selected = sections.Select(value => value.Trim().ToLowerInvariant()).ToArray();
        var scope = TryScope(usageScope ?? "all", out var parsedScope) ? parsedScope : SymbolScopeType.All;
        var generated = includeGenerated ?? false;
        var references = includeReferences ?? false;
        var effectivePageSize = maxResults ?? 10;
        var bodyLines = maxBodyLines ?? 80;
        var bodyStart = startLine ?? 1;
        var args = new { symbolIdentifier, sections, usageScope, memberNameFilter, memberKindFilter, memberSortBy, memberScope, includeGenerated, includeReferences, testHelperDepth, maxResults, maxBodyLines, startLine };
        var requestBinding = System.Text.Json.JsonSerializer.Serialize(new { usageScope, memberNameFilter, memberKindFilter, memberSortBy, memberScope, includeGenerated, includeReferences, testHelperDepth, maxResults, maxBodyLines, startLine });

        return NavigationToolSupport.RouteAsync(runtime, "get_context", targetPath, args, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, coreCursor, ct) =>
            {
                var continuation = ParseContextCursor(coreCursor, selected);
                if (continuation.Error is not null) return McpToolResults.InvalidArgument(continuation.Error, "$.resultCursor",
                    "Use a resultCursor returned for this exact get_context selection.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                var activeSections = continuation.Section is null ? selected : [continuation.Section];
                if (target.TargetType == AnalysisTargetType.Project)
                {
                    if (ValidateSourceReferenceInput(symbolIdentifier, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier") is { } sourceRouteError)
                        return sourceRouteError;
                    return await NavigationToolSupport.WithSourceSolutionAsync(runtime, target,
                        async (solution, source, token) => await BuildSourceContextAsync(target, solution, source, symbolIdentifier,
                            selected, activeSections, requestBinding, memberNameFilter, memberKindFilter, memberSortBy ?? "lines", memberScope ?? "all", scope, generated, testHelperDepth ?? 1, effectivePageSize, bodyLines, bodyStart, coreCursor,
                            continuation.Section, maxResponseBytes, maxResponseTokens, token).ConfigureAwait(false),
                        maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
                }
                return await BuildAssemblyContextAsync(target, symbolIdentifier, selected, activeSections, requestBinding, memberNameFilter, memberKindFilter, memberSortBy ?? "lines", generated, references,
                    effectivePageSize, bodyLines, bodyStart, coreCursor, continuation.Section, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
            }, requiredType: null, cancellationToken, resultCursor, "get_context");
    }

    private CallToolResult? ValidateContextArguments(string? targetPath, string? symbolIdentifier, string[]? sections,
        string? usageScope, string? memberNameFilter, string? memberKindFilter, string? memberSortBy, string? memberScope, bool? includeGenerated, bool? includeReferences, int? testHelperDepth, int? maxResults, int? maxBodyLines,
        int? startLine, string? resultCursor, int maxResponseBytes, int? maxResponseTokens)
    {
        if (string.IsNullOrWhiteSpace(symbolIdentifier))
            return McpToolResults.InvalidArgument("symbolIdentifier must be a non-empty symbol identifier.", "$.symbolIdentifier",
                "Provide a declaration name, documentation ID, source position, or stable src:/asm: reference.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (sections is null || sections.Length == 0 || sections.Any(string.IsNullOrWhiteSpace))
            return McpToolResults.InvalidArgument("sections must contain at least one supported section.", "$.sections",
                "Choose one or more of body, members, uses, and tests.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        var normalized = sections.Select(value => value.Trim().ToLowerInvariant()).ToArray();
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            return McpToolResults.InvalidArgument("sections cannot contain duplicates.", "$.sections",
                "List each requested section once.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (normalized.Any(value => value is not ("body" or "members" or "uses" or "tests"))
            || sections.Where((value, index) => !string.Equals(value, normalized[index], StringComparison.Ordinal)).Any())
            return McpToolResults.InvalidArgument("sections contains an unsupported section.", "$.sections",
                "Choose from body, members, uses, and tests.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        foreach (var option in new[] { (Name: "memberNameFilter", Value: memberNameFilter), (Name: "memberKindFilter", Value: memberKindFilter), (Name: "memberSortBy", Value: memberSortBy), (Name: "memberScope", Value: memberScope) })
            if (option.Value is not null && !normalized.Contains("members"))
                return McpToolResults.InvalidArgument($"{option.Name} requires the members section.", "$." + option.Name,
                    "Select members or omit this argument.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (testHelperDepth is < 0 or > 2)
            return McpToolResults.InvalidArgument("testHelperDepth must be from 0 to 2.", "$.testHelperDepth",
                "Use zero, one, or two intermediate source helpers.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (testHelperDepth is not null && !normalized.Contains("tests"))
            return McpToolResults.InvalidArgument("testHelperDepth requires the tests section.", "$.testHelperDepth",
                "Select tests or omit this argument.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (memberSortBy is not null && memberSortBy is not ("lines" or "kind" or "name"))
            return McpToolResults.InvalidArgument("memberSortBy is unsupported.", "$.memberSortBy", "Use lines, kind, or name.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (memberScope is not null && !TryScope(memberScope, out _))
            return McpToolResults.InvalidArgument("memberScope is unsupported.", "$.memberScope", "Use all, production, or tests.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (maxResults is < 1 or > 100) return McpToolResults.InvalidArgument("maxResults must be from 1 to 100.", "$.maxResults",
            "Use a positive list page size no greater than 100.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (maxResults is not null && !normalized.Intersect(["members", "uses", "tests"], StringComparer.Ordinal).Any())
            return McpToolResults.InvalidArgument("maxResults applies only to list sections.", "$.maxResults",
                "Select members, uses, or tests, or omit maxResults.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (maxBodyLines is < 1 or > 1000) return McpToolResults.InvalidArgument("maxBodyLines must be from 1 to 1000.", "$.maxBodyLines",
            "Use a positive body window size.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        if (startLine is < 1) return McpToolResults.InvalidArgument("startLine must be positive.", "$.startLine",
            "Use a one-based body window position.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        var type = Path.GetExtension(targetPath ?? string.Empty).ToLowerInvariant() is ".dll" or ".exe"
            ? AnalysisTargetType.Assembly : AnalysisTargetType.Project;
        if (type == AnalysisTargetType.Project)
        {
            if (includeReferences is not null) return McpToolResults.InvalidArgument("includeReferences applies only to assembly uses.", "$.includeReferences",
                "Omit this argument for source targets.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            if (usageScope is not null && !normalized.Contains("uses"))
                return McpToolResults.InvalidArgument("usageScope requires the uses section.", "$.usageScope",
                    "Select uses or omit usageScope.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            if (usageScope is not null && !TryScope(usageScope, out _))
                return McpToolResults.InvalidArgument("usageScope is unsupported.", "$.usageScope", "Use all, production, or tests.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        }
        else
        {
            if (testHelperDepth is not null) return McpToolResults.InvalidArgument("testHelperDepth applies only to source tests.", "$.testHelperDepth",
                "Use a source solution target or omit this argument.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            if (memberScope is not null) return McpToolResults.InvalidArgument("memberScope applies only to source members.", "$.memberScope",
                "Omit this argument for assembly targets.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            if (normalized.Contains("tests")) return McpToolResults.InvalidArgument("The tests section supports source solutions only.", "$.sections",
                "Use a source solution target for static test candidates.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            if (usageScope is not null) return McpToolResults.InvalidArgument("usageScope applies only to source uses.", "$.usageScope",
                "Omit this argument for assembly targets.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            if (includeGenerated is not null) return McpToolResults.InvalidArgument("includeGenerated applies only to source targets.", "$.includeGenerated",
                "Omit this argument for assembly targets.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
            if (includeReferences is not null && !normalized.Contains("uses"))
                return McpToolResults.InvalidArgument("includeReferences requires the uses section.", "$.includeReferences",
                    "Select uses or omit includeReferences.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
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
        string? memberNameFilter, string? memberKindFilter, string memberSortBy, string memberScope,
        SymbolScopeType usageScope, bool includeGenerated, int testHelperDepth, int pageSize, int bodyLines, int startLine,
        string? internalCursor, string? continuationSection, int bytes, int? tokens, CancellationToken ct)
    {
        var resolved = await SourceSymbolResolver.ResolveAsync(solution, identifier, source.Identity, source.IdentityRequest, ct).ConfigureAwait(false);
        if (!resolved.IsSuccess) return NavigationToolSupport.Failure(resolved.Error!.Value, bytes, tokens, "$.symbolIdentifier");
        var symbol = resolved.Symbol!;
        if (selected.Contains("members", StringComparer.Ordinal) && symbol is not INamedTypeSymbol)
            return McpToolResults.InvalidArgument("members requires a type target.", "$.sections",
                "Select a type declaration or remove members.", maxResponseBytes: bytes, maxResponseTokens: tokens);
        var generatedDocumentOwners = await ExactSourceSymbolResolver.GetSourceGeneratedDocumentOwnersAsync(solution, ct)
            .ConfigureAwait(false);
        var targetDeclarations = symbol.DeclaringSyntaxReferences;
        SyntaxReference? selectedDeclaration = includeGenerated ? targetDeclarations.FirstOrDefault() : null;
        foreach (var candidate in targetDeclarations)
        {
            if (selectedDeclaration is not null) break;
            var document = GetSourceDocument(solution, candidate.SyntaxTree, generatedDocumentOwners);
            if (document is null || (document is not SourceGeneratedDocument
                && !await GeneratedDocumentDetector.IsGeneratedDocumentAsync(document, ct).ConfigureAwait(false)))
                selectedDeclaration = candidate;
        }
        if (!includeGenerated && targetDeclarations.Length > 0 && selectedDeclaration is null)
            return McpToolResults.InvalidArgument("The selected declaration is generated source and excluded by includeGenerated=false.", "$.includeGenerated",
                "Set includeGenerated=true to inspect generated declarations.", maxResponseBytes: bytes, maxResponseTokens: tokens);
        var declaration = BuildContextDeclaration(symbol, solution, target.CanonicalPath, selectedDeclaration,
            symbolValue => source.FormatHandoff(symbolValue, solution));
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
                        handoffId: source.FormatHandoff(symbol, solution), solution: solution)
                    : SourceSymbolBodyResolver.Resolve(symbol, bodyLines, startLine,
                        handoffId: source.FormatHandoff(symbol, solution), solution: solution);
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
                _ = TryScope(memberScope, out var memberScopeType);
                var structure = await ClassStructureScanner.ScanResolvedTypeAsync(new ClassStructureScanRequest(
                    solution, identifier, memberSortBy, KindFilter: memberKindFilter, NameFilter: memberNameFilter,
                    HandoffIdentity: source.Identity, ScopeType: memberScopeType, IncludeGenerated: includeGenerated, CollectAllMembers: true)
                    { CurrentIdentityRequest = source.IdentityRequest }, type, ct).ConfigureAwait(false);
                var all = structure.Members;
                var page = PageContextList(all, target.CanonicalPath, source.Identity.ContentHash, selected, section,
                    pageSize, internalCursor, identifier, usageScope, null, includeGenerated, null, null, requestBinding, bytes, tokens);
                if (page.Error is not null) return page.Error;
                sections.Add(new ContextSection(section, page.NextCursor is null ? "complete" : "partial", "direct declared members", [], all.Count, page.Items!, page.NextCursor,
                    null, null, page.NextCursor is null ? null : "Continue this section with its resultCursor.",
                    AnalysisComplete: true, ResultContinuationAvailable: page.NextCursor is not null, Structure: new ContextMemberStructure(structure.TypeName, structure.Kind, target.CanonicalPath, null, structure.Files, structure.TotalLines)));
            }
                else if (section == "uses")
                {
                var refs = await FindReferencesResolver.FindReferencesAsync(symbol, solution, int.MaxValue, 1, ct,
                    scope: usageScope, includeGenerated: includeGenerated,
                    handoffFormatter: symbolValue => source.FormatHandoff(symbolValue, solution)).ConfigureAwait(false);
                var page = PageContextList(refs.References, target.CanonicalPath, source.Identity.ContentHash, selected, section,
                    pageSize, internalCursor, identifier, usageScope, null, includeGenerated, null, null, requestBinding, bytes, tokens);
                if (page.Error is not null) return page.Error;
                if (!refs.IsComplete) omissions.Add("usageAnalysisLimit");
                sections.Add(new ContextSection(section, !refs.IsComplete ? "partial" : page.NextCursor is null ? "complete" : "partial", "direct incoming source references",
                    refs.IsComplete ? [] : ["usageAnalysisLimit"], refs.TotalCount, page.Items!, page.NextCursor, null, null,
                    page.NextCursor is null ? null : "Continue this section with its resultCursor.",
                    AnalysisComplete: refs.IsComplete, ResultContinuationAvailable: page.NextCursor is not null));
            }
                else
                {
                var tests = await TestRecommendationBuilder.BuildAsync(symbol, solution, source.IdentityRequest, ct,
                    includeGenerated, SymbolScopeType.All, testHelperDepth,
                    maxExpandedHelpers: TestHelperExpansionLimitForTesting ?? TestRecommendationBuilder.MaxExpandedHelpers).ConfigureAwait(false);
                var fixtures = tests.TestFixtures;
                var page = PageContextList(fixtures, target.CanonicalPath, source.Identity.ContentHash, selected, section,
                    pageSize, internalCursor, identifier, usageScope, null, includeGenerated, null, null, requestBinding, bytes, tokens);
                if (page.Error is not null) return page.Error;
                var limited = tests.ImplementationExpansionLimitReached || tests.CandidateExpansionLimitReached || tests.ReferenceInspectionLimitReached || tests.HelperExpansionLimitReached;
                var reasons = new List<string>();
                if (tests.ImplementationExpansionLimitReached) reasons.Add("implementationExpansionLimit");
                if (tests.CandidateExpansionLimitReached) reasons.Add("candidateExpansionLimit");
                if (tests.ReferenceInspectionLimitReached) reasons.Add("referenceInspectionLimit");
                if (tests.HelperExpansionLimitReached) reasons.Add("helperExpansionLimit");
                sections.Add(new ContextSection(section, limited || page.NextCursor is not null ? "partial" : "complete", "recognized source test projects and files",
                    reasons, tests.TotalTestFixtures, page.Items!, page.NextCursor, null, null,
                    limited ? "Select a narrower symbol to inspect beyond the bounded test-candidate analysis." :
                        page.NextCursor is null ? null : "Continue this section with its resultCursor.",
                    AnalysisComplete: !limited, ResultContinuationAvailable: page.NextCursor is not null,
                    Analysis: new { tests.EvidenceMode, tests.ExpandedImplementationCount, tests.ImplementationExpansionLimitReached,
                        tests.CandidateExpansionLimitReached, tests.ReferenceInspectionLimitReached,
                        tests.TestHelperDepth, tests.ExpandedHelperCount, tests.HelperExpansionLimitReached }));
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
        return source.WithMetadata(response, $"get_context(symbol={identifier.Trim()}, sections={string.Join('|', selected)}, usageScope={usageScope}, includeGenerated={includeGenerated}, testHelperDepth={testHelperDepth}, maxResults={pageSize}, bodyStart={startLine}, bodyLines={bodyLines})",
            omissions.ToArray(), sections.Any(item => item is ContextSection { ResultCursor: not null }));
    }

    private static Document? GetSourceDocument(
        Solution solution,
        SyntaxTree syntaxTree,
        IReadOnlyDictionary<SyntaxTree, SourceGeneratedDocument> generatedDocumentOwners) =>
        solution.GetDocument(syntaxTree)
        ?? (generatedDocumentOwners.TryGetValue(syntaxTree, out var generatedDocument) ? generatedDocument : null);

    private async Task<CallToolResult> BuildAssemblyContextAsync(AnalysisTarget target, string identifier, string[] selected,
        string[] active, string requestBinding, string? memberNameFilter, string? memberKindFilter, string memberSortBy, bool includeGenerated, bool includeReferences, int pageSize, int bodyLines, int startLine,
        string? internalCursor, string? continuationSection, int bytes, int? tokens, CancellationToken ct)
    {
        if (includeReferences && selected.Contains("uses", StringComparer.Ordinal))
            return await BuildAssemblyContextWithReferencesAsync(target, identifier, selected, active, requestBinding, memberNameFilter, memberKindFilter, memberSortBy, pageSize,
                bodyLines, startLine, internalCursor, continuationSection, bytes, tokens, ct).ConfigureAwait(false);
        var normalizedIdentifier = InputNormalizer.NormalizeSymbolIdentifier(identifier);
        if (AssemblySymbolInputResolver.TryRouteIdentifier(identifier, normalizedIdentifier, out var reference, out var referenceError))
        {
            if (referenceError is not null) return NavigationToolSupport.Failure(referenceError.Value, bytes, tokens, "$.symbolIdentifier");
            if (reference is not StableSymbolReference.Assembly)
                return NavigationToolSupport.Failure(new ResultError(NavigationErrorCodes.TargetMismatch,
                    "A source reference cannot be resolved in an assembly target.",
                    "Open the source solution target and use the src: reference."), bytes, tokens, "$.symbolIdentifier");
        }
        var opened = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
        if (!opened.IsSuccess) return NavigationToolSupport.Failure(opened.Error!.Value, bytes, tokens, "$.targetPath");
        await using var scope = opened.Value!;
        var symbolResult = await AssemblySymbolInputResolver.ResolveAsync(scope, identifier, ct).ConfigureAwait(false);
        if (!symbolResult.IsSuccess) return NavigationToolSupport.Failure(symbolResult.Error!.Value, bytes, tokens, "$.symbolIdentifier");
        var symbol = symbolResult.Symbol!;
        var symbolHandoff = symbolResult.HandoffId;
        var identity = AnalysisSymbolIdentity.ForAssembly(scope.Context.Origin.CanonicalPath, scope.Context.Origin.ContentHash,
            scope.Context.Generation, scope.Context.ReferenceSnapshotHash);
        if (selected.Contains("members", StringComparer.Ordinal) && symbol is not INamedTypeSymbol)
            return McpToolResults.InvalidArgument("members requires a type target.", "$.sections",
                "Select a type declaration or remove members.", maxResponseBytes: bytes, maxResponseTokens: tokens);
        var ownerFormatter = CreateAssemblyHandoffFormatter(scope.Solution, scope.Context);
        var declaration = BuildContextDeclaration(symbol, scope.Solution, scope.Context.Origin.CanonicalPath,
            referenceFormatter: ownerFormatter);
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
                var sourceRoot = scope.Context.DecompiledProjectPaths?.DecompiledSourceRoot;
                var structure = BuildAssemblyClassStructure(type, memberSortBy,
                    memberKindFilter, memberNameFilter, sourceRoot, formatReference: ownerFormatter);
                var all = structure.Members;
                var page = PageContextList(all, target.CanonicalPath, identity.ContentHash + "|" + scope.Context.ReferenceSnapshotHash, selected, section,
                    pageSize, internalCursor, identifier, SymbolScopeType.All, includeReferences, false, null, null, requestBinding, bytes, tokens);
                if (page.Error is not null) return page.Error;
                sections.Add(new ContextSection(section, page.NextCursor is null ? "complete" : "partial", "direct declared members", [], all.Count, page.Items!, page.NextCursor, null, null,
                    page.NextCursor is null ? null : "Continue this section with its resultCursor.",
                    AnalysisComplete: true, ResultContinuationAvailable: page.NextCursor is not null, Structure: new ContextMemberStructure(structure.TypeName, structure.Kind, scope.Context.Origin.CanonicalPath, sourceRoot, structure.Files, structure.TotalLines)));
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
                    : new[] { incompleteClosure ? "referenceClosureIncomplete" : incompleteOwner ? "assemblyOwnerIncomplete" : "usageAnalysisLimit" };
                sections.Add(new ContextSection(section, reasons.Length > 0 || page.NextCursor is not null ? "partial" : "complete",
                    includeReferences ? "direct uses in selected assembly reference scope" : "direct uses in selected assembly owner",
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
        string[] selected, string[] active, string requestBinding, string? memberNameFilter, string? memberKindFilter, string memberSortBy, int pageSize, int bodyLines, int startLine, string? internalCursor,
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
        var declaration = BuildContextDeclaration(symbol, owner.Scope.Solution, owner.TargetPath,
            referenceFormatter: _ => handoff);
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
                var sourceRoot = owner.Scope.Context.DecompiledProjectPaths?.DecompiledSourceRoot;
                var structure = BuildAssemblyClassStructure(type, memberSortBy,
                    memberKindFilter, memberNameFilter, sourceRoot, formatReference: member => AssemblyReferenceClosureSession.Externalize(formatter(member)));
                var all = structure.Members;
                var page = PageContextList(all, target.CanonicalPath, session.RootAnalysisIdentity.ContentHash + "|" + session.RootReferenceSnapshotHash + "|" + session.RootAnalysisIdentity.Generation,
                    selected, section, pageSize, internalCursor, identifier, SymbolScopeType.All, true, false, null, null, requestBinding, bytes, tokens);
                if (page.Error is not null) return page.Error;
                sections.Add(new ContextSection(section, page.NextCursor is null ? "complete" : "partial", "direct declared members", [], all.Count, page.Items!, page.NextCursor, null, null,
                    page.NextCursor is null ? null : "Continue this section with its resultCursor.",
                    AnalysisComplete: true, ResultContinuationAvailable: page.NextCursor is not null, Structure: new ContextMemberStructure(structure.TypeName, structure.Kind, owner.TargetPath, sourceRoot, structure.Files, structure.TotalLines)));
            }
            else if (section == "uses")
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
        string identifier, SymbolScopeType usageScope, bool? includeReferences, bool includeGenerated,
        int? bodyLines, int? startLine, string requestBinding, int bytes, int? tokens)
    {
        var binding = BoundResultCursor.CreateBinding(target, snapshot, "get_context." + section,
            identifier.Trim(), string.Join("\0", selected), usageScope.ToString(), includeReferences?.ToString(),
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

    private static ContextDeclaration BuildContextDeclaration(ISymbol symbol, Solution solution,
        string ownerPath, SyntaxReference? preferredDeclaration = null,
        Func<ISymbol, string?>? referenceFormatter = null)
    {
        var location = preferredDeclaration is null
            ? symbol.Locations.FirstOrDefault(item => item.IsInSource)
            : Location.Create(preferredDeclaration.SyntaxTree, preferredDeclaration.Span);
        var line = location?.GetLineSpan().StartLinePosition.Line + 1 ?? 0;
        var handoff = referenceFormatter?.Invoke(symbol);
        return new ContextDeclaration(symbol.Name, symbol.Kind.ToString().ToLowerInvariant(),
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), SymbolVisibilityResolver.ResolveVisibility(symbol),
            location?.SourceTree?.FilePath ?? string.Empty, line, handoff, ownerPath, string.Empty);
    }

    private sealed record ContextDeclaration(string Name, string Kind, string Signature, string Visibility,
        string FilePath, int Line, string? HandoffId, string OwnerTargetPath, string SnapshotId);
    private static ClassStructurePayload BuildAssemblyClassStructure(INamedTypeSymbol type,
        string sortBy, string? kindFilter, string? nameFilter, string? decompiledSourceRoot,
        Func<ISymbol, string?>? formatReference = null)
    {
        var sourceRoot = string.IsNullOrWhiteSpace(decompiledSourceRoot) ? null : decompiledSourceRoot;
        var members = type.GetMembers().Where(member => !member.IsImplicitlyDeclared)
            .Select(member =>
            {
                var location = member.Locations.FirstOrDefault(item => item.IsInSource);
                var span = location?.GetLineSpan();
                var handoff = formatReference?.Invoke(member);
                var relative = span?.Path is { } file ? FormatAssemblySourcePath(sourceRoot, file) : string.Empty;
                var kind = member switch
                {
                    IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => "Constructor",
                    IMethodSymbol => "Method",
                    IPropertySymbol => "Property",
                    IFieldSymbol { IsConst: true } => "Constant",
                    IFieldSymbol => "Field",
                    IEventSymbol => "Event",
                    INamedTypeSymbol named => named.TypeKind.ToString(),
                    _ => member.Kind.ToString(),
                };
                var start = span?.StartLinePosition.Line + 1 ?? 0;
                var end = span?.EndLinePosition.Line + 1 ?? 0;
                return new ClassStructureMemberEntry(kind, member.Name, member.DeclaredAccessibility.ToString().ToLowerInvariant(), start, end,
                    Math.Max(0, end - start + 1), member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), relative, handoff);
            })
            .Where(member => string.IsNullOrWhiteSpace(kindFilter) || kindFilter.Equals("all", StringComparison.OrdinalIgnoreCase) || member.Kind.Contains(kindFilter, StringComparison.OrdinalIgnoreCase))
            .Where(member => string.IsNullOrWhiteSpace(nameFilter) || member.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        members = sortBy.ToLowerInvariant() switch
        {
            "kind" => members.OrderBy(member => member.Kind, StringComparer.Ordinal).ThenBy(member => member.Name, StringComparer.Ordinal).ToList(),
            "name" => members.OrderBy(member => member.Name, StringComparer.Ordinal).ToList(),
            _ => members.OrderBy(member => member.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(member => member.StartLine)
                .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
        var typeKind = type.IsRecord ? type.TypeKind == TypeKind.Struct ? "Record Struct" : "Record Class" : type.TypeKind.ToString();
        var files = type.Locations.Where(location => location.IsInSource && location.SourceTree?.FilePath is not null)
            .Select(location => FormatAssemblySourcePath(sourceRoot, location.SourceTree!.FilePath))
            .Concat(members.Select(member => member.FilePath))
            .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(filePath => filePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ClassStructurePayload(type.ToDisplayString(), typeKind, files, members.Sum(item => item.LineCount), members.Count, members.Count,
            false, members, []);
    }

    private static string FormatAssemblySourcePath(string? decompiledSourceRoot, string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (string.IsNullOrWhiteSpace(decompiledSourceRoot)) return fullPath.Replace('\\', '/');
        var fullRoot = Path.GetFullPath(decompiledSourceRoot);
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? fullPath.Replace('\\', '/') : relative.Replace('\\', '/');
    }

    private sealed record ContextMemberStructure(string TypeName, string Kind, string TargetPath, string? DecompiledSourceRoot, IReadOnlyList<string> Files, int TotalLines);
    private sealed record ContextSectionError(string Code, string Message);
    private sealed record ContextSection(string Name, string Status, string AnalyzedScope, IReadOnlyList<string> Omissions,
        int TotalCount, object Items, string? ResultCursor, int? NextStartLine, ContextSectionError? Error,
        string? NextAction = null, bool AnalysisComplete = true, bool ResultContinuationAvailable = false, object? Analysis = null, ContextMemberStructure? Structure = null);
    private sealed record ContextResult(string Status, ContextDeclaration Target, IReadOnlyList<object> Sections,
        string? ContinuationSection, IReadOnlyList<string> Omissions);

}
