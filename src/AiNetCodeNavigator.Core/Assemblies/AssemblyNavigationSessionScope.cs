#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Assemblies;

public sealed class AssemblyNavigationSessionScope : IAsyncDisposable
{
    private readonly AssemblyAnalysisSessionRegistry.AssemblySessionAccess sessionAccess;

    private AssemblyNavigationSessionScope(AssemblyAnalysisSessionRegistry.AssemblySessionAccess sessionAccess, AssemblyContext context)
    {
        this.sessionAccess = sessionAccess;
        Context = context;
    }

    public AssemblyContext Context { get; }
    public Solution Solution => sessionAccess.Generation.Snapshot.Solution;

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The successful result transfers ownership of the session snapshot lease to the returned scope.")]
    public static async Task<Result<AssemblyNavigationSessionScope>> OpenAsync(
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
        return Result<AssemblyNavigationSessionScope>.Success(Create(acquired.Value!));
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "A successful result transfers the acquired resident snapshot lease to the returned scope.")]
    internal static async Task<Result<AssemblyNavigationSessionScope>> OpenResidentAsync(
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

        var acquired = await AssemblyAnalysisSessionRegistry.Default.AcquireResidentAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (!acquired.IsSuccess) return Result<AssemblyNavigationSessionScope>.Failure(acquired.Error);
        return Result<AssemblyNavigationSessionScope>.Success(Create(acquired.Value!));
    }

    private static AssemblyNavigationSessionScope Create(AssemblyAnalysisSessionRegistry.AssemblySessionAccess sessionAccess)
    {
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
            generation.DecompiledProjectPaths,
            generation.ReferenceSnapshotHash);
        return new AssemblyNavigationSessionScope(sessionAccess, context);
    }

    public ValueTask DisposeAsync() => sessionAccess.DisposeAsync();
}
