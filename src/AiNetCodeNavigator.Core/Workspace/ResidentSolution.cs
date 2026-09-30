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
/// Hält die geladene Roslyn-<see cref="Solution"/> über die Lebensdauer resident im Speicher.
/// Unterstützt Hintergrund-Laden und lazy Staleness-Erkennung (Dateiänderungen auf der Platte
/// werden nach einem Inhalts-Hashvergleich auf alle Dokumente des Dateipfads angewendet und inkrementell
/// über <see cref="Solution.WithDocumentText"/> übernommen).
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
    /// Erzeugt eine synchrone residente Instanz aus einem bereits geladenen Snapshot.
    /// </summary>
    public ResidentSolution(Solution solution, Microsoft.CodeAnalysis.Workspace? workspace = null, string? solutionPath = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        currentSolution = solution;
        currentWorkspace = workspace;
        this.solutionPath = solutionPath;
        InitializeFileStates(solution);
        if (!string.IsNullOrEmpty(solutionPath))
        {
            structureFingerprint = SolutionStructureFingerprint.Create(solution, solutionPath);
        }
    }

    /// <summary>
    /// Erzeugt eine asynchrone residente Instanz mit Hintergrund-Laden.
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
    /// Erzeugt eine asynchrone residente Instanz mit Hintergrund-Laden inkl. Workspace-Besitz.
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
                        currentSolution = result.Solution;
                        currentWorkspace = result.Workspace;
                        structureInputs = result.StructureInputs;
                        InitializeFileStates(result.Solution);
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
    /// Liefert die geladene Solution mit automatischer Prüfung auf geänderte Datei-Inhalte.
    /// Für Änderungen an Dateien, Projekten oder Referenzen die asynchrone Snapshot-Methode verwenden.
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
                    RefreshStalenessUnderLock();
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
                    if ((retryFailedLoad || !StringComparer.Ordinal.Equals(expectedFingerprint, observedFingerprint))
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
                RefreshStalenessUnderLock();
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
                currentSolution = newSolution;
                currentWorkspace = newWorkspace;
                structureInputs = loadedState.StructureInputs;
                newlyLoadedWorkspace = null;
                fileStates.Clear();
                InitializeFileStates(newSolution);
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

    private void InitializeFileStates(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var path = document.FilePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path) && !fileStates.ContainsKey(path))
                {
                    try
                    {
                        var mtime = File.GetLastWriteTimeUtc(path);
                        var hash = ComputeFileHash(path);
                        fileStates[path] = new DocumentFileState(mtime, hash);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }
    }

    private void RefreshStalenessUnderLock()
    {
        if (currentSolution is null)
        {
            return;
        }

        var updated = currentSolution;

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

                fileStates.Remove(path);
                continue;
            }

            try
            {
                var currentMtime = File.GetLastWriteTimeUtc(path);
                var hasPreviousState = fileStates.TryGetValue(path, out var state);
                var currentHash = ComputeFileHash(path);
                if (hasPreviousState && state.Hash == currentHash)
                {
                    if (state.MtimeUtc != currentMtime)
                    {
                        fileStates[path] = state with { MtimeUtc = currentMtime };
                    }

                    continue;
                }

                var text = SourceText.From(File.ReadAllText(path));
                foreach (var document in documents)
                {
                    updated = updated.WithDocumentText(document.Id, text);
                }

                fileStates[path] = new DocumentFileState(currentMtime, currentHash);
            }
            catch (IOException)
            {
            }
        }

        currentSolution = updated;
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

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
