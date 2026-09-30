#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Assemblies;

internal sealed class AssemblyAnalysisSession : IDisposable, IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly AssemblyAnalysisSessionOptions sessionOptions;
    private readonly AssemblyDecompilationOptions decompilationOptions;
    private readonly AssemblyDecompilationCache cache;
    private readonly AssemblyReferenceResolver referenceResolver;
    private readonly AssemblyDecompilationAdapter decompilationAdapter;
    private readonly AssemblyRoslynWorkspaceFactory workspaceFactory;
    private readonly List<AssemblySessionGeneration> generations = [];
    private readonly TaskCompletionSource<object?> leasesDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AssemblySessionGeneration? current;
    private AssemblySessionState state;
    private long nextGeneration;
    private bool disposed;

    internal AssemblyAnalysisSession(string assemblyPath, AssemblyDecompilationOptions? options = null, string? cacheRoot = null)
        : this(new AssemblyAnalysisSessionOptions(
            assemblyPath,
            options ?? AssemblyDecompilationOptions.Default,
            AssemblyCacheContract.ResolveRootPath(cacheRoot)))
    {
    }

    internal AssemblyAnalysisSession(
        AssemblyAnalysisSessionOptions options,
        AssemblyDecompilationAdapter? decompilationAdapter = null,
        AssemblyRoslynWorkspaceFactory? workspaceFactory = null,
        AssemblyDecompilationCache? cache = null,
        AssemblyReferenceResolver? referenceResolver = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        sessionOptions = options;
        decompilationOptions = options.Decompilation ?? AssemblyDecompilationOptions.Default;
        this.cache = cache ?? new AssemblyDecompilationCache(options.CacheRoot ?? AssemblyCacheContract.ResolveRootPath(null));
        this.decompilationAdapter = decompilationAdapter ?? new();
        this.workspaceFactory = workspaceFactory ?? new();
        this.referenceResolver = referenceResolver ?? new();
        nextGeneration = options.GenerationStart;
        state = new AssemblySessionState(AssemblySessionStatus.Loading, null, null, null, [], DateTime.UtcNow);
    }

    internal AssemblySessionState State
    {
        get
        {
            lock (gate) return state;
        }
    }

    internal AssemblySessionGeneration? CurrentGeneration
    {
        get
        {
            lock (gate) return current;
        }
    }

    internal AssemblyAnalysisSnapshotLease? AcquireSnapshot()
    {
        lock (gate)
        {
            if (disposed || current is null) return null;
            current.ActiveLeaseCount++;
            return new AssemblyAnalysisSnapshotLease(this, current);
        }
    }

    internal async Task<AssemblySessionRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public void Dispose()
    {
        List<AssemblyRoslynSnapshot> snapshotsToDispose;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            current = null;
            state = state with { Status = AssemblySessionStatus.Failed, CurrentGeneration = null, UpdatedUtc = DateTime.UtcNow };
            snapshotsToDispose = generations
                .Where(generation => generation.ActiveLeaseCount == 0)
                .Select(generation => generation.Snapshot)
                .ToList();
            generations.RemoveAll(generation => generation.ActiveLeaseCount == 0);
            if (generations.Count == 0) leasesDrained.TrySetResult(null);
        }

        foreach (var snapshot in snapshotsToDispose) snapshot.Dispose();
        refreshGate.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
#pragma warning disable MA0042 // Dispose(): all cleanup is synchronous, no async dispose needed
        Dispose();
#pragma warning restore MA0042
        await leasesDrained.Task.ConfigureAwait(false);
    }

    private async Task<AssemblySessionRefreshResult> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (IsDisposed())
        {
            return FailureResultSingle(new(
                AssemblyDiagnosticCodes.For(nameof(AssemblyAnalysisSession), nameof(AssemblyAnalysisSession.Dispose)),
                "Die Assembly-Session wurde bereits beendet.",
                AssemblyDiagnosticSeverity.Error));
        }

        if (!ValidateOptions(out var optionsDiagnostic))
        {
            return FailureResultSingle(optionsDiagnostic!);
        }

        if (!AssemblyFingerprintCalculator.TryCreate(sessionOptions.AssemblyPath, out var fingerprint, out var fingerprintDiagnostic))
        {
            return FailureResultSingle(fingerprintDiagnostic!);
        }

        if (fingerprint is null)
        {
            return FailureResultSingle(new(
                AssemblyDiagnosticCodes.For(nameof(AssemblyFingerprintCalculator), nameof(AssemblyFingerprintCalculator.TryCreate)),
                "Die Assembly-Fingerprint konnte nicht erzeugt werden.",
                AssemblyDiagnosticSeverity.Error));
        }

        var references = referenceResolver.Resolve(fingerprint.CanonicalPath);
        if (references.Identity is null)
        {
            var metadataDiagnostic = references.Diagnostics.FirstOrDefault(
                diagnostic => diagnostic.Code == AssemblyDiagnosticCodes.MetadataMissing);
            return FailureResult(
                references.Diagnostics,
                new AssemblySessionFailure(
                    AssemblySessionFailureKind.MetadataUnavailable,
                    metadataDiagnostic
                        ?? references.Diagnostics.FirstOrDefault()
                        ?? new AssemblySessionDiagnostic(
                            AssemblyDiagnosticCodes.MetadataMissing,
                            "Assembly-Metadaten konnten nicht gelesen werden.",
                            AssemblyDiagnosticSeverity.Error)));
        }

        var referenceSnapshotHash = AssemblyReferenceSnapshotFingerprint.Create(references);
        if (TryReuseCurrent(fingerprint, referenceSnapshotHash, out var reused)) return reused;
        return await RefreshGenerationAsync(fingerprint, references, referenceSnapshotHash, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AssemblySessionRefreshResult> RefreshGenerationAsync(
        AssemblyFingerprint fingerprint,
        AssemblyReferenceResolution references,
        string referenceSnapshotHash,
        CancellationToken cancellationToken)
    {
        var key = AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, decompilationOptions);

        if (TryReadCache(key, fingerprint, references, out var cached, out var cacheDiagnostics) && cached is not null)
        {
            var status = ResolveManifestStatus(cached.Manifest.Status.Status, references.Diagnostics, ManifestDiagnostics(cached.Manifest));
            return await CreateAndInstallGenerationAsync(
                new AssemblyGenerationBuildRequest(
                    fingerprint,
                    key,
                    references,
                    cached.Documents,
                    status,
                    CombineDiagnostics(references.Diagnostics, ManifestDiagnostics(cached.Manifest)),
                    referenceSnapshotHash,
                    ProjectFilePath: cached.ProjectFilePath),
                cancellationToken).ConfigureAwait(false);
        }

        return await BuildFreshGenerationAsync(fingerprint, key, references, referenceSnapshotHash, cacheDiagnostics, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AssemblySessionRefreshResult> BuildFreshGenerationAsync(
        AssemblyFingerprint fingerprint,
        AssemblyDecompilationCacheKey key,
        AssemblyReferenceResolution references,
        string referenceSnapshotHash,
        IReadOnlyList<AssemblySessionDiagnostic> cacheDiagnostics,
        CancellationToken cancellationToken)
    {
        var stagingDirectory = cache.CreateStagingDirectory(key);
        try
        {
            var decompilation = await decompilationAdapter.DecompileAsync(
                new DecompilationRequest(fingerprint.CanonicalPath, fingerprint, key, decompilationOptions, cancellationToken, stagingDirectory),
                references).ConfigureAwait(false);
            var diagnostics = CombineDiagnostics(cacheDiagnostics, references.Diagnostics, decompilation.Diagnostics);
            if (!decompilation.IsComplete || decompilation.Documents.Count == 0)
            {
                return FailureResult(EnsureDiagnostic(diagnostics, AssemblyDiagnosticCodes.For(nameof(AssemblyAnalysisSession), nameof(DecompilationResult.Documents)), "Die Decompilation hat keine vollständige, analysierbare Generation erzeugt."));
            }

            var status = DetermineStatus(references.Diagnostics, decompilation);
            return await CreateAndInstallGenerationAsync(
                new AssemblyGenerationBuildRequest(
                    fingerprint,
                    key,
                    references,
                    decompilation.Documents,
                    status,
                    diagnostics,
                    referenceSnapshotHash,
                    new AssemblyCachePublishRequest(fingerprint, key, decompilationOptions, references, decompilation, status, stagingDirectory),
                    decompilation.ProjectFilePath),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            cache.DiscardStagingDirectory(stagingDirectory);
        }
    }

    private async Task<AssemblySessionRefreshResult> CreateAndInstallGenerationAsync(
        AssemblyGenerationBuildRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotResult = await CreateSnapshotAsync(request.Fingerprint, request.References, request.Documents, request.Status, request.ProjectFilePath, cancellationToken).ConfigureAwait(false);
        if (snapshotResult.Snapshot is null) return FailureResult(CombineDiagnostics(request.Diagnostics, snapshotResult.Diagnostics));
        var finalStatus = snapshotResult.Diagnostics.Count == 0 ? request.Status : AssemblySessionStatus.Partial;

        if (request.PublishRequest is not null)
        {
            var publish = await cache.PublishAsync(request.PublishRequest with { Status = finalStatus }, cancellationToken).ConfigureAwait(false);
            if (!publish.Succeeded)
            {
                snapshotResult.Snapshot.Dispose();
                return FailureResult(CombineDiagnostics(request.Diagnostics, OptionalDiagnostic(publish.Diagnostic)));
            }

            if (!cache.TryRead(
                    new AssemblyCacheReadRequest(request.Key, request.Fingerprint, request.References),
                    out var published,
                    out var cacheDiagnostic)
                || published is null)
            {
                snapshotResult.Snapshot.Dispose();
                return FailureResult(CombineDiagnostics(request.Diagnostics, OptionalDiagnostic(cacheDiagnostic)));
            }

            snapshotResult.Snapshot.Dispose();
            request = request with
            {
                Documents = published.Documents,
                ProjectFilePath = published.ProjectFilePath,
                Status = ResolveManifestStatus(published.Manifest.Status.Status, request.References.Diagnostics, ManifestDiagnostics(published.Manifest)),
                PublishRequest = null,
            };
            snapshotResult = await CreateSnapshotAsync(request.Fingerprint, request.References, request.Documents, request.Status, request.ProjectFilePath, cancellationToken).ConfigureAwait(false);
            if (snapshotResult.Snapshot is null) return FailureResult(CombineDiagnostics(request.Diagnostics, snapshotResult.Diagnostics));
            finalStatus = snapshotResult.Diagnostics.Count == 0 ? request.Status : AssemblySessionStatus.Partial;
        }

        var generation = new AssemblySessionGeneration(
            Interlocked.Increment(ref nextGeneration),
            request.Fingerprint,
            request.Key,
            request.References.Identity!,
            finalStatus,
            snapshotResult.Snapshot,
            request.References.References,
            CombineDiagnostics(request.Diagnostics, snapshotResult.Diagnostics),
            CreateGenerationOrigin(request.Fingerprint, request.Documents, finalStatus),
            DecompiledProjectPaths.Create(request.ProjectFilePath, request.Documents))
        {
            ReferenceSnapshotHash = request.ReferenceSnapshotHash,
        };

        return InstallGeneration(generation);
    }

#pragma warning disable CA2000 // Ownership of snapshot transferred to WorkspaceCreationResult / caller
    private async Task<WorkspaceCreationResult> CreateSnapshotAsync(
        AssemblyFingerprint fingerprint,
        AssemblyReferenceResolution references,
        IReadOnlyList<DecompiledDocument> documents,
        AssemblySessionStatus status,
        string? projectFilePath,
        CancellationToken cancellationToken)
    {
        AssemblyRoslynSnapshot? snapshot = null;
        try
        {
            snapshot = await workspaceFactory.CreateAsync(
                new AssemblyWorkspaceRequest(fingerprint.CanonicalPath, fingerprint, documents, references.MetadataReferences, status, projectFilePath),
                references.Identity!.Name,
                fingerprint.Sha256,
                cancellationToken).ConfigureAwait(false);
            var diagnostics = ValidateCompilation(snapshot.Compilation, cancellationToken);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == AssemblyDiagnosticSeverity.Error))
            {
                snapshot.Dispose();
                return new WorkspaceCreationResult(null, diagnostics);
            }

            return new WorkspaceCreationResult(snapshot, diagnostics);
        }
        catch (OperationCanceledException)
        {
            snapshot?.Dispose();
            throw;
        }
        catch (InvalidOperationException ex)
        {
            snapshot?.Dispose();
            return new WorkspaceCreationResult(null, [new(AssemblyDiagnosticCodes.For(nameof(AssemblyRoslynWorkspaceFactory), nameof(AssemblySessionStatus.Failed)), $"Roslyn-Snapshot konnte nicht erzeugt werden: {ex.Message}", AssemblyDiagnosticSeverity.Error)]);
        }
    }
#pragma warning restore CA2000

    private static IReadOnlyList<AssemblySessionDiagnostic> ValidateCompilation(Compilation compilation, CancellationToken cancellationToken)
    {
        var errors = compilation.GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Where(diagnostic => !AssemblyDiagnosticCodes.IsExpectedDeclarationOnlyDiagnostic(diagnostic.Id))
            .ToList();
        if (errors.Count == 0)
        {
            return [];
        }

        var syntaxErrors = compilation.SyntaxTrees
            .SelectMany(tree => tree.GetDiagnostics(cancellationToken))
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(5)
            .ToList();
        if (syntaxErrors.Count > 0)
        {
            return [new(
                AssemblyDiagnosticCodes.For(nameof(AssemblyRoslynWorkspaceFactory), nameof(AssemblyRoslynSnapshot.Compilation)),
                $"Die dekompilierte Compilation enthält nicht parsbaren Quelltext: {string.Join("; ", syntaxErrors.Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage()))}.",
                AssemblyDiagnosticSeverity.Warning)];
        }

        return [new(
            AssemblyDiagnosticCodes.For(nameof(AssemblyRoslynWorkspaceFactory), nameof(AssemblyRoslynSnapshot.Solution)),
            $"Die dekompilierte Compilation enthält {errors.Count} semantische Decompiler-/Referenzdiagnosen: {string.Join("; ", errors.Take(5).Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage()))}.",
            AssemblyDiagnosticSeverity.Warning)];
    }

    private AssemblySessionRefreshResult InstallGeneration(AssemblySessionGeneration generation)
    {
        AssemblyRoslynSnapshot? retiredSnapshot = null;
        lock (gate)
        {
            if (disposed)
            {
                generation.Snapshot.Dispose();
                var diagnostic = new AssemblySessionDiagnostic(AssemblyDiagnosticCodes.For(nameof(AssemblyAnalysisSession), nameof(AssemblyAnalysisSession.Dispose)), "Die Assembly-Session wurde während des Aufbaus beendet.", AssemblyDiagnosticSeverity.Error);
                state = state with
                {
                    Status = AssemblySessionStatus.Failed,
                    CurrentGeneration = null,
                    Diagnostics = [diagnostic],
                    UpdatedUtc = DateTime.UtcNow,
                };
                return new AssemblySessionRefreshResult(AssemblySessionStatus.Failed, null, false, [diagnostic]);
            }

            current = generation;
            generations.Add(generation);
            if (generations.Count > 1)
            {
                var previous = generations[^2];
                if (previous.ActiveLeaseCount == 0)
                {
                    generations.Remove(previous);
                    retiredSnapshot = previous.Snapshot;
                }
            }
            state = new AssemblySessionState(
                generation.Status,
                generation.Number,
                generation.Number,
                generation.Fingerprint,
                generation.Diagnostics,
                DateTime.UtcNow);
        }

        retiredSnapshot?.Dispose();
        return new AssemblySessionRefreshResult(generation.Status, generation.Number, false, generation.Diagnostics);
    }

    private bool TryReadCache(
        AssemblyDecompilationCacheKey key,
        AssemblyFingerprint fingerprint,
        AssemblyReferenceResolution references,
        out CachedDecompilationGeneration? generation,
        out IReadOnlyList<AssemblySessionDiagnostic> diagnostics)
    {
        if (cache.TryRead(new AssemblyCacheReadRequest(key, fingerprint, references), out generation, out var cacheDiagnostic) && generation is not null)
        {
            diagnostics = [];
            return true;
        }

        diagnostics = OptionalDiagnostic(cacheDiagnostic);
        return false;
    }

    private bool TryReuseCurrent(AssemblyFingerprint fingerprint, string referenceSnapshotHash, out AssemblySessionRefreshResult result)
    {
        lock (gate)
        {
            if (current is null
                || !string.Equals(current.Fingerprint.Sha256, fingerprint.Sha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(current.ReferenceSnapshotHash, referenceSnapshotHash, StringComparison.OrdinalIgnoreCase))
            {
                result = null!;
                return false;
            }

            state = new AssemblySessionState(
                current.Status,
                current.Number,
                current.Number,
                fingerprint,
                current.Diagnostics,
                DateTime.UtcNow);
            result = new AssemblySessionRefreshResult(current.Status, current.Number, true, current.Diagnostics);
            return true;
        }
    }

    private AssemblySessionRefreshResult FailureResultSingle(AssemblySessionDiagnostic diagnostic) => FailureResult([diagnostic]);

    private AssemblySessionRefreshResult FailureResult(
        IReadOnlyList<AssemblySessionDiagnostic> diagnostics,
        AssemblySessionFailure? failure = null)
    {
        lock (gate)
        {
            var status = current is null ? AssemblySessionStatus.Failed : AssemblySessionStatus.Degraded;
            var visible = DistinctDiagnostics(EnsureDiagnostic(diagnostics, AssemblyDiagnosticCodes.For(nameof(AssemblyAnalysisSession), nameof(AssemblySessionRefreshResult.Diagnostics)), "Assembly-Refresh konnte keinen neuen analysierbaren Snapshot erzeugen."));
            state = state with
            {
                Status = status,
                CurrentGeneration = current?.Number,
                LastGoodGeneration = current?.Number,
                Diagnostics = visible,
                UpdatedUtc = DateTime.UtcNow,
            };
            return new AssemblySessionRefreshResult(status, current?.Number, false, visible, failure);
        }
    }

    private bool IsDisposed()
    {
        lock (gate) return disposed;
    }

    internal void ReleaseSnapshot(AssemblySessionGeneration generation)
    {
        AssemblyRoslynSnapshot? snapshotToDispose = null;
        lock (gate)
        {
            if (generation.ActiveLeaseCount == 0) return;
            generation.ActiveLeaseCount--;
            if (generation.ActiveLeaseCount == 0
                && !ReferenceEquals(current, generation)
                && generations.Remove(generation))
            {
                snapshotToDispose = generation.Snapshot;
            }

            if (disposed && generations.Count == 0)
            {
                leasesDrained.TrySetResult(null);
            }
        }

        snapshotToDispose?.Dispose();
    }

    private bool ValidateOptions(out AssemblySessionDiagnostic? diagnostic)
    {
        if (decompilationOptions.EffectiveTimeout > TimeSpan.Zero
            && AssemblyDecompilationOptions.IsSupportedTimeout(decompilationOptions.EffectiveTimeout)
            && !string.IsNullOrWhiteSpace(decompilationOptions.DecompilerVersion)
            && !string.IsNullOrWhiteSpace(decompilationOptions.CacheSchemaVersion))
        {
            diagnostic = null;
            return true;
        }

        diagnostic = new(AssemblyDiagnosticCodes.For(nameof(AssemblyAnalysisSessionOptions), nameof(AssemblyAnalysisSessionOptions.CacheRoot)), "Die Assembly-Decompilation-Optionen enthalten ungültige Werte.", AssemblyDiagnosticSeverity.Error);
        return false;
    }

    private static AssemblySessionStatus DetermineStatus(IReadOnlyList<AssemblySessionDiagnostic> references, DecompilationResult decompilation) =>
        references.Count == 0 && decompilation.IsComplete && decompilation.Diagnostics.Count == 0 ? AssemblySessionStatus.Complete : AssemblySessionStatus.Partial;

    private static AssemblySessionStatus ResolveManifestStatus(
        string status,
        IReadOnlyList<AssemblySessionDiagnostic> referenceDiagnostics,
        IReadOnlyList<AssemblySessionDiagnostic> manifestDiagnostics) =>
        referenceDiagnostics.Count > 0 || manifestDiagnostics.Any(diagnostic => diagnostic.Severity == AssemblyDiagnosticSeverity.Error)
            ? AssemblySessionStatus.Partial
            : AssemblySessionStatusExtensions.TryParsePersisted(status, out var parsed) ? parsed : AssemblySessionStatus.Partial;

    private static IReadOnlyList<AssemblySessionDiagnostic> ManifestDiagnostics(AssemblyDecompilationManifest manifest) =>
        manifest.Diagnostics.Warnings.Select(message => new AssemblySessionDiagnostic(AssemblyDiagnosticCodes.For(nameof(AssemblyDecompilationManifest), nameof(AssemblyDecompilationManifest.Diagnostics)), message))
            .Concat(manifest.Diagnostics.Errors.Select(message => new AssemblySessionDiagnostic(AssemblyDiagnosticCodes.For(nameof(AssemblyDecompilationManifest), nameof(AssemblyDecompilationManifest.Status)), message, AssemblyDiagnosticSeverity.Error)))
            .ToList();

    private static IReadOnlyList<AssemblySessionDiagnostic> CombineDiagnostics(params IEnumerable<AssemblySessionDiagnostic>[] groups) =>
        DistinctDiagnostics(groups.SelectMany(group => group));

    private static IReadOnlyList<AssemblySessionDiagnostic> OptionalDiagnostic(AssemblySessionDiagnostic? diagnostic) => diagnostic is null ? [] : [diagnostic];

    private static IReadOnlyList<AssemblySessionDiagnostic> EnsureDiagnostic(IReadOnlyList<AssemblySessionDiagnostic> diagnostics, string code, string message) =>
        diagnostics.Count == 0 ? [new AssemblySessionDiagnostic(code, message, AssemblyDiagnosticSeverity.Error)] : diagnostics;

    private static IReadOnlyList<AssemblySessionDiagnostic> DistinctDiagnostics(IEnumerable<AssemblySessionDiagnostic> diagnostics) =>
        diagnostics.Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic.Message))
            .GroupBy(diagnostic => diagnostic.Code + "|" + diagnostic.Message, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(diagnostic => diagnostic.Severity == AssemblyDiagnosticSeverity.Error ? 0 : 1)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .Take(100)
            .ToList();

    private static AssemblyOrigin CreateGenerationOrigin(AssemblyFingerprint fingerprint, IReadOnlyList<DecompiledDocument> documents, AssemblySessionStatus status) =>
        new("decompiled", fingerprint.CanonicalPath, fingerprint.Sha256, documents.FirstOrDefault()?.GeneratedPath ?? string.Empty, status == AssemblySessionStatus.Complete ? "high" : "medium");

    private sealed record WorkspaceCreationResult(AssemblyRoslynSnapshot? Snapshot, IReadOnlyList<AssemblySessionDiagnostic> Diagnostics);

    private sealed record AssemblyGenerationBuildRequest(
        AssemblyFingerprint Fingerprint,
        AssemblyDecompilationCacheKey Key,
        AssemblyReferenceResolution References,
        IReadOnlyList<DecompiledDocument> Documents,
        AssemblySessionStatus Status,
        IReadOnlyList<AssemblySessionDiagnostic> Diagnostics,
        string ReferenceSnapshotHash,
        AssemblyCachePublishRequest? PublishRequest = null,
        string? ProjectFilePath = null);
}

internal sealed class AssemblyAnalysisSnapshotLease : IDisposable
{
    private readonly AssemblyAnalysisSession session;
    private readonly AssemblySessionGeneration generation;
    private int disposed;

    internal AssemblyAnalysisSnapshotLease(
        AssemblyAnalysisSession session,
        AssemblySessionGeneration generation)
    {
        this.session = session;
        this.generation = generation;
    }

    internal AssemblySessionGeneration Generation => generation;

    internal AssemblyRoslynSnapshot Snapshot => generation.Snapshot;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            session.ReleaseSnapshot(generation);
        }
    }
}
