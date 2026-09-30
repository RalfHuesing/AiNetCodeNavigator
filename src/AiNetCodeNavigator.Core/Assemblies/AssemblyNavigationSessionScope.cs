#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Assemblies;

internal sealed class AssemblyNavigationSessionScope : IAsyncDisposable
{
    private readonly AssemblyAnalysisSession session;

    private AssemblyNavigationSessionScope(AssemblyAnalysisSession session, AssemblyContext context)
    {
        this.session = session;
        Context = context;
    }

    internal AssemblyContext Context { get; }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The successful result transfers ownership of the session to the returned scope.")]
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

        var session = new AssemblyAnalysisSession(fullPath);
        AssemblySessionRefreshResult refresh;
        try
        {
            refresh = await session.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        var generation = session.CurrentGeneration;
        if (generation is null || refresh.Status == AssemblySessionStatus.Failed)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            var message = refresh.Diagnostics.Count == 0
                ? "The assembly could not be analyzed."
                : string.Join(" ", refresh.Diagnostics.Select(diagnostic => diagnostic.Message));
            if (refresh.Failure?.Kind == AssemblySessionFailureKind.MetadataUnavailable
                || message.Contains(AssemblyReferenceResolver.NativeMetadataFailureMessage, StringComparison.Ordinal)
                || message.Contains("BadImageFormatException", StringComparison.OrdinalIgnoreCase))
            {
                return Result<AssemblyNavigationSessionScope>.Failure(
                    NavigationErrorCodes.InvalidAssembly,
                    $"'{Path.GetFileName(fullPath)}' is not a valid managed .NET assembly.",
                    "assemblyPath must point to a managed .NET .dll or .exe containing IL.");
            }

            return Result<AssemblyNavigationSessionScope>.Failure(
                NavigationErrorCodes.WorkspaceDiagnostic,
                message,
                fullPath);
        }

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
        return Result<AssemblyNavigationSessionScope>.Success(new(session, context));
    }

    public ValueTask DisposeAsync() => session.DisposeAsync();
}
