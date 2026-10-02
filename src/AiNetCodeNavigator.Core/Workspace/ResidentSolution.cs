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
    private readonly string? solutionPath;
    private Solution? currentSolution;
    private Microsoft.CodeAnalysis.Workspace? currentWorkspace;
    private string? structureFingerprint;
    private SolutionStructureInputs? structureInputs;
    private ResidentSolutionLoadError? loadFailure;
    private int disposed;

    /// <summary>
    /// Creates a synchronous resident instance from an already loaded snapshot.
    /// </summary>
    public ResidentSolution(Solution solution, Microsoft.CodeAnalysis.Workspace? workspace = null, string? solutionPath = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        currentSolution = solution;
        currentWorkspace = workspace;
        this.solutionPath = solutionPath;
        currentSolution = InitializeFileStates(solution);
        if (!string.IsNullOrEmpty(solutionPath))
        {
            structureFingerprint = SolutionStructureFingerprint.Create(solution, solutionPath);
        }
    }

    /// <summary>
    /// Creates an asynchronous resident instance with background loading.
    /// </summary>
    public ResidentSolution(Func<CancellationToken, Task<Solution?>> loadFunc)
        : this(async ct =>
        {
            var sol = await loadFunc(ct).ConfigureAwait(false);
            return sol is not null ? new ResidentLoadedState(sol, null) : null;
        })
    {
    }

    /// <summary>
    /// Creates an asynchronous resident instance with background loading and workspace ownership.
    /// </summary>
    public ResidentSolution(Func<CancellationToken, Task<ResidentLoadedState?>> loadFunc, string? solutionPath = null)
    {
        ArgumentNullException.ThrowIfNull(loadFunc);
        this.solutionPath = solutionPath;
        loadTask = Task.Run(async () =>
        {
            try
            {
                var result = await loadFunc(loadCancellation.Token).ConfigureAwait(false);
                if (result is not null)
                {
                    lock (syncLock)
                    {
                        currentSolution = InitializeFileStates(result.Solution);
                        currentWorkspace = result.Workspace;
                        structureInputs = result.StructureInputs;
                        if (!string.IsNullOrEmpty(this.solutionPath))
                        {
                            structureFingerprint = SolutionStructureFingerprint.Create(result.Solution, this.solutionPath, structureInputs);
                        }

                        loadFailure = null;
                    }

                    return result;
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                lock (syncLock)
                {
                    loadFailure = CreateLoadError(ex);
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
                lock (syncLock)
                {
                    try
                    {
                        RefreshStalenessUnderLock();
                        loadFailure = null;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        loadFailure = CreateLoadError(exception);
                        return new ResidentSolutionSnapshot(null, loadFailure);
                    }

                    return new ResidentSolutionSnapshot(currentSolution, loadFailure);
                }
            }

            if (current is null)
            {
                if (!await TryReloadAsync(solutionPath, refreshToken).ConfigureAwait(false))
                {
                    return new ResidentSolutionSnapshot(null, LoadFailure);
                }
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
                        && !await TryReloadAsync(solutionPath, refreshToken).ConfigureAwait(false))
                    {
                        return new ResidentSolutionSnapshot(null, LoadFailure);
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

            lock (syncLock)
            {
                try
                {
                    RefreshStalenessUnderLock();
                    loadFailure = null;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    loadFailure = CreateLoadError(exception);
                    return new ResidentSolutionSnapshot(null, loadFailure);
                }

                return new ResidentSolutionSnapshot(currentSolution, loadFailure);
            }
        }
        finally
        {
            reloadGate.Release();
        }
    }

    private async Task<bool> TryReloadAsync(string path, CancellationToken cancellationToken)
    {
        Microsoft.CodeAnalysis.Workspace? newlyLoadedWorkspace = null;
        try
        {
            var loadedState = await MSBuildSolutionLoader.LoadResidentStateAsync(path, cancellationToken).ConfigureAwait(false);
            var newSolution = loadedState.Solution;
            var newWorkspace = loadedState.Workspace;
            newlyLoadedWorkspace = newWorkspace;
            var newStructureFingerprint = SolutionStructureFingerprint.Create(newSolution, path, loadedState.StructureInputs);
            Microsoft.CodeAnalysis.Workspace? oldWorkspace;
            lock (syncLock)
            {
                oldWorkspace = currentWorkspace;
                currentSolution = InitializeFileStates(newSolution);
                currentWorkspace = newWorkspace;
                structureInputs = loadedState.StructureInputs;
                newlyLoadedWorkspace = null;
                structureFingerprint = newStructureFingerprint;
                loadFailure = null;
            }

            oldWorkspace?.Dispose();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            newlyLoadedWorkspace?.Dispose();
            SetLoadFailure(ex);
            return false;
        }
    }

    private ResidentSolutionLoadError CreateLoadError(Exception exception) => new(
        ProjectErrorCodes.ProjectLoadFailed,
        solutionPath,
        $"Unable to load solution '{solutionPath ?? "unknown path"}': {exception.Message}",
        Retryable: true);

    private void SetLoadFailure(Exception exception)
    {
        lock (syncLock)
        {
            loadFailure = CreateLoadError(exception);
        }
    }

    private Solution InitializeFileStates(Solution solution)
    {
        var initializedFileStates = new Dictionary<string, DocumentFileState>(StringComparer.OrdinalIgnoreCase);
        var initialized = solution;
        var documentsByPath = solution.Projects
            .SelectMany(project => project.Documents)
            .Where(document => !string.IsNullOrEmpty(document.FilePath))
            .GroupBy(document => document.FilePath!, StringComparer.OrdinalIgnoreCase);

        foreach (var documents in documentsByPath)
        {
            var path = documents.Key;
            if (File.Exists(path))
            {
                var file = ReadFileSnapshot(path);
                initializedFileStates[path] = new DocumentFileState(file.MtimeUtc, file.Hash);
                foreach (var document in documents)
                {
                    initialized = initialized.WithDocumentText(document.Id, file.Text);
                }
            }
        }

        fileStates.Clear();
        foreach (var (path, state) in initializedFileStates)
        {
            fileStates[path] = state;
        }

        return initialized;
    }

    private void RefreshStalenessUnderLock()
    {
        if (currentSolution is null)
        {
            return;
        }

        var updated = currentSolution;
        var updatedFileStates = new Dictionary<string, DocumentFileState>(fileStates, StringComparer.OrdinalIgnoreCase);

        var documentsByPath = updated.Projects
            .SelectMany(project => project.Documents)
            .Where(document => !string.IsNullOrEmpty(document.FilePath))
            .GroupBy(document => document.FilePath!, StringComparer.OrdinalIgnoreCase);

        foreach (var documents in documentsByPath)
        {
            var path = documents.Key;
            if (!File.Exists(path))
            {
                foreach (var document in documents)
                {
                    updated = updated.RemoveDocument(document.Id);
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
                updated = updated.WithDocumentText(document.Id, file.Text);
            }

            updatedFileStates[path] = new DocumentFileState(file.MtimeUtc, file.Hash);
        }

        currentSolution = updated;
        fileStates.Clear();
        foreach (var (path, state) in updatedFileStates)
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
            }

            workspace?.Dispose();
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
            }

            workspace?.Dispose();
        }
        finally
        {
            reloadGate.Release();
            reloadGate.Dispose();
        }

        loadCancellation.Dispose();
    }
}
