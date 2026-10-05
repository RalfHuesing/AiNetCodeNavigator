using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Core.Symbols;
using Microsoft.Extensions.Hosting;

namespace AiNetCodeNavigator.Mcp;

/// <summary>Owns process-wide navigation state and drains operations before releasing resident workspaces.</summary>
public sealed class NavigatorHostRuntime : IAsyncDisposable, IDisposable
{
    internal static readonly TimeSpan ResponseWindow = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan PollResponseWindow = TimeSpan.FromSeconds(1);
    private int _disposeStarted;
    private readonly CancellationTokenSource scratchCleanupCancellation = new();
    private Task scratchCleanupTask = Task.CompletedTask;

    // Internal override keeps routing and polling tests deterministic; hosts retain the production default.
    internal NavigatorHostRuntime(
        IHostApplicationLifetime lifetime,
        TimeSpan? operationResponseWindow = null,
        ProjectRegistry? projectRegistry = null,
        DependencyGraphCache? dependencyGraphCache = null,
        TimeSpan? operationPollResponseWindow = null,
        Serilog.ILogger? workspaceLogger = null)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        WorkspaceLogger = workspaceLogger ?? Serilog.Log.ForContext<NavigatorHostRuntime>();
        ProjectRegistry = projectRegistry ?? new ProjectRegistry(ProjectRegistryOptions.ForMSBuild());
        DependencyGraphCache = dependencyGraphCache ?? new DependencyGraphCache(runtimeLifetime: lifetime.ApplicationStopping);
        ProjectRegistry.SourceOwnerRetiring = DependencyGraphCache.RetireTargetAsync;
        AssemblyRegistry = AssemblyAnalysisSessionRegistry.Default;
        AnalysisIdentities = new AnalysisSymbolIdentityService(lifetime.ApplicationStopping);
        Operations = new LongRunningToolCallStore(operationResponseWindow ?? ResponseWindow, lifetime.ApplicationStopping,
            pollResponseWindow: operationPollResponseWindow ?? PollResponseWindow);
    }

    internal ProjectRegistry ProjectRegistry { get; }
    internal Serilog.ILogger WorkspaceLogger { get; }
    internal DependencyGraphCache DependencyGraphCache { get; }
    internal AssemblyAnalysisSessionRegistry AssemblyRegistry { get; }
    internal AnalysisSymbolIdentityService AnalysisIdentities { get; }
    internal LongRunningToolCallStore Operations { get; }

    internal void StartScratchCleanup() => scratchCleanupTask = Task.Run(() =>
        DesignTimeScratchMaintenance.CleanupAbandonedDirectories(scratchCleanupCancellation.Token));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        await scratchCleanupCancellation.CancelAsync().ConfigureAwait(false);
        await scratchCleanupTask.ConfigureAwait(false);
        scratchCleanupCancellation.Dispose();

        await Operations.DisposeAsync().ConfigureAwait(false);
        await AnalysisIdentities.DisposeAsync().ConfigureAwait(false);
        await DependencyGraphCache.DisposeAsync().ConfigureAwait(false);
        ProjectRegistry.SourceOwnerRetiring = null;
        await ProjectRegistry.DisposeAsync().ConfigureAwait(false);
        await AssemblyRegistry.DisposeAsync().ConfigureAwait(false);
        MSBuildSolutionLoader.CleanupDesignTimeScratch();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
