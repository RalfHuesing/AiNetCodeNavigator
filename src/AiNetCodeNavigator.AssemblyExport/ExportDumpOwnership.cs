using System.Text;

namespace AiNetCodeNavigator.AssemblyExport;

internal sealed class ExportDumpOwnership(ExportPlan plan)
{
    internal const string MarkerName = ".ainetcodenavigator-assembly-export";
    internal const string MarkerContent = "AiNetCodeNavigator.AssemblyExport:1\n";
    internal const string TemporaryMarkerName = ".ainetcodenavigator-assembly-export-tmp";
    internal const string TemporaryMarkerContent = "AiNetCodeNavigator.AssemblyExport.Temporary:1\n";
    private string MarkerPath => Path.Combine(plan.OutputDirectory, MarkerName);
    private string TemporaryRoot => Path.Combine(plan.OutputDirectory, ".assembly-export-tmp");
    private string TemporaryMarkerPath => Path.Combine(TemporaryRoot, TemporaryMarkerName);

    internal IDisposable AcquireRunLock()
    {
        var canonicalOutput = Path.GetFullPath(plan.OutputDirectory);
        if (OperatingSystem.IsWindows()) canonicalOutput = canonicalOutput.ToUpperInvariant();
        var mutexName = "AiNetCodeNavigator.AssemblyExport." + Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonicalOutput)));
        return RunLockLease.Acquire(mutexName);
    }

    internal void ResetRoot()
    {
        RejectVolumeRoot(plan.OutputDirectory);
        RejectReparseAncestors(plan.OutputDirectory);
        if (File.Exists(plan.OutputDirectory)) throw new InvalidOperationException("Output root is a file.");
        if (Directory.Exists(plan.OutputDirectory))
        {
            ValidateMarker();
            DeleteMarkedDirectory(plan.OutputDirectory, MarkerName, MarkerContent);
        }
        Directory.CreateDirectory(plan.OutputDirectory);
        WriteMarker(MarkerPath, MarkerContent);
    }

    internal void CreateTemporaryRoot()
    {
        ValidateRootPreflight();
        ValidateMarker();
        if (Directory.Exists(TemporaryRoot) || File.Exists(TemporaryRoot))
        {
            ValidateTemporaryRoot();
            DeleteMarkedDirectory(TemporaryRoot, TemporaryMarkerName, TemporaryMarkerContent);
        }
        Directory.CreateDirectory(TemporaryRoot);
        WriteMarker(TemporaryMarkerPath, TemporaryMarkerContent);
    }

    internal string CreateStagingPath()
    {
        var path = Path.Combine(TemporaryRoot, ".assembly-export-stage-" + Guid.NewGuid().ToString("N"));
        ValidateStagingPath(path);
        return path;
    }

    internal void DeleteTemporaryRoot()
    {
        ValidateRootPreflight();
        ValidateMarker();
        if (File.Exists(TemporaryRoot)) throw new InvalidOperationException($"Temporary root is a file: {TemporaryRoot}");
        if (!Directory.Exists(TemporaryRoot)) return;
        ValidateTemporaryRoot();
        DeleteMarkedDirectory(TemporaryRoot, TemporaryMarkerName, TemporaryMarkerContent);
    }

    internal void ValidateRootPreflight()
    {
        RejectVolumeRoot(plan.OutputDirectory);
        RejectReparseAncestors(plan.OutputDirectory);
        if (File.Exists(plan.OutputDirectory)) throw new InvalidOperationException("Output root is a file.");
        if (Directory.Exists(plan.OutputDirectory)) ValidateMarker();
        ValidateLogPath();
    }

    internal void ValidateSelectedChild(string path)
    {
        var canonical = Path.GetFullPath(path);
        if (!plan.Assemblies.Any(item => item.ChildPath.Equals(canonical, StringComparison.OrdinalIgnoreCase))
            || !IsWithin(canonical, plan.OutputDirectory)
            || string.Equals(canonical, plan.OutputDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path is not a selected dump child: {path}");
        RejectReparseTree(canonical);
        if (File.Exists(canonical)) throw new InvalidOperationException($"Selected child is a file: {path}");
    }

    internal void ValidateStagingPath(string path)
    {
        var canonical = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(canonical);
        if (!string.Equals(parent, TemporaryRoot, StringComparison.OrdinalIgnoreCase)
            || !IsUuidStageName(Path.GetFileName(canonical))
            || plan.Assemblies.Any(item => IsWithin(canonical, item.ChildPath)))
            throw new InvalidOperationException($"Invalid staging path: {path}");
        ValidateRootPreflight();
        ValidateMarker();
        ValidateTemporaryRoot();
        RejectReparseTree(canonical);
    }

    internal void ValidateLogPath()
    {
        var path = Path.Combine(plan.OutputDirectory, "last-run.log");
        RejectReparseAncestors(path);
        if (Directory.Exists(path)) throw new InvalidOperationException("Run log path is a directory.");
    }

    internal void DeleteStaging(string path)
    {
        ValidateStagingPath(path);
        ValidateMarker();
        if (Directory.Exists(path)) DeleteCheckedTree(path, requireTemporaryMarker: true);
    }

    internal void PublishStaging(string stage, string child)
    {
        ValidateRootPreflight();
        ValidateMarker();
        ValidateSelectedChild(child);
        ValidateStagingPath(stage);
        if (Directory.Exists(child)) throw new InvalidOperationException("Selected child unexpectedly reappeared before publication.");
        var parent = Path.GetDirectoryName(child)!;
        RejectReparseAncestors(parent);
        Directory.CreateDirectory(parent);
        Directory.Move(stage, child);
    }

    private void ValidateMarker()
    {
        ValidateExactMarker(MarkerPath, MarkerContent);
    }

    private void ValidateTemporaryRoot()
    {
        ValidateRootPreflight();
        ValidateMarker();
        RejectReparseAncestors(TemporaryRoot);
        if (!Directory.Exists(TemporaryRoot) || File.Exists(TemporaryRoot))
            throw new InvalidOperationException($"Temporary root is missing or is not a directory: {TemporaryRoot}");
        ValidateExactMarker(TemporaryMarkerPath, TemporaryMarkerContent);
    }

    private static void ValidateExactMarker(string path, string content)
    {
        RejectReparseAncestors(path);
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.Directory) != 0
            || !File.ReadAllBytes(path).SequenceEqual(Encoding.UTF8.GetBytes(content)))
            throw new InvalidOperationException($"Directory has no valid ownership marker: {Path.GetDirectoryName(path)}");
    }

    private static void WriteMarker(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private void DeleteMarkedDirectory(string directory, string markerName, string markerContent)
    {
        var marker = Path.Combine(directory, markerName);
        ValidateExactMarker(marker, markerContent);
        RejectReparseTree(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            ValidateExactMarker(marker, markerContent);
            if (!IsWithin(entry, directory)) throw new InvalidOperationException($"Cleanup escaped owned directory: {entry}");
            if (string.Equals(entry, marker, StringComparison.OrdinalIgnoreCase)) continue;
            RejectReparseAncestors(entry);
            if (Directory.Exists(entry)) DeleteCheckedTree(entry, requireTemporaryMarker: markerName == TemporaryMarkerName);
            else File.Delete(entry);
        }
        ValidateExactMarker(marker, markerContent);
        File.Delete(marker);
        Directory.Delete(directory, recursive: false);
    }

    private static bool IsUuidStageName(string name)
    {
        const string prefix = ".assembly-export-stage-";
        return name.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParseExact(name[prefix.Length..], "N", out _);
    }

    private static void RejectVolumeRoot(string path)
    {
        var canonical = Path.GetFullPath(path);
        var root = Path.GetPathRoot(canonical);
        if (root is not null && string.Equals(Path.TrimEndingDirectorySeparator(canonical),
                Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Output root cannot be a volume root: {canonical}");
    }

    private void DeleteCheckedTree(string directory, bool requireTemporaryMarker = false)
    {
        RejectReparseAncestors(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            ValidateMarker();
            if (requireTemporaryMarker) ValidateTemporaryRoot();
            if (!IsWithin(entry, directory)) throw new InvalidOperationException($"Cleanup escaped selected directory: {entry}");
            RejectReparseAncestors(entry);
            if (Directory.Exists(entry)) DeleteCheckedTree(entry, requireTemporaryMarker);
            else File.Delete(entry);
        }
        if (requireTemporaryMarker) ValidateTemporaryRoot();
        RejectReparseAncestors(directory);
        Directory.Delete(directory, recursive: false);
    }

    internal static bool IsWithin(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private sealed class RunLockLease : IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        private readonly Thread _ownerThread;
        private bool _disposed;

        private RunLockLease(string mutexName, out bool acquired, out Exception? failure)
        {
            using var ready = new ManualResetEventSlim(false);
            var ownsMutex = false;
            Exception? lockFailure = null;
            _ownerThread = new Thread(() =>
            {
                try
                {
                    using var mutex = new Mutex(initiallyOwned: false, name: mutexName);
                    try { ownsMutex = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { ownsMutex = true; }
                    ready.Set();
                    if (!ownsMutex) return;
                    _release.Wait();
                    mutex.ReleaseMutex();
                }
                catch (Exception exception)
                {
                    lockFailure = exception;
                    ready.Set();
                }
            }) { IsBackground = true };
            _ownerThread.Start();
            ready.Wait();
            acquired = ownsMutex;
            failure = lockFailure;
            if (!acquired) _ownerThread.Join();
        }

        internal static IDisposable Acquire(string mutexName)
        {
            var lease = new RunLockLease(mutexName, out var acquired, out var failure);
            if (failure is not null)
            {
                lease.Dispose();
                throw new InvalidOperationException("Could not acquire the output run lock.", failure);
            }
            if (!acquired)
            {
                lease.Dispose();
                throw new InvalidOperationException("Another assembly export run already owns this output directory.");
            }
            return lease;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _release.Set();
            _ownerThread.Join();
            _release.Dispose();
        }
    }

    internal static void RejectReparseAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException($"Reparse point is unsafe for dump access: {current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void RejectReparseTree(string path)
    {
        RejectReparseAncestors(path);
        if (!Directory.Exists(path)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            RejectReparseAncestors(entry);
            if (Directory.Exists(entry)) RejectReparseTree(entry);
        }
    }

}
