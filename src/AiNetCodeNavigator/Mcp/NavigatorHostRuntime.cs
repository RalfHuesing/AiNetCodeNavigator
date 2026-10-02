using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
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
        TimeSpan? operationResponseWindow = null)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ProjectRegistry = new ProjectRegistry(ProjectRegistryOptions.ForMSBuild());
        AssemblyRegistry = AssemblyAnalysisSessionRegistry.Default;
        HandoffHandles = HandoffHandleRegistry.Default;
        Operations = new LongRunningToolCallStore(operationResponseWindow ?? ResponseWindow, lifetime.ApplicationStopping);
    }

    internal ProjectRegistry ProjectRegistry { get; }
    internal AssemblyAnalysisSessionRegistry AssemblyRegistry { get; }
    internal HandoffHandleRegistry HandoffHandles { get; }
    internal LongRunningToolCallStore Operations { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        await Operations.DisposeAsync().ConfigureAwait(false);
        await ProjectRegistry.DisposeAsync().ConfigureAwait(false);
        await AssemblyRegistry.DisposeAsync().ConfigureAwait(false);
        MSBuildSolutionLoader.CleanupDesignTimeScratch();
        HandoffHandles.Clear();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
