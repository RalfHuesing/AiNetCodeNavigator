using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using Serilog;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>Reclaims abandoned, marked analysis output without touching active process directories.</summary>
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
            // A hypothetical child lets the same central check validate the root and all its ancestors.
            DesignTimeScratchSafety.EnsureSafePath(root, OwnerFilePath(root, 1));
            if (!Directory.Exists(root)) return;
            foreach (var directory in Directory.GetDirectories(root))
            {
                if (cancellationToken.IsCancellationRequested) return;
                var name = Path.GetFileName(directory);
                if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var processId)
                    || processId <= 0 || isProcessAlive(processId)) continue;
                try
                {
                    if (!DesignTimeScratchSafety.IsOwnedDirectory(root, directory)) continue;
                    bool removed;
                    using (var ownership = DesignTimeScratchSafety.AcquireProcessOwnership(root, processId, FileShare.Delete))
                    {
                        if (isProcessAlive(processId)) continue;
                        // Hold ownership through deletion so an instance with a reused PID cannot create new output.
                        removed = DesignTimeScratchSafety.TryDeleteOwnedDirectory(root, directory, cancellationToken);
                    }
                    if (removed) DesignTimeScratchSafety.DeleteProcessOwnershipFile(root, processId);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    Log.Warning(exception, "Could not reclaim owned design-time scratch directory {ScratchDirectory}.", directory);
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
                    using (var ownership = DesignTimeScratchSafety.AcquireProcessOwnership(root, processId, FileShare.Delete))
                    {
                        if (isProcessAlive(processId)) continue;
                    }
                    DesignTimeScratchSafety.DeleteProcessOwnershipFile(root, processId);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    Log.Warning(exception, "Could not reclaim design-time scratch ownership file {OwnerFile}.", ownerFile);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warning(exception, "Could not safely enumerate design-time scratch directories in {ScratchRoot}.", root);
        }
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
