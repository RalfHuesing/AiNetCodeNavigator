using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

/// <summary>Owns an in-memory source solution and the real navigation runtime used by handler contracts.</summary>
internal sealed class InMemorySourceTestHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly IReadOnlyList<TestSolutionHandle> _workspaces;
    private readonly NavigatorHostRuntime _runtime;

    private InMemorySourceTestHost(
        IHost host,
        IReadOnlyList<TestSolutionHandle> workspaces,
        NavigatorHostRuntime runtime)
    {
        _host = host;
        _workspaces = workspaces;
        _runtime = runtime;
    }

    public NavigatorHostRuntime Runtime => _runtime;

    public static InMemorySourceTestHost Create(
        string solutionPath,
        IEnumerable<ProjectSpec> projects,
        DependencyGraphCache? dependencyGraphCache = null,
        TimeSpan? operationResponseWindow = null,
        TimeSpan? operationPollResponseWindow = null) =>
        CreateForSolutions([(solutionPath, projects)], dependencyGraphCache, operationResponseWindow, operationPollResponseWindow);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "On success NavigatorHostRuntime owns and disposes the registry; if runtime construction fails, this factory disposes the still-owned registry in its catch path.")]
    public static InMemorySourceTestHost CreateForSolutions(
        IEnumerable<(string SolutionPath, IEnumerable<ProjectSpec> Projects)> solutions,
        DependencyGraphCache? dependencyGraphCache = null,
        TimeSpan? operationResponseWindow = null,
        TimeSpan? operationPollResponseWindow = null)
    {
        ArgumentNullException.ThrowIfNull(solutions);
        var workspaces = new List<TestSolutionHandle>();
        var solutionsByPath = new Dictionary<string, Microsoft.CodeAnalysis.Solution>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        IHost? host = null;
        ProjectRegistry? registry = null;
        NavigatorHostRuntime? runtime = null;
        try
        {
            foreach (var (solutionPath, projects) in solutions)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
                var builder = TestWorkspaceBuilder.Create()
                    .WithVirtualSolutionPath(solutionPath)
                    .WithCapturedCoreReferences();
                foreach (var project in projects)
                    builder.WithProject(project);
                var workspace = builder.Build();
                workspaces.Add(workspace);
                if (!solutionsByPath.TryAdd(Path.GetFullPath(solutionPath), workspace.Solution))
                    throw new ArgumentException($"The in-memory source fixture repeats solution path '{solutionPath}'.", nameof(solutions));
            }
            if (workspaces.Count == 0)
                throw new ArgumentException("At least one in-memory source solution is required.", nameof(solutions));

            host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
            registry = new ProjectRegistry(new ProjectRegistryOptions(definition =>
            {
                var requestedTarget = Path.GetFullPath(definition.SolutionPath);
                if (!solutionsByPath.TryGetValue(requestedTarget, out var matchingSolution))
                    throw new InvalidOperationException($"The in-memory source fixture does not own '{requestedTarget}'.");
                return ResidentSolutionCreation.Resident(new ResidentSolution(matchingSolution));
            }, TimeProvider.System));
            var createdRuntime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>(),
                operationResponseWindow, registry, dependencyGraphCache, operationPollResponseWindow);
            runtime = createdRuntime;
            registry = null;
            return new InMemorySourceTestHost(host, workspaces, runtime);
        }
        catch
        {
            try
            {
                runtime?.Dispose();
                if (runtime is null)
                    registry?.Dispose();
            }
            finally
            {
                try
                {
                    foreach (var workspace in workspaces)
                        workspace.Dispose();
                }
                finally
                {
                    host?.Dispose();
                }
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _runtime.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                foreach (var workspace in _workspaces)
                    workspace.Dispose();
            }
            finally
            {
                _host.Dispose();
            }
        }
    }
}
