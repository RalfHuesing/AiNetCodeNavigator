#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace AiNetCodeNavigator.Core.Assemblies;

internal static class AssemblyCacheCleanup
{
    internal static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Assembly-Cache-Cleanup failed: Kind={CleanupKind}, Path={Path}", "File", path);
        }
    }

    internal static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Assembly-Cache-Cleanup failed: Kind={CleanupKind}, Path={Path}", "Directory", directory);
        }
    }

    internal static void RetainGenerations(string entryDirectory, string currentGeneration)
    {
        try
        {
            if (!Directory.Exists(entryDirectory)) return;
            var generations = Directory.EnumerateDirectories(
                    entryDirectory,
                    AssemblyCacheContract.GenerationDirectoryPrefix + "*",
                    SearchOption.TopDirectoryOnly)
                .Where(path => AssemblyCacheContract.IsSafeGenerationName(Path.GetFileName(path)))
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .ThenByDescending(Path.GetFileName, StringComparer.Ordinal)
                .ToList();
            foreach (var staging in Directory.EnumerateDirectories(
                         entryDirectory,
                         AssemblyCacheContract.GenerationDirectoryPrefix + "*" + AssemblyCacheContract.StagingDirectorySuffix,
                         SearchOption.TopDirectoryOnly)
                     .Where(path => AssemblyCacheContract.IsSafeStagingName(Path.GetFileName(path))))
            {
                TryDeleteInactiveStagingDirectory(staging);
            }

            var retained = new HashSet<string>(StringComparer.Ordinal)
            {
                currentGeneration,
            };
            foreach (var generation in generations)
            {
                var name = Path.GetFileName(generation);
                if (retained.Contains(name))
                {
                    continue;
                }

                if (retained.Count < AssemblyCacheContract.MaxRetainedGenerations)
                {
                    retained.Add(name);
                    continue;
                }

                DeleteDirectory(generation);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warning(ex, "Assembly-Cache-Cleanup failed: Kind={CleanupKind}, Path={Path}", "Retention", entryDirectory);
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The owner lock is held in a using block while deleting the staging directory.")]
    private static void TryDeleteInactiveStagingDirectory(string stagingDirectory)
    {
        var ownerLockPath = AssemblyDecompilationCache.GetStagingOwnerLockPath(stagingDirectory);
        FileStream ownerLock;
        try
        {
            ownerLock = new FileStream(ownerLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The lock is held by a decompilation that still owns this staging directory.
            return;
        }

        using (ownerLock)
        {
            DeleteDirectory(stagingDirectory);
        }

        DeleteFile(ownerLockPath);
    }
}
