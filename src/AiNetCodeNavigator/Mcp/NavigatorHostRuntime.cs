using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Caching;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.Extensions.Hosting;

namespace AiNetCodeNavigator.Mcp;

/// <summary>Owns process-wide navigation state and drains operations before releasing resident workspaces.</summary>
public sealed class NavigatorHostRuntime : IAsyncDisposable, IDisposable
{
    internal static readonly TimeSpan ResponseWindow = TimeSpan.FromSeconds(15);

    // Internal override keeps routing and polling tests deterministic; hosts retain the production default.
    internal NavigatorHostRuntime(
        Configuration.NavigatorHostConfiguration configuration,
        IHostApplicationLifetime lifetime,
        TimeSpan? operationResponseWindow = null)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        Configuration = configuration;
        ProjectRegistry = new ProjectRegistry(ProjectRegistryOptions.ForMSBuild());
        AssemblyRegistry = AssemblyAnalysisSessionRegistry.Default;
        CompilationCache = new CompilationCacheManager();
        HandoffHandles = HandoffHandleRegistry.Default;
        Operations = new LongRunningToolCallStore(operationResponseWindow ?? ResponseWindow, lifetime.ApplicationStopping);
        StartedUtc = DateTimeOffset.UtcNow;
    }

    internal Configuration.NavigatorHostConfiguration Configuration { get; }
    internal ProjectRegistry ProjectRegistry { get; }
    internal AssemblyAnalysisSessionRegistry AssemblyRegistry { get; }
    internal CompilationCacheManager CompilationCache { get; }
    internal HandoffHandleRegistry HandoffHandles { get; }
    internal LongRunningToolCallStore Operations { get; }
    internal DateTimeOffset StartedUtc { get; }

    public async ValueTask DisposeAsync()
    {
        await Operations.DisposeAsync().ConfigureAwait(false);
        await ProjectRegistry.DisposeAsync().ConfigureAwait(false);
        await AssemblyRegistry.DisposeAsync().ConfigureAwait(false);
        MSBuildSolutionLoader.CleanupDesignTimeScratch();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
