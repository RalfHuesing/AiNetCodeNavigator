using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Mcp.Tools.Relationships;

internal sealed record AssemblyReferenceClosureOwner(string TargetPath, AssemblyNavigationSessionScope Scope)
{
    internal AnalysisSymbolIdentity HandoffIdentity => AnalysisSymbolIdentity.ForAssembly(
        Scope.Context.Origin.CanonicalPath,
        Scope.Context.Origin.ContentHash,
        Scope.Context.Generation,
        Scope.Context.ReferenceSnapshotHash);
}

internal sealed record AssemblyReferenceClosureOwnerSymbol(
    AssemblyReferenceClosureOwner Owner,
    string DocumentationCommentId,
    ISymbol Symbol);

internal sealed record AssemblyReferenceClosureSessionOpenResult(
    AssemblyReferenceClosureSession? Session,
    ResultError? Error,
    string? ErrorField);

/// <summary>Holds the resolved owner scopes and exact original PE identities for one bounded assembly closure query.</summary>
internal sealed class AssemblyReferenceClosureSession : IAsyncDisposable
{
    internal const int MaxAssemblies = 32;

    private readonly List<AssemblyNavigationSessionScope> ownedScopes;
    private readonly AssemblySymbolHandoffAccess handoffAccess;
    private readonly AssemblyNavigationSessionScope rootScope;
    private bool disposed;

    private AssemblyReferenceClosureSession(
        AssemblyNavigationSessionScope rootScope,
        AssemblySymbolHandoffAccess handoffAccess,
        IReadOnlyList<AssemblyReferenceClosureOwner> owners,
        List<AssemblyNavigationSessionScope> ownedScopes,
        string rootPath,
        string handoffOwnerPath,
        string declarationCommentId,
        bool ownerLimitReached,
        bool hasFailedOwners,
        bool hasUnresolvedReferences)
    {
        this.rootScope = rootScope;
        this.handoffAccess = handoffAccess;
        this.ownedScopes = ownedScopes;
        Owners = owners;
        RootPath = rootPath;
        HandoffOwnerPath = handoffOwnerPath;
        DeclarationCommentId = declarationCommentId;
        OwnerLimitReached = ownerLimitReached;
        HasFailedOwners = hasFailedOwners;
        HasUnresolvedReferences = hasUnresolvedReferences;
    }

    internal IReadOnlyList<AssemblyReferenceClosureOwner> Owners { get; }
    internal string RootPath { get; }
    internal string HandoffOwnerPath { get; }
    internal string DeclarationCommentId { get; }
    internal ISymbol HandoffSymbol => handoffAccess.Symbol;
    internal AssemblyIdentityDto HandoffIdentity => handoffAccess.Identity;
    internal bool OwnerLimitReached { get; }
    internal bool HasFailedOwners { get; }
    internal bool HasUnresolvedReferences { get; }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "A successful result transfers all acquired leases to the returned closure session.")]
    internal static async Task<AssemblyReferenceClosureSessionOpenResult> OpenAsync(
        string targetPath,
        string identifier,
        CancellationToken cancellationToken)
    {
        var openedRoot = await AssemblyNavigationSessionScope.OpenAsync(targetPath, cancellationToken).ConfigureAwait(false);
        if (!openedRoot.IsSuccess) return Failed(openedRoot.Error!.Value, "$.targetPath");
        var root = openedRoot.Value!;
        AssemblySymbolHandoffAccess? handoff = null;
        var scopes = new List<AssemblyNavigationSessionScope>();
        try
        {
            var resolvedHandoff = await AssemblySymbolHandoffResolver.ResolveAsync(identifier, cancellationToken).ConfigureAwait(false);
            if (!resolvedHandoff.IsSuccess) return await FailedAndDisposeAsync(resolvedHandoff.Error!.Value, "$.symbolIdentifier").ConfigureAwait(false);
            handoff = resolvedHandoff.Value!;

            var rootPath = Path.GetFullPath(root.Context.Origin.CanonicalPath);
            var handoffOwnerPath = Path.GetFullPath(handoff.Origin.CanonicalPath);
            var references = root.Context.References
                .Where(reference => reference.Resolved && !string.IsNullOrWhiteSpace(reference.ResolvedPath))
                .Select(reference => (Reference: reference, Path: Path.GetFullPath(reference.ResolvedPath!)))
                .ToArray();
            if (!string.Equals(rootPath, handoffOwnerPath, StringComparison.OrdinalIgnoreCase)
                && !references.Any(item => string.Equals(item.Path, handoffOwnerPath, StringComparison.OrdinalIgnoreCase)))
                return await FailedAndDisposeAsync(new ResultError(NavigationErrorCodes.TargetMismatch,
                    "The symbol handoff is not owned by the selected assembly or its current reference snapshot.",
                    "Use a handoff returned by this assembly's find_symbol(includeReferences=true) result."), "$.symbolIdentifier").ConfigureAwait(false);

            var declarationId = DocumentationCommentId.CreateDeclarationId(handoff.Symbol);
            if (string.IsNullOrWhiteSpace(declarationId) || handoff.Symbol.ContainingAssembly is null)
                return await FailedAndDisposeAsync(new ResultError(NavigationErrorCodes.InvalidArgument,
                    "The selected handoff has no stable assembly declaration identity.",
                    "Use a declaration returned by find_symbol(includeReferences=true)."), "$.symbolIdentifier").ConfigureAwait(false);

            var ownerPaths = new List<string> { rootPath };
            foreach (var item in references.OrderBy(item => item.Reference.Depth).ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (IsFrameworkPath(item.Path) || ownerPaths.Contains(item.Path, StringComparer.OrdinalIgnoreCase)) continue;
                ownerPaths.Add(item.Path);
            }
            var ownerLimitReached = ownerPaths.Count > MaxAssemblies;
            if (ownerLimitReached)
            {
                ownerPaths = ownerPaths.Take(MaxAssemblies).ToList();
                if (!ownerPaths.Contains(handoffOwnerPath, StringComparer.OrdinalIgnoreCase))
                {
                    ownerPaths[^1] = handoffOwnerPath;
                    ownerPaths = ownerPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
            }

            var owners = new List<AssemblyReferenceClosureOwner>();
            var hasFailedOwners = false;
            foreach (var ownerPath in ownerPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AssemblyNavigationSessionScope ownerScope;
                if (string.Equals(ownerPath, rootPath, StringComparison.OrdinalIgnoreCase)) ownerScope = root;
                else
                {
                    var opened = await AssemblyNavigationSessionScope.OpenAsync(ownerPath, cancellationToken).ConfigureAwait(false);
                    if (!opened.IsSuccess)
                    {
                        hasFailedOwners = true;
                        continue;
                    }
                    ownerScope = opened.Value!;
                    scopes.Add(ownerScope);
                }
                if (ownerScope.Context.Status is not AssemblySessionStatus.Complete) hasFailedOwners = true;
                owners.Add(new(ownerPath, ownerScope));
            }

            if (!owners.Any(owner => string.Equals(owner.TargetPath, handoffOwnerPath, StringComparison.OrdinalIgnoreCase)))
                return await FailedAndDisposeAsync(new ResultError(NavigationErrorCodes.TargetUnreadable,
                    "The handoff owner could not be opened in the current reference snapshot.",
                    "Refresh the assembly query and retry with a current owner target path."), "$.targetPath").ConfigureAwait(false);

            var session = new AssemblyReferenceClosureSession(root, handoff, owners, scopes, rootPath,
                handoffOwnerPath, declarationId, ownerLimitReached, hasFailedOwners,
                root.Context.References.Any(reference => !reference.Resolved));
            root = null!;
            handoff = null;
            scopes = [];
            return new(session, null, null);
        }
        catch
        {
            foreach (var scope in scopes) await scope.DisposeAsync().ConfigureAwait(false);
            if (handoff is not null) await handoff.DisposeAsync().ConfigureAwait(false);
            await root.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        async Task<AssemblyReferenceClosureSessionOpenResult> FailedAndDisposeAsync(ResultError error, string field)
        {
            foreach (var scope in scopes) await scope.DisposeAsync().ConfigureAwait(false);
            if (handoff is not null) await handoff.DisposeAsync().ConfigureAwait(false);
            await root.DisposeAsync().ConfigureAwait(false);
            return Failed(error, field);
        }
    }

    internal ISymbol? ResolveDeclaration(AssemblyReferenceClosureOwner owner, string originalOwnerPath,
        string declarationId, AssemblyIdentityDto originalIdentity)
    {
        if (string.Equals(owner.TargetPath, originalOwnerPath, StringComparison.OrdinalIgnoreCase)
            && AssemblyIdentityDtosMatch(owner.Scope.Context.Identity, originalIdentity))
            return ResolveOwnedSourceSymbol(owner, declarationId);
        return ResolveMetadataSymbol(declarationId, originalIdentity, owner.Scope.Context.Compilation);
    }

    private static bool AssemblyIdentityDtosMatch(AssemblyIdentityDto? left, AssemblyIdentityDto right) => left is not null
        &&
        string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Version, right.Version, StringComparison.Ordinal)
        && string.Equals(string.IsNullOrWhiteSpace(left.Culture) ? "neutral" : left.Culture,
            string.IsNullOrWhiteSpace(right.Culture) ? "neutral" : right.Culture, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.PublicKeyToken, right.PublicKeyToken, StringComparison.OrdinalIgnoreCase);

    internal AssemblyReferenceClosureOwnerSymbol? ResolveInternalSourceHandoff(string? internalIdentifier)
    {
        if (string.IsNullOrWhiteSpace(internalIdentifier)
            || !SymbolHandoffIdentifier.TryParse(internalIdentifier, out var identifier)
            || identifier.Origin != SymbolHandoffOrigin.Assembly)
            return null;

        var owners = new List<AssemblyReferenceClosureOwnerSymbol>();
        foreach (var owner in Owners)
        {
            var matches = DocumentationCommentId.GetSymbolsForDeclarationId(identifier.DocumentationCommentId,
                    owner.Scope.Context.Compilation)
                .Where(symbol => SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, owner.Scope.Context.Assembly)
                    && HasSourceDeclaration(symbol, owner.Scope.Solution, owner.Scope.Context.DecompiledProjectPaths?.DecompiledSourceRoot)
                    && string.Equals(owner.HandoffIdentity.FormatHandoff(symbol), internalIdentifier, StringComparison.Ordinal))
                .Distinct(SymbolEqualityComparer.Default)
                .Take(2)
                .ToArray();
            if (matches.Length == 1)
                owners.Add(new(owner, identifier.DocumentationCommentId, matches[0]));
        }
        return owners.Count == 1 ? owners[0] : null;
    }

    internal Func<ISymbol, string?> CreateInternalFormatter(AssemblyReferenceClosureOwner owner) => symbol =>
    {
        if (!HasSourceDeclaration(symbol, owner.Scope.Solution, owner.Scope.Context.DecompiledProjectPaths?.DecompiledSourceRoot)) return null;
        var declarationId = DocumentationCommentId.CreateDeclarationId(symbol);
        if (string.IsNullOrWhiteSpace(declarationId)) return null;
        var matches = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, owner.Scope.Context.Compilation)
            .Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, owner.Scope.Context.Assembly))
            .Distinct(SymbolEqualityComparer.Default)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? owner.HandoffIdentity.FormatHandoff(matches[0]) : null;
    };

    internal static string? Externalize(string? internalIdentifier) => string.IsNullOrWhiteSpace(internalIdentifier)
        ? null
        : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalIdentifier);

    internal static bool IdentityMatches(AssemblyIdentity actual, AssemblyIdentityDto expected) =>
        string.Equals(actual.Name, expected.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(actual.Version?.ToString(), expected.Version, StringComparison.Ordinal)
        && string.Equals(string.IsNullOrWhiteSpace(actual.CultureName) ? "neutral" : actual.CultureName,
            string.IsNullOrWhiteSpace(expected.Culture) ? "neutral" : expected.Culture, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Convert.ToHexString(actual.PublicKeyToken.ToArray()), expected.PublicKeyToken, StringComparison.OrdinalIgnoreCase);

    internal static bool HasSourceDeclaration(ISymbol symbol, Solution solution, string? sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot)) return false;
        var root = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return symbol.Locations.Any(location => location.IsInSource && location.SourceTree is { FilePath.Length: > 0 } tree
            && Path.GetFullPath(tree.FilePath).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && solution.GetDocument(tree) is not null);
    }

    private static ISymbol? ResolveOwnedSourceSymbol(AssemblyReferenceClosureOwner owner, string declarationId)
    {
        var matches = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, owner.Scope.Context.Compilation)
            .Where(symbol => SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, owner.Scope.Context.Assembly)
                && HasSourceDeclaration(symbol, owner.Scope.Solution, owner.Scope.Context.DecompiledProjectPaths?.DecompiledSourceRoot))
            .Distinct(SymbolEqualityComparer.Default).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static ISymbol? ResolveMetadataSymbol(string declarationId, AssemblyIdentityDto expected, Compilation compilation)
    {
        var matches = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, compilation)
            .Where(symbol => symbol.ContainingAssembly is { } assembly && IdentityMatches(assembly.Identity, expected))
            .Distinct(SymbolEqualityComparer.Default).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool IsFrameworkPath(string path) => path.Contains("\\shared\\Microsoft.NETCore.App\\", StringComparison.OrdinalIgnoreCase)
        || path.Contains("\\packs\\Microsoft.NETCore.App.Ref\\", StringComparison.OrdinalIgnoreCase);

    private static AssemblyReferenceClosureSessionOpenResult Failed(ResultError error, string field) => new(null, error, field);

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        foreach (var scope in ownedScopes) await scope.DisposeAsync().ConfigureAwait(false);
        await handoffAccess.DisposeAsync().ConfigureAwait(false);
        await rootScope.DisposeAsync().ConfigureAwait(false);
    }
}
