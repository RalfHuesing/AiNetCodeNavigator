#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AiNetCodeNavigator.Core.Common;

namespace AiNetCodeNavigator.Core.FileStructure;

/// <summary>
/// Schneller Scanner für die Datei- und Ordnerstruktur eines Workspaces oder einer Solution.
/// </summary>
public static class GetFileTreeScanner
{
    public static FileTreeScanResult Scan(FileTreeScanRequest request)
    {
        var targetDir = string.IsNullOrWhiteSpace(request.RelativeRoot) || request.RelativeRoot == "."
            ? request.RootDirectory
            : Path.GetFullPath(Path.Combine(request.RootDirectory, request.RelativeRoot));

        if (!Directory.Exists(targetDir))
        {
            return new FileTreeScanResult(
                RootPath: request.RelativeRoot,
                Entries: Array.Empty<FileTreeEntry>(),
                Summaries: Array.Empty<FileTreeSummaryEntry>(),
                TotalFiles: 0,
                TotalDirectories: 0,
                IsTruncated: false,
                FormattedText: $"Verzeichnis '{targetDir}' existiert nicht.");
        }

        var entries = new List<FileTreeEntry>();
        var summaryMap = new Dictionary<string, (int FileCount, long TotalBytes)>(StringComparer.OrdinalIgnoreCase);

        var totalFiles = 0;
        var totalDirs = 0;
        var maxDepth = request.MaxDepth ?? 3;

        WalkDirectory(targetDir, request.RootDirectory, 0, maxDepth, request, entries, summaryMap, ref totalFiles, ref totalDirs);

        var isTruncated = entries.Count > request.MaxResults;
        var shownEntries = entries.Take(request.MaxResults).ToList();

        var summaries = summaryMap
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new FileTreeSummaryEntry(kv.Key, kv.Value.FileCount, kv.Value.TotalBytes))
            .ToList();

        var formattedText = FormatOutput(request.View, request.RelativeRoot, shownEntries, summaries, isTruncated, totalFiles, request.MaxResults);

        return new FileTreeScanResult(
            RootPath: request.RelativeRoot,
            Entries: shownEntries,
            Summaries: summaries,
            TotalFiles: totalFiles,
            TotalDirectories: totalDirs,
            IsTruncated: isTruncated,
            FormattedText: formattedText);
    }

    private static void WalkDirectory(
        string currentDir,
        string rootDir,
        int currentDepth,
        int maxDepth,
        FileTreeScanRequest request,
        List<FileTreeEntry> entries,
        Dictionary<string, (int FileCount, long TotalBytes)> summaryMap,
        ref int totalFiles,
        ref int totalDirs)
    {
        var dirInfo = new DirectoryInfo(currentDir);
        if (FileTreeFilter.IsExcludedDirectory(dirInfo.Name)) return;

        var relDirPath = PathNormalizer.ToRelative(rootDir, currentDir);
        if (!string.IsNullOrEmpty(relDirPath) && !FileTreeFilter.MatchesFolderFilter(relDirPath, request.FolderFilter))
        {
            return;
        }

        totalDirs++;
        int dirFiles = 0;
        long dirBytes = 0;

        try
        {
            foreach (var file in dirInfo.GetFiles())
            {
                if (!FileTreeFilter.MatchesFileFilter(file.Name, request.FileFilter)) continue;

                totalFiles++;
                dirFiles++;
                dirBytes += file.Length;

                var relFilePath = PathNormalizer.ToRelative(rootDir, file.FullName);
                entries.Add(new FileTreeEntry(
                    RelativePath: relFilePath,
                    Name: file.Name,
                    IsDirectory: false,
                    Size: file.Length,
                    Extension: file.Extension));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // ignore inaccessible files
        }

        var summaryKey = string.IsNullOrEmpty(relDirPath) ? "." : relDirPath;
        summaryMap[summaryKey] = (dirFiles, dirBytes);

        if (currentDepth < maxDepth)
        {
            try
            {
                foreach (var subDir in dirInfo.GetDirectories())
                {
                    WalkDirectory(subDir.FullName, rootDir, currentDepth + 1, maxDepth, request, entries, summaryMap, ref totalFiles, ref totalDirs);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // ignore inaccessible directories
            }
        }
    }

    private static string FormatOutput(
        string view,
        string rootPath,
        IReadOnlyList<FileTreeEntry> entries,
        IReadOnlyList<FileTreeSummaryEntry> summaries,
        bool isTruncated,
        int totalFiles,
        int maxResults)
    {
        var sb = new StringBuilder();

        if (string.Equals(view, "summary", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"# File Summary for '{rootPath}'");
            sb.AppendLine("| Verzeichnis | Dateien | Gesamtgröße |");
            sb.AppendLine("| :--- | :---: | :---: |");
            foreach (var s in summaries)
            {
                var sizeKb = s.TotalSizeBytes / 1024.0;
                sb.AppendLine($"| {s.DirectoryPath} | {s.FileCount} | {sizeKb:F1} KB |");
            }
            return sb.ToString().TrimEnd();
        }

        if (string.Equals(view, "files", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"# Files ({entries.Count} von {totalFiles}):");
            foreach (var e in entries)
            {
                var sizeKb = e.Size / 1024.0;
                sb.AppendLine($"- {e.RelativePath} ({sizeKb:F1} KB)");
            }
            if (isTruncated)
            {
                sb.AppendLine($"... ({totalFiles - entries.Count} weitere Dateien abgeschnitten, maxResults={maxResults})");
            }
            return sb.ToString().TrimEnd();
        }

        // Tree view
        sb.AppendLine($"# File Tree: {rootPath}");
        foreach (var e in entries)
        {
            sb.AppendLine($"- {e.RelativePath}");
        }
        if (isTruncated)
        {
            sb.AppendLine($"... ({totalFiles - entries.Count} weitere abgeschnitten)");
        }

        return sb.ToString().TrimEnd();
    }
}
