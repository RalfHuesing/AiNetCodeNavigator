#nullable enable

namespace AiNetCodeNavigator.Core.Caching;

using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.CodeAnalysis;

/// <summary>
/// Statistiken für Cache-Abfragen.
/// </summary>
public sealed record CacheStatistics(long Hits, long Misses, int CachedTreesCount, int CachedCompilationsCount);

/// <summary>
/// Cache-Eintrag für einen geparsten Roslyn-Syntaxbaum.
/// </summary>
public sealed record CachedTreeEntry(DateTime LastWriteTimeUtc, string? ContentHash, SyntaxTree Tree);

/// <summary>
/// Cache-Eintrag für eine Roslyn-Kompilation.
/// </summary>
public sealed record CachedCompilationEntry(DateTime LatestSourceMTimeUtc, Compilation Compilation, string? SourceHash = null);

/// <summary>
/// Thread-sicherer In-Memory- und Zeitstempel-basierter Cache für Roslyn-Syntaxbäume und Kompilationen.
/// Ermöglicht schnelle inkrementelle Abfragen ohne unnötiges Re-Parsing unveränderter Dateien.
/// </summary>
public sealed class CompilationCacheManager
{
    private readonly ConcurrentDictionary<string, CachedTreeEntry> _treeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedCompilationEntry> _compilationCache = new(StringComparer.OrdinalIgnoreCase);

    private long _hits;
    private long _misses;

    /// <summary>
    /// Aktuelle Cache-Statistiken abrufen.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics(
            Interlocked.Read(ref _hits),
            Interlocked.Read(ref _misses),
            _treeCache.Count,
            _compilationCache.Count);
    }

    /// <summary>
    /// Versucht, einen gecachten SyntaxTree anhand des Dateipfads und Zeitstempels abzurufen.
    /// Wenn ein Hash angegeben wird, muss auch dieser mit dem gespeicherten Inhaltshash übereinstimmen.
    /// </summary>
    public bool TryGetTree(string filePath, DateTime lastWriteTimeUtc, out SyntaxTree? tree, string? contentHash = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ValidateUtcTimestamp(lastWriteTimeUtc, nameof(lastWriteTimeUtc));

        if (_treeCache.TryGetValue(filePath, out var entry)
            && entry.LastWriteTimeUtc == lastWriteTimeUtc
            && (contentHash is null || string.Equals(entry.ContentHash, contentHash, StringComparison.Ordinal)))
        {
            Interlocked.Increment(ref _hits);
            tree = entry.Tree;
            return true;
        }

        Interlocked.Increment(ref _misses);
        tree = null;
        return false;
    }

    /// <summary>
    /// Speichert einen geparsten SyntaxTree im Cache.
    /// </summary>
    public void StoreTree(string filePath, DateTime lastWriteTimeUtc, string? contentHash, SyntaxTree tree)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ValidateUtcTimestamp(lastWriteTimeUtc, nameof(lastWriteTimeUtc));
        ArgumentNullException.ThrowIfNull(tree);

        _treeCache[filePath] = new CachedTreeEntry(lastWriteTimeUtc, contentHash, tree);
    }

    /// <summary>
    /// Versucht, eine gecachte Kompilation anhand des Projektpfads und Zeitstempels der jüngsten Datei abzurufen.
    /// Wenn ein Quellenhash angegeben wird, muss auch dieser mit dem gespeicherten Hash übereinstimmen.
    /// </summary>
    public bool TryGetCompilation(
        string projectPath,
        DateTime latestSourceMTimeUtc,
        out Compilation? compilation,
        string? sourceHash = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ValidateUtcTimestamp(latestSourceMTimeUtc, nameof(latestSourceMTimeUtc));

        if (_compilationCache.TryGetValue(projectPath, out var entry)
            && entry.LatestSourceMTimeUtc == latestSourceMTimeUtc
            && (sourceHash is null || string.Equals(entry.SourceHash, sourceHash, StringComparison.Ordinal)))
        {
            Interlocked.Increment(ref _hits);
            compilation = entry.Compilation;
            return true;
        }

        Interlocked.Increment(ref _misses);
        compilation = null;
        return false;
    }

    /// <summary>
    /// Speichert eine Kompilation im Cache.
    /// </summary>
    public void StoreCompilation(
        string projectPath,
        DateTime latestSourceMTimeUtc,
        Compilation compilation,
        string? sourceHash = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ValidateUtcTimestamp(latestSourceMTimeUtc, nameof(latestSourceMTimeUtc));
        ArgumentNullException.ThrowIfNull(compilation);

        _compilationCache[projectPath] = new CachedCompilationEntry(latestSourceMTimeUtc, compilation, sourceHash);
    }

    /// <summary>
    /// Invalidiert den Cache für eine einzelne Datei.
    /// </summary>
    public bool InvalidateFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        return _treeCache.TryRemove(filePath, out _);
    }

    /// <summary>
    /// Invalidiert den Cache für ein Projekt.
    /// </summary>
    public bool InvalidateProject(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return false;
        }

        return _compilationCache.TryRemove(projectPath, out _);
    }

    /// <summary>
    /// Leert den gesamten Cache und setzt die Zähler zurück.
    /// </summary>
    public void Clear()
    {
        _treeCache.Clear();
        _compilationCache.Clear();
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
    }

    private static void ValidateUtcTimestamp(DateTime timestamp, string parameterName)
    {
        if (timestamp.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Cache timestamps must use UTC.", parameterName);
        }
    }
}
