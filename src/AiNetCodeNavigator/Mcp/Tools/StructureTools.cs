using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.FileStructure;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Skeletons;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp.Formatting;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AiNetCodeNavigator.Mcp.Tools;

[McpServerToolType]
public sealed class StructureTools(NavigatorHostRuntime runtime)
{
    internal Action<int>? BeforeAssemblySkeletonItemForTesting { get; set; }

    internal Action<string>? BeforeBrowseScanForTesting { get; set; }

    [McpServerTool(Name = "browse_target", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Browse one explicit view: loaded source scope, or source/assembly namespaces and types.")]
    public Task<CallToolResult> BrowseTarget(
        [Required, System.ComponentModel.Description("Absolute source .sln/.slnx or managed .dll/.exe target path; scope supports source only.")] string targetPath,
        [Required, System.ComponentModel.Description("Required selected view: scope or namespaces. Only the selected scanner runs.")] string view,
        [System.ComponentModel.Description("Namespace view only: exact project name or canonical project path.")] string? project = null,
        [System.ComponentModel.Description("Namespace view only: namespace prefix.")] string? namespacePrefix = null,
        [Range(1, 3), System.ComponentModel.Description("Namespace view only: namespace depth (1–3; default 1).")] int? depth = null,
        [System.ComponentModel.Description("Namespace view only: include type declarations (default true).")] bool? includeTypes = null,
        [System.ComponentModel.Description("Namespace view only: all (default), class, interface, record, struct, enum, or delegate.")] string? kind = null,
        [System.ComponentModel.Description("Namespace view only: include generated source declarations (default false).")] bool? includeGenerated = null,
        [Range(1, NamespaceTreeScanner.MaxResultsCap), System.ComponentModel.Description("Inventory page size: scope defaults to 100 (1–128); namespaces defaults to 50 (1–200). All discovered entries remain reachable through resultCursor.")] int? maxResults = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).")] int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target, view and query to poll.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token for the next outer response page; repeat the same target, view and query.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque cursor for the next inventory page of the same selected view and original options.")] string? resultCursor = null,
        CancellationToken cancellationToken = default)
    {
        if (view is not ("scope" or "namespaces")) return Invalid("view", "Choose scope or namespaces explicitly.");
        if (view == "scope")
        {
            foreach (var option in new[] { ("project", project is not null), ("namespacePrefix", namespacePrefix is not null),
                ("depth", depth is not null), ("includeTypes", includeTypes is not null), ("kind", kind is not null), ("includeGenerated", includeGenerated is not null) })
                if (option.Item2) return Invalid(option.Item1, "Omit namespace-view options when selecting scope.");
            if (Path.GetExtension(targetPath ?? string.Empty).ToLowerInvariant() is ".dll" or ".exe")
                return Invalid("view", "Scope supports source solutions only; choose namespaces for an assembly.");
        }
        var pageSize = maxResults ?? (view == "scope" ? 100 : 50);
        var pageCap = view == "scope" ? IndexScopeScanner.MaxFileTypesCap : NamespaceTreeScanner.MaxResultsCap;
        if (pageSize < 1 || pageSize > pageCap) return Invalid("maxResults", $"Use a page size from 1 to {pageCap} for this view.");
        if (depth is < 1 or > 3) return Invalid("depth", "Use a namespace depth from 1 to 3.");
        if (kind is not null && kind is not ("all" or "class" or "interface" or "record" or "struct" or "enum" or "delegate"))
            return Invalid("kind", "Choose all, class, interface, record, struct, enum, or delegate.");
        var args = new { view, project, namespacePrefix, depth, includeTypes, kind, includeGenerated, maxResults };
        var requestBinding = JsonSerializer.Serialize(args);
        return NavigationToolSupport.RouteAsync(runtime, "browse_target", targetPath, args, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens, async (target, coreCursor, ct) =>
            {
                BeforeBrowseScanForTesting?.Invoke(view);
                if (view == "scope") return await BuildScopeViewAsync(target, coreCursor, pageSize, requestBinding, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
                return await BuildNamespaceViewAsync(target, project, namespacePrefix, depth ?? 1, includeTypes ?? true, kind ?? "all", includeGenerated ?? false,
                    pageSize, requestBinding, coreCursor, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
            }, view == "scope" ? AnalysisTargetType.Project : null, cancellationToken, resultCursor, "browse_target." + view);

        Task<CallToolResult> Invalid(string field, string hint) => Task.FromResult(McpToolResults.InvalidArgument("The requested option is not supported for this view.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
    }

    private async Task<CallToolResult> BuildScopeViewAsync(AnalysisTarget target, string? coreCursor, int maxResults,
        string requestBinding, int maxResponseBytes, int? maxResponseTokens, CancellationToken ct) =>
        await NavigationToolSupport.WithSourceSolutionAsync(runtime, target, async (solution, source, token) =>
            {
                var result = await IndexScopeScanner.ScanAsync(solution, token,
                    new IndexScopeScanOptions(MaxProjects: IndexScopeScanner.MaxProjectsCap,
                        MaxFileTypes: IndexScopeScanner.MaxFileTypesCap, CollectAllInventory: true,
                        ConfiguredFrameworksByProject: source.ConfiguredTargetFrameworks)).ConfigureAwait(false);
                if (!result.ScanCompleted)
                    return McpToolResults.Recoverable("INDEX_SCOPE_FAILED", result.Error ?? "The source index scope could not be scanned.",
                        "Check the loaded solution and repeat the query.");
                var items = new List<object>(result.Projects.Count + result.FileTypes.Count);
                foreach (var entry in result.Projects)
                    items.Add(new { Kind = "project", entry.Name,
                        ProjectIdentity = $"{entry.ProjectPath ?? entry.Name}::{entry.LoadedFrameworkContext ?? "unknown"}",
                        entry.ProjectPath, entry.DocumentCount, entry.CSharpDocumentCount, entry.IsTestProject, entry.IsCSharpProject,
                        LoadedFrameworkContext = entry.LoadedFrameworkContext ?? "unknown",
                        Exclusions = entry.Exclusions ?? Array.Empty<string>(),
                        entry.ConfiguredFrameworksKnown,
                        ConfiguredFrameworksNotAnalyzed = entry.ConfiguredFrameworksNotAnalyzed ?? Array.Empty<string>() });
                foreach (var entry in result.FileTypes)
                    items.Add(new { Kind = "fileType", entry.Extension, entry.Count, entry.SymbolGraphCovered });
                var indexScopeSnapshotHash = source.CreateIndexScopeSnapshotHash();
                var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, indexScopeSnapshotHash,
                    "browse_target.scope", "allProjectsAndFileTypes", requestBinding,
                    maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var page = NavigationToolSupport.PageResults(items, maxResults, coreCursor, binding,
                    maxResponseBytes, maxResponseTokens);
                if (page.Error is not null) return page.Error;
                var response = NavigationToolSupport.Success(new
                {
                    View = "scope", Inventory = "loadedRoslynDocuments", result.SolutionPath, result.ProjectCount, result.TotalDocumentCount, result.CSharpFileCount,
                    result.TestProjectCount, result.GeneratedDocumentCount, result.TestDocumentCount, result.TotalFileTypeCount,
                    Summary = $"Roslyn documents: {result.TotalDocumentCount}; .cs: {result.CSharpFileCount}; generated C#: {result.GeneratedDocumentCount}; tests: {result.TestDocumentCount}.",
                    Items = page.Items, TotalItems = items.Count, ReturnedItems = page.Items.Length, ResultCursor = page.NextCursor,
                });
                return source.WithMetadata(response,
                    $"indexScope(project=*, pageSize={maxResults}, loadedProjects={result.ProjectCount}, loadedFileTypes={result.TotalFileTypeCount})",
                    resultContinuationAvailable: page.NextCursor is not null,
                    snapshotContentHash: indexScopeSnapshotHash);
            }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);

    private async Task<CallToolResult> BuildNamespaceViewAsync(AnalysisTarget target, string? project, string? namespacePrefix, int depth,
        bool includeTypes, string kind, bool includeGenerated, int maxResults, string requestBinding, string? coreCursor,
        int maxResponseBytes, int? maxResponseTokens, CancellationToken ct)
    {
        if (target.TargetType == AnalysisTargetType.Project)
            return await NavigationToolSupport.WithSourceSolutionAsync(runtime, target,
                async (solution, source, token) =>
                {
                    var response = await ScanNamespaceTreeAsync(solution, target.CanonicalPath, project, namespacePrefix, depth, includeTypes, kind, maxResults,
                        includeGenerated, source.Identity.ContentHash, coreCursor, token, maxResponseBytes, maxResponseTokens,
                        includeProjectOverview: true, requestBinding: requestBinding,
                        formatTypeReference: symbol => source.FormatHandoff(symbol, solution)).ConfigureAwait(false);
                    var omissions = ReadStringArrayFromResult(response, "truncatedBy");
                    return source.WithMetadata(response,
                        $"namespaceTree(project={project?.Trim() ?? "*"}, prefix={namespacePrefix?.Trim() ?? "*"}, depth={depth}, includeTypes={includeTypes}, kind={kind.Trim().ToLowerInvariant()}, pageSize={maxResults}, includeGenerated={includeGenerated})",
                        omissions.Where(reason => reason != "maxResults").ToArray(), HasResultCursor(response));
                }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);

        var opened = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
        if (!opened.IsSuccess) return NavigationToolSupport.Failure(opened.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
        await using var scope = opened.Value!;
        var identity = AnalysisSymbolIdentity.ForAssembly(scope.Context.Origin.CanonicalPath, scope.Context.Origin.ContentHash,
            scope.Context.Generation, scope.Context.ReferenceSnapshotHash);
        var response = await ScanNamespaceTreeAsync(scope.Solution, target.CanonicalPath, project, namespacePrefix, depth, includeTypes, kind, maxResults, includeGenerated,
            identity.ContentHash + "|" + scope.Context.ReferenceSnapshotHash, coreCursor, ct,
            maxResponseBytes, maxResponseTokens, includeProjectOverview: false, requestBinding: requestBinding,
            formatTypeReference: type => FormatAssemblyReference(type, scope)).ConfigureAwait(false);
        var omissions = ReadStringArrayFromResult(response, "truncatedBy");
        return NavigationToolSupport.WithAssemblyMetadata(response, identity,
            $"namespaceTree(project={project?.Trim() ?? "*"}, prefix={namespacePrefix?.Trim() ?? "*"}, depth={depth}, includeTypes={includeTypes}, kind={kind.Trim().ToLowerInvariant()}, pageSize={maxResults}, includeGenerated={includeGenerated})",
            omissions.Where(reason => reason != "maxResults").ToArray(), HasResultCursor(response));
    }

    [McpServerTool(Name = "get_file_skeleton", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Show declarations in selected source files and provide stable src:/asm: references when declarations support them.")]
    public Task<CallToolResult> GetFileSkeleton(
        [Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath,
        [Required, System.ComponentModel.Description("One or more indexed relative or absolute source paths, or stable src:/asm: references that identify declaring documents.")] string[] filePaths,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 24576).") ] int maxResponseBytes = 24 * 1024,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        CancellationToken cancellationToken = default)
    {
        if (filePaths.Length == 0 || filePaths.Any(string.IsNullOrWhiteSpace))
            return Task.FromResult(McpToolResults.InvalidArgument("filePaths must contain one or more non-empty paths or stable references.", "$.filePaths",
                "Provide an indexed source path or canonical src:/asm: reference.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        return NavigationToolSupport.RouteAsync(runtime, "get_file_skeleton", targetPath, new { filePaths }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens, async (target, coreCursor, ct) =>
            {
                var referenceRouteErrors = GetFileReferenceRouteErrors(filePaths, target.TargetType);
                if (referenceRouteErrors.All(static error => error is not null))
                    return NavigationToolSupport.Failure(AggregateFileSkeletonFailures(filePaths, referenceRouteErrors),
                        maxResponseBytes, maxResponseTokens, "$.filePaths");
                if (target.TargetType == AnalysisTargetType.Project)
                    return await NavigationToolSupport.WithSourceSolutionAsync(runtime, target,
                        async (solution, source, token) =>
                        {
                            var omissions = new List<string>();
                            var response = await BuildSkeletonsAsync(solution, target.CanonicalPath, filePaths,
                                token, maxResponseBytes, maxResponseTokens,
                                preflightErrors: referenceRouteErrors, omissionReasons: omissions,
                                sourceIdentityRequest: source.IdentityRequest).ConfigureAwait(false);
                            return source.WithMetadata(response,
                                FormatFileSkeletonAnalyzedScope(filePaths, Path.GetDirectoryName(target.CanonicalPath)!), omissions.ToArray());
                        },
                        maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);

                var opened = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
                if (!opened.IsSuccess) return NavigationToolSupport.Failure(opened.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
                await using var scope = opened.Value!;
                var sourceRoot = scope.Context.DecompiledProjectPaths?.DecompiledSourceRoot;
                if (string.IsNullOrWhiteSpace(sourceRoot))
                    return McpToolResults.Recoverable(NavigationErrorCodes.AssemblyTargetUnsupported,
                        "The assembly has no materialized decompiled source tree.",
                        "Use inspect_assembly for metadata-only navigation.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                var identity = AnalysisSymbolIdentity.ForAssembly(scope.Context.Origin.CanonicalPath, scope.Context.Origin.ContentHash,
                    scope.Context.Generation, scope.Context.ReferenceSnapshotHash);
                var omissions = new List<string>();
                var response = await BuildSkeletonsAsync(scope.Solution, target.CanonicalPath, filePaths, ct,
                    maxResponseBytes, maxResponseTokens, sourceRoot, scope, BeforeAssemblySkeletonItemForTesting, omissions,
                    referenceRouteErrors).ConfigureAwait(false);
                return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                    FormatFileSkeletonAnalyzedScope(filePaths, sourceRoot), omissions);
            }, null, cancellationToken);
    }

    private static ResultError?[] GetFileReferenceRouteErrors(string[] filePaths, AnalysisTargetType targetType)
    {
        var errors = new ResultError?[filePaths.Length];
        for (var index = 0; index < filePaths.Length; index++)
        {
            var path = filePaths[index];
            var discoveryProbe = InputNormalizer.NormalizeSymbolIdentifier(path);
            if (!StableSymbolReferenceCodec.TryParseReferenceInput(path, discoveryProbe,
                out var reference, out var referenceError)) continue;
            if (referenceError is { } invalidReference)
            {
                errors[index] = invalidReference;
                continue;
            }

            if (targetType == AnalysisTargetType.Project && reference is not StableSymbolReference.Source
                || targetType == AnalysisTargetType.Assembly && reference is not StableSymbolReference.Assembly)
                errors[index] = new ResultError(NavigationErrorCodes.TargetMismatch,
                    "The stable reference belongs to a different target kind.",
                    "Use the src: reference with its source solution or the asm: reference with its owner targetPath.");
        }
        return errors;
    }

    private static ResultError AggregateFileSkeletonFailures(string[] filePaths, IReadOnlyList<ResultError?> errors)
    {
        var primary = errors.First(static error => error is not null)!.Value;
        var itemResults = filePaths.Select((path, index) => FormatFileSkeletonFailure(path, errors[index]!.Value));
        return new ResultError(primary.Code,
            $"Every requested file skeleton item failed reference validation. Item results:{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, itemResults)}",
            "Use each item's next action, then repeat get_file_skeleton with the same targetPath and corrected reference.");
    }

    private static string FormatFileSkeletonAnalyzedScope(IEnumerable<string> filePaths, string baseDirectory)
    {
        var selectors = filePaths.Select(path =>
        {
            var probe = InputNormalizer.NormalizeSymbolIdentifier(path);
            if (StableSymbolReferenceCodec.TryParseReferenceInput(path, probe, out _, out _)) return path;
            try
            {
                return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                return path;
            }
        });
        return $"fileSkeleton(paths={string.Join('|', selectors)})";
    }

    private static string FormatFileSkeletonFailure(string selector, ResultError error) =>
        $"## {selector}\nResolution status: failed ({error.Code})\nError: {error.Message}\nNext action: {error.Hint ?? "Rediscover the declaration and use the reference returned by that discovery."}";

    private static async Task<CallToolResult> BuildSkeletonsAsync(Solution solution, string targetPath, string[] filePaths,
        CancellationToken ct, int maxResponseBytes, int? maxResponseTokens,
        string? selectedRoot = null, AssemblyNavigationSessionScope? assemblyScope = null, Action<int>? beforeAssemblyItem = null,
        List<string>? omissionReasons = null, IReadOnlyList<ResultError?>? preflightErrors = null,
        SourceIdentityRequest? sourceIdentityRequest = null)
    {
        var targetDirectory = Path.GetFullPath(selectedRoot ?? Path.GetDirectoryName(targetPath)!);
        if (assemblyScope is null && (sourceIdentityRequest is null || !sourceIdentityRequest.IsForSolution(solution)))
            return NavigationToolSupport.Failure(new ResultError(NavigationErrorCodes.WorkspaceDiagnostic,
                "The current source solution has no matching validated identity request.",
                "Repeat the query after the source snapshot has been validated."),
                maxResponseBytes, maxResponseTokens, "$.targetPath");
        var chunks = new List<string>();
        var successfulFiles = 0;
        var hasMissing = false;
        ResultError? firstHandoffError = null;
        for (var index = 0; index < filePaths.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var path = filePaths[index];
            if (preflightErrors?[index] is { } preflightError)
            {
                var routeDiscoveryProbe = InputNormalizer.NormalizeSymbolIdentifier(path);
                if (assemblyScope is not null && StableSymbolReferenceCodec.TryParseReferenceInput(path, routeDiscoveryProbe, out _, out _))
                    beforeAssemblyItem?.Invoke(index);
                chunks.Add(FormatFileSkeletonFailure(path, preflightError));
                firstHandoffError ??= preflightError;
                hasMissing = true;
                continue;
            }
            var discoveryProbe = InputNormalizer.NormalizeSymbolIdentifier(path);
            if (StableSymbolReferenceCodec.TryParseReferenceInput(path, discoveryProbe, out var reference, out var referenceError))
            {
                if (assemblyScope is not null) beforeAssemblyItem?.Invoke(index);
                if (referenceError is { } invalidReference)
                {
                    chunks.Add(FormatFileSkeletonFailure(path, invalidReference));
                    firstHandoffError ??= invalidReference;
                    hasMissing = true;
                    continue;
                }
                var resolved = await ResolveSkeletonReferenceAsync(reference!, solution, targetPath, sourceIdentityRequest, assemblyScope, ct).ConfigureAwait(false);
                if (resolved.Error is { } handoffError)
                {
                    chunks.Add(FormatFileSkeletonFailure(path, handoffError));
                    firstHandoffError ??= handoffError;
                    hasMissing = true;
                    continue;
                }
                var handleDocuments = resolved.Documents!;
                if (handleDocuments.Length == 0)
                {
                    chunks.Add($"## {path}\nThe reference resolves to no declaring documents in the current owner.");
                    hasMissing = true;
                    continue;
                }
                var declarationMarkdown = new List<string>();
                foreach (var declarationDocument in handleDocuments)
                {
                    var handleMarkdown = await FileSkeletonBuilder.BuildMarkdownForDocumentAsync(declarationDocument, targetPath,
                        formatSymbolId: null,
                        formatSymbol: symbol => assemblyScope is not null
                            ? FormatAssemblyReference(symbol, assemblyScope)
                            : sourceIdentityRequest?.FormatHandoff(symbol, solution),
                        ct: ct).ConfigureAwait(false);
                    declarationMarkdown.Add(handleMarkdown);
                }
                chunks.Add(string.Join("\n\n", declarationMarkdown));
                successfulFiles++;
                continue;
            }
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(targetDirectory, path));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                chunks.Add($"## {path}\nThe requested path is invalid: {ex.Message}");
                hasMissing = true;
                continue;
            }
            if (assemblyScope is not null && !IsWithin(targetDirectory, fullPath))
            {
                chunks.Add($"## {path}\nThe requested path is outside the selected target.");
                hasMissing = true;
                continue;
            }
            var indexedDocuments = solution.Projects.SelectMany(project => project.Documents)
                .Where(item => item.FilePath is not null)
                .ToArray();
            var documents = indexedDocuments
                .Where(item => string.Equals(Path.GetFullPath(item.FilePath!), fullPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (documents.Length == 0 && !Path.IsPathRooted(path))
            {
                var normalizedSuffix = path.Replace('\\', '/').TrimStart('/');
                documents = indexedDocuments.Where(item =>
                {
                    var candidate = Path.GetFullPath(item.FilePath!).Replace('\\', '/');
                    return candidate.EndsWith("/" + normalizedSuffix, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Path.GetFileName(candidate), normalizedSuffix, StringComparison.OrdinalIgnoreCase);
                }).ToArray();
            }
            if (documents.Length == 0)
            {
                chunks.Add($"## {path}\nThe file is not part of the loaded source index.");
                hasMissing = true;
                continue;
            }
            if (documents.Length > 1)
            {
                var projects = string.Join(", ", documents.Select(document => document.Project.FilePath ?? document.Project.Name).Distinct(StringComparer.OrdinalIgnoreCase));
                chunks.Add($"## {path}\nThe file is linked into multiple projects and has no unique semantic owner: {projects}");
                hasMissing = true;
                continue;
            }
            var document = documents[0];
            var markdown = await FileSkeletonBuilder.BuildMarkdownForDocumentAsync(document, targetPath,
                formatSymbolId: null, formatSymbol: symbol => assemblyScope is not null
                    ? FormatAssemblyReference(symbol, assemblyScope)
                    : sourceIdentityRequest?.FormatHandoff(symbol, solution), ct: ct).ConfigureAwait(false);
            chunks.Add(markdown);
            successfulFiles++;
        }
        if (successfulFiles == 0)
        {
            if (firstHandoffError is { } error)
                return NavigationToolSupport.Failure(new ResultError(error.Code,
                    $"Every requested file skeleton item failed. Item results:{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, chunks)}",
                    "Use each item's next action, then repeat get_file_skeleton with the same targetPath and corrected selector."),
                    maxResponseBytes, maxResponseTokens, "$.filePaths");
            return McpToolResults.InvalidArgument(
                $"Every requested file skeleton item failed:{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, chunks)}",
                "$.filePaths",
                "Use an indexed source path present in exactly one loaded project, or a stable src:/asm: reference that identifies declaring documents.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        }
        if (hasMissing) omissionReasons?.Add("unresolvedOrMissingFiles");
        var skeletonText = string.Join("\n\n", chunks);
        if (assemblyScope is not null)
            skeletonText = $"Decompiled source root: `{targetDirectory.Replace('\\', '/')}`{Environment.NewLine}{Environment.NewLine}{skeletonText}";
        return NavigationToolSupport.SuccessText(skeletonText, hasMissing,
            hasMissing ? "Correct invalid, missing, out-of-target, or ambiguous paths and repeat the file skeleton query." : null);
    }

    private static IReadOnlyList<string> ReadStringArrayFromResult(CallToolResult response, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(response.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}");
            return document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => item.GetString()!).ToArray()
                : Array.Empty<string>();
        }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    private static bool HasResultCursor(CallToolResult response)
    {
        try
        {
            using var document = JsonDocument.Parse(response.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}");
            return document.RootElement.TryGetProperty("resultCursor", out var value)
                && value.ValueKind == JsonValueKind.String;
        }
        catch (JsonException) { return false; }
    }

    private static async Task<(Document[]? Documents, ResultError? Error)> ResolveSkeletonReferenceAsync(
        StableSymbolReference reference, Solution solution, string targetPath, SourceIdentityRequest? sourceIdentityRequest,
        AssemblyNavigationSessionScope? assemblyScope, CancellationToken ct)
    {
        if (reference is StableSymbolReference.Source sourceReference)
        {
            if (assemblyScope is not null)
                return (null, new ResultError(NavigationErrorCodes.TargetMismatch,
                    "A source reference cannot be resolved in an assembly target.",
                    "Open its source solution target and use the same reference there."));
            if (sourceIdentityRequest is null || !sourceIdentityRequest.IsForSolution(solution))
                return (null, new ResultError(NavigationErrorCodes.WorkspaceDiagnostic,
                    "The current source solution has no matching validated identity request.",
                    "Repeat the query after the source snapshot has been validated."));
            var resolved = await ExactSourceSymbolResolver.ResolveAsync(solution, sourceReference, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess) return (null, resolved.Error);
            var documents = await GetDeclaringDocumentsAsync(solution, resolved.Value!, ct).ConfigureAwait(false);
            return (documents, null);
        }

        if (assemblyScope is null)
            return (null, new ResultError(NavigationErrorCodes.TargetMismatch,
                "An assembly reference requires its owner target.",
                "Open the exact ownerTargetPath returned by discovery and use this reference there."));
        if (!string.Equals(Path.GetFullPath(assemblyScope.Context.Origin.CanonicalPath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            return (null, new ResultError(NavigationErrorCodes.TargetMismatch, "The pinned assembly scope belongs to another target."));
        if (reference is not StableSymbolReference.Assembly assemblyReference)
            return (null, new ResultError(NavigationErrorCodes.TargetMismatch,
                "An assembly reference cannot be resolved in a source target.",
                "Open the returned assembly ownerTargetPath and use that target's reference."));
        var assembly = ExactAssemblySymbolResolver.Resolve(assemblyScope, assemblyReference);
        if (!assembly.IsSuccess) return (null, assembly.Error);
        var assemblyDocuments = await GetDeclaringDocumentsAsync(assemblyScope.Solution, assembly.Value!, ct).ConfigureAwait(false);
        return (assemblyDocuments, null);
    }

    private static async Task<Document[]> GetDeclaringDocumentsAsync(Solution solution, ISymbol symbol, CancellationToken ct)
    {
        var trees = symbol.DeclaringSyntaxReferences.Select(reference => reference.SyntaxTree).ToHashSet();
        var documents = new List<Document>();
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                ct.ThrowIfCancellationRequested();
                var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
                if (tree is not null && trees.Contains(tree)) documents.Add(document);
            }
            foreach (var generated in await project.GetSourceGeneratedDocumentsAsync(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                var tree = await generated.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
                if (tree is not null && trees.Contains(tree)) documents.Add(generated);
            }
        }
        return documents.DistinctBy(document => document.Id).ToArray();
    }

    private static async Task<CallToolResult> ScanNamespaceTreeAsync(Solution solution, string targetPath, string? project, string? prefix, int depth,
        bool includeTypes, string kind, int pageSize, bool includeGenerated, string snapshotBinding,
        string? coreCursor, CancellationToken ct, int bytes, int? tokens, bool includeProjectOverview,
        string requestBinding, Func<INamedTypeSymbol, string?>? formatTypeReference = null)
    {
        if (kind is not ("all" or "class" or "record" or "struct" or "interface" or "enum" or "delegate"))
            return McpToolResults.InvalidArgument("kind is not supported.", "$.kind", "Choose all, class, record, struct, interface, enum, or delegate.", maxResponseBytes: bytes, maxResponseTokens: tokens);
        var payload = await NamespaceTreeScanner.ScanSolutionAsync(solution, project, ct,
            new NamespaceTreeScanOptions(Math.Clamp(depth, 1, 3), NamespaceTreeScanner.MaxResultsCap, includeGenerated, prefix, kind, includeTypes,
                IncludeProjectOverview: includeProjectOverview,
                FormatTypeHandoff: formatTypeReference,
                CollectAllInventory: true, AllowProjectSelectionRecovery: includeProjectOverview)).ConfigureAwait(false);
        if (payload.Error is not null)
            return payload.ErrorCode == NavigationErrorCodes.AmbiguousSymbol
                ? McpToolResults.Recoverable(NavigationErrorCodes.AmbiguousSymbol, payload.Error,
                    "Pass an exact project name or canonical project path.", maxResponseBytes: bytes, maxResponseTokens: tokens)
                : McpToolResults.InvalidArgument(payload.Error, "$.project", "Correct the project or namespace query.", maxResponseBytes: bytes, maxResponseTokens: tokens);
        var items = new List<object>();
        if (payload.Projects is { } projects)
        {
            foreach (var entry in projects)
                items.Add(new { Kind = "project", entry.ProjectName, entry.ProjectType, entry.ProjectPath, entry.NamespaceCount, entry.TypeCount });
        }
        else
        {
            void AddNamespace(NamespaceNode node)
            {
                items.Add(new { Kind = "namespace", node.Name, node.FullName, node.TypeCount });
                foreach (var type in node.Types)
                    items.Add(new { Kind = "type", type.Name, TypeKind = type.Kind, type.FilePath, type.Line, type.HandoffId, Namespace = node.FullName });
                foreach (var child in node.Children) AddNamespace(child);
            }
            foreach (var node in payload.RootNamespaces) AddNamespace(node);
        }
        var binding = BoundResultCursor.CreateBinding(targetPath, snapshotBinding, "browse_target.namespaces", requestBinding,
            project, prefix, depth.ToString(System.Globalization.CultureInfo.InvariantCulture), includeTypes.ToString(), kind, includeGenerated.ToString(),
            pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var page = NavigationToolSupport.PageResults(items, pageSize, coreCursor, binding, bytes, tokens);
        if (page.Error is not null) return page.Error;
        var response = new
        {
            View = "namespaces", payload.SolutionName, payload.ProjectName, payload.TotalNamespaces, payload.TotalTypes,
            TotalsScope = new { Namespaces = payload.Projects is not null ? "loadedCSharpProjects" : string.IsNullOrWhiteSpace(prefix) ? "selectedProject" : "selectedPrefix",
                Types = payload.Projects is not null ? "loadedCSharpProjects" : "selectedProject",
                NamespaceDepth = payload.EffectiveMaxDepth, NamespacePrefix = prefix },
            payload.TotalProjects, payload.RequestedMaxDepth, payload.EffectiveMaxDepth,
            payload.IncludeGenerated, Items = page.Items, TotalItems = items.Count, ReturnedItems = page.Items.Length,
            TruncatedBy = (payload.TruncatedBy ?? Array.Empty<string>()).Where(reason => reason != "maxResults").ToArray(),
            ResultCursor = page.NextCursor,
        };
        var depthLimited = payload.TruncatedBy?.Contains("maxDepth", StringComparer.Ordinal) == true;
        return NavigationToolSupport.Success(response, depthLimited,
            depthLimited ? GetNamespaceTreeNextAction(project, prefix, depth, includeProjectOverview) : null);
    }

    private static string GetNamespaceTreeNextAction(string? project, string? prefix, int depth, bool isSourceTarget)
    {
        var queryRecovery = !string.IsNullOrWhiteSpace(prefix)
            ? depth < 3
                ? $"increase depth to {depth + 1} for the selected namespacePrefix '{prefix.Trim()}'"
                : $"set namespacePrefix to a deeper namespace shown in this result and keep depth at 3"
            : !string.IsNullOrWhiteSpace(project)
                ? $"set namespacePrefix within the selected project and use depth up to 3"
                : isSourceTarget
                    ? "select a project or namespacePrefix, then use depth up to 3"
                    : "set namespacePrefix for the assembly and use depth up to 3";
        return $"Read all outer response pages before using resultCursor, then {queryRecovery}.";
    }

    private static string? FormatAssemblyReference(ISymbol symbol, AssemblyNavigationSessionScope scope)
    {
        var reference = ExactAssemblySymbolResolver.CreateReference(scope, symbol);
        return reference.IsSuccess ? StableSymbolReferenceCodec.Format(reference.Value!) : null;
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
