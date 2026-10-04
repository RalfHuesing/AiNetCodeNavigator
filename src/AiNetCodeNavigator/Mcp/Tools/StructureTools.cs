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

    [McpServerTool(Name = "get_index_scope", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Summarize which source projects and documents are included in the loaded solution index.")]
    public Task<CallToolResult> GetIndexScope(
        [Required, System.ComponentModel.Description("Absolute path to a source .sln or .slnx solution.")] string targetPath,
        [Range(1, IndexScopeScanner.MaxFileTypesCap), System.ComponentModel.Description("Page size for the combined project and file-type inventory (maximum 128 entries per page). All discovered entries remain reachable across resultCursor pages.")] int maxResults = 100,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).")] int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [System.ComponentModel.Description("Opaque cursor for the next page of the complete loaded-solution inventory.")] string? resultCursor = null,
        CancellationToken cancellationToken = default)
    {
        return NavigationToolSupport.RouteAsync(runtime, "get_index_scope", targetPath, new { maxResults }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, coreCursor, ct) => await NavigationToolSupport.WithSourceSolutionAsync(runtime, target, async (solution, source, token) =>
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
                        entry.DocumentCount, entry.CSharpDocumentCount, entry.IsTestProject, entry.IsCSharpProject,
                        LoadedFrameworkContext = entry.LoadedFrameworkContext ?? "unknown",
                        Exclusions = entry.Exclusions ?? Array.Empty<string>(),
                        entry.ConfiguredFrameworksKnown,
                        ConfiguredFrameworksNotAnalyzed = entry.ConfiguredFrameworksNotAnalyzed ?? Array.Empty<string>() });
                foreach (var entry in result.FileTypes)
                    items.Add(new { Kind = "fileType", entry.Extension, entry.Count, entry.SymbolGraphCovered });
                var indexScopeSnapshotHash = source.CreateIndexScopeSnapshotHash();
                var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, indexScopeSnapshotHash,
                    "get_index_scope.inventory", "allProjectsAndFileTypes",
                    maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var page = NavigationToolSupport.PageResults(items, maxResults, coreCursor, binding,
                    maxResponseBytes, maxResponseTokens);
                if (page.Error is not null) return page.Error;
                var response = NavigationToolSupport.Success(new
                {
                    result.SolutionPath, result.ProjectCount, result.TotalDocumentCount, result.CSharpFileCount,
                    result.TestProjectCount, result.GeneratedDocumentCount, result.TestDocumentCount, result.TotalFileTypeCount,
                    Summary = $"Roslyn documents: {result.TotalDocumentCount}; .cs: {result.CSharpFileCount}; generated C#: {result.GeneratedDocumentCount}; tests: {result.TestDocumentCount}.",
                    Items = page.Items, TotalItems = items.Count, ReturnedItems = page.Items.Length, ResultCursor = page.NextCursor,
                });
                return source.WithMetadata(response,
                    $"indexScope(project=*, pageSize={maxResults}, loadedProjects={result.ProjectCount}, loadedFileTypes={result.TotalFileTypeCount})",
                    resultContinuationAvailable: page.NextCursor is not null,
                    snapshotContentHash: indexScopeSnapshotHash);
            }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false),
            AnalysisTargetType.Project, cancellationToken, resultCursor, "get_index_scope.inventory");
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

    [McpServerTool(Name = "get_class_structure", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Inspect the members and declaration structure of a source or decompiled type.")]
    public Task<CallToolResult> GetClassStructure(
        [Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath,
        [Required, System.ComponentModel.Description("Type name, documentation ID, or stable src:/asm: reference identifying the type.")] string symbolIdentifier,
        [System.ComponentModel.Description("Member ordering: lines (default), kind, or name.")] string sortBy = "lines",
        [Range(1, 200), System.ComponentModel.Description("Page size for type members (maximum 200 entries per page). All filtered members remain reachable across resultCursor pages.")] int maxMembers = 50,
        [System.ComponentModel.Description("Optional member-kind filter, such as method, property, field, event, or constructor.")] string? kindFilter = null,
        [System.ComponentModel.Description("Optional substring filter applied to member names.")] string? nameFilter = null,
        [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all",
        [System.ComponentModel.Description("Include generated source files; defaults to false.")] bool includeGenerated = false,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16 * 1024,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque cursor for the next page of the filtered type member list.")] string? resultCursor = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolIdentifier))
            return Task.FromResult(McpToolResults.InvalidArgument("symbolIdentifier is required.", "$.symbolIdentifier", "Use a type name, declaration ID, or stable src:/asm: reference.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        if (scopeType is not ("all" or "production" or "tests"))
            return Task.FromResult(McpToolResults.InvalidArgument("scopeType must be all, production, or tests.", "$.scopeType", "Choose a supported source scope.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        var parsedScope = scopeType switch
        {
            "production" => SymbolScopeType.Production,
            "tests" => SymbolScopeType.Tests,
            _ => SymbolScopeType.All,
        };
        var args = new { symbolIdentifier, sortBy, maxMembers, kindFilter, nameFilter, scopeType, includeGenerated };
        return NavigationToolSupport.RouteAsync(runtime, "get_class_structure", targetPath, args, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens, async (target, coreCursor, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Project)
                {
                    var sourceProbe = InputNormalizer.NormalizeSymbolIdentifier(symbolIdentifier);
                    if (StableSymbolReferenceCodec.TryParseReferenceInput(symbolIdentifier, sourceProbe,
                        out var sourceReference, out var sourceReferenceError))
                    {
                        if (sourceReferenceError is not null)
                            return NavigationToolSupport.Failure(sourceReferenceError.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                        if (sourceReference is not StableSymbolReference.Source)
                            return NavigationToolSupport.Failure(new ResultError(NavigationErrorCodes.TargetMismatch,
                                "An assembly reference cannot be resolved in a source solution.",
                                "Open the assembly owner targetPath and use the asm: reference."), maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    }
                    return await NavigationToolSupport.WithSourceSolutionAsync(runtime, target, async (solution, source, token) =>
                    {
                        var result = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(
                            solution, symbolIdentifier, sortBy, maxMembers, kindFilter, nameFilter, source.Identity, parsedScope, includeGenerated,
                            CollectAllMembers: true)
                        {
                            CurrentIdentityRequest = source.IdentityRequest,
                        }, token).ConfigureAwait(false);
                        if (result is null) return McpToolResults.InvalidArgument("The identifier did not resolve to a type.", "$.symbolIdentifier", "Use a type name or stable src: reference.",
                            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                        if (result.Error is { } error) return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                        var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, source.Identity.ContentHash,
                            "get_class_structure.members", symbolIdentifier.Trim(), scopeType, includeGenerated.ToString(), kindFilter?.Trim(), nameFilter?.Trim(), sortBy.Trim().ToLowerInvariant(),
                            maxMembers.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        var response = CreateClassStructurePage(result, target.CanonicalPath, maxMembers, coreCursor, binding,
                            maxResponseBytes, maxResponseTokens);
                        if (response.IsError == true) return response;
                        return source.WithMetadata(response,
                            $"classStructure(symbol={symbolIdentifier.Trim()}, scope={scopeType}, includeGenerated={includeGenerated}, kind={kindFilter?.Trim() ?? "*"}, name={nameFilter?.Trim() ?? "*"}, sortBy={sortBy.Trim().ToLowerInvariant()}, pageSize={maxMembers})",
                            [], HasResultCursor(response));
                    }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
                }

                var normalizedAssemblyIdentifier = InputNormalizer.NormalizeSymbolIdentifier(symbolIdentifier);
                if (AssemblySymbolInputResolver.TryRouteIdentifier(symbolIdentifier, normalizedAssemblyIdentifier,
                    out var assemblyReference, out var assemblyReferenceError))
                {
                    if (assemblyReferenceError is not null)
                        return NavigationToolSupport.Failure(assemblyReferenceError.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    if (assemblyReference is not StableSymbolReference.Assembly)
                        return NavigationToolSupport.Failure(new ResultError(NavigationErrorCodes.TargetMismatch,
                            "A source reference cannot be resolved in an assembly target.",
                            "Open the source solution target and use the src: reference."), maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                }
                var opened = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
                if (!opened.IsSuccess) return NavigationToolSupport.Failure(opened.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
                await using var assemblyScope = opened.Value!;
                var assembly = await AssemblySymbolInputResolver.ResolveAsync(assemblyScope, symbolIdentifier, ct).ConfigureAwait(false);
                if (!assembly.IsSuccess) return NavigationToolSupport.Failure(assembly.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                var type = assembly.Symbol as INamedTypeSymbol ?? assembly.Symbol!.ContainingType;
                if (type is null) return McpToolResults.InvalidArgument("The assembly identifier did not resolve to a type.", "$.symbolIdentifier", "Use a type or member declared in a type.",
                    maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                var identity = AssemblySymbolInputResolver.CreateIdentity(assemblyScope);
                var decompiledSourceRoot = assemblyScope.Context.DecompiledProjectPaths?.DecompiledSourceRoot;
                var result = BuildAssemblyClassStructure(type, target.CanonicalPath, identity, sortBy, maxMembers, kindFilter, nameFilter,
                    decompiledSourceRoot,
                    collectAll: true, formatReference: symbol => FormatAssemblyReference(symbol, assemblyScope));
                var binding = BoundResultCursor.CreateBinding(target.CanonicalPath, identity.ContentHash + "|" + assemblyScope.Context.ReferenceSnapshotHash,
                    "get_class_structure.members", symbolIdentifier.Trim(), scopeType, includeGenerated.ToString(), kindFilter?.Trim(), nameFilter?.Trim(), sortBy.Trim().ToLowerInvariant(),
                    maxMembers.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var response = CreateClassStructurePage(result, target.CanonicalPath, maxMembers, coreCursor, binding,
                    maxResponseBytes, maxResponseTokens, decompiledSourceRoot);
                if (response.IsError == true) return response;
                return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                    $"classStructure(symbol={symbolIdentifier.Trim()}, pageSize={maxMembers}, kind={kindFilter?.Trim() ?? "*"}, name={nameFilter?.Trim() ?? "*"}, sortBy={sortBy.Trim().ToLowerInvariant()})",
                    [], HasResultCursor(response));
            }, null, cancellationToken, resultCursor, "get_class_structure.members");
    }

    [McpServerTool(Name = "get_namespace_tree", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Browse namespaces and their types in a source project or owned assembly target.")]
    public Task<CallToolResult> GetNamespaceTree(
        [Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath,
        [System.ComponentModel.Description("Optional exact project name or canonical project path used to disambiguate duplicate names.")] string? project = null,
        [System.ComponentModel.Description("Optional namespace prefix to select a project or namespace subtree.")] string? namespacePrefix = null,
        [System.ComponentModel.Description("Namespace depth from 1 through 3; defaults to 1.")] [Range(1, 3)] int depth = 1,
        [System.ComponentModel.Description("Include type declarations beneath each namespace.")] bool includeTypes = true,
        [System.ComponentModel.Description("Type kind: all (default), class, interface, record, struct, or enum.")] string kind = "all",
        [Range(1, NamespaceTreeScanner.MaxResultsCap), System.ComponentModel.Description("Page size for the combined project, namespace, and type inventory (maximum 200 entries per page). All discovered entries remain reachable across resultCursor pages.")] int maxResults = 50,
        [System.ComponentModel.Description("Include declarations from generated source files.")] bool includeGenerated = false,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16 * 1024,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque cursor for the next page of the complete filtered inventory.")] string? resultCursor = null,
        CancellationToken cancellationToken = default)
    {
        var args = new { project, namespacePrefix, depth, includeTypes, kind, maxResults, includeGenerated };
        return NavigationToolSupport.RouteAsync(runtime, "get_namespace_tree", targetPath, args, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens, async (target, coreCursor, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Project)
                    return await NavigationToolSupport.WithSourceSolutionAsync(runtime, target,
                        async (solution, source, token) =>
                        {
                            var response = await ScanNamespaceTreeAsync(solution, target.CanonicalPath, project, namespacePrefix, depth, includeTypes, kind, maxResults,
                                includeGenerated, source.Identity.ContentHash, coreCursor, token, maxResponseBytes, maxResponseTokens,
                                includeProjectOverview: true,
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
                    maxResponseBytes, maxResponseTokens, includeProjectOverview: false,
                    formatTypeReference: type => FormatAssemblyReference(type, scope)).ConfigureAwait(false);
                var omissions = ReadStringArrayFromResult(response, "truncatedBy");
                return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                    $"namespaceTree(project={project?.Trim() ?? "*"}, prefix={namespacePrefix?.Trim() ?? "*"}, depth={depth}, includeTypes={includeTypes}, kind={kind.Trim().ToLowerInvariant()}, pageSize={maxResults}, includeGenerated={includeGenerated})",
                    omissions.Where(reason => reason != "maxResults").ToArray(), HasResultCursor(response));
            }, null, cancellationToken, resultCursor, "get_namespace_tree.inventory");
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
        Func<INamedTypeSymbol, string?>? formatTypeReference = null)
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
        var binding = BoundResultCursor.CreateBinding(targetPath, snapshotBinding, "get_namespace_tree.inventory",
            project, prefix, depth.ToString(System.Globalization.CultureInfo.InvariantCulture), includeTypes.ToString(), kind, includeGenerated.ToString(),
            pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var page = NavigationToolSupport.PageResults(items, pageSize, coreCursor, binding, bytes, tokens);
        if (page.Error is not null) return page.Error;
        var response = new
        {
            payload.SolutionName, payload.ProjectName, payload.TotalNamespaces, payload.TotalTypes,
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

    internal static string FormatClassStructure(ClassStructurePayload result)
    {
        var output = new StringBuilder();
        output.AppendLine($"# {result.Kind} {result.TypeName}");
        output.AppendLine($"> {result.ShownMemberCount}/{result.TotalMemberCount} members; {result.TotalLines} source lines");
        foreach (var file in result.Files) output.AppendLine($"> {file}");
        foreach (var member in result.Members)
        {
            var handoff = member.HandoffId is null ? string.Empty : $" [handoff: {member.HandoffId}]";
            output.AppendLine($"- {member.Kind} {member.Visibility} {member.Signature} ({member.FilePath}:{member.StartLine}-{member.EndLine}){handoff}");
        }
        return output.ToString().TrimEnd();
    }

    private static CallToolResult CreateClassStructurePage(ClassStructurePayload result, string targetPath, int pageSize,
        string? cursor, string binding, int maxResponseBytes, int? maxResponseTokens, string? decompiledSourceRoot = null)
    {
        var page = NavigationToolSupport.PageResults(result.Members, pageSize, cursor, binding,
            maxResponseBytes, maxResponseTokens);
        if (page.Error is not null) return page.Error;
        return NavigationToolSupport.Success(new
        {
            result.TypeName,
            result.Kind,
            TargetPath = targetPath,
            DecompiledSourceRoot = decompiledSourceRoot,
            result.Files,
            result.TotalLines,
            result.TotalMemberCount,
            ReturnedMemberCount = page.Items.Length,
            Members = page.Items,
            ResultCursor = page.NextCursor,
        });
    }

    internal static ClassStructurePayload BuildAssemblyClassStructure(INamedTypeSymbol type, string assemblyPath, AnalysisSymbolIdentity identity,
        string sortBy, int maxMembers, string? kindFilter, string? nameFilter, string? decompiledSourceRoot, bool collectAll = false,
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
        var shown = members.Take(collectAll ? int.MaxValue : Math.Clamp(maxMembers, 1, 200)).ToArray();
        var typeKind = type.IsRecord ? type.TypeKind == TypeKind.Struct ? "Record Struct" : "Record Class" : type.TypeKind.ToString();
        var files = type.Locations.Where(location => location.IsInSource && location.SourceTree?.FilePath is not null)
            .Select(location => FormatAssemblySourcePath(sourceRoot, location.SourceTree!.FilePath))
            .Concat(members.Select(member => member.FilePath))
            .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(filePath => filePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ClassStructurePayload(type.ToDisplayString(), typeKind, files, shown.Sum(item => item.LineCount), members.Count, shown.Length,
            members.Count > shown.Length, shown, members.Count > shown.Length ? ["maxMembers"] : []);
    }

    private static string FormatAssemblySourcePath(string? decompiledSourceRoot, string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (string.IsNullOrWhiteSpace(decompiledSourceRoot)) return fullPath.Replace('\\', '/');
        var fullRoot = Path.GetFullPath(decompiledSourceRoot);
        if (!IsWithin(fullRoot, fullPath)) return fullPath.Replace('\\', '/');
        return Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/');
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
