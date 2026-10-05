#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using AiNetCodeNavigator.Core.Symbols;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Cached file state for staleness detection.
/// </summary>
public readonly record struct DocumentFileState(DateTime MtimeUtc, string Hash);

/// <summary>
/// Internal state container for loaded solution and optional workspace.
/// </summary>
public sealed record ResidentLoadedState(Solution Solution, Microsoft.CodeAnalysis.Workspace? Workspace)
{
    internal SolutionStructureInputs? StructureInputs { get; init; }

    internal SourceIdentityValidatedInputs? IdentityInputs { get; init; }

    internal WorkspaceInputProvenance? InputProvenance { get; init; }
}

/// <summary>
/// Keeps the loaded Roslyn <see cref="Solution"/> resident in memory for its lifetime.
/// Supports background loading and lazy staleness detection (file changes on disk
/// are applied to all documents sharing the file path after a content-hash comparison and incorporated incrementally
/// through <see cref="Solution.WithDocumentText"/>).
/// </summary>
public sealed class ResidentSolution : IDisposable, IAsyncDisposable
{
    private readonly Lock syncLock = new();
    private readonly Dictionary<string, DocumentFileState> fileStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource loadCancellation = new();
    private readonly SemaphoreSlim reloadGate = new(1, 1);
    private readonly Task<ResidentLoadedState?>? loadTask;
    private Func<CancellationToken, Task<ResidentLoadedState?>>? loadFunc;
    private readonly string? solutionPath;
    private Solution? currentSolution;
    private Microsoft.CodeAnalysis.Workspace? currentWorkspace;
    private string? structureFingerprint;
    private SolutionStructureInputs? structureInputs;
    private SourceIdentityValidatedInputs? identityInputs;
    private WorkspaceInputProvenance? inputProvenance;
    private ResidentSolutionLoadError? loadFailure;
    private int disposed;

    internal MetadataReferenceImageCapture.MetadataImageCaptureObserver? ImageCaptureObserver { get; set; }

    /// <summary>
    /// Creates a synchronous resident instance from an already loaded snapshot.
    /// </summary>
    public ResidentSolution(Solution solution, Microsoft.CodeAnalysis.Workspace? workspace = null, string? solutionPath = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        currentSolution = solution;
        currentWorkspace = workspace;
        this.solutionPath = solutionPath;
        inputProvenance = WorkspaceInputProvenance.FindTestWorkspaceBuilderOutput(solution);
        try
        {
            var captured = MetadataReferenceImageCapture.Capture(solution, previousInputs: null, provenance: inputProvenance);
            identityInputs = captured.Inputs;
            currentSolution = InitializeFileStates(captured.Solution);
            inputProvenance = captured.Provenance?.CarryKnownTextChanges(captured.Solution, currentSolution);
            if (!string.IsNullOrEmpty(solutionPath))
            {
                structureFingerprint = SolutionStructureFingerprint.Create(currentSolution, solutionPath);
            }
        }
        catch (MetadataReferenceImageCapture.MetadataImageUnsupportedException exception)
        {
            currentSolution = InitializeFileStates(solution);
            loadFailure = CreateWorkspaceDiagnostic(exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            currentSolution = InitializeFileStates(solution);
            loadFailure = CreateLoadError(exception);
        }
    }

    /// <summary>
    /// Creates an asynchronous resident instance with background loading.
    /// </summary>
    public ResidentSolution(Func<CancellationToken, Task<Solution?>> loadFunc)
        : this(async ct =>
        {
            var sol = await loadFunc(ct).ConfigureAwait(false);
            return sol is not null
                ? new ResidentLoadedState(sol, null)
                {
                    InputProvenance = WorkspaceInputProvenance.FindTestWorkspaceBuilderOutput(sol),
                }
                : null;
        })
    {
    }

    /// <summary>
    /// Creates an asynchronous resident instance with background loading and workspace ownership.
    /// </summary>
    public ResidentSolution(Func<CancellationToken, Task<ResidentLoadedState?>> loadFunc, string? solutionPath = null)
    {
        ArgumentNullException.ThrowIfNull(loadFunc);
        this.loadFunc = loadFunc;
        this.solutionPath = solutionPath;
        loadTask = Task.Run(async () =>
        {
            ResidentLoadedState? uncommittedResult = null;
            try
            {
                var result = await loadFunc(loadCancellation.Token).ConfigureAwait(false);
                if (result is not null)
                {
                    uncommittedResult = result;
                    var captured = MetadataReferenceImageCapture.Capture(
                        result.Solution,
                        previousInputs: null,
                        cancellationToken: loadCancellation.Token,
                    provenance: result.InputProvenance,
                    observer: ImageCaptureObserver);
                    var (initializedSolution, initializedFileStates) = CreateInitializedFileStates(captured.Solution);
                    var initializedProvenance = captured.Provenance?.CarryKnownTextChanges(captured.Solution, initializedSolution);
                    var loaded = result with
                    {
                        Solution = initializedSolution,
                        IdentityInputs = captured.Inputs,
                        InputProvenance = initializedProvenance,
                    };
                    var newStructureFingerprint = string.IsNullOrEmpty(this.solutionPath)
                        ? null
                        : SolutionStructureFingerprint.Create(initializedSolution, this.solutionPath, loaded.StructureInputs);
                    lock (syncLock)
                    {
                        currentSolution = initializedSolution;
                        ReplaceFileStates(initializedFileStates);
                        currentWorkspace = loaded.Workspace;
                        structureInputs = loaded.StructureInputs;
                        identityInputs = loaded.IdentityInputs;
                        inputProvenance = initializedProvenance;
                        structureFingerprint = newStructureFingerprint;
                        loadFailure = null;
                    }

                    uncommittedResult = null;
                    return loaded;
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                MSBuildSolutionLoader.DisposeWorkspace(uncommittedResult?.Workspace);
                return null;
            }
            catch (Exception ex)
            {
                MSBuildSolutionLoader.DisposeWorkspace(uncommittedResult?.Workspace);
                lock (syncLock)
                {
                    loadFailure = CreateResidentError(ex);
                }

                return null;
            }
        });
    }

    public bool IsLoaded => currentSolution is not null;

    public ServerLoadState LoadState
    {
        get
        {
            if (loadTask is null)
            {
                return currentSolution is null ? ServerLoadState.LoadFailed : ServerLoadState.Loaded;
            }

            if (loadTask.IsCompletedSuccessfully)
            {
                return currentSolution is null ? ServerLoadState.LoadFailed : ServerLoadState.Loaded;
            }

            if (loadTask.IsFaulted || loadTask.IsCanceled)
            {
                return ServerLoadState.LoadFailed;
            }

            return ServerLoadState.Loading;
        }
    }

    public Task<ResidentLoadedState?>? LoadTask => loadTask;

    /// <summary>
    /// The last initial-load or reload failure. A later snapshot request retries the load.
    /// </summary>
    public ResidentSolutionLoadError? LoadFailure
    {
        get
        {
            lock (syncLock)
            {
                return loadFailure;
            }
        }
    }

    /// <summary>
    /// Returns the loaded solution with automatic checks for changed file contents.
    /// Use the asynchronous snapshot method for changes to files, projects or references.
    /// </summary>
    public Solution? GetCurrentSolution()
    {
        lock (syncLock)
        {
            if (currentSolution is null)
            {
                return null;
            }

            RefreshStalenessUnderLock();
            return currentSolution;
        }
    }

    /// <summary>
    /// Returns the latest solution snapshot, rebuilding it through MSBuildWorkspace when solution,
    /// project, or source-file structure has changed. Failed reloads retain the last good internal
    /// snapshot and return a structured error so callers can report it and retry on a later request.
    /// </summary>
    public async Task<ResidentSolutionSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, loadCancellation.Token);
        var refreshToken = linkedCancellation.Token;
        if (loadTask is not null)
        {
            try
            {
                await loadTask.WaitAsync(refreshToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (refreshToken.IsCancellationRequested)
            {
                throw;
            }
        }

        await reloadGate.WaitAsync(refreshToken).ConfigureAwait(false);
        try
        {
            var captureContext = new MetadataReferenceImageCapture.CaptureContext();
            Solution? current;
            string? expectedFingerprint;
            SolutionStructureInputs? inputs;
            lock (syncLock)
            {
                current = currentSolution;
                expectedFingerprint = structureFingerprint;
                inputs = structureInputs;
            }

            if (string.IsNullOrEmpty(solutionPath))
            {
                if (current is null)
                {
                    return new ResidentSolutionSnapshot(null, LoadFailure);
                }

                try
                {
                    if (!await RefreshMetadataReferencesAsync(refreshToken, captureContext).ConfigureAwait(false))
                    {
                        return new ResidentSolutionSnapshot(null, LoadFailure);
                    }

                    lock (syncLock)
                    {
                        loadFailure = null;

                        return CreateSnapshot(currentSolution, loadFailure, structureInputs?.ConfiguredTargetFrameworks);
                    }
                }
                catch (OperationCanceledException) when (refreshToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (MetadataReferenceImageCapture.MetadataImageUnsupportedException exception)
                {
                    SetResidentFailure(exception);
                    return new ResidentSolutionSnapshot(null, LoadFailure);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    SetLoadFailure(exception);
                    return new ResidentSolutionSnapshot(null, LoadFailure);
                }
            }

            var snapshotAlreadyRefreshed = false;
            if (current is null)
            {
                if (!await TryReloadAsync(solutionPath, refreshToken, captureContext: captureContext).ConfigureAwait(false))
                {
                    return new ResidentSolutionSnapshot(null, LoadFailure);
                }

                snapshotAlreadyRefreshed = true;
            }
            else
            {
                try
                {
                    var observedFingerprint = SolutionStructureFingerprint.Create(current, solutionPath, inputs);
                    var retryFailedLoad = LoadFailure is not null;
                    if ((retryFailedLoad
                            || inputs?.HasUnexpandedExpressions == true
                            || !StringComparer.Ordinal.Equals(expectedFingerprint, observedFingerprint))
                        && !await TryReloadAsync(solutionPath, refreshToken, captureContext: captureContext).ConfigureAwait(false))
                    {
                        return new ResidentSolutionSnapshot(null, LoadFailure);
                    }

                    if (retryFailedLoad
                        || inputs?.HasUnexpandedExpressions == true
                        || !StringComparer.Ordinal.Equals(expectedFingerprint, observedFingerprint))
                    {
                        snapshotAlreadyRefreshed = true;
                    }
                }
                catch (OperationCanceledException) when (refreshToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    SetLoadFailure(ex);
                    return new ResidentSolutionSnapshot(null, LoadFailure);
                }
            }

            try
            {
                if (!snapshotAlreadyRefreshed
                    && !await RefreshMetadataReferencesAsync(refreshToken, captureContext).ConfigureAwait(false))
                {
                    return new ResidentSolutionSnapshot(null, LoadFailure);
                }

                lock (syncLock)
                {
                    loadFailure = null;

                    return CreateSnapshot(currentSolution, loadFailure, structureInputs?.ConfiguredTargetFrameworks);
                }
            }
            catch (OperationCanceledException) when (refreshToken.IsCancellationRequested)
            {
                throw;
            }
            catch (MetadataReferenceImageCapture.MetadataImageUnsupportedException exception)
            {
                SetResidentFailure(exception);
                return new ResidentSolutionSnapshot(null, LoadFailure);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                SetLoadFailure(exception);
                return new ResidentSolutionSnapshot(null, LoadFailure);
            }
        }
        finally
        {
            reloadGate.Release();
        }
    }

    private async Task<bool> TryReloadAsync(
        string path,
        CancellationToken cancellationToken,
        int metadataCaptureAttempts = 3,
        MetadataReferenceImageCapture.CaptureContext? captureContext = null)
    {
        Microsoft.CodeAnalysis.Workspace? newlyLoadedWorkspace = null;
        try
        {
            var reloadFunc = loadFunc;
            var loadedState = reloadFunc is null
                ? await MSBuildSolutionLoader.LoadResidentStateAsync(path, cancellationToken).ConfigureAwait(false)
                : await reloadFunc(cancellationToken).ConfigureAwait(false);
            if (loadedState is null)
            {
                throw new MetadataReferenceImageCapture.MetadataImageUnsupportedException(
                    $"The resident solution creator returned no workspace for '{path}' during reload.");
            }

            newlyLoadedWorkspace = loadedState.Workspace;
            var captured = MetadataReferenceImageCapture.Capture(
                loadedState.Solution,
                previousInputs: null,
                cancellationToken: cancellationToken,
                maxAttempts: metadataCaptureAttempts,
                provenance: loadedState.InputProvenance,
                observer: ImageCaptureObserver,
                context: captureContext);
            var (initializedSolution, initializedFileStates) = CreateInitializedFileStates(captured.Solution);
            var refreshedFileStates = new Dictionary<string, DocumentFileState>(initializedFileStates, StringComparer.OrdinalIgnoreCase);
            var newSolution = RefreshStaleness(initializedSolution, refreshedFileStates);
            var refreshedProvenance = captured.Provenance?.CarryKnownTextChanges(captured.Solution, newSolution);
            var newWorkspace = loadedState.Workspace;
            var newStructureFingerprint = SolutionStructureFingerprint.Create(newSolution, path, loadedState.StructureInputs);
            Microsoft.CodeAnalysis.Workspace? oldWorkspace;
            lock (syncLock)
            {
                oldWorkspace = currentWorkspace;
                currentSolution = newSolution;
                ReplaceFileStates(refreshedFileStates);
                currentWorkspace = newWorkspace;
                structureInputs = loadedState.StructureInputs;
                identityInputs = captured.Inputs;
                inputProvenance = refreshedProvenance;
                newlyLoadedWorkspace = null;
                structureFingerprint = newStructureFingerprint;
                loadFailure = null;
            }

            MSBuildSolutionLoader.DisposeWorkspace(oldWorkspace);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetResidentFailure(ex);
            return false;
        }
        finally
        {
            MSBuildSolutionLoader.DisposeWorkspace(newlyLoadedWorkspace);
        }
    }

    private async Task<bool> RefreshMetadataReferencesAsync(
        CancellationToken cancellationToken,
        MetadataReferenceImageCapture.CaptureContext captureContext)
    {
        MetadataReferenceImageCapture.CapturedMetadataReferences captured;
        lock (syncLock)
        {
            if (currentSolution is null)
            {
                return true;
            }

            captured = MetadataReferenceImageCapture.Capture(
                currentSolution,
                identityInputs,
                cancellationToken,
                provenance: inputProvenance,
                observer: ImageCaptureObserver,
                context: captureContext);
            if (!captured.RequiresWorkspaceReload)
            {
                var refreshedFileStates = new Dictionary<string, DocumentFileState>(fileStates, StringComparer.OrdinalIgnoreCase);
                var refreshedSolution = RefreshStaleness(captured.Solution, refreshedFileStates);
                var refreshedProvenance = captured.Provenance?.CarryKnownTextChanges(captured.Solution, refreshedSolution);
                currentSolution = refreshedSolution;
                identityInputs = captured.Inputs;
                inputProvenance = refreshedProvenance;
                ReplaceFileStates(refreshedFileStates);
                return true;
            }
        }

        if (string.IsNullOrEmpty(solutionPath))
        {
            throw new MetadataReferenceImageCapture.MetadataImageUnsupportedException(
                "An analyzer or source-generator image changed in a resident solution without a reloadable owner.");
        }

        var remainingAttempts = 3 - captured.AttemptsUsed;
        if (remainingAttempts < 1)
        {
            throw new IOException("Metadata capture exhausted the three refresh attempts before the required workspace reload could be validated.");
        }

        return await TryReloadAsync(solutionPath, cancellationToken, remainingAttempts, captureContext).ConfigureAwait(false);
    }

    private ResidentSolutionSnapshot CreateSnapshot(
        Solution? solution,
        ResidentSolutionLoadError? error,
        IReadOnlyDictionary<string, ConfiguredTargetFrameworks>? frameworks) =>
        new(solution, error, frameworks) { IdentityInputs = solution is null ? null : identityInputs };

    private ResidentSolutionLoadError CreateWorkspaceDiagnostic(Exception exception) => new(
        NavigationErrorCodes.WorkspaceDiagnostic,
        solutionPath,
        exception.Message,
        Retryable: false) { DiagnosticDetails = exception.ToString() };

    private ResidentSolutionLoadError CreateResidentError(Exception exception) =>
        exception is MetadataReferenceImageCapture.MetadataImageUnsupportedException
            ? CreateWorkspaceDiagnostic(exception)
            : CreateLoadError(exception);

    private void SetResidentFailure(Exception exception)
    {
        lock (syncLock)
        {
            loadFailure = CreateResidentError(exception);
        }
    }

    private ResidentSolutionLoadError CreateLoadError(Exception exception) => new(
        ProjectErrorCodes.ProjectLoadFailed,
        solutionPath,
        $"Unable to load solution '{solutionPath ?? "unknown path"}': {exception.Message}",
        Retryable: true) { DiagnosticDetails = exception.ToString() };

    private void SetLoadFailure(Exception exception)
    {
        lock (syncLock)
        {
            loadFailure = CreateLoadError(exception);
        }
    }

    private Solution InitializeFileStates(Solution solution)
    {
        var (initialized, initializedFileStates) = CreateInitializedFileStates(solution);
        ReplaceFileStates(initializedFileStates);
        return initialized;
    }

    private static (Solution Solution, Dictionary<string, DocumentFileState> FileStates) CreateInitializedFileStates(Solution solution)
    {
        var initializedFileStates = new Dictionary<string, DocumentFileState>(StringComparer.OrdinalIgnoreCase);
        var initialized = solution;
        var documentsByPath = GetTrackedTextDocuments(solution)
            .Where(document => !string.IsNullOrEmpty(document.Document.FilePath))
            .GroupBy(document => document.Document.FilePath!, StringComparer.OrdinalIgnoreCase);

        foreach (var documents in documentsByPath)
        {
            var path = documents.Key;
            if (File.Exists(path))
            {
                var file = ReadFileSnapshot(path);
                initializedFileStates[path] = new DocumentFileState(file.MtimeUtc, file.Hash);
                foreach (var document in documents)
                {
                    initialized = WithText(initialized, document, file.Text);
                }
            }
        }

        return (initialized, initializedFileStates);
    }

    private void RefreshStalenessUnderLock()
    {
        if (currentSolution is null)
        {
            return;
        }

        var updatedFileStates = new Dictionary<string, DocumentFileState>(fileStates, StringComparer.OrdinalIgnoreCase);
        var updated = RefreshStaleness(currentSolution, updatedFileStates);
        currentSolution = updated;
        ReplaceFileStates(updatedFileStates);
    }

    private static Solution RefreshStaleness(Solution solution, Dictionary<string, DocumentFileState> updatedFileStates)
    {
        var updated = solution;
        var documentsByPath = GetTrackedTextDocuments(updated)
            .Where(document => !string.IsNullOrEmpty(document.Document.FilePath))
            .GroupBy(document => document.Document.FilePath!, StringComparer.OrdinalIgnoreCase);

        foreach (var documents in documentsByPath)
        {
            var path = documents.Key;
            if (!File.Exists(path))
            {
                foreach (var document in documents)
                {
                    updated = RemoveTextDocument(updated, document);
                }

                updatedFileStates.Remove(path);
                continue;
            }

            var file = ReadFileSnapshot(path);
            var hasPreviousState = updatedFileStates.TryGetValue(path, out var state);
            if (hasPreviousState && state.Hash == file.Hash)
            {
                if (state.MtimeUtc != file.MtimeUtc)
                {
                    updatedFileStates[path] = state with { MtimeUtc = file.MtimeUtc };
                }

                continue;
            }

            foreach (var document in documents)
            {
                updated = WithText(updated, document, file.Text);
            }

            updatedFileStates[path] = new DocumentFileState(file.MtimeUtc, file.Hash);
        }

        return updated;
    }

    private static IEnumerable<TrackedTextDocument> GetTrackedTextDocuments(Solution solution) =>
        solution.Projects.SelectMany(project =>
            project.Documents.Select(document => new TrackedTextDocument(document, TrackedTextDocumentKind.Source))
                .Concat(project.AdditionalDocuments.Select(document => new TrackedTextDocument(document, TrackedTextDocumentKind.Additional)))
                .Concat(project.AnalyzerConfigDocuments.Select(document => new TrackedTextDocument(document, TrackedTextDocumentKind.AnalyzerConfig))));

    private static Solution WithText(Solution solution, TrackedTextDocument document, SourceText text) => document.Kind switch
    {
        TrackedTextDocumentKind.Source => solution.WithDocumentText(document.Document.Id, text),
        TrackedTextDocumentKind.Additional => solution.WithAdditionalDocumentText(document.Document.Id, text),
        TrackedTextDocumentKind.AnalyzerConfig => solution.WithAnalyzerConfigDocumentText(document.Document.Id, text),
        _ => throw new ArgumentOutOfRangeException(nameof(document), document.Kind, "Unknown tracked text document kind."),
    };

    private static Solution RemoveTextDocument(Solution solution, TrackedTextDocument document) => document.Kind switch
    {
        TrackedTextDocumentKind.Source => solution.RemoveDocument(document.Document.Id),
        TrackedTextDocumentKind.Additional => solution.RemoveAdditionalDocument(document.Document.Id),
        TrackedTextDocumentKind.AnalyzerConfig => solution.RemoveAnalyzerConfigDocument(document.Document.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(document), document.Kind, "Unknown tracked text document kind."),
    };

    private enum TrackedTextDocumentKind
    {
        Source,
        Additional,
        AnalyzerConfig,
    }

    private readonly record struct TrackedTextDocument(TextDocument Document, TrackedTextDocumentKind Kind);

    private void ReplaceFileStates(IReadOnlyDictionary<string, DocumentFileState> replacement)
    {
        fileStates.Clear();
        foreach (var (path, state) in replacement)
        {
            fileStates[path] = state;
        }
    }

    private static FileSnapshot ReadFileSnapshot(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var textStream = new MemoryStream(bytes, writable: false);
        var text = SourceText.From(textStream);
        var mtime = File.GetLastWriteTimeUtc(path);
        return new FileSnapshot(mtime, hash, text);
    }

    private sealed record FileSnapshot(DateTime MtimeUtc, string Hash, SourceText Text);

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Workspace is managed by this class")]
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        loadCancellation.Cancel();
        try
        {
            loadTask?.GetAwaiter().GetResult();
        }
        catch
        {
        }

        reloadGate.Wait();
        try
        {
            Microsoft.CodeAnalysis.Workspace? workspace;
            lock (syncLock)
            {
                workspace = currentWorkspace;
                currentWorkspace = null;
                loadFunc = null;
            }

            MSBuildSolutionLoader.DisposeWorkspace(workspace);
        }
        finally
        {
            reloadGate.Release();
            reloadGate.Dispose();
        }

        loadCancellation.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await loadCancellation.CancelAsync().ConfigureAwait(false);
        if (loadTask is not null)
        {
            try
            {
                await loadTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        await reloadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Microsoft.CodeAnalysis.Workspace? workspace;
            lock (syncLock)
            {
                workspace = currentWorkspace;
                currentWorkspace = null;
                loadFunc = null;
            }

            MSBuildSolutionLoader.DisposeWorkspace(workspace);
        }
        finally
        {
            reloadGate.Release();
            reloadGate.Dispose();
        }

        loadCancellation.Dispose();
    }
}
