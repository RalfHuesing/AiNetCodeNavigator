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
public sealed record ResidentLoadedState(Solution Solution, Microsoft.CodeAnalysis.Workspace? Workspace);

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
    private readonly Task<ResidentLoadedState?>? loadTask;
    private Solution? currentSolution;
    private Microsoft.CodeAnalysis.Workspace? currentWorkspace;
    private int disposed;

    /// <summary>
    /// Erzeugt eine synchrone residente Instanz aus einem bereits geladenen Snapshot.
    /// </summary>
    public ResidentSolution(Solution solution, Microsoft.CodeAnalysis.Workspace? workspace = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        currentSolution = solution;
        currentWorkspace = workspace;
        InitializeFileStates(solution);
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
    public ResidentSolution(Func<CancellationToken, Task<ResidentLoadedState?>> loadFunc)
    {
        ArgumentNullException.ThrowIfNull(loadFunc);
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
                        InitializeFileStates(result.Solution);
                    }

                    return result;
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
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
    /// Liefert die aktuelle Solution mit automatischer Staleness-Prüfung auf modifizierte oder gelöschte Dateien.
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

        currentWorkspace?.Dispose();
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

        currentWorkspace?.Dispose();
        loadCancellation.Dispose();
    }
}
