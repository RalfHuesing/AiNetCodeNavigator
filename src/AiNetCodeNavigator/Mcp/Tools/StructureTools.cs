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
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).")] int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        return NavigationToolSupport.RouteAsync(runtime, "get_index_scope", targetPath, new { }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, ct) => await NavigationToolSupport.WithSourceSolutionAsync(runtime, target, async (solution, source, token) =>
            {
                var result = await IndexScopeScanner.ScanAsync(solution, token).ConfigureAwait(false);
                if (!result.ScanCompleted)
                    return McpToolResults.Recoverable("INDEX_SCOPE_FAILED", result.Error ?? "The source index scope could not be scanned.",
                        "Check the loaded solution and repeat the query.");
                var response = NavigationToolSupport.SuccessText(result.FormattedText, result.IsTruncated, result.NextAction);
                return source.WithMetadata(response,
                    $"indexScope(project=*, maxProjects={result.EffectiveMaxProjects}, maxFileTypes={result.EffectiveMaxFileTypes})");
            }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false),
            AnalysisTargetType.Project, cancellationToken);
    }

    [McpServerTool(Name = "get_file_skeleton", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Show declarations in selected source files and provide navigable handles for their symbols.")]
    public Task<CallToolResult> GetFileSkeleton(
        [Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath,
        [Required, System.ComponentModel.Description("One or more indexed relative or absolute source paths, or current symbol handoffs that identify a declaration file.")] string[] filePaths,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 24576).") ] int maxResponseBytes = 24 * 1024,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        CancellationToken cancellationToken = default)
    {
        if (filePaths.Length == 0 || filePaths.Any(string.IsNullOrWhiteSpace))
            return Task.FromResult(McpToolResults.InvalidArgument("filePaths must contain one or more non-empty paths or symbol handoffs.", "$.filePaths",
                "Provide an indexed source path or a source/assembly symbol handoff.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        return NavigationToolSupport.RouteAsync(runtime, "get_file_skeleton", targetPath, new { filePaths }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens, async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Project)
                    return await NavigationToolSupport.WithSourceSolutionAsync(runtime, target,
                        async (solution, source, token) =>
                        {
                            var omissions = new List<string>();
                            var response = await BuildSkeletonsAsync(solution, target.CanonicalPath, filePaths,
                                source.Identity, token, maxResponseBytes, maxResponseTokens, omissionReasons: omissions).ConfigureAwait(false);
                            return source.WithMetadata(response,
                                $"fileSkeleton(paths={string.Join('|', filePaths.Select(Path.GetFullPath))})", omissions.ToArray());
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
                    var response = await BuildSkeletonsAsync(scope.Solution, target.CanonicalPath, filePaths, identity, ct,
                        maxResponseBytes, maxResponseTokens, sourceRoot, scope, BeforeAssemblySkeletonItemForTesting, omissions).ConfigureAwait(false);
                    return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                        $"fileSkeleton(paths={string.Join('|', filePaths.Select(Path.GetFullPath))})", omissions);
            }, null, cancellationToken);
    }

    [McpServerTool(Name = "get_class_structure", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [System.ComponentModel.Description("Inspect the members and declaration structure of a source or decompiled type.")]
    public Task<CallToolResult> GetClassStructure(
        [Required, System.ComponentModel.Description("Absolute path to an existing source solution or managed assembly target.")] string targetPath,
        [Required, System.ComponentModel.Description("Type name, documentation ID, or current symbol handoff identifying the type.")] string symbolIdentifier,
        [System.ComponentModel.Description("Member ordering: lines (default), kind, or name.")] string sortBy = "lines",
        [Range(1, 200), System.ComponentModel.Description("Maximum members to include in the structure.")] int maxMembers = 50,
        [System.ComponentModel.Description("Optional member-kind filter, such as method, property, field, event, or constructor.")] string? kindFilter = null,
        [System.ComponentModel.Description("Optional substring filter applied to member names.")] string? nameFilter = null,
        [System.ComponentModel.Description("Source scope: all (default), production, or tests.")] string scopeType = "all",
        [System.ComponentModel.Description("Include generated source files; defaults to false.")] bool includeGenerated = false,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16 * 1024,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolIdentifier))
            return Task.FromResult(McpToolResults.InvalidArgument("symbolIdentifier is required.", "$.symbolIdentifier", "Use a type name, declaration ID, or type handoff.",
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
            maxResponseBytes, maxResponseTokens, async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Project)
                    return await NavigationToolSupport.WithSourceSolutionAsync(runtime, target, async (solution, source, token) =>
                    {
                        var result = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(
                            solution, symbolIdentifier, sortBy, maxMembers, kindFilter, nameFilter, source.Identity, parsedScope, includeGenerated), token).ConfigureAwait(false);
                        if (result is null) return McpToolResults.InvalidArgument("The identifier did not resolve to a type.", "$.symbolIdentifier", "Use a type name or type handoff.",
                            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                        if (result.Error is { } error) return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                        var response = NavigationToolSupport.SuccessText(FormatClassStructure(result), result.Truncated,
                            result.Truncated ? "Increase maxMembers up to 200 and repeat the query." : null);
                        return source.WithMetadata(response,
                            $"classStructure(symbol={symbolIdentifier.Trim()}, scope={scopeType}, includeGenerated={includeGenerated}, kind={kindFilter?.Trim() ?? "*"}, name={nameFilter?.Trim() ?? "*"}, sortBy={sortBy.Trim().ToLowerInvariant()}, maxMembers={maxMembers})",
                            result.TruncatedBy.ToArray());
                    }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);

                var normalizedIdentifier = InputNormalizer.NormalizeSymbolIdentifier(symbolIdentifier);
                if (!InputNormalizer.HasOpaqueHandoffPrefix(normalizedIdentifier)
                    && !normalizedIdentifier.StartsWith("i:", StringComparison.OrdinalIgnoreCase))
                {
                    var openedRaw = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
                    if (!openedRaw.IsSuccess) return NavigationToolSupport.Failure(openedRaw.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
                    await using var rawScope = openedRaw.Value!;
                    var raw = await AssemblySymbolInputResolver.ResolveAsync(rawScope, normalizedIdentifier, ct).ConfigureAwait(false);
                    if (!raw.IsSuccess) return NavigationToolSupport.Failure(raw.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    var rawType = raw.Symbol as INamedTypeSymbol ?? raw.Symbol!.ContainingType;
                    if (rawType is null) return McpToolResults.InvalidArgument("The assembly identifier did not resolve to a type.", "$.symbolIdentifier", "Use a type or member declared in a type.",
                        maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    var rawIdentity = AssemblySymbolInputResolver.CreateIdentity(rawScope);
                    var rawStructure = BuildAssemblyClassStructure(rawType, target.CanonicalPath, rawIdentity, sortBy, maxMembers, kindFilter, nameFilter);
                    var rawResponse = NavigationToolSupport.SuccessText(FormatClassStructure(rawStructure), rawStructure.Truncated,
                        rawStructure.Truncated ? "Increase maxMembers up to 200 and repeat the query." : null);
                    return NavigationToolSupport.WithAssemblyMetadata(rawResponse, rawIdentity,
                        $"classStructure(symbol={normalizedIdentifier}, maxMembers={maxMembers}, kind={kindFilter?.Trim() ?? "*"}, name={nameFilter?.Trim() ?? "*"}, sortBy={sortBy.Trim().ToLowerInvariant()})",
                        rawStructure.Truncated ? ["maxMembers"] : []);
                }

                var assembly = await AssemblySymbolHandoffResolver.ResolveAsync(symbolIdentifier, ct).ConfigureAwait(false);
                if (!assembly.IsSuccess) return NavigationToolSupport.Failure(assembly.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                await using var access = assembly.Value!;
                if (!string.Equals(Path.GetFullPath(access.Origin.CanonicalPath), target.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                    return McpToolResults.InvalidArgument("The symbol handoff belongs to another assembly.", "$.symbolIdentifier",
                        "Use a handoff produced by this targetPath.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                var type = access.Symbol as INamedTypeSymbol ?? access.Symbol.ContainingType;
                if (type is null) return McpToolResults.InvalidArgument("The assembly handle did not resolve to a type.", "$.symbolIdentifier", "Use a type handoff from an assembly navigation result.",
                    maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                var identity = AnalysisSymbolIdentity.ForAssembly(access.Origin.CanonicalPath, access.Origin.ContentHash,
                    access.Generation, access.ReferenceSnapshotHash);
                var result = BuildAssemblyClassStructure(type, access.Origin.CanonicalPath, identity, sortBy, maxMembers, kindFilter, nameFilter);
                var response = NavigationToolSupport.SuccessText(FormatClassStructure(result), result.Truncated,
                    result.Truncated ? "Increase maxMembers up to 200 and repeat the query." : null);
                return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                    $"classStructure(symbol={symbolIdentifier.Trim()}, maxMembers={maxMembers}, kind={kindFilter?.Trim() ?? "*"}, name={nameFilter?.Trim() ?? "*"}, sortBy={sortBy.Trim().ToLowerInvariant()})",
                    result.Truncated ? ["maxMembers"] : []);
            }, null, cancellationToken);
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
        [Range(1, 200), System.ComponentModel.Description("Maximum namespaces and types to return.")] int maxResults = 50,
        [System.ComponentModel.Description("Include declarations from generated source files.")] bool includeGenerated = false,
        [System.ComponentModel.Description("Opaque token returned for background work; repeat the same target and query to poll the operation.")] string? operationToken = null,
        [System.ComponentModel.Description("Opaque token returned for the next outer response page; repeat the same target and query to read the stored page.")] string? continuationToken = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), System.ComponentModel.Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).") ] int maxResponseBytes = 16 * 1024,
        [Range(1, int.MaxValue), System.ComponentModel.Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null,
        CancellationToken cancellationToken = default)
    {
        var args = new { project, namespacePrefix, depth, includeTypes, kind, maxResults, includeGenerated };
        return NavigationToolSupport.RouteAsync(runtime, "get_namespace_tree", targetPath, args, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens, async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Project)
                    return await NavigationToolSupport.WithSourceSolutionAsync(runtime, target,
                        async (solution, source, token) =>
                        {
                            var response = await ScanNamespaceTreeAsync(solution, project, namespacePrefix, depth, includeTypes, kind, maxResults,
                                includeGenerated, source.Identity, token, maxResponseBytes, maxResponseTokens, includeProjectOverview: true).ConfigureAwait(false);
                            var omissions = ReadStringArrayFromResult(response, "truncatedBy");
                            return source.WithMetadata(response,
                                $"namespaceTree(project={project?.Trim() ?? "*"}, prefix={namespacePrefix?.Trim() ?? "*"}, depth={depth}, includeTypes={includeTypes}, kind={kind.Trim().ToLowerInvariant()}, maxResults={maxResults}, includeGenerated={includeGenerated})",
                                omissions.ToArray());
                        }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);

                var opened = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
                if (!opened.IsSuccess) return NavigationToolSupport.Failure(opened.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
                await using var scope = opened.Value!;
                var identity = AnalysisSymbolIdentity.ForAssembly(scope.Context.Origin.CanonicalPath, scope.Context.Origin.ContentHash,
                    scope.Context.Generation, scope.Context.ReferenceSnapshotHash);
                var response = await ScanNamespaceTreeAsync(scope.Solution, project, namespacePrefix, depth, includeTypes, kind, maxResults, includeGenerated, identity, ct,
                    maxResponseBytes, maxResponseTokens, includeProjectOverview: false).ConfigureAwait(false);
                var omissions = ReadStringArrayFromResult(response, "truncatedBy");
                return NavigationToolSupport.WithAssemblyMetadata(response, identity,
                    $"namespaceTree(project={project?.Trim() ?? "*"}, prefix={namespacePrefix?.Trim() ?? "*"}, depth={depth}, includeTypes={includeTypes}, kind={kind.Trim().ToLowerInvariant()}, maxResults={maxResults}, includeGenerated={includeGenerated})",
                    omissions);
            }, null, cancellationToken);
    }

    private static async Task<CallToolResult> BuildSkeletonsAsync(Solution solution, string targetPath, string[] filePaths,
        AnalysisSymbolIdentity? identity, CancellationToken ct, int maxResponseBytes, int? maxResponseTokens,
        string? selectedRoot = null, AssemblyNavigationSessionScope? assemblyScope = null, Action<int>? beforeAssemblyItem = null,
        List<string>? omissionReasons = null)
    {
        var targetDirectory = Path.GetFullPath(selectedRoot ?? Path.GetDirectoryName(targetPath)!);
        var chunks = new List<string>();
        var successfulFiles = 0;
        var hasMissing = false;
        ResultError? firstHandoffError = null;
        for (var index = 0; index < filePaths.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var path = filePaths[index];
            if (path.StartsWith("h:", StringComparison.Ordinal))
            {
                if (assemblyScope is not null) beforeAssemblyItem?.Invoke(index);
                var resolved = await ResolveSkeletonHandoffAsync(path, solution, targetPath, identity, assemblyScope, ct).ConfigureAwait(false);
                if (resolved.Error is { } handoffError)
                {
                    chunks.Add($"## {path}\n{handoffError.Code}: {handoffError.Message}");
                    firstHandoffError ??= handoffError;
                    hasMissing = true;
                    continue;
                }
                var handleDocuments = resolved.Documents!;
                if (handleDocuments.Length != 1)
                {
                    chunks.Add($"## {path}\nThe handoff resolves to {handleDocuments.Length} declaration files and cannot select one file skeleton.");
                    hasMissing = true;
                    continue;
                }
                var handleMarkdown = await FileSkeletonBuilder.BuildMarkdownForDocumentAsync(handleDocuments[0], targetPath,
                    formatSymbolId: null, formatSymbol: symbol =>
                    {
                        var internalId = identity?.FormatHandoff(symbol, solution);
                        return internalId is null ? null : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalId);
                    }, ct: ct).ConfigureAwait(false);
                chunks.Add(handleMarkdown);
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
            if (identity?.IsAssembly == true && !IsWithin(targetDirectory, fullPath))
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
                formatSymbolId: null, formatSymbol: symbol =>
                {
                    var internalId = identity?.FormatHandoff(symbol, solution);
                    return internalId is null ? null : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalId);
                }, ct: ct).ConfigureAwait(false);
            chunks.Add(markdown);
            successfulFiles++;
        }
        if (successfulFiles == 0)
        {
            if (firstHandoffError is { } error)
                return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, "$.filePaths");
            return McpToolResults.InvalidArgument(chunks.FirstOrDefault() ?? "No source file could be selected.", "$.filePaths",
                "Use an indexed source path present in exactly one loaded project, or a current handoff that identifies one declaration file.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        }
        if (hasMissing) omissionReasons?.Add("unresolvedOrMissingFiles");
        return NavigationToolSupport.SuccessText(string.Join("\n\n", chunks), hasMissing,
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

    private static async Task<(Document[]? Documents, ResultError? Error)> ResolveSkeletonHandoffAsync(
        string handoff, Solution solution, string targetPath, AnalysisSymbolIdentity? identity,
        AssemblyNavigationSessionScope? assemblyScope, CancellationToken ct)
    {
        if (identity is null)
            return (null, new ResultError(NavigationErrorCodes.InvalidHandoff, "A canonical target identity could not be created."));
        if (!identity.IsAssembly)
        {
            var resolved = await SourceHandoffResolver.ResolveAsync(solution, handoff, identity, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess) return (null, resolved.Error);
            var documents = resolved.Value?.DeclaringSyntaxReferences
                .Select(reference => solution.GetDocument(reference.SyntaxTree))
                .Where(document => document is not null)
                .Cast<Document>()
                .DistinctBy(document => document.Id)
                .ToArray() ?? [];
            return (documents, null);
        }

        if (assemblyScope is null)
            return (null, new ResultError(NavigationErrorCodes.InvalidHandoff, "The assembly handoff has no pinned target scope."));
        if (!string.Equals(Path.GetFullPath(assemblyScope.Context.Origin.CanonicalPath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            return (null, new ResultError(NavigationErrorCodes.TargetMismatch, "The pinned assembly scope belongs to another target."));
        var assembly = AssemblySymbolHandoffResolver.ResolveWithinScope(handoff, assemblyScope);
        if (!assembly.IsSuccess) return (null, assembly.Error);

        var rootPath = assemblyScope.Context.DecompiledProjectPaths?.DecompiledSourceRoot;
        if (string.IsNullOrWhiteSpace(rootPath))
            return (null, new ResultError(NavigationErrorCodes.AssemblyTargetUnsupported, "The assembly has no materialized decompiled source tree."));
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var assemblyDocuments = assembly.Value!.DeclaringSyntaxReferences
            .Select(reference => assemblyScope.Solution.GetDocument(reference.SyntaxTree))
            .Where(document => document?.FilePath is { } filePath
                && (Path.GetFullPath(filePath).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFullPath(filePath), root, StringComparison.OrdinalIgnoreCase)))
            .Cast<Document>()
            .DistinctBy(document => document.Id)
            .ToArray();
        return (assemblyDocuments, null);
    }

    private static async Task<CallToolResult> ScanNamespaceTreeAsync(Solution solution, string? project, string? prefix, int depth,
        bool includeTypes, string kind, int maxResults, bool includeGenerated, AnalysisSymbolIdentity? identity, CancellationToken ct, int bytes, int? tokens,
        bool includeProjectOverview)
    {
        if (kind is not ("all" or "class" or "record" or "struct" or "interface" or "enum" or "delegate"))
            return McpToolResults.InvalidArgument("kind is not supported.", "$.kind", "Choose all, class, record, struct, interface, enum, or delegate.", maxResponseBytes: bytes, maxResponseTokens: tokens);
        var payload = await NamespaceTreeScanner.ScanSolutionAsync(solution, project, ct,
            new NamespaceTreeScanOptions(Math.Clamp(depth, 1, 3), maxResults, includeGenerated, prefix, kind, includeTypes,
                IncludeProjectOverview: includeProjectOverview,
                FormatTypeHandoff: identity is null ? null : symbol =>
                {
                    var internalId = identity.FormatHandoff(symbol, solution);
                    return internalId is null ? null : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalId);
                })).ConfigureAwait(false);
        if (payload.Error is not null)
            return payload.ErrorCode == NavigationErrorCodes.AmbiguousSymbol
                ? McpToolResults.Recoverable(NavigationErrorCodes.AmbiguousSymbol, payload.Error,
                    "Pass an exact project name or canonical project path.", maxResponseBytes: bytes, maxResponseTokens: tokens)
                : McpToolResults.InvalidArgument(payload.Error, "$.project", "Correct the project or namespace query.", maxResponseBytes: bytes, maxResponseTokens: tokens);
        return NavigationToolSupport.SuccessText(payload.FormattedText, payload.Truncated, payload.NextAction);
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

    internal static ClassStructurePayload BuildAssemblyClassStructure(INamedTypeSymbol type, string assemblyPath, AnalysisSymbolIdentity identity,
        string sortBy, int maxMembers, string? kindFilter, string? nameFilter)
    {
        var directory = Path.GetDirectoryName(assemblyPath)!;
        var members = type.GetMembers().Where(member => !member.IsImplicitlyDeclared)
            .Select(member =>
            {
                var location = member.Locations.FirstOrDefault(item => item.IsInSource);
                var span = location?.GetLineSpan();
                var internalId = identity.FormatHandoff(member);
                var handoff = internalId is null ? null : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalId);
                var relative = span?.Path is { } file ? Path.GetRelativePath(directory, file) : string.Empty;
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
        var shown = members.Take(Math.Clamp(maxMembers, 1, 200)).ToArray();
        var typeKind = type.IsRecord ? type.TypeKind == TypeKind.Struct ? "Record Struct" : "Record Class" : type.TypeKind.ToString();
        return new ClassStructurePayload(type.ToDisplayString(), typeKind, [], shown.Sum(item => item.LineCount), members.Count, shown.Length,
            members.Count > shown.Length, shown, members.Count > shown.Length ? ["maxMembers"] : []);
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
