using System.Globalization;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using AiNetCodeNavigator.Configuration;
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
    [Description("Report host health, configuration, cache, resident workspaces, and optional target residency without loading the target.")]
    public CallToolResult GetServerHealth(
        [Description("Optional absolute solution or assembly path whose current resident status should be reported without loading it.")] string? targetPath = null,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).")] int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        [Range(1, int.MaxValue), Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null)
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

        var fullText = string.Join('\n', lines);
        var conservativeLines = lines.Select(line => IsHealthCounter(line)
            ? line[..(line.IndexOf(": ", StringComparison.Ordinal) + 2)] + "9999999999999999999"
            : line);
        var conservativeText = string.Join('\n', conservativeLines);
        var minimumBytes = Math.Max(
            McpResponseBudgetLimits.MinimumBytes,
            System.Text.Encoding.UTF8.GetByteCount(McpToolResults.SuccessStatusPrefix + conservativeText));
        var minimumTokens = McpResponseFormatter.CountTokens(McpToolResults.SuccessStatusPrefix + conservativeText);
        return BuildBudgetedResult(() => McpToolResults.CompleteOrBudgetTooSmall(
            fullText,
            maxResponseBytes,
            maxResponseTokens,
            minimumBytes,
            minimumTokens), maxResponseTokens);
    }

    [McpServerTool(Name = "reload_config", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Reload the host settings file and atomically publish the validated settings and effective log level.")]
    public async Task<CallToolResult> ReloadConfig(
        CancellationToken cancellationToken,
        [Range(McpResponseBudgetLimits.MinimumBytes, McpResponseBudgetLimits.MaximumBytes), Description("Maximum response text size in UTF-8 bytes (512–65536; default 16384).")] int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        [Range(1, int.MaxValue), Description("Optional positive maximum response token count; uses cl100k_base.")] int? maxResponseTokens = null)
    {
        CallToolResult? preparedConfirmation = null;
        ConfigurationReloadResult result;
        try
        {
            result = await runtime.Configuration.ReloadAsync(settings =>
            {
                preparedConfirmation = McpToolResults.CompleteOrBudgetTooSmall(
                    $"Configuration reloaded.\nversion: {settings.Version}\nminimumLogLevel: {settings.MinimumLogLevel}",
                    maxResponseBytes,
                    maxResponseTokens);
                return preparedConfirmation.IsError != true;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentOutOfRangeException) when (maxResponseTokens is not null)
        {
            throw new McpProtocolException(
                "The response token budget is too small to represent the required tool result.",
                null,
                McpErrorCode.InvalidParams);
        }

        if (!result.Succeeded)
        {
            if (result.ErrorCode == "RESPONSE_BUDGET_TOO_SMALL")
            {
                return preparedConfirmation!;
            }

            return BuildBudgetedResult(() => McpToolResults.Recoverable(
                result.ErrorCode!,
                result.Message!,
                "Correct the host settings file and call reload_config again.",
                maxResponseBytes: maxResponseBytes,
                maxResponseTokens: maxResponseTokens), maxResponseTokens);
        }

        return preparedConfirmation!;
    }

    private static bool IsHealthCounter(string line)
    {
        var fieldEnd = line.IndexOf(": ", StringComparison.Ordinal);
        if (fieldEnd < 0)
        {
            return false;
        }

        return line[..fieldEnd] is "uptimeSeconds" or "residentSolutions" or "loadingSolutions"
            or "residentAssemblySessions" or "activeAssemblyAccesses" or "handoffHandles"
            or "cacheHits" or "cacheMisses" or "cachedSyntaxTrees" or "cachedCompilations"
            or "managedMemoryBytes" or "settingsVersion" or "targetActiveAccesses";
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
