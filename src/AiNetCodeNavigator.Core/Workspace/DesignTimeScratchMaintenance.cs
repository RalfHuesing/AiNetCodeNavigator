using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Serilog;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>Reclaims abandoned analysis output without touching active process directories.</summary>
internal static class DesignTimeScratchMaintenance
{
    internal static string Root => Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator", "msbuild-analysis");
    internal static string OwnerFilePath(string root, int processId) => Path.Combine(root, $".owner-{processId}.lock");

    internal static void CleanupAbandonedDirectories(CancellationToken cancellationToken) =>
        CleanupAbandonedDirectories(Root, IsProcessAlive, cancellationToken);

    internal static void CleanupAbandonedDirectories(string root, Func<int, bool> isProcessAlive, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (var directory in Directory.GetDirectories(root))
            {
                if (cancellationToken.IsCancellationRequested) return;
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                    var name = Path.GetFileName(directory);
                    if (name.StartsWith("orphan-", StringComparison.Ordinal)
                        && Guid.TryParseExact(name[7..], "N", out _))
                    {
                        DeleteQuarantine(directory, cancellationToken);
                        continue;
                    }
                    if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var processId)
                        || processId <= 0 || isProcessAlive(processId)) continue;
                    // Legacy output has GUID-named workspace directories but no owner lock.
                    // Preserve unexpected content rather than assuming every numeric directory is ours.
                    if (Directory.EnumerateFileSystemEntries(directory).Any(entry =>
                            !Guid.TryParseExact(Path.GetFileName(entry), "N", out _))) continue;
                    var quarantine = Path.Combine(root, $"orphan-{Guid.NewGuid():N}");
                    var ownerFile = OwnerFilePath(root, processId);
                    using (var ownership = new FileStream(ownerFile,
                               FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Delete))
                    {
                        if (isProcessAlive(processId)) continue;
                        // Claim the exact directory before deleting, so PID reuse cannot redirect deletion to new output.
                        Directory.Move(directory, quarantine);
                    }
                    DeleteQuarantine(quarantine, cancellationToken);
                    File.Delete(ownerFile);
                }
                catch (IOException exception)
                {
                    Log.Warning(exception, "Could not reclaim design-time scratch directory {ScratchDirectory}; a later startup will retry.", directory);
                }
                catch (UnauthorizedAccessException exception)
                {
                    Log.Warning(exception, "Could not reclaim design-time scratch directory {ScratchDirectory}; a later startup will retry.", directory);
                }
            }
            foreach (var ownerFile in Directory.GetFiles(root, ".owner-*.lock"))
            {
                if (cancellationToken.IsCancellationRequested) return;
                var name = Path.GetFileName(ownerFile);
                if (!int.TryParse(name[7..^5], NumberStyles.None, CultureInfo.InvariantCulture, out var processId)
                    || processId <= 0
                    || Directory.Exists(Path.Combine(root, processId.ToString(CultureInfo.InvariantCulture)))
                    || isProcessAlive(processId)) continue;
                try
                {
                    using var ownership = new FileStream(ownerFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
                    if (!isProcessAlive(processId)) File.Delete(ownerFile);
                }
                catch (IOException exception)
                {
                    Log.Warning(exception, "Could not reclaim design-time scratch ownership file {OwnerFile}.", ownerFile);
                }
                catch (UnauthorizedAccessException exception)
                {
                    Log.Warning(exception, "Could not reclaim design-time scratch ownership file {OwnerFile}.", ownerFile);
                }
            }
        }
        catch (IOException exception)
        {
            Log.Warning(exception, "Could not enumerate design-time scratch directories in {ScratchRoot}.", root);
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "Could not enumerate design-time scratch directories in {ScratchRoot}.", root);
        }
    }

    private static void DeleteQuarantine(string path, CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if (cancellationToken.IsCancellationRequested) return;
            if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                Directory.Delete(entry, recursive: true);
            else
                File.Delete(entry);
        }
        if (!cancellationToken.IsCancellationRequested) Directory.Delete(path);
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return true; // Inaccessible process ownership is not evidence that deletion is safe.
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
