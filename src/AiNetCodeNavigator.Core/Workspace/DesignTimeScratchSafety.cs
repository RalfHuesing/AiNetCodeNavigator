using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Serilog;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>Owns the path, link and ownership checks required for deleting Navigator scratch output.</summary>
internal static class DesignTimeScratchSafety
{
    internal const string MarkerFileName = ".navigator-scratch.json";
    private const string Owner = "AiNetCodeNavigator.DesignTimeScratch";

    private sealed record OwnershipMarker(int Version, string Owner, string Root, string RelativePath, string Scope);

    internal static bool TryInitializeDirectory(string root, string path)
    {
        EnsureSafePath(root, path);
        var scope = GetDirectoryScope(root, path);
        Directory.CreateDirectory(path);
        if (HasMarker(root, path, scope)) return true;
        // Never adopt existing unmarked contents, even if their names match our layout.
        if (Directory.EnumerateFileSystemEntries(path).Any()) return false;
        WriteMarker(Path.Combine(path, MarkerFileName), CreateMarker(root, path, scope));
        return true;
    }

    internal static bool IsOwnedDirectory(string root, string path)
    {
        EnsureSafePath(root, path);
        return HasMarker(root, path, GetDirectoryScope(root, path));
    }

    internal static FileStream AcquireProcessOwnership(string root, int processId, FileShare sharing)
    {
        var path = DesignTimeScratchMaintenance.OwnerFilePath(root, processId);
        EnsureSafePath(root, path);
        var expected = CreateMarker(root, path, "process-lock");
        FileStream stream;
        bool created;
        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, sharing);
            created = true;
        }
        catch (IOException) when (File.Exists(path))
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, sharing);
            created = false;
        }
        try
        {
            if (created)
            {
                JsonSerializer.Serialize(stream, expected);
                stream.Flush();
            }
            else if (stream.Length > 4096 || JsonSerializer.Deserialize<OwnershipMarker>(stream) != expected)
            {
                throw new IOException($"Scratch ownership file has an invalid marker: '{path}'.");
            }
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal static bool TryDeleteOwnedDirectory(string root, string path, CancellationToken cancellationToken = default)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested) return false;
            EnsureSafePath(root, path);
            if (!Directory.Exists(path)) return true;
            var scope = GetDirectoryScope(root, path);
            if (!HasMarker(root, path, scope))
                throw new IOException($"Scratch directory has no valid ownership marker: '{path}'.");
            ValidateTree(root, path, scope == "process", cancellationToken);
            return DeleteContents(root, path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            Log.Warning(exception, "Scratch deletion refused or incomplete for {ScratchDirectory}.", path);
            return false;
        }
    }

    internal static void DeleteProcessOwnershipFile(string root, int processId)
    {
        var path = DesignTimeScratchMaintenance.OwnerFilePath(root, processId);
        EnsureSafePath(root, path);
        if (!File.Exists(path)) return;
        EnsureSafePath(root, path);
        var marker = ReadMarker(path);
        if (marker != CreateMarker(root, path, "process-lock"))
            throw new IOException($"Scratch ownership file has an invalid marker: '{path}'.");
        File.Delete(path);
    }

    internal static void EnsureSafePath(string root, string path)
    {
        var fullRoot = Normalize(root);
        var fullPath = Normalize(path);
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        if (relative == "." || Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new IOException($"Scratch operation must target a strict descendant of '{fullRoot}': '{fullPath}'.");
        // Inspect every existing component, including the scratch root and its ancestors.
        for (var current = fullPath; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Scratch path contains a link or reparse point: '{current}'.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static string GetDirectoryScope(string root, string path)
    {
        var parts = Path.GetRelativePath(Normalize(root), Normalize(path)).Split(Path.DirectorySeparatorChar);
        if (parts.Length is < 1 or > 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var processId)
            || processId <= 0 || parts[0] != processId.ToString(CultureInfo.InvariantCulture)
            || (parts.Length == 2 && !Guid.TryParseExact(parts[1], "N", out _)))
            throw new IOException($"Invalid scratch directory layout: '{path}'.");
        return parts.Length == 1 ? "process" : "workspace";
    }

    private static bool HasMarker(string root, string path, string scope)
    {
        var markerPath = Path.Combine(path, MarkerFileName);
        EnsureSafePath(root, markerPath);
        return File.Exists(markerPath) && ReadMarker(markerPath) == CreateMarker(root, path, scope);
    }

    private static OwnershipMarker? ReadMarker(string path)
    {
        // Markers are tiny; refuse arbitrary large files before deserialization.
        if (new FileInfo(path).Length > 4096) return null;
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<OwnershipMarker>(stream);
    }

    private static OwnershipMarker CreateMarker(string root, string path, string scope) =>
        new(1, Owner, Normalize(root), Path.GetRelativePath(Normalize(root), Normalize(path)), scope);

    private static void WriteMarker(string path, OwnershipMarker marker)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, marker);
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static void ValidateTree(string root, string path, bool isProcessRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSafePath(root, path);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafePath(root, entry);
            if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
            {
                if (isProcessRoot && !IsOwnedDirectory(root, entry))
                    throw new IOException($"Process scratch contains an unowned directory: '{entry}'.");
                ValidateTree(root, entry, isProcessRoot: false, cancellationToken);
            }
            else if (isProcessRoot && Path.GetFileName(entry) != MarkerFileName)
            {
                throw new IOException($"Process scratch contains an unexpected file: '{entry}'.");
            }
        }
    }

    private static bool DeleteContents(string root, string path, CancellationToken cancellationToken)
    {
        EnsureSafePath(root, path);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if (cancellationToken.IsCancellationRequested) return false;
            if (Path.GetFileName(entry) == MarkerFileName) continue;
            EnsureSafePath(root, entry);
            if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
            {
                if (!DeleteContents(root, entry, cancellationToken)) return false;
            }
            else
            {
                File.Delete(entry);
            }
        }
        if (cancellationToken.IsCancellationRequested) return false;
        EnsureSafePath(root, path);
        // Keep markers until the contents are removed, so cancelled cleanup can safely resume.
        var marker = Path.Combine(path, MarkerFileName);
        EnsureSafePath(root, marker);
        if (File.Exists(marker)) File.Delete(marker);
        Directory.Delete(path); // Never delegate recursive traversal to an unchecked deletion call.
        return true;
    }
}
