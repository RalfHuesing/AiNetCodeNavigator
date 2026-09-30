using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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

[McpServerToolType]
public sealed class RelationshipTools(NavigatorHostRuntime runtime)
{
    [McpServerTool(Name = "get_call_tree", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public async Task<CallToolResult> GetCallTree([Required] string targetPath, [Required] string symbolIdentifier,
        string direction = "incoming", [Range(1, 5)] int depth = 2, [Range(1, 250)] int topN = 10,
        string format = "ascii", bool includeBcl = false, string scopeType = "all", bool includeGenerated = false,
        bool includeReferences = false, [Range(512, 65536)] int maxResponseBytes = 32768,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (!TryDirection(direction, out var parsedDirection)) return Invalid("direction", "Use incoming, outgoing, or both.");
        if (format is not ("ascii" or "mermaid")) return Invalid("format", "Use ascii or mermaid.");
        if (!TryScope(scopeType, out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "get_call_tree", targetPath,
            new { symbolIdentifier, direction, depth, topN, format, includeBcl, scopeType, includeGenerated, includeReferences },
            operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) => await WithSource(target, async solution =>
            {
                var symbol = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(solution, symbol.Symbol!, depth, topN, parsedDirection, includeBcl, scope, includeGenerated), ct).ConfigureAwait(false);
                var body = format == "mermaid" ? CallTreeMermaidRenderer.RenderMermaid(graph) : CallGraphTextRenderer.RenderAscii(graph);
                return NavigationToolSupport.SuccessText(body, graph.Truncated,
                    graph.Truncated ? "Increase depth or topN and repeat the query." : null);
            }, maxResponseBytes, maxResponseTokens, ct), AnalysisTargetType.Project, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "find_references", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public async Task<CallToolResult> FindReferences([Required] string targetPath, [Required] string symbolIdentifier,
        [Range(1, 3)] int depth = 1, [Range(1, 50)] int maxResults = 50, string scopeType = "all",
        bool includeGenerated = false, bool includeReferences = false, [Range(512, 65536)] int maxResponseBytes = 16384,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (!TryScope(scopeType, out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "find_references", targetPath,
            new { symbolIdentifier, depth, maxResults, scopeType, includeGenerated, includeReferences }, operationToken,
            continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) => await WithSource(target, async solution =>
            {
                var symbol = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var result = await FindReferencesResolver.FindReferencesAsync(symbol.Symbol!, solution, maxResults, depth, ct,
                    scope: scope, includeGenerated: includeGenerated).ConfigureAwait(false);
                return NavigationToolSupport.Success(result, result.IsTruncated || result.IsTruncatedByNodeLimit || result.IsDepthClamped,
                    "Increase depth or maxResults and repeat the query.");
            }, maxResponseBytes, maxResponseTokens, ct), AnalysisTargetType.Project, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "get_type_hierarchy", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public async Task<CallToolResult> GetTypeHierarchy([Required] string targetPath, [Required] string symbolIdentifier,
        [Range(1, 1000)] int maxResults = 50, string scopeType = "all", bool includeGenerated = false,
        [Range(512, 65536)] int maxResponseBytes = 16384, [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        string? operationToken = null, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (!TryScope(scopeType, out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "get_type_hierarchy", targetPath,
            new { symbolIdentifier, maxResults, scopeType, includeGenerated }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, ct) => await WithSource(target, async solution =>
            {
                var symbol = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                if (symbol.Symbol is not INamedTypeSymbol named) return Invalid("symbolIdentifier", "Resolve a named class, interface, or struct.");
                var result = await TypeHierarchyScanner.ScanAsync(named, solution, maxResults, ct, scope, includeGenerated).ConfigureAwait(false);
                if (!result.IsSuccess) return McpToolResults.InvalidArgument(result.ErrorMessage!, "$.symbolIdentifier", "Choose a supported named type.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                return NavigationToolSupport.Success(result, result.IsTruncated, "Increase maxResults and repeat the query.");
            }, maxResponseBytes, maxResponseTokens, ct), AnalysisTargetType.Project, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "find_implementations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public async Task<CallToolResult> FindImplementations([Required] string targetPath, [Required] string symbolIdentifier,
        [Range(1, 1000)] int maxResults = 50, string scopeType = "all", bool includeGenerated = false,
        [Range(512, 65536)] int maxResponseBytes = 16384, [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        string? operationToken = null, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (!TryScope(scopeType, out var scope)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "find_implementations", targetPath,
            new { symbolIdentifier, maxResults, scopeType, includeGenerated }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, ct) => await WithSource(target, async solution =>
            {
                var symbol = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
                if (symbol.Error is not null) return Fail(symbol.Error.Value, "$.symbolIdentifier");
                var result = await FindReferencesResolver.FindImplementationsAsync(symbol.Symbol!, solution, maxResults, ct, scope, includeGenerated).ConfigureAwait(false);
                if (result.ErrorMessage is not null) return McpToolResults.InvalidArgument(result.ErrorMessage, "$.symbolIdentifier", "Use an interface, abstract/virtual member, or overridable class.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                return NavigationToolSupport.Success(result, result.IsTruncated, "Increase maxResults and repeat the query.");
            }, maxResponseBytes, maxResponseTokens, ct), AnalysisTargetType.Project, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        CallToolResult Fail(ResultError error, string field) => NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, field);
    }

    [McpServerTool(Name = "get_impact", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public async Task<CallToolResult> GetImpact([Required] string targetPath, string? symbolIdentifier = null,
        string? gitRef = null, string detailLevel = "callers", [Range(1, 3)] int depth = 1,
        [Range(1, 1000)] int maxResults = 50, [Range(1, 100)] int maxChangedSymbols = 20,
        [Range(1, 50)] int maxTestsPerSymbol = 10, bool includeReferences = false,
        [Range(512, 65536)] int maxResponseBytes = 16384, [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        string? operationToken = null, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (detailLevel is not ("callers" or "change-context")) return Invalid("detailLevel", "Use callers or change-context.");
        if (symbolIdentifier is not null && gitRef is not null) return Invalid("symbolIdentifier", "Specify symbolIdentifier or gitRef, not both.");
        return await NavigationToolSupport.RouteAsync(runtime, "get_impact", targetPath,
            new { symbolIdentifier, gitRef, detailLevel, depth, maxResults, maxChangedSymbols, maxTestsPerSymbol, includeReferences },
            operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    if (symbolIdentifier is null || gitRef is not null || detailLevel == "change-context") return Invalid("symbolIdentifier", "Assembly impact requires a symbolIdentifier and does not accept gitRef or change-context.");
                    var access = await AssemblySymbolHandoffResolver.ResolveAsync(symbolIdentifier, ct).ConfigureAwait(false);
                    if (!access.IsSuccess) return NavigationToolSupport.Failure(access.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    await using var lease = access.Value!;
                    if (!string.Equals(Path.GetFullPath(lease.Origin.CanonicalPath), target.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                        return Invalid("symbolIdentifier", "Use a handoff produced by this targetPath.");
                    var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(lease.Symbol, lease.Solution, depth, maxResults, ct).ConfigureAwait(false);
                    return NavigationToolSupport.Success(impact, impact.IsTruncated || impact.IsTruncatedByNodeLimit || impact.IsDepthClamped, "Increase depth or maxResults and repeat the query.");
                }
                if (symbolIdentifier is not null)
                    return await WithSource(target, async solution =>
                    {
                        var symbol = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
                        if (symbol.Error is not null) return NavigationToolSupport.Failure(symbol.Error.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                        var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(symbol.Symbol!, solution, depth, maxResults, ct).ConfigureAwait(false);
                        return NavigationToolSupport.Success(impact, impact.IsTruncated || impact.IsTruncatedByNodeLimit || impact.IsDepthClamped, "Increase depth or maxResults and repeat the query.");
                    }, maxResponseBytes, maxResponseTokens, ct);
                return await BuildGitImpactAsync(target, gitRef, detailLevel, maxResults, maxChangedSymbols, maxTestsPerSymbol, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
    }

    [McpServerTool(Name = "dependency_graph", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public async Task<CallToolResult> DependencyGraph([Required] string targetPath, string? filePath = null,
        string? symbolIdentifier = null, string? project = null, string direction = "both", [Range(1, 3)] int depth = 1,
        [Range(1, 500)] int maxResults = 50, string scopeType = "all", bool includeGenerated = false,
        [Range(512, 65536)] int maxResponseBytes = 24576, [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        string? operationToken = null, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if ((filePath is null) == (symbolIdentifier is null)) return Invalid("filePath", "Specify exactly one of filePath or symbolIdentifier.");
        if (!TryDependencyDirection(direction, out var parsedDirection)) return Invalid("direction", "Use incoming, outgoing, or both.");
        if (!TryScope(scopeType, out _)) return Invalid("scopeType", "Use all, production, or tests.");
        return await NavigationToolSupport.RouteAsync(runtime, "dependency_graph", targetPath,
            new { filePath, symbolIdentifier, project, direction, depth, maxResults, scopeType, includeGenerated }, operationToken,
            continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) => await WithSource(target, async solution =>
            {
                string? typeName = null;
                if (symbolIdentifier is not null)
                {
                    var resolved = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
                    if (resolved.Error is not null) return NavigationToolSupport.Failure(resolved.Error.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                    typeName = resolved.Symbol is INamedTypeSymbol type ? type.ToDisplayString() : resolved.Symbol!.ContainingType?.ToDisplayString();
                    if (typeName is null) return McpToolResults.InvalidArgument("The symbol has no containing type.", "$.symbolIdentifier", "Choose a type or member declared in a type.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                }
                var scan = await DependencyGraphScanner.ScanSolutionAsync(solution, ct,
                    new DependencyGraphScanOptions(PageSize: maxResults, TargetFilePath: filePath, TargetTypeName: typeName,
                        TargetProject: project, Direction: parsedDirection, Depth: depth)).ConfigureAwait(false);
                return NavigationToolSupport.Success(scan, scan.IsTruncated, "Increase maxResults, depth, or document coverage and repeat the query.");
            }, maxResponseBytes, maxResponseTokens, ct), AnalysisTargetType.Project, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
    }

    [McpServerTool(Name = "resolve_type_origin", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public async Task<CallToolResult> ResolveTypeOrigin([Required] string targetPath, string? symbolIdentifier = null,
        string? typeName = null, bool includeReferences = true, [Range(512, 65536)] int maxResponseBytes = 16384,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if ((symbolIdentifier is null) == (typeName is null)) return Invalid("symbolIdentifier", "Specify exactly one of symbolIdentifier or typeName.");
        return await NavigationToolSupport.RouteAsync(runtime, "resolve_type_origin", targetPath,
            new { symbolIdentifier, typeName, includeReferences }, operationToken, continuationToken, maxResponseBytes, maxResponseTokens,
            async (target, ct) =>
            {
                if (target.TargetType == AnalysisTargetType.Assembly)
                {
                    var input = symbolIdentifier ?? typeName!;
                    if (symbolIdentifier is not null)
                    {
                        var access = await AssemblySymbolHandoffResolver.ResolveAsync(symbolIdentifier, ct).ConfigureAwait(false);
                        if (!access.IsSuccess) return NavigationToolSupport.Failure(access.Error!.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                        await using var lease = access.Value!;
                        input = lease.Symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                    }
                    var result = await ResolveTypeOriginScanner.ResolveAsync(new ResolveTypeOriginRequest(target.CanonicalPath, input, includeReferences), ct).ConfigureAwait(false);
                    return result.IsSuccess ? NavigationToolSupport.Success(result.Value!) : NavigationToolSupport.Failure(result.Error!.Value, maxResponseBytes, maxResponseTokens, "$.typeName");
                }
                return await WithSource(target, async solution =>
                {
                    var resolved = symbolIdentifier is not null
                        ? await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false)
                        : await Resolve(solution, typeName!, ct).ConfigureAwait(false);
                    if (resolved.Error is not null) return NavigationToolSupport.Failure(resolved.Error.Value, maxResponseBytes, maxResponseTokens, symbolIdentifier is null ? "$.typeName" : "$.symbolIdentifier");
                    var symbol = resolved.Symbol as INamedTypeSymbol ?? resolved.Symbol?.ContainingType;
                    if (symbol is null) return McpToolResults.InvalidArgument("The identifier does not resolve to a type.", "$.symbolIdentifier", "Resolve a named type or a member declared by one.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
                    var sourcePath = symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath;
                    var payload = new { typeName = symbol.ToDisplayString(), originKind = "source", projectName = symbol.ContainingAssembly?.Name, filePath = sourcePath };
                    return NavigationToolSupport.Success(payload);
                }, maxResponseBytes, maxResponseTokens, ct);
            }, null, cancellationToken);

        CallToolResult Invalid(string field, string hint) => McpToolResults.InvalidArgument("The requested value is not supported.", "$." + field, hint,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
    }

    [McpServerTool(Name = "get_feature_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public async Task<CallToolResult> GetFeatureContext([Required] string targetPath, [Required] string symbolIdentifier,
        string scopeType = "all", bool includeGenerated = false, [Range(1, 50)] int maxCallers = 10,
        [Range(1, 50)] int maxTests = 10, [Range(512, 65536)] int maxResponseBytes = 24576,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null, string? operationToken = null,
        string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (!TryScope(scopeType, out var scope)) return McpToolResults.InvalidArgument("scopeType is unsupported.", "$.scopeType", "Use all, production, or tests.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        return await NavigationToolSupport.RouteAsync(runtime, "get_feature_context", targetPath,
            new { symbolIdentifier, scopeType, includeGenerated, maxCallers, maxTests }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, ct) => await WithSource(target, async solution =>
            {
                var identity = await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
                var payload = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(solution, symbolIdentifier, maxCallers, maxTests, scope, identity, includeGenerated), ct).ConfigureAwait(false);
                if (payload?.Error is { } error) return NavigationToolSupport.Failure(error, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                return NavigationToolSupport.Success(payload!, payload!.CallersTruncated || payload.TestsTruncated,
                    "Increase maxCallers or maxTests and repeat the query.");
            }, maxResponseBytes, maxResponseTokens, ct), AnalysisTargetType.Project, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "get_test_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public async Task<CallToolResult> GetTestContext([Required] string targetPath, [Required] string symbolIdentifier,
        string scopeType = "all", bool includeGenerated = false, [Range(1, 100)] int maxResults = 30,
        [Range(512, 65536)] int maxResponseBytes = 16384, [Range(1, int.MaxValue)] int? maxResponseTokens = null,
        string? operationToken = null, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (!TryScope(scopeType, out var scope)) return McpToolResults.InvalidArgument("scopeType is unsupported.", "$.scopeType", "Use all, production, or tests.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        return await NavigationToolSupport.RouteAsync(runtime, "get_test_context", targetPath,
            new { symbolIdentifier, scopeType, includeGenerated, maxResults }, operationToken, continuationToken,
            maxResponseBytes, maxResponseTokens,
            async (target, ct) => await WithSource(target, async solution =>
            {
                var resolved = await Resolve(solution, symbolIdentifier, ct).ConfigureAwait(false);
                if (resolved.Error is not null) return NavigationToolSupport.Failure(resolved.Error.Value, maxResponseBytes, maxResponseTokens, "$.symbolIdentifier");
                var payload = await TestRecommendationBuilder.BuildAsync(resolved.Symbol!, solution, ct, includeGenerated, scope).ConfigureAwait(false);
                var fixtures = payload.TestFixtures.Take(maxResults).ToArray();
                var shown = payload with { TestFixtures = fixtures };
                var truncated = fixtures.Length < payload.TestFixtures.Count;
                return NavigationToolSupport.Success(shown, truncated, "Increase maxResults and repeat the query.");
            }, maxResponseBytes, maxResponseTokens, ct), AnalysisTargetType.Project, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CallToolResult> WithSource(AnalysisTarget target, Func<Solution, Task<CallToolResult>> operation,
        int bytes, int? tokens, CancellationToken ct) => await NavigationToolSupport.WithSourceSolutionAsync(runtime, target,
        (solution, _) => operation(solution), bytes, tokens, ct).ConfigureAwait(false);

    private static async Task<(ISymbol? Symbol, ResultError? Error)> Resolve(Solution solution, string identifier, CancellationToken ct)
    {
        var identity = await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
        var result = await SourceSymbolResolver.ResolveAsync(solution, identifier, identity, ct).ConfigureAwait(false);
        return result.IsSuccess ? (result.Symbol, null) : (null, result.Error);
    }

    private static bool TryScope(string value, out SymbolScopeType scope)
    {
        scope = value switch { "all" => SymbolScopeType.All, "production" => SymbolScopeType.Production, "tests" => SymbolScopeType.Tests, _ => (SymbolScopeType)(-1) };
        return Enum.IsDefined(scope);
    }

    private static bool TryDirection(string value, out CallTreeDirection direction)
    {
        direction = value switch { "incoming" => CallTreeDirection.Incoming, "outgoing" => CallTreeDirection.Outgoing, "both" => CallTreeDirection.Both, _ => (CallTreeDirection)(-1) };
        return Enum.IsDefined(direction);
    }

    private static bool TryDependencyDirection(string value, out DependencyGraphDirection direction)
    {
        direction = value switch { "incoming" => DependencyGraphDirection.Incoming, "outgoing" => DependencyGraphDirection.Outgoing, "both" => DependencyGraphDirection.Both, _ => (DependencyGraphDirection)(-1) };
        return Enum.IsDefined(direction);
    }

    private async Task<CallToolResult> BuildGitImpactAsync(AnalysisTarget target, string? gitRef, string detailLevel,
        int maxResults, int maxChangedSymbols, int maxTestsPerSymbol, int maxResponseBytes, int? maxResponseTokens, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(target.CanonicalPath)!;
        var rootResult = await RunGitAsync(directory, ["rev-parse", "--show-toplevel"], ct).ConfigureAwait(false);
        if (rootResult.ExitCode != 0)
            return NavigationToolSupport.Success(new { impactStatus = "not_git_repository", changedFiles = Array.Empty<string>(), symbols = Array.Empty<object>() });
        var gitRoot = Path.GetFullPath(rootResult.StandardOutput.Trim());
        var reference = gitRef ?? "HEAD";
        var verification = await RunGitAsync(gitRoot, ["rev-parse", "--verify", "--end-of-options", reference + "^{commit}"], ct).ConfigureAwait(false);
        if (verification.ExitCode != 0)
            return McpToolResults.Recoverable(NavigationErrorCodes.InvalidArgument, "gitRef could not be resolved.", "Provide a commit, branch, or tag that exists in this repository.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);

        var resolvedCommit = verification.StandardOutput.Trim();
        var diff = await RunGitAsync(gitRoot, ["diff", "--no-ext-diff", "--no-textconv", "--name-only", "--diff-filter=ACMRD", resolvedCommit, "--"], ct).ConfigureAwait(false);
        if (diff.ExitCode != 0)
            return McpToolResults.Recoverable(NavigationErrorCodes.InvalidArgument, "Git could not compute the requested change set.", "Check gitRef and repository availability, then retry.", maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);
        var untracked = gitRef is null
            ? await RunGitAsync(gitRoot, ["ls-files", "--others", "--exclude-standard"], ct).ConfigureAwait(false)
            : (0, string.Empty, string.Empty);
        var changedPaths = diff.StandardOutput.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Concat(untracked.Item2.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (changedPaths.Length == 0)
            return NavigationToolSupport.Success(new { impactStatus = "clean_worktree", changedFiles = Array.Empty<string>(), symbols = Array.Empty<object>() });
        return await WithSource(target, async solution =>
        {
            var byPath = solution.Projects.SelectMany(project => project.Documents)
                .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
                .ToDictionary(document => Path.GetFullPath(document.FilePath!), StringComparer.OrdinalIgnoreCase);
            var changedSymbols = new List<ISymbol>();
            foreach (var relative in changedPaths)
            {
                ct.ThrowIfCancellationRequested();
                var fullPath = Path.GetFullPath(Path.Combine(gitRoot, relative));
                if (!IsWithin(gitRoot, fullPath) || !byPath.TryGetValue(fullPath, out var document)) continue;
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                if (root is null || model is null) continue;
                foreach (var node in root.DescendantNodes().Where(node => node is Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax
                             or Microsoft.CodeAnalysis.CSharp.Syntax.DelegateDeclarationSyntax or Microsoft.CodeAnalysis.CSharp.Syntax.BaseMethodDeclarationSyntax
                             or Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax or Microsoft.CodeAnalysis.CSharp.Syntax.EventDeclarationSyntax))
                {
                    if (model.GetDeclaredSymbol(node, ct) is { } symbol) changedSymbols.Add(symbol);
                    if (changedSymbols.Count >= Math.Clamp(maxChangedSymbols, 1, 100)) break;
                }
                if (changedSymbols.Count >= Math.Clamp(maxChangedSymbols, 1, 100)) break;
            }
            var distinctSymbols = changedSymbols.Distinct(SymbolEqualityComparer.Default).ToArray();
            var symbolLimit = detailLevel == "change-context" ? Math.Clamp(maxChangedSymbols, 1, 100) : Math.Clamp(maxResults, 1, 1000);
            var entries = new List<object>();
            var callSiteCount = 0;
            foreach (var symbol in distinctSymbols.Take(symbolLimit))
            {
                ct.ThrowIfCancellationRequested();
                var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(symbol, solution, maxDepth: 1, maxResults: maxResults, ct: ct).ConfigureAwait(false);
                callSiteCount += impact.CallSites.Count;
                if (detailLevel == "change-context")
                {
                    var tests = await TestRecommendationBuilder.BuildAsync(symbol, solution, ct).ConfigureAwait(false);
                    entries.Add(new { symbol = symbol.ToDisplayString(), impact, testCandidates = tests.TestFixtures.Take(Math.Clamp(maxTestsPerSymbol, 1, 50)).ToArray(), testEvidence = tests.EvidenceMode });
                }
                else entries.Add(new { symbol = symbol.ToDisplayString(), impact });
            }
            var incomplete = changedPaths.Length > maxResults || distinctSymbols.Length > symbolLimit ||
                             distinctSymbols.Length < changedPaths.Length || changedPaths.Any(relative => !byPath.ContainsKey(Path.GetFullPath(Path.Combine(gitRoot, relative))));
            var status = detailLevel == "callers" && callSiteCount == 0 && !incomplete ? "diff_without_callsite_impact" : "changes_found";
            return NavigationToolSupport.Success(new { impactStatus = status, completeness = incomplete ? "partial" : "complete",
                changedFiles = changedPaths.Take(maxResults).ToArray(), analyzedSymbols = entries }, incomplete,
                "Increase maxResults or maxChangedSymbols and repeat the query; inspect listed unresolved files before treating an empty symbol list as complete.");
        }, maxResponseBytes, maxResponseTokens, ct).ConfigureAwait(false);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Git.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); throw; }
        return (process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
