using System.Text;

namespace AiNetCodeNavigator.AssemblyExport;

internal sealed class ExportDumpOwnership(ExportPlan plan)
{
    internal const string MarkerName = ".ainetcodenavigator-assembly-export";
    internal const string MarkerContent = "AiNetCodeNavigator.AssemblyExport:1\n";
    private string MarkerPath => Path.Combine(plan.OutputDirectory, MarkerName);

    internal void ValidateRootPreflight()
    {
        RejectReparseAncestors(plan.OutputDirectory);
        if (File.Exists(plan.OutputDirectory)) throw new InvalidOperationException("Output root is a file.");
        if (Directory.Exists(plan.OutputDirectory)) ValidateMarker();
        ValidateRunReportPath();
        ValidateLogPath();
    }

    internal void ValidateVariantLayout(IReadOnlyList<PlannedAssembly> group)
    {
        var legacyFlatChild = Path.Combine(plan.OutputDirectory, Path.GetFileName(group[0].SourcePath));
        RejectReparseAncestors(legacyFlatChild);
        if (File.Exists(legacyFlatChild))
            throw new InvalidOperationException($"Cannot place filename variants because a legacy flat child already exists: {legacyFlatChild}");
        if (!Directory.Exists(legacyFlatChild)) return;
        var variantNames = group.Select(item => Path.GetFileName(item.ChildPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = Directory.EnumerateFileSystemEntries(legacyFlatChild).ToArray();
        if (entries.Any(entry => !Directory.Exists(entry)
            || (!variantNames.Contains(Path.GetFileName(entry)) && !LooksLikeVariantPath(Path.GetFileName(entry)))))
            throw new InvalidOperationException($"Cannot place filename variants because a legacy flat child contains unowned content: {legacyFlatChild}");
    }

    internal void CreateOrValidateRoot()
    {
        ValidateRootPreflight();
        if (Directory.Exists(plan.OutputDirectory)) return;
        Directory.CreateDirectory(plan.OutputDirectory);
        using var stream = new FileStream(MarkerPath, FileMode.CreateNew, FileAccess.Write);
        stream.Write(Encoding.UTF8.GetBytes(MarkerContent));
    }

    internal void DeleteSelectedChild(string childPath)
    {
        ValidateRootPreflight();
        ValidateMarker();
        ValidateSelectedChild(childPath);
        if (Directory.Exists(childPath)) DeleteCheckedTree(Path.GetFullPath(childPath));
        else if (File.Exists(childPath)) throw new InvalidOperationException($"Selected child is a file: {childPath}");
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
        if (!string.Equals(Path.GetDirectoryName(canonical), plan.OutputDirectory, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(canonical).StartsWith(".assembly-export-stage-", StringComparison.Ordinal)
            || plan.Assemblies.Any(item => IsWithin(canonical, item.ChildPath)))
            throw new InvalidOperationException($"Invalid staging path: {path}");
        RejectReparseTree(canonical);
    }

    internal void ValidateRunReportPath()
    {
        var path = Path.Combine(plan.OutputDirectory, "last-run.json");
        RejectReparseAncestors(path);
        if (Directory.Exists(path)) throw new InvalidOperationException("Run report path is a directory.");
    }

    internal void ValidateLogPath()
    {
        var path = Path.Combine(plan.OutputDirectory, "last-run.log");
        RejectReparseAncestors(path);
        if (Directory.Exists(path)) throw new InvalidOperationException("Run log path is a directory.");
    }

    internal void AppendRunFailureIfSafe(string message, DateTimeOffset runStartedAt)
    {
        if (!Directory.Exists(plan.OutputDirectory)) return;
        ValidateRootPreflight();
        ValidateMarker();
        ValidateLogPath();
        var path = Path.Combine(plan.OutputDirectory, "last-run.log");
        if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < runStartedAt.UtcDateTime) return;
        RejectReparseAncestors(path);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.WriteLine(message);
    }

    internal void DeleteStaging(string path)
    {
        ValidateStagingPath(path);
        ValidateMarker();
        if (Directory.Exists(path)) DeleteCheckedTree(path);
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
        RejectReparseAncestors(MarkerPath);
        if (!File.Exists(MarkerPath) || !File.ReadAllBytes(MarkerPath).SequenceEqual(Encoding.UTF8.GetBytes(MarkerContent)))
            throw new InvalidOperationException($"Output root has no valid ownership marker: {plan.OutputDirectory}");
    }

    private void DeleteCheckedTree(string directory)
    {
        RejectReparseAncestors(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            ValidateMarker();
            if (!IsWithin(entry, directory)) throw new InvalidOperationException($"Cleanup escaped selected directory: {entry}");
            RejectReparseAncestors(entry);
            if (Directory.Exists(entry)) DeleteCheckedTree(entry);
            else File.Delete(entry);
        }
        RejectReparseAncestors(directory);
        Directory.Delete(directory, recursive: false);
    }

    internal static bool IsWithin(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

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

    internal static bool HasVariantChildren(string outputDirectory, string filename)
    {
        var group = Path.Combine(outputDirectory, filename);
        if (!Directory.Exists(group)) return false;
        // A redirected child is reported by per-child preflight, not by variant detection.
        try
        {
            RejectReparseAncestors(group);
            return Directory.EnumerateDirectories(group).Any(path => LooksLikeVariantPath(Path.GetFileName(path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool LooksLikeVariantPath(string name)
    {
        var separator = name.IndexOf('-');
        if (separator < 0 || name.Length - separator - 1 != 64) return false;
        var origin = name[..separator];
        return origin is "local" or "gac32" or "gac64" or "gacmsil"
            && name[(separator + 1)..].All(Uri.IsHexDigit);
    }
}
