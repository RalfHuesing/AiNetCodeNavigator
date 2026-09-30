using System.Globalization;
using System.ComponentModel.DataAnnotations;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp.Formatting;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AiNetCodeNavigator.Mcp.Tools.Maintenance;

[McpServerToolType]
public sealed class MaintenanceTools(NavigatorHostRuntime runtime)
{
    [McpServerTool(Name = "get_server_health", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public CallToolResult GetServerHealth(
        string? targetPath = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes)] int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null)
    {
        var projectSnapshots = runtime.ProjectRegistry.Snapshots();
        var assemblySnapshots = runtime.AssemblyRegistry.GetHealthSnapshot();
        var cache = runtime.CompilationCache.GetStatistics();
        var memoryBytes = GC.GetTotalMemory(forceFullCollection: false);
        var settings = runtime.Configuration.Current;
        var lines = new List<string>
        {
            "Navigator host: running",
            $"uptimeSeconds: {Math.Max(0, (long)(DateTimeOffset.UtcNow - runtime.StartedUtc).TotalSeconds).ToString(CultureInfo.InvariantCulture)}",
            $"residentSolutions: {projectSnapshots.Count}",
            $"loadingSolutions: {projectSnapshots.Count(snapshot => snapshot.ResidentSolution.LoadState == ServerLoadState.Loading)}",
            $"residentAssemblySessions: {assemblySnapshots.Count}",
            $"activeAssemblyAccesses: {assemblySnapshots.Sum(snapshot => snapshot.ActiveAccesses)}",
            $"handoffHandles: {runtime.HandoffHandles.Count}",
            $"cacheHits: {cache.Hits}",
            $"cacheMisses: {cache.Misses}",
            $"cachedSyntaxTrees: {cache.CachedTreesCount}",
            $"cachedCompilations: {cache.CachedCompilationsCount}",
            $"managedMemoryBytes: {memoryBytes.ToString(CultureInfo.InvariantCulture)}",
            $"settingsVersion: {settings.Version}",
            $"minimumLogLevel: {settings.MinimumLogLevel}",
        };

        if (targetPath is not null)
        {
            var resolution = AnalysisTargetResolver.Resolve(new AnalysisTargetRequest(targetPath));
            if (resolution.Error is { } error)
            {
                var message = error.Code == NavigationErrorCodes.TargetUnreadable
                    ? "targetPath could not be read while checking resident status."
                    : "targetPath must be an absolute path to an existing .sln, .slnx, .dll, or .exe file.";
                return BuildBudgetedResult(() => McpToolResults.Recoverable(
                    error.Code,
                    message,
                    "Provide an existing absolute solution or managed assembly path.",
                    fieldPath: error.FieldPath,
                    maxResponseBytes: maxResponseBytes,
                    maxResponseTokens: maxResponseTokens), maxResponseTokens);
            }

            var target = resolution.Target!;
            var project = target.TargetType == AnalysisTargetType.Project
                ? projectSnapshots.FirstOrDefault(snapshot => string.Equals(snapshot.RootPath, target.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                : null;
            var assembly = target.TargetType == AnalysisTargetType.Assembly
                ? assemblySnapshots.FirstOrDefault(snapshot => string.Equals(snapshot.Path, target.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                : null;
            lines.Add($"targetResident: {(project is not null || assembly is not null).ToString().ToLowerInvariant()}");
            lines.Add($"targetKind: {(target.TargetType == AnalysisTargetType.Project ? "source" : "assembly")}");
            if (project is not null)
            {
                lines.Add($"targetState: {project.ResidentSolution.LoadState}");
            }
            else if (assembly is not null)
            {
                lines.Add($"targetState: {(assembly.HasResidentGeneration ? "loaded" : "loading")}");
                lines.Add($"targetActiveAccesses: {assembly.ActiveAccesses}");
            }
            else
            {
                lines.Add("targetState: not_resident");
            }
        }

        return BuildBudgetedResult(() => McpToolResults.Success(
            string.Join('\n', lines),
            maxResponseBytes: maxResponseBytes,
            maxResponseTokens: maxResponseTokens), maxResponseTokens);
    }

    [McpServerTool(Name = "reload_config", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    public async Task<CallToolResult> ReloadConfig(
        CancellationToken cancellationToken,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes)] int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        [Range(1, int.MaxValue)] int? maxResponseTokens = null)
    {
        var result = await runtime.Configuration.ReloadAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return BuildBudgetedResult(() => McpToolResults.Recoverable(
                result.ErrorCode!,
                result.Message!,
                "Correct the host settings file and call reload_config again.",
                maxResponseBytes: maxResponseBytes,
                maxResponseTokens: maxResponseTokens), maxResponseTokens);
        }

        return BuildBudgetedResult(() => McpToolResults.Success(
            $"Configuration reloaded.\nversion: {result.Settings.Version}\nminimumLogLevel: {result.Settings.MinimumLogLevel}",
            maxResponseBytes: maxResponseBytes,
            maxResponseTokens: maxResponseTokens), maxResponseTokens);
    }

    private static CallToolResult BuildBudgetedResult(Func<CallToolResult> build, int? maxResponseTokens)
    {
        try
        {
            return build();
        }
        catch (ArgumentOutOfRangeException) when (maxResponseTokens is not null)
        {
            throw new McpProtocolException(
                "The response token budget is too small to represent the required tool result.",
                null,
                McpErrorCode.InvalidParams);
        }
    }
}
