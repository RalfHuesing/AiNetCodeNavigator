#nullable enable

namespace AiNetCodeNavigator.Core.Caching;

using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.CodeAnalysis;

/// <summary>
/// Statistics for cache queries.
/// </summary>
public sealed record CacheStatistics(long Hits, long Misses, int CachedTreesCount, int CachedCompilationsCount);

/// <summary>
/// Cache entry for a parsed Roslyn syntax tree.
/// </summary>
public sealed record CachedTreeEntry(DateTime LastWriteTimeUtc, string? ContentHash, SyntaxTree Tree);

/// <summary>
/// Fingerprint for every semantic input that can affect a Roslyn compilation: source tree paths and contents,
/// per-tree parse options, assembly identity and compilation options, project reference identities and versions,
/// and metadata reference identities, versions, properties, and content. Each field must represent its complete
/// input category.
/// </summary>
public sealed record CompilationInputFingerprint(
    string SourceTreesHash,
    string ParseOptionsHash,
    string CompilationOptionsHash,
    string ProjectReferencesHash,
    string MetadataReferencesHash);

/// <summary>
/// Cached Roslyn compilation and the complete fingerprint used to validate its inputs.
/// </summary>
public sealed record CachedCompilationEntry(
    DateTime LatestSourceMTimeUtc,
    CompilationInputFingerprint Fingerprint,
    Compilation Compilation);

/// <summary>
/// Thread-safe in-memory, timestamp-based cache for Roslyn syntax trees and compilations.
/// Enables fast incremental queries without unnecessary reparsing of unchanged files.
/// </summary>
public sealed class CompilationCacheManager
{
    private readonly ConcurrentDictionary<string, CachedTreeEntry> _treeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedCompilationEntry> _compilationCache = new(StringComparer.OrdinalIgnoreCase);

    private long _hits;
    private long _misses;

    /// <summary>
    /// Retrieves current cache statistics.
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
    /// Tries to retrieve a cached SyntaxTree by file path, timestamp and content hash.
    /// An entry with a hash therefore cannot be reused by a query without a hash.
    /// </summary>
    public bool TryGetTree(string filePath, DateTime lastWriteTimeUtc, out SyntaxTree? tree, string? contentHash = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ValidateUtcTimestamp(lastWriteTimeUtc, nameof(lastWriteTimeUtc));

        if (_treeCache.TryGetValue(filePath, out var entry)
            && entry.LastWriteTimeUtc == lastWriteTimeUtc
            && string.Equals(entry.ContentHash, contentHash, StringComparison.Ordinal))
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
    /// Stores a parsed SyntaxTree in the cache.
    /// </summary>
    public void StoreTree(string filePath, DateTime lastWriteTimeUtc, string? contentHash, SyntaxTree tree)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ValidateUtcTimestamp(lastWriteTimeUtc, nameof(lastWriteTimeUtc));
        ArgumentNullException.ThrowIfNull(tree);

        _treeCache[filePath] = new CachedTreeEntry(lastWriteTimeUtc, contentHash, tree);
    }

    /// <summary>
    /// Tries to retrieve a cached compilation by project path, latest file timestamp and all
    /// semantically relevant compilation inputs.
    /// </summary>
    public bool TryGetCompilation(
        string projectPath,
        DateTime latestSourceMTimeUtc,
        CompilationInputFingerprint fingerprint,
        out Compilation? compilation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ValidateUtcTimestamp(latestSourceMTimeUtc, nameof(latestSourceMTimeUtc));
        ValidateFingerprint(fingerprint, nameof(fingerprint));

        if (_compilationCache.TryGetValue(projectPath, out var entry)
            && entry.LatestSourceMTimeUtc == latestSourceMTimeUtc
            && entry.Fingerprint == fingerprint)
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
    /// Stores a compilation in the cache.
    /// </summary>
    public void StoreCompilation(
        string projectPath,
        DateTime latestSourceMTimeUtc,
        CompilationInputFingerprint fingerprint,
        Compilation compilation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ValidateUtcTimestamp(latestSourceMTimeUtc, nameof(latestSourceMTimeUtc));
        ValidateFingerprint(fingerprint, nameof(fingerprint));
        ArgumentNullException.ThrowIfNull(compilation);

        _compilationCache[projectPath] = new CachedCompilationEntry(latestSourceMTimeUtc, fingerprint, compilation);
    }

    /// <summary>
    /// Invalidates the cache for a single file.
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
    /// Invalidates the cache for a project.
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
    /// Clears the entire cache and resets the counters.
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

    private static void ValidateFingerprint(CompilationInputFingerprint fingerprint, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(fingerprint, parameterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint.SourceTreesHash, $"{parameterName}.{nameof(fingerprint.SourceTreesHash)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint.ParseOptionsHash, $"{parameterName}.{nameof(fingerprint.ParseOptionsHash)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint.CompilationOptionsHash, $"{parameterName}.{nameof(fingerprint.CompilationOptionsHash)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint.ProjectReferencesHash, $"{parameterName}.{nameof(fingerprint.ProjectReferencesHash)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint.MetadataReferencesHash, $"{parameterName}.{nameof(fingerprint.MetadataReferencesHash)}");
    }
}
