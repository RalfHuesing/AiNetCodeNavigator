using System.Text;

namespace AiNetCodeNavigator.AssemblyExport;

internal sealed class ExportDumpOwnership(ExportPlan plan)
{
    internal const string MarkerName = ".ainetcodenavigator-assembly-export";
    internal const string MarkerContent = "AiNetCodeNavigator.AssemblyExport:1\n";
    private string MarkerPath => Path.Combine(plan.OutputDirectory, MarkerName);

    internal void ValidatePreflight()
    {
        RejectReparseAncestors(plan.OutputDirectory);
        if (File.Exists(plan.OutputDirectory)) throw new InvalidOperationException("Output root is a file.");
        if (Directory.Exists(plan.OutputDirectory)) ValidateMarker();
        ValidateRunReportPath();
        foreach (var assembly in plan.Assemblies) ValidateSelectedChild(assembly.ChildPath);
    }

    internal void CreateOrValidateRoot()
    {
        ValidatePreflight();
        if (Directory.Exists(plan.OutputDirectory)) return;
        Directory.CreateDirectory(plan.OutputDirectory);
        using var stream = new FileStream(MarkerPath, FileMode.CreateNew, FileAccess.Write);
        stream.Write(Encoding.UTF8.GetBytes(MarkerContent));
    }

    internal void DeleteSelectedChild(string childPath)
    {
        ValidatePreflight();
        ValidateMarker();
        ValidateSelectedChild(childPath);
        if (Directory.Exists(childPath)) DeleteCheckedTree(Path.GetFullPath(childPath));
        else if (File.Exists(childPath)) throw new InvalidOperationException($"Selected child is a file: {childPath}");
    }

    internal void ValidateSelectedChild(string path)
    {
        var canonical = Path.GetFullPath(path);
        if (!plan.Assemblies.Any(item => item.ChildPath.Equals(canonical, StringComparison.OrdinalIgnoreCase))
            || !string.Equals(Path.GetDirectoryName(canonical), plan.OutputDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path is not a selected direct dump child: {path}");
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

    internal void DeleteStaging(string path)
    {
        ValidateStagingPath(path);
        ValidateMarker();
        if (Directory.Exists(path)) DeleteCheckedTree(path);
    }

    internal void PublishStaging(string stage, string child)
    {
        ValidatePreflight();
        ValidateMarker();
        ValidateSelectedChild(child);
        ValidateStagingPath(stage);
        if (Directory.Exists(child)) throw new InvalidOperationException("Selected child unexpectedly reappeared before publication.");
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
}
