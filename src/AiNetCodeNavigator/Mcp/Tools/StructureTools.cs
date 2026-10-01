using System.ComponentModel.DataAnnotations;
using System.Text;
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
    [McpServerTool(Name = "get_index_scope", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public Task<CallToolResult> GetIndexScope([Required] string targetPath, CancellationToken cancellationToken = default)
    {
        return NavigationToolSupport.RouteAsync(runtime, "get_index_scope", targetPath, new { targetPath }, null, null,
            McpResponseBudgetLimits.DefaultBytes, null,
            async (target, ct) => await NavigationToolSupport.WithSourceSolutionAsync(runtime, target, async (solution, token) =>
            {
                var result = await IndexScopeScanner.ScanAsync(solution, token).ConfigureAwait(false);
                if (!result.ScanCompleted)
                    return McpToolResults.Recoverable("INDEX_SCOPE_FAILED", result.Error ?? "The source index scope could not be scanned.",
                        "Check the loaded solution and repeat the query.");
                return NavigationToolSupport.SuccessText(result.FormattedText, result.IsTruncated, result.NextAction);
            }, McpResponseBudgetLimits.DefaultBytes, null, ct).ConfigureAwait(false),
            AnalysisTargetType.Project, cancellationToken);
    }

    [McpServerTool(Name = "get_file_tree", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public Task<CallToolResult> GetFileTree(
        [Required] string targetPath,
        string? root = null,
        string view = "tree",
        string[]? includeExtensions = null,
        string? fileFilter = null,
        string[]? excludePatterns = null,
        [Range(0, 32)] int? maxDepth = null,
        [Range(0, 32)] int? treeDepth = null,
        [Range(1, 2000)] int maxResults = 20,
        string sortBy = "path",
        bool includeMetadata = true,
        bool includeLineCount = false,
        string? operationToken = null,
        string? continuationToken = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes)] int maxResponseBytes = 8 * 1024,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new { root, view, includeExtensions, fileFilter, excludePatterns, maxDepth, treeDepth, maxResults, sortBy, includeMetadata, includeLineCount };
        return NavigationToolSupport.RouteAsync(runtime, "get_file_tree", targetPath, arguments, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens, (target, ct) =>
            {
                var rootDirectory = Path.GetDirectoryName(target.CanonicalPath)!;
                var result = GetFileTreeScanner.Scan(new FileTreeScanRequest(
                    rootDirectory,
                    root ?? ".",
                    view,
                    fileFilter,
                    MaxDepth: maxDepth,
                    MaxResults: maxResults,
                    IncludeExtensions: includeExtensions,
                    ExcludePatterns: excludePatterns,
                    TreeDepth: treeDepth,
                    SortBy: sortBy,
                    IncludeMetadata: includeMetadata,
                    IncludeLineCount: includeLineCount), ct);
                if (!result.ScanCompleted || result.Error is not null)
                    return Task.FromResult(McpToolResults.InvalidArgument(result.Error ?? "The requested file tree could not be scanned.", "$.root",
                        "Use a relative root within the selected target directory and supported view/filter values.",
                        maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
                return Task.FromResult(NavigationToolSupport.SuccessText(result.FormattedText, result.IsTruncated,
                    result.Next?.Action ?? "Increase maxResults or depth and repeat the query."));
            }, null, cancellationToken);
    }

    [McpServerTool(Name = "get_file_skeleton", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public Task<CallToolResult> GetFileSkeleton(
        [Required] string targetPath,
        [Required] string[] filePaths,
        string? operationToken = null,
        string? continuationToken = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes)] int maxResponseBytes = 24 * 1024,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        CancellationToken cancellationToken = default)
    {
        if (filePaths.Length == 0 || filePaths.Any(string.IsNullOrWhiteSpace))
            return Task.FromResult(McpToolResults.InvalidArgument("filePaths must contain one or more non-empty relative paths.", "$.filePaths",
                "Provide workspace-relative source paths.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        return NavigationToolSupport.RouteAsync(runtime, "get_file_skeleton", targetPath, new { filePaths }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens, async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Project)
                    return await NavigationToolSupport.WithSourceSolutionAsync(runtime, target,
                        async (solution, token) => await BuildSkeletonsAsync(solution, target.CanonicalPath, filePaths,
                            await AnalysisSymbolIdentity.ForSourceAsync(solution, token).ConfigureAwait(false), token).ConfigureAwait(false),
                        maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);

                var opened = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
                if (!opened.IsSuccess) return NavigationToolSupport.Failure(opened.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
                await using var scope = opened.Value!;
                var identity = AnalysisSymbolIdentity.ForAssembly(scope.Context.Origin.CanonicalPath, scope.Context.Origin.ContentHash,
                    scope.Context.Generation, scope.Context.ReferenceSnapshotHash);
                return await BuildSkeletonsAsync(scope.Solution, target.CanonicalPath, filePaths, identity, ct).ConfigureAwait(false);
            }, null, cancellationToken);
    }

    [McpServerTool(Name = "get_class_structure", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public Task<CallToolResult> GetClassStructure(
        [Required] string targetPath,
        [Required] string symbolIdentifier,
        string sortBy = "lines",
        [Range(1, 200)] int maxMembers = 50,
        string? kindFilter = null,
        string? nameFilter = null,
        string scopeType = "all",
        bool includeGenerated = false,
        string? operationToken = null,
        string? continuationToken = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes)] int maxResponseBytes = 16 * 1024,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolIdentifier))
            return Task.FromResult(McpToolResults.InvalidArgument("symbolIdentifier is required.", "$.symbolIdentifier", "Use a type name, declaration ID, or type handoff.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        if (scopeType is not ("all" or "production" or "tests"))
            return Task.FromResult(McpToolResults.InvalidArgument("scopeType must be all, production, or tests.", "$.scopeType", "Choose a supported source scope.",
                maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens));
        var args = new { symbolIdentifier, sortBy, maxMembers, kindFilter, nameFilter, scopeType, includeGenerated };
        return NavigationToolSupport.RouteAsync(runtime, "get_class_structure", targetPath, args, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens, async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Project)
                    return await NavigationToolSupport.WithSourceSolutionAsync(runtime, target, async (solution, token) =>
                    {
                        var identity = await AnalysisSymbolIdentity.ForSourceAsync(solution, token).ConfigureAwait(false);
                        var result = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(
                            solution, symbolIdentifier, sortBy, maxMembers, kindFilter, nameFilter, identity), token).ConfigureAwait(false);
                        if (result is null) return McpToolResults.InvalidArgument("The identifier did not resolve to a type.", "$.symbolIdentifier", "Use a type name or type handoff.",
                            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                        if (result.Error is { } error) return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                        return NavigationToolSupport.SuccessText(FormatClassStructure(result), result.Truncated,
                            result.Truncated ? "Increase maxMembers up to 200 and repeat the query." : null);
                    }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);

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
                return NavigationToolSupport.SuccessText(FormatClassStructure(result), result.Truncated,
                    result.Truncated ? "Increase maxMembers up to 200 and repeat the query." : null);
            }, null, cancellationToken);
    }

    [McpServerTool(Name = "get_namespace_tree", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public Task<CallToolResult> GetNamespaceTree(
        [Required] string targetPath,
        string? project = null,
        string? namespacePrefix = null,
        [Range(1, 3)] int depth = 1,
        bool includeTypes = true,
        string kind = "all",
        [Range(1, 200)] int maxResults = 50,
        bool includeGenerated = false,
        string? operationToken = null,
        string? continuationToken = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes)] int maxResponseBytes = 16 * 1024,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        CancellationToken cancellationToken = default)
    {
        var args = new { project, namespacePrefix, depth, includeTypes, kind, maxResults, includeGenerated };
        return NavigationToolSupport.RouteAsync(runtime, "get_namespace_tree", targetPath, args, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens, async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Project)
                    return await NavigationToolSupport.WithSourceSolutionAsync(runtime, target,
                        async (solution, token) =>
                        {
                            var identity = await AnalysisSymbolIdentity.ForSourceAsync(solution, token).ConfigureAwait(false);
                            return await ScanNamespaceTreeAsync(solution, project, namespacePrefix, depth, includeTypes, kind, maxResults,
                                includeGenerated, identity, token, maxResponseBytes, maxResponseTokens, includeProjectOverview: true).ConfigureAwait(false);
                        }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);

                var opened = await AssemblyNavigationSessionScope.OpenAsync(target.CanonicalPath, ct).ConfigureAwait(false);
                if (!opened.IsSuccess) return NavigationToolSupport.Failure(opened.Error!.Value, maxResponseBytes, maxResponseTokens, "$.targetPath");
                await using var scope = opened.Value!;
                var identity = AnalysisSymbolIdentity.ForAssembly(scope.Context.Origin.CanonicalPath, scope.Context.Origin.ContentHash,
                    scope.Context.Generation, scope.Context.ReferenceSnapshotHash);
                return await ScanNamespaceTreeAsync(scope.Solution, project, namespacePrefix, depth, includeTypes, kind, maxResults, includeGenerated, identity, ct,
                    maxResponseBytes, maxResponseTokens, includeProjectOverview: false).ConfigureAwait(false);
            }, null, cancellationToken);
    }

    private static async Task<CallToolResult> BuildSkeletonsAsync(Solution solution, string targetPath, string[] filePaths, AnalysisSymbolIdentity? identity, CancellationToken ct)
    {
        var targetDirectory = Path.GetDirectoryName(targetPath)!;
        var chunks = new List<string>();
        foreach (var path in filePaths)
        {
            ct.ThrowIfCancellationRequested();
            if (Path.IsPathRooted(path))
            {
                chunks.Add($"## {path}\nAbsolute paths are not accepted; use a path relative to the target.");
                continue;
            }
            var fullPath = Path.GetFullPath(Path.Combine(targetDirectory, path));
            if (!IsWithin(targetDirectory, fullPath))
            {
                chunks.Add($"## {path}\nThe requested path is outside the selected target.");
                continue;
            }
            var document = solution.Projects.SelectMany(project => project.Documents)
                .FirstOrDefault(item => item.FilePath is { } documentPath && string.Equals(Path.GetFullPath(documentPath), fullPath, StringComparison.OrdinalIgnoreCase));
            if (document is null)
            {
                chunks.Add($"## {path}\nThe file is not part of the loaded source index.");
                continue;
            }
            var markdown = await FileSkeletonBuilder.BuildMarkdownForDocumentAsync(document, targetPath,
                formatSymbolId: null, formatSymbol: symbol =>
                {
                    var internalId = identity?.FormatHandoff(symbol, solution);
                    return internalId is null ? null : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalId);
                }, ct: ct).ConfigureAwait(false);
            chunks.Add(markdown);
        }
        var hasMissing = chunks.Any(chunk => chunk.Contains("not part of the loaded source index", StringComparison.Ordinal)
            || chunk.Contains("outside the selected target", StringComparison.Ordinal));
        return NavigationToolSupport.SuccessText(string.Join("\n\n", chunks), hasMissing,
            hasMissing ? "Correct missing or out-of-target paths and repeat the file skeleton query." : null);
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
                FormatTypeHandoff: identity is null ? null : symbol => identity.FormatHandoff(symbol, solution))).ConfigureAwait(false);
        if (payload.Error is not null)
            return payload.ErrorCode == NavigationErrorCodes.AmbiguousSymbol
                ? McpToolResults.Recoverable(NavigationErrorCodes.AmbiguousSymbol, payload.Error,
                    "Pass an exact project name or canonical project path.", maxResponseBytes: bytes, maxResponseTokens: tokens)
                : McpToolResults.InvalidArgument(payload.Error, "$.project", "Correct the project or namespace query.", maxResponseBytes: bytes, maxResponseTokens: tokens);
        return NavigationToolSupport.SuccessText(payload.FormattedText, payload.Truncated, payload.NextAction);
    }

    private static string FormatClassStructure(ClassStructurePayload result)
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

    private static ClassStructurePayload BuildAssemblyClassStructure(INamedTypeSymbol type, string assemblyPath, AnalysisSymbolIdentity identity,
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
            _ => members.OrderBy(member => member.LineCount).ThenBy(member => member.Name, StringComparer.Ordinal).ToList(),
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
