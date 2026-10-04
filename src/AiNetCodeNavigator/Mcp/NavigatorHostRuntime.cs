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
    private int _disposeStarted;

    // Internal override keeps routing and polling tests deterministic; hosts retain the production default.
    internal NavigatorHostRuntime(
        IHostApplicationLifetime lifetime,
        TimeSpan? operationResponseWindow = null,
        ProjectRegistry? projectRegistry = null,
        DependencyGraphCache? dependencyGraphCache = null)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ProjectRegistry = projectRegistry ?? new ProjectRegistry(ProjectRegistryOptions.ForMSBuild());
        DependencyGraphCache = dependencyGraphCache ?? new DependencyGraphCache(runtimeLifetime: lifetime.ApplicationStopping);
        ProjectRegistry.SourceOwnerRetiring = DependencyGraphCache.RetireTargetAsync;
        AssemblyRegistry = AssemblyAnalysisSessionRegistry.Default;
        AnalysisIdentities = new AnalysisSymbolIdentityService(lifetime.ApplicationStopping);
        Operations = new LongRunningToolCallStore(operationResponseWindow ?? ResponseWindow, lifetime.ApplicationStopping);
    }

    internal ProjectRegistry ProjectRegistry { get; }
    internal DependencyGraphCache DependencyGraphCache { get; }
    internal AssemblyAnalysisSessionRegistry AssemblyRegistry { get; }
    internal AnalysisSymbolIdentityService AnalysisIdentities { get; }
    internal LongRunningToolCallStore Operations { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

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
