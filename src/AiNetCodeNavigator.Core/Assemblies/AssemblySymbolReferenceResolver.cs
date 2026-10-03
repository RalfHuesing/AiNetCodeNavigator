#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Assemblies;

/// <summary>Resolves exact assembly references only in an explicitly selected owner target.</summary>
public static class AssemblySymbolReferenceResolver
{
    public static Result<ISymbol> ResolveWithinScope(string input, AssemblyNavigationSessionScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentNullException.ThrowIfNull(scope);
        if (!TryParseReference(input, out var reference, out var error))
            return Result<ISymbol>.Failure(error!);
        return ExactAssemblySymbolResolver.Resolve(scope, reference!);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "On success the returned AssemblySymbolReferenceAccess takes ownership of and disposes the session snapshot lease; on resolution failure this method disposes the scope before returning.")]
    public static async Task<Result<AssemblySymbolReferenceAccess>> OpenAndResolveAsync(
        string targetPath,
        string input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        if (!TryParseReference(input, out var reference, out var error))
            return Result<AssemblySymbolReferenceAccess>.Failure(error!);

        var opened = await AssemblyNavigationSessionScope.OpenAsync(targetPath, cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) return Result<AssemblySymbolReferenceAccess>.Failure(opened.Error);
        var scope = opened.Value!;
        var ownershipTransferred = false;
        try
        {
            var resolved = ExactAssemblySymbolResolver.Resolve(scope, reference!);
            if (!resolved.IsSuccess)
                return Result<AssemblySymbolReferenceAccess>.Failure(resolved.Error!);

            var result = Result<AssemblySymbolReferenceAccess>.Success(
                new AssemblySymbolReferenceAccess(scope, resolved.Value!));
            ownershipTransferred = true;
            return result;
        }
        finally
        {
            if (!ownershipTransferred)
                await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool TryParseReference(string input, out StableSymbolReference.Assembly? reference, out ResultError? error)
    {
        reference = null;
        error = null;
        if (!StableSymbolReferenceCodec.TryParseReferenceInput(input, null, out var parsed, out error))
        {
            error = new ResultError(NavigationErrorCodes.InvalidSymbolReference,
                "An assembly follow-up requires a canonical assembly reference.",
                "Rediscover the declaration on its owner target and use the returned reference.");
            return false;
        }
        if (error is not null) return false;
        if (parsed is not StableSymbolReference.Assembly assembly)
        {
            error = new ResultError(NavigationErrorCodes.TargetMismatch,
                "The supplied reference belongs to a source solution, not an assembly target.",
                "Open the returned source solution target and use the source reference there.");
            return false;
        }
        reference = assembly;
        return true;
    }
}

public sealed class AssemblySymbolReferenceAccess : IAsyncDisposable
{
    private AssemblyNavigationSessionScope? scope;

    internal AssemblySymbolReferenceAccess(AssemblyNavigationSessionScope scope, ISymbol symbol)
    {
        this.scope = scope;
        Symbol = symbol;
    }

    public static AssemblySymbolReferenceAccess FromResolvedScope(AssemblyNavigationSessionScope scope, ISymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(symbol);
        return new AssemblySymbolReferenceAccess(scope, symbol);
    }

    private AssemblyNavigationSessionScope Scope => scope
        ?? throw new InvalidOperationException("The assembly reference scope has been disposed.");

    public ISymbol Symbol { get; }
    public Solution Solution => Scope.Solution;
    public Compilation Compilation => Scope.Context.Compilation;
    public IAssemblySymbol Assembly => Scope.Context.Assembly;
    public DecompiledProjectPaths? DecompiledProjectPaths => Scope.Context.DecompiledProjectPaths;
    public AssemblyOrigin Origin => Scope.Context.Origin;
    public AssemblyIdentityDto Identity => Scope.Context.Identity!;
    public long Generation => Scope.Context.Generation;
    public string ReferenceSnapshotHash => Scope.Context.ReferenceSnapshotHash;
    public IReadOnlyList<string> Diagnostics => Scope.Context.Diagnostics;
    public AssemblyNavigationSessionScope ScopeValue => Scope;

    internal AssemblyNavigationSessionScope DetachScope()
    {
        var owned = Scope;
        scope = null;
        return owned;
    }

    public async ValueTask DisposeAsync()
    {
        var owned = scope;
        scope = null;
        if (owned is not null) await owned.DisposeAsync().ConfigureAwait(false);
    }
}
