#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Dependencies;

/// <summary>
/// Entry point for building dependency graphs.
/// </summary>
public static class DependencyGraphBuilder
{
    public static Task<DependencyGraphPayload> BuildForSolutionAsync(
        Solution solution,
        CancellationToken ct = default) =>
        DependencyGraphScanner.ScanSolutionAsync(solution, ct);
}
