using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
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
    internal AnalysisSymbolIdentity RootAnalysisIdentity => AnalysisSymbolIdentity.ForAssembly(
        rootScope.Context.Origin.CanonicalPath, rootScope.Context.Origin.ContentHash,
        rootScope.Context.Generation, rootScope.Context.ReferenceSnapshotHash);
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
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? afterRawDiscovery = null,
        Func<AssemblyNavigationSessionScope, CancellationToken, Task>? afterRootScopeOpened = null,
        Func<AssemblySymbolHandoffAccess, CancellationToken, Task>? afterHandoffResolved = null)
    {
        var normalizedIdentifier = InputNormalizer.NormalizeSymbolIdentifier(identifier);
        if (!InputNormalizer.HasOpaqueHandoffPrefix(normalizedIdentifier)
            && !normalizedIdentifier.StartsWith("i:", System.StringComparison.OrdinalIgnoreCase))
        {
            var rawResolution = await ResolveRawAcrossReferencesAsync(targetPath, normalizedIdentifier, cancellationToken).ConfigureAwait(false);
            if (!rawResolution.IsSuccess)
                return Failed(rawResolution.Error!.Value, "$.symbolIdentifier");
            if (afterRawDiscovery is not null)
                await afterRawDiscovery(cancellationToken).ConfigureAwait(false);
            return await OpenAsync(targetPath, rawResolution.HandoffId!, cancellationToken,
                afterRootScopeOpened: afterRootScopeOpened,
                afterHandoffResolved: afterHandoffResolved).ConfigureAwait(false);
        }

        var openedRoot = await AssemblyNavigationSessionScope.OpenAsync(targetPath, cancellationToken).ConfigureAwait(false);
        if (!openedRoot.IsSuccess) return Failed(openedRoot.Error!.Value, "$.targetPath");
        var root = openedRoot.Value!;
        AssemblySymbolHandoffAccess? handoff = null;
        var scopes = new List<AssemblyNavigationSessionScope>();
        try
        {
            if (afterRootScopeOpened is not null)
                await afterRootScopeOpened(root, cancellationToken).ConfigureAwait(false);
            var resolvedHandoff = await AssemblySymbolHandoffResolver.ResolveAsync(identifier, cancellationToken).ConfigureAwait(false);
            if (!resolvedHandoff.IsSuccess) return await FailedAndDisposeAsync(resolvedHandoff.Error!.Value, "$.symbolIdentifier").ConfigureAwait(false);
            handoff = resolvedHandoff.Value!;
            if (afterHandoffResolved is not null)
                await afterHandoffResolved(handoff, cancellationToken).ConfigureAwait(false);

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

            if (string.Equals(rootPath, handoffOwnerPath, StringComparison.OrdinalIgnoreCase)
                && (!string.Equals(root.Context.Origin.ContentHash, handoff.Origin.ContentHash, StringComparison.OrdinalIgnoreCase)
                    || root.Context.Generation != handoff.Generation
                    || !string.Equals(root.Context.ReferenceSnapshotHash, handoff.ReferenceSnapshotHash, StringComparison.OrdinalIgnoreCase)))
                return await FailedAndDisposeAsync(new ResultError(NavigationErrorCodes.StaleSnapshot,
                    "The root assembly changed between root-scope and handoff acquisition.",
                    "Repeat the query so the root scope and symbol handoff share one assembly generation."), "$.targetPath").ConfigureAwait(false);

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
                if (string.Equals(ownerPath, handoffOwnerPath, StringComparison.OrdinalIgnoreCase)
                    && (!string.Equals(ownerScope.Context.Origin.ContentHash, handoff.Origin.ContentHash, StringComparison.OrdinalIgnoreCase)
                        || ownerScope.Context.Generation != handoff.Generation
                        || !string.Equals(ownerScope.Context.ReferenceSnapshotHash, handoff.ReferenceSnapshotHash, StringComparison.OrdinalIgnoreCase)))
                    return await FailedAndDisposeAsync(new ResultError(NavigationErrorCodes.StaleSnapshot,
                        "The handoff owner changed between handoff and closure-owner acquisition.",
                        "Repeat the query so the handoff and closure owner share one assembly generation."), "$.targetPath").ConfigureAwait(false);
                if (!string.Equals(ownerPath, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    var validation = AssemblyReferenceSnapshotValidator.ValidateOwner(
                        root.Context, ownerPath, ownerScope.Context, out var staleError);
                    if (validation == AssemblyReferenceSnapshotValidator.OwnerValidationStatus.Stale)
                        return await FailedAndDisposeAsync(staleError!.Value, "$.targetPath").ConfigureAwait(false);
                    if (validation == AssemblyReferenceSnapshotValidator.OwnerValidationStatus.Incomplete)
                        hasFailedOwners = true;
                }
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

    internal static async Task<AssemblySymbolInputResolution> ResolveRawAcrossReferencesAsync(
        string targetPath,
        string identifier,
        CancellationToken cancellationToken)
    {
        var openedRoot = await AssemblyNavigationSessionScope.OpenAsync(targetPath, cancellationToken).ConfigureAwait(false);
        if (!openedRoot.IsSuccess)
            return new(null, null, System.Array.Empty<AssemblySymbolInputCandidate>(), openedRoot.Error!.Value);

        var root = openedRoot.Value!;
        var scopes = new List<AssemblyNavigationSessionScope>();
        try
        {
            var rootPath = Path.GetFullPath(root.Context.Origin.CanonicalPath);
            var ownerOpenFailed = root.Context.Status is not AssemblySessionStatus.Complete;
            var ownerPaths = root.Context.References
                .Where(reference => reference.Resolved && !string.IsNullOrWhiteSpace(reference.ResolvedPath))
                .Select(reference => Path.GetFullPath(reference.ResolvedPath!))
                .Where(path => !IsFrameworkPath(path))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .Prepend(rootPath)
                .ToArray();
            var ownerLimitReached = ownerPaths.Length > MaxAssemblies;
            var unresolvedReferences = root.Context.References.Any(reference => !reference.Resolved);
            var selectedOwnerPaths = ownerPaths.Take(MaxAssemblies).ToArray();
            var candidates = new List<(AssemblyNavigationSessionScope Scope, ISymbol Symbol, SymbolResolutionCandidate Metadata)>();
            ResultError? resolutionError = null;
            foreach (var ownerPath in selectedOwnerPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AssemblyNavigationSessionScope scope;
                if (string.Equals(ownerPath, rootPath, System.StringComparison.OrdinalIgnoreCase)) scope = root;
                else
                {
                    var opened = await AssemblyNavigationSessionScope.OpenAsync(ownerPath, cancellationToken).ConfigureAwait(false);
                    if (!opened.IsSuccess)
                    {
                        ownerOpenFailed = true;
                        continue;
                    }
                    scope = opened.Value!;
                    scopes.Add(scope);
                }
                if (scope.Context.Status is not AssemblySessionStatus.Complete) ownerOpenFailed = true;
                if (!string.Equals(ownerPath, rootPath, System.StringComparison.OrdinalIgnoreCase))
                {
                    var validation = AssemblyReferenceSnapshotValidator.ValidateOwner(
                        root.Context, ownerPath, scope.Context, out var staleError);
                    if (validation == AssemblyReferenceSnapshotValidator.OwnerValidationStatus.Stale)
                        return new(null, null, System.Array.Empty<AssemblySymbolInputCandidate>(), staleError!.Value);
                    if (validation == AssemblyReferenceSnapshotValidator.OwnerValidationStatus.Incomplete)
                        return new(null, null, System.Array.Empty<AssemblySymbolInputCandidate>(), new ResultError(
                            NavigationErrorCodes.TargetUnreadable,
                            "The current reference snapshot reaches a boundary before raw symbol resolution can verify every owner.",
                            "Use a handoff returned by find_symbol(includeReferences=true) or resolve the missing reference boundary."));
                }

                var resolved = await SourceSymbolResolver.ResolveRawWithoutHandoffsAsync(
                    scope.Solution, identifier, cancellationToken).ConfigureAwait(false);
                foreach (var symbol in resolved.CandidateSymbols ?? System.Array.Empty<ISymbol>())
                {
                    if (!AssemblySymbolInputResolver.IsOwnedSourceSymbol(scope, symbol)) continue;
                    var metadata = SourceSymbolResolver.DescribeCandidate(symbol, scope.Solution);
                    if (metadata is not null) candidates.Add((scope, symbol, metadata));
                }
                if (resolved.CandidateSymbols is not { Count: > 0 }
                    && resolved.Error is { } error
                    && !string.Equals(error.Code, NavigationErrorCodes.SymbolNotFound, System.StringComparison.Ordinal))
                    resolutionError ??= error;
            }

            var distinctCandidates = candidates
                .GroupBy(candidate => (OwnerPath: Path.GetFullPath(candidate.Scope.Context.Origin.CanonicalPath),
                    ProjectId: candidate.Scope.Solution.GetDocument(candidate.Symbol.Locations.First(location => location.IsInSource).SourceTree!)?.Project.Id,
                    candidate.Metadata.DocCommentId), OwnerDocComparer.Instance)
                .Select(group => group.First())
                .OrderBy(candidate => candidate.Scope.Context.Origin.CanonicalPath, System.StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Metadata.FilePath, System.StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Metadata.Line)
                .ToArray();

            if (ownerLimitReached || ownerOpenFailed || unresolvedReferences || resolutionError is not null)
            {
                var message = ownerLimitReached
                    ? "The current reference snapshot exceeds the raw-resolution owner limit."
                    : ownerOpenFailed
                        ? "One or more referenced assemblies could not be opened for raw symbol resolution."
                        : unresolvedReferences
                            ? "The current reference snapshot contains unresolved assemblies."
                        : resolutionError!.Value.Message;
                var error = resolutionError ?? new ResultError(NavigationErrorCodes.TargetUnreadable, message,
                    "Resolve the incomplete reference snapshot before using a raw identifier; an owner-bound handoff avoids cross-owner name ambiguity.");
                return new(null, null, System.Array.Empty<AssemblySymbolInputCandidate>(), error);
            }

            var candidatesToExpose = distinctCandidates
                .Select(candidate => AssemblySymbolInputResolver.CreateCandidate(candidate.Scope, candidate.Symbol, candidate.Metadata))
                .Where(candidate => candidate is not null)
                .Cast<AssemblySymbolInputCandidate>()
                .ToArray();
            if (candidatesToExpose.Length == 1)
            {
                var candidate = candidatesToExpose[0];
                return new(candidate.Symbol, candidate.HandoffId, candidatesToExpose, null);
            }
            if (candidatesToExpose.Length > 1)
            {
                var choices = string.Join("; ", candidatesToExpose.Select(candidate =>
                    $"{candidate.Signature} at {candidate.FilePath}:{candidate.Line} [targetPath: '{candidate.OwnerTargetPath}', handoffId: `{candidate.HandoffId}`]"));
                return new(null, null, candidatesToExpose, new ResultError(NavigationErrorCodes.AmbiguousSymbol,
                    $"'{identifier}' matches declarations in multiple assemblies. Select a candidate by its owner targetPath and handoffId. {choices}"));
            }

            return new(null, null, System.Array.Empty<AssemblySymbolInputCandidate>(), resolutionError ?? new ResultError(NavigationErrorCodes.SymbolNotFound,
                $"No declaration matching '{identifier}' was found in the selected assembly or its current reference snapshot."));
        }
        finally
        {
            foreach (var scope in scopes) await scope.DisposeAsync().ConfigureAwait(false);
            await root.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class OwnerDocComparer : System.Collections.Generic.IEqualityComparer<(string OwnerPath, ProjectId? ProjectId, string? DocCommentId)>
    {
        internal static OwnerDocComparer Instance { get; } = new();
        public bool Equals((string OwnerPath, ProjectId? ProjectId, string? DocCommentId) x,
            (string OwnerPath, ProjectId? ProjectId, string? DocCommentId) y) =>
            string.Equals(x.OwnerPath, y.OwnerPath, System.StringComparison.OrdinalIgnoreCase)
            && Equals(x.ProjectId, y.ProjectId)
            && string.Equals(x.DocCommentId, y.DocCommentId, System.StringComparison.Ordinal);
        public int GetHashCode((string OwnerPath, ProjectId? ProjectId, string? DocCommentId) value) =>
            HashCode.Combine(System.StringComparer.OrdinalIgnoreCase.GetHashCode(value.OwnerPath), value.ProjectId,
                value.DocCommentId is null ? 0 : System.StringComparer.Ordinal.GetHashCode(value.DocCommentId));
    }

    internal ISymbol? ResolveDeclaration(AssemblyReferenceClosureOwner owner, string originalOwnerPath,
        string declarationId, AssemblyIdentityDto originalIdentity)
    {
        if (string.Equals(owner.TargetPath, originalOwnerPath, StringComparison.OrdinalIgnoreCase)
            && AssemblyIdentityMatcher.Matches(owner.Scope.Context.Identity, originalIdentity))
            return ResolveOwnedSourceSymbol(owner, declarationId);
        return ResolveMetadataSymbol(declarationId, originalIdentity, owner.Scope.Context.Compilation);
    }

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
        AssemblyIdentityMatcher.Matches(actual, expected);

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
