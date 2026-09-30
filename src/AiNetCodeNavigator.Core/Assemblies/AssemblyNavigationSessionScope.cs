#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;

namespace AiNetCodeNavigator.Core.Assemblies;

internal sealed class AssemblyNavigationSessionScope : IAsyncDisposable
{
    private readonly AssemblyAnalysisSessionRegistry.AssemblySessionAccess sessionAccess;

    private AssemblyNavigationSessionScope(AssemblyAnalysisSessionRegistry.AssemblySessionAccess sessionAccess, AssemblyContext context)
    {
        this.sessionAccess = sessionAccess;
        Context = context;
    }

    internal AssemblyContext Context { get; }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The successful result transfers ownership of the session snapshot lease to the returned scope.")]
    internal static async Task<Result<AssemblyNavigationSessionScope>> OpenAsync(
        string? assemblyPath,
        CancellationToken cancellationToken)
    {
        if (!InspectAssemblyScanner.TryValidatePath(assemblyPath, out var fullPath, out var pathError))
        {
            return Result<AssemblyNavigationSessionScope>.Failure(
                NavigationErrorCodes.InvalidArgument,
                pathError,
                "assemblyPath must be an absolute path to an existing local .dll or .exe file.");
        }

        var acquired = await AssemblyAnalysisSessionRegistry.Default.AcquireAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (!acquired.IsSuccess) return Result<AssemblyNavigationSessionScope>.Failure(acquired.Error);
        var sessionAccess = acquired.Value!;
        var generation = sessionAccess.Generation;

        var context = new AssemblyContext(
            generation.Snapshot.Compilation.Assembly,
            generation.Identity,
            generation.References,
            generation.Diagnostics.Select(diagnostic => diagnostic.Message).Distinct(StringComparer.Ordinal).ToArray(),
            generation.Snapshot.Compilation,
            generation.Origin with { BodyAvailability = "available", ContentMode = "decompiledProject" },
            generation.Number,
            generation.Status,
            generation.DecompiledProjectPaths);
        return Result<AssemblyNavigationSessionScope>.Success(new(sessionAccess, context));
    }

    public ValueTask DisposeAsync() => sessionAccess.DisposeAsync();
}
