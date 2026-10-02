#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Assemblies;

/// <summary>Restores inspect_assembly handles against their resident target and current content snapshot.</summary>
public static class AssemblySymbolHandoffResolver
{
    internal static Result<ISymbol> ResolveWithinScope(string handoff, AssemblyNavigationSessionScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handoff);
        ArgumentNullException.ThrowIfNull(scope);
        var restored = HandoffHandleRegistry.Default.RestoreInternalHandoffForInput(handoff);
        if (!restored.IsSuccess) return Result<ISymbol>.Failure(restored.Error);
        if (!SymbolHandoffIdentifier.TryParse(restored.Value!, out var identifier))
            return Result<ISymbol>.Failure(NavigationErrorCodes.InvalidHandoff, "The handoff identifier is not canonical.", "Use a handoff returned by inspect_assembly.");
        if (identifier.Origin != SymbolHandoffOrigin.Assembly)
            return Result<ISymbol>.Failure(NavigationErrorCodes.TargetMismatch, "The handoff belongs to a source solution, not an assembly.");

        if (!SymbolHandoffToken.TryCreateTarget(scope.Context.Origin.CanonicalPath, out var targetToken)
            || !string.Equals(targetToken, identifier.TargetToken, StringComparison.Ordinal))
            return Result<ISymbol>.Failure(NavigationErrorCodes.TargetMismatch, "The assembly handoff belongs to another target assembly.");

        var contentHash = AnalysisSymbolIdentity.CreateAssemblyHandoffContentHash(
            scope.Context.Origin.ContentHash,
            scope.Context.ReferenceSnapshotHash,
            scope.Context.Generation);
        if (!SymbolHandoffToken.TryCreateContent(contentHash, out var contentToken)
            || !string.Equals(contentToken, identifier.ContentToken, StringComparison.Ordinal))
            return Result<ISymbol>.Failure(NavigationErrorCodes.StaleSnapshot,
                "The assembly handoff does not belong to the pinned batch snapshot.",
                "Repeat get_symbol_body with handles from the current assembly snapshot.");

        var symbols = DocumentationCommentId.GetSymbolsForDeclarationId(identifier.DocumentationCommentId, scope.Context.Compilation)
            .Where(symbol => SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, scope.Context.Compilation.Assembly))
            .Distinct(SymbolEqualityComparer.Default)
            .ToArray();
        return symbols.Length switch
        {
            1 => Result<ISymbol>.Success(symbols[0]),
            0 => Result<ISymbol>.Failure(NavigationErrorCodes.SymbolNotFound, "The assembly symbol no longer resolves in the pinned snapshot."),
            _ => Result<ISymbol>.Failure(NavigationErrorCodes.AmbiguousSymbol, "The assembly handoff resolves to more than one symbol in the pinned snapshot."),
        };
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "A successful result transfers the session lease to the caller.")]
    public static async Task<Result<AssemblySymbolHandoffAccess>> ResolveAsync(
        string handoff,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handoff);
        var restored = HandoffHandleRegistry.Default.RestoreInternalHandoffForInput(handoff);
        if (!restored.IsSuccess) return Result<AssemblySymbolHandoffAccess>.Failure(restored.Error);
        if (!SymbolHandoffIdentifier.TryParse(restored.Value!, out var identifier))
        {
            return Result<AssemblySymbolHandoffAccess>.Failure(
                NavigationErrorCodes.InvalidHandoff,
                "The handoff identifier is not canonical.",
                "Use a handoff returned by inspect_assembly.");
        }

        if (identifier.Origin != SymbolHandoffOrigin.Assembly)
        {
            return Result<AssemblySymbolHandoffAccess>.Failure(
                NavigationErrorCodes.TargetMismatch,
                "The handoff belongs to a source solution, not an assembly.");
        }

        var acquired = await AssemblyAnalysisSessionRegistry.Default
            .AcquireByTargetTokenAsync(identifier.TargetToken, cancellationToken)
            .ConfigureAwait(false);
        if (!acquired.IsSuccess) return Result<AssemblySymbolHandoffAccess>.Failure(acquired.Error);
        var sessionAccess = acquired.Value!;
        if (!AssemblyFingerprintCalculator.TryCreate(sessionAccess.Path, out var currentFingerprint, out var fingerprintDiagnostic)
            || currentFingerprint is null)
        {
            await sessionAccess.DisposeAsync().ConfigureAwait(false);
            return Result<AssemblySymbolHandoffAccess>.Failure(
                NavigationErrorCodes.TargetUnreadable,
                fingerprintDiagnostic?.Message ?? "The assembly target is no longer readable.");
        }

        var currentHandoffContentHash = AnalysisSymbolIdentity.CreateAssemblyHandoffContentHash(
            currentFingerprint.Sha256,
            sessionAccess.Generation.ReferenceSnapshotHash,
            sessionAccess.Generation.Number);
        var contentMatches = SymbolHandoffToken.TryCreateContent(
                currentHandoffContentHash,
                out var currentContentToken)
            && string.Equals(currentContentToken, identifier.ContentToken, StringComparison.Ordinal);
        if (!contentMatches)
        {
            await sessionAccess.DisposeAsync().ConfigureAwait(false);
            return Result<AssemblySymbolHandoffAccess>.Failure(
                NavigationErrorCodes.StaleSnapshot,
                "The assembly changed after this handoff was produced.",
                "Run inspect_assembly again and use a handle from the current response.");
        }

        var symbols = DocumentationCommentId.GetSymbolsForDeclarationId(
                identifier.DocumentationCommentId,
                sessionAccess.Generation.Snapshot.Compilation)
            .Where(symbol => SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, sessionAccess.Generation.Snapshot.Compilation.Assembly))
            .Distinct(SymbolEqualityComparer.Default)
            .ToArray();
        if (symbols.Length != 1)
        {
            await sessionAccess.DisposeAsync().ConfigureAwait(false);
            return Result<AssemblySymbolHandoffAccess>.Failure(
                symbols.Length == 0 ? NavigationErrorCodes.SymbolNotFound : NavigationErrorCodes.AmbiguousSymbol,
                symbols.Length == 0
                    ? "The assembly symbol no longer resolves in the current snapshot."
                    : "The assembly handoff resolved to more than one symbol.");
        }

        return Result<AssemblySymbolHandoffAccess>.Success(new AssemblySymbolHandoffAccess(sessionAccess, symbols[0]));
    }
}

public sealed class AssemblySymbolHandoffAccess : IAsyncDisposable
{
    private readonly AssemblyAnalysisSessionRegistry.AssemblySessionAccess sessionAccess;

    internal AssemblySymbolHandoffAccess(AssemblyAnalysisSessionRegistry.AssemblySessionAccess sessionAccess, ISymbol symbol)
    {
        this.sessionAccess = sessionAccess;
        Symbol = symbol;
    }

    public ISymbol Symbol { get; }
    public Solution Solution => sessionAccess.Generation.Snapshot.Solution;
    public Compilation Compilation => sessionAccess.Generation.Snapshot.Compilation;
    public IAssemblySymbol Assembly => sessionAccess.Generation.Snapshot.Compilation.Assembly;
    public DecompiledProjectPaths? DecompiledProjectPaths => sessionAccess.Generation.DecompiledProjectPaths;
    public AssemblyOrigin Origin => sessionAccess.Generation.Origin;
    public AssemblyIdentityDto Identity => sessionAccess.Generation.Identity;
    public long Generation => sessionAccess.Generation.Number;
    public string ReferenceSnapshotHash => sessionAccess.Generation.ReferenceSnapshotHash;
    public IReadOnlyList<string> Diagnostics => sessionAccess.Generation.Diagnostics
        .Select(diagnostic => diagnostic.Message)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public ValueTask DisposeAsync() => sessionAccess.DisposeAsync();
}
