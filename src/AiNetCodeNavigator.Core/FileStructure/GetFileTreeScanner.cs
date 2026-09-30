#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AiNetCodeNavigator.Core.Common;

namespace AiNetCodeNavigator.Core.FileStructure;

/// <summary>Scans a physical workspace tree without changing its contents.</summary>
public static class GetFileTreeScanner
{
    public const int MaxDepthCap = 32;
    public const int MaxResultsCap = 2_000;

    public static FileTreeScanResult Scan(FileTreeScanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RootDirectory))
        {
            return ErrorResult(request, "RootDirectory must be an absolute directory path.");
        }

        string analysisRoot;
        string targetDir;
        try
        {
            analysisRoot = Path.GetFullPath(request.RootDirectory);
            if (!Path.IsPathFullyQualified(analysisRoot))
            {
                return ErrorResult(request, "RootDirectory must be an absolute directory path.");
            }

            var relativeRoot = string.IsNullOrWhiteSpace(request.RelativeRoot) ? "." : request.RelativeRoot.Trim();
            if (Path.IsPathRooted(relativeRoot))
            {
                return ErrorResult(request, "RelativeRoot must be relative to RootDirectory.");
            }

            targetDir = Path.GetFullPath(Path.Combine(analysisRoot, relativeRoot));
            var relativeTarget = PathNormalizer.NormalizeSeparators(Path.GetRelativePath(analysisRoot, targetDir));
            if (relativeTarget == ".." || relativeTarget.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relativeTarget))
            {
                return ErrorResult(request, "RelativeRoot is outside RootDirectory.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return ErrorResult(request, $"RootDirectory and RelativeRoot must be valid paths: {ex.Message}");
        }

        var optionError = ValidateOptions(request);
        if (optionError is not null) return ErrorResult(request, optionError);
        if (!Directory.Exists(targetDir)) return ErrorResult(request, $"Directory '{targetDir}' was not found.");

        try
        {
            if (IsReparsePoint(targetDir)) return ErrorResult(request, "RelativeRoot is a reparse point and will not be traversed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ErrorResult(request, $"Directory '{targetDir}' could not be inspected: {ex.Message}");
        }

        var effectiveDepth = DetermineEffectiveWalkDepth(request);
        var directories = new Dictionary<string, DirectoryAggregate>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<FileTreeCandidate>();
        var warnings = new List<string>();
        var scanReasons = new HashSet<string>(StringComparer.Ordinal);
        var scannedFiles = 0;
        var scannedDirectories = 0;
        var totalBytes = 0L;
        var excludedFileCount = 0;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((targetDir, 0));
        var rootRelative = NormalizeRelative(Path.GetRelativePath(analysisRoot, targetDir));

        while (pending.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                scanReasons.Add("cancellation");
                break;
            }

            var (directory, depth) = pending.Pop();
            var directoryName = Path.GetFileName(directory);
            if (FileTreeFilter.IsExcludedDirectory(directoryName)) continue;

            try
            {
                if (IsReparsePoint(directory))
                {
                    warnings.Add($"{ToRelative(analysisRoot, directory)}: reparse point skipped.");
                    continue;
                }

                scannedDirectories++;
                EnsureDirectory(directories, ToRelative(analysisRoot, directory), rootRelative);
                foreach (var filePath in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        scanReasons.Add("cancellation");
                        break;
                    }

                    scannedFiles++;
                    var relativePath = ToRelative(analysisRoot, filePath);
                    if (!FileTreeFilter.Matches(filePath, relativePath, request))
                    {
                        if (FileTreeFilter.IsExcluded(relativePath, request.ExcludePatterns ?? [])) excludedFileCount++;
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(filePath);
                        var size = info.Length;
                        var lineCount = request.IncludeLineCount ? CountLines(filePath) : null;
                        var extension = NormalizeExtension(info.Extension);
                        matches.Add(new FileTreeCandidate(relativePath, extension, size, lineCount, GetDepth(relativePath, rootRelative)));
                        totalBytes += size;
                        AddFileToAncestors(directories, relativePath, rootRelative, size);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
                    {
                        warnings.Add($"{relativePath}: file metadata or requested line count could not be read ({ex.Message}).");
                    }
                }

                if (depth >= effectiveDepth)
                {
                    if (HasDirectories(directory, warnings)) scanReasons.Add("maxDepth");
                    continue;
                }

                foreach (var child in Directory.EnumerateDirectories(directory).OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(child);
                    if (FileTreeFilter.IsExcludedDirectory(name)) continue;
                    try
                    {
                        if (IsReparsePoint(child))
                        {
                            warnings.Add($"{ToRelative(analysisRoot, child)}: reparse point skipped.");
                            continue;
                        }
                        pending.Push((child, depth + 1));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        warnings.Add($"{ToRelative(analysisRoot, child)}: directory could not be inspected ({ex.Message}).");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"{ToRelative(analysisRoot, directory)}: directory could not be read ({ex.Message}).");
            }
        }

        if (warnings.Count > 0) scanReasons.Add("inaccessibleSubtree");
        var sortedMatches = SortMatches(matches, request.SortBy);
        var isSummary = string.Equals(request.View, "summary", StringComparison.OrdinalIgnoreCase);
        var shownMatches = isSummary ? [] : sortedMatches.Take(request.MaxResults).ToArray();
        var summaryEntries = BuildSummaryEntries(directories, rootRelative, effectiveDepth);
        var shownSummaries = summaryEntries.Take(request.MaxResults).ToArray();
        if ((!isSummary && sortedMatches.Count > request.MaxResults) || summaryEntries.Count > request.MaxResults)
        {
            scanReasons.Add("maxResults");
        }
        if (cancellationToken.IsCancellationRequested) scanReasons.Add("cancellation");

        IReadOnlyList<FileTreeSummaryEntry> resultDirectories = isSummary
            ? shownSummaries
            : BuildTreeDirectories(directories, rootRelative, request.TreeDepth);
        if (!isSummary && resultDirectories.Count > request.MaxResults)
        {
            scanReasons.Add("maxResults");
            resultDirectories = resultDirectories.Take(request.MaxResults).ToArray();
        }
        var truncatedBy = scanReasons.OrderBy(reason => reason, StringComparer.Ordinal).ToArray();
        var formatted = FormatOutput(request, shownMatches, resultDirectories, sortedMatches.Count, scannedFiles);

        return new FileTreeScanResult(
            RootPath: rootRelative,
            Entries: shownMatches.Select(candidate => new FileTreeEntry(
                candidate.Path,
                Path.GetFileName(candidate.Path),
                false,
                request.IncludeMetadata ? candidate.Size : null,
                candidate.Extension,
                candidate.LineCount,
                candidate.Depth)).ToArray(),
            Summaries: shownSummaries,
            TotalFiles: sortedMatches.Count,
            TotalDirectories: scannedDirectories,
            IsTruncated: truncatedBy.Length > 0,
            FormattedText: formatted,
            ScanCompleted: !truncatedBy.Contains("cancellation", StringComparer.Ordinal) && !truncatedBy.Contains("inaccessibleSubtree", StringComparer.Ordinal),
            TruncatedBy: truncatedBy,
            Warnings: warnings.Distinct(StringComparer.Ordinal).Take(50).ToArray(),
            ExcludedFileCount: excludedFileCount,
            ScannedFileCount: scannedFiles,
            TotalBytes: totalBytes,
            Next: CreateNext(truncatedBy));
    }

    private static string? ValidateOptions(FileTreeScanRequest request)
    {
        if (!IsOneOf(request.View, "tree", "files", "summary")) return "View must be tree, files, or summary.";
        if (!IsOneOf(request.SortBy, "path", "size_desc", "extension")) return "SortBy must be path, size_desc, or extension.";
        if (request.MaxDepth is < 0 or > MaxDepthCap) return $"MaxDepth must be between 0 and {MaxDepthCap}.";
        if (request.TreeDepth is < 0 or > MaxDepthCap) return $"TreeDepth must be between 0 and {MaxDepthCap}.";
        if (request.MaxResults is < 1 or > MaxResultsCap) return $"MaxResults must be between 1 and {MaxResultsCap}.";
        foreach (var extension in request.IncludeExtensions ?? [])
        {
            if (!FileTreeFilter.IsValidExtension(extension)) return $"Invalid file extension '{extension}'.";
        }
        foreach (var pattern in (request.ExcludePatterns ?? []).Append(request.FileFilter))
        {
            if (pattern is not null && !FileTreeFilter.IsValidRelativeGlob(pattern)) return $"Invalid relative glob '{pattern}'.";
        }
        return null;
    }

    private static int DetermineEffectiveWalkDepth(FileTreeScanRequest request)
    {
        if (request.MaxDepth.HasValue) return request.MaxDepth.Value;
        if (request.TreeDepth.HasValue) return request.TreeDepth.Value;
        if (!string.IsNullOrWhiteSpace(request.FileFilter)) return MaxDepthCap;
        if (request.View.Equals("files", StringComparison.OrdinalIgnoreCase) && request.RelativeRoot != ".") return MaxDepthCap;
        if (request.View.Equals("summary", StringComparison.OrdinalIgnoreCase)) return MaxDepthCap;
        return 2;
    }

    private static bool IsOneOf(string? value, params string[] expected) =>
        value is not null && expected.Any(item => value.Equals(item, StringComparison.OrdinalIgnoreCase));

    private static FileTreeScanResult ErrorResult(FileTreeScanRequest request, string message) => new(
        request.RelativeRoot,
        Array.Empty<FileTreeEntry>(),
        Array.Empty<FileTreeSummaryEntry>(),
        0,
        0,
        false,
        message,
        false,
        ["invalidRoot"],
        [],
        0,
        0,
        0,
        message,
        new FileTreeNext("refine_scope", "Correct the root or options and scan again."));

    private static FileTreeNext CreateNext(IReadOnlyList<string> reasons) => reasons.Count == 0
        ? new FileTreeNext("none", "No further scan is required.")
        : reasons.Contains("inaccessibleSubtree", StringComparer.Ordinal)
            ? new FileTreeNext("refine_scope", "Choose a reachable root or narrow the file filter, then scan again.")
            : reasons.Contains("maxDepth", StringComparer.Ordinal)
                ? new FileTreeNext("refine_scope", "Increase MaxDepth for the required subtree and scan again.")
                : reasons.Contains("cancellation", StringComparer.Ordinal)
                    ? new FileTreeNext("refine_scope", "Narrow the root or filter and scan again.")
                    : new FileTreeNext("refine_scope", "Narrow the root or filter, or increase MaxResults and scan again.");

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool HasDirectories(string path, List<string> warnings)
    {
        try
        {
            return Directory.EnumerateDirectories(path).Any(child => !FileTreeFilter.IsExcludedDirectory(Path.GetFileName(child)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"{path}: child directories could not be listed ({ex.Message}).");
            return true;
        }
    }

    private static int? CountLines(string path)
    {
        using var reader = new StreamReader(path);
        var count = 0;
        while (reader.ReadLine() is not null) count++;
        return count;
    }

    private static void EnsureDirectory(Dictionary<string, DirectoryAggregate> directories, string path, string root)
    {
        var current = NormalizeRelative(path);
        while (true)
        {
            if (!directories.ContainsKey(current)) directories[current] = new DirectoryAggregate(current);
            if (current.Equals(root, StringComparison.OrdinalIgnoreCase) || current == ".") break;
            current = GetParent(current) ?? root;
        }
    }

    private static void AddFileToAncestors(Dictionary<string, DirectoryAggregate> directories, string filePath, string root, long size)
    {
        var current = GetParent(filePath) ?? root;
        while (true)
        {
            if (!directories.TryGetValue(current, out var aggregate))
            {
                aggregate = new DirectoryAggregate(current);
                directories[current] = aggregate;
            }
            aggregate.FileCount++;
            aggregate.TotalBytes += size;
            if (current.Equals(root, StringComparison.OrdinalIgnoreCase) || current == ".") break;
            current = GetParent(current) ?? root;
        }
    }

    private static List<FileTreeSummaryEntry> BuildSummaryEntries(
        Dictionary<string, DirectoryAggregate> directories,
        string root,
        int maxDepth) => directories.Values
        .Where(directory => directory.FileCount > 0 && (GetDepth(directory.Path, root) <= 1 || directory.Path.Equals(root, StringComparison.OrdinalIgnoreCase)))
        .Where(directory => GetDepth(directory.Path, root) <= maxDepth)
        .OrderBy(directory => directory.Path, StringComparer.OrdinalIgnoreCase)
        .Select(directory => new FileTreeSummaryEntry(directory.Path, directory.FileCount, directory.TotalBytes))
        .ToList();

    private static List<FileTreeSummaryEntry> BuildTreeDirectories(
        Dictionary<string, DirectoryAggregate> directories,
        string root,
        int? treeDepth) => directories.Values
        .Where(directory => directory.Path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            (directory.FileCount > 0 && GetDepth(directory.Path, root) <= (treeDepth ?? 2)))
        .OrderBy(directory => directory.Path, StringComparer.OrdinalIgnoreCase)
        .Select(directory => new FileTreeSummaryEntry(directory.Path, directory.FileCount, directory.TotalBytes))
        .ToList();

    private static List<FileTreeCandidate> SortMatches(IEnumerable<FileTreeCandidate> matches, string sortBy) =>
        sortBy.ToLowerInvariant() switch
        {
            "size_desc" => matches.OrderByDescending(match => match.Size).ThenBy(match => match.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            "extension" => matches.OrderBy(match => match.Extension ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(match => match.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => matches.OrderBy(match => match.Path, StringComparer.OrdinalIgnoreCase).ToList(),
        };

    private static string FormatOutput(
        FileTreeScanRequest request,
        IReadOnlyList<FileTreeCandidate> files,
        IReadOnlyList<FileTreeSummaryEntry> directories,
        int matchedFileCount,
        int scannedFileCount)
    {
        if (request.View.Equals("summary", StringComparison.OrdinalIgnoreCase))
        {
            return "# File Summary\n| Directory | Files | Total size |\n| :--- | :---: | :---: |\n" +
                string.Join("\n", directories.Select(directory => $"| {directory.DirectoryPath} | {directory.FileCount} | {directory.TotalSizeBytes} bytes |"));
        }

        if (request.View.Equals("tree", StringComparison.OrdinalIgnoreCase))
        {
            var root = NormalizeRelative(string.IsNullOrWhiteSpace(request.RelativeRoot) ? "." : request.RelativeRoot);
            var lines = new List<string> { root == "." ? "." : $"{root}/" };
            foreach (var directory in directories.Where(directory => !directory.DirectoryPath.Equals(root, StringComparison.OrdinalIgnoreCase)))
            {
                var depth = GetDepth(directory.DirectoryPath, root);
                var name = Path.GetFileName(directory.DirectoryPath.TrimEnd('/'));
                lines.Add($"{new string(' ', Math.Max(0, depth - 1) * 2)}├── {name}/ {directory.FileCount} files");
            }
            foreach (var file in files.Where(file => (GetParent(file.Path) ?? ".").Equals(root, StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"├── {file.Path}");
            }
            return "# File Tree\n" + string.Join("\n", lines);
        }

        var header = request.View.Equals("files", StringComparison.OrdinalIgnoreCase)
            ? $"# Files ({files.Count} shown of {matchedFileCount} matched; {scannedFileCount} scanned)"
            : $"# File Tree: {request.RelativeRoot}";
        return header + "\n" + string.Join("\n", files.Select(file => $"- {file.Path}"));
    }

    private static string ToRelative(string root, string path) => NormalizeRelative(Path.GetRelativePath(root, path));
    private static string NormalizeRelative(string path)
    {
        var normalized = PathNormalizer.NormalizeSeparators(path).Trim('/');
        return string.IsNullOrEmpty(normalized) || normalized == "." ? "." : normalized;
    }

    private static string? GetParent(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? null : NormalizeRelative(path[..separator]);
    }

    private static int GetDepth(string path, string root)
    {
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return 0;
        var relative = root == "." ? path : path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) ? path[(root.Length + 1)..] : path;
        return NormalizeRelative(relative).Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static string? NormalizeExtension(string extension) => string.IsNullOrEmpty(extension) ? null : extension.ToLowerInvariant();

    private sealed class DirectoryAggregate(string path)
    {
        public string Path { get; } = path;
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }
    }

    private sealed record FileTreeCandidate(string Path, string? Extension, long Size, int? LineCount, int Depth);
}
