#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.FileStructure;

public sealed record FileTreeEntry(
    string RelativePath,
    string Name,
    bool IsDirectory,
    long Size,
    string Extension);

public sealed record FileTreeSummaryEntry(
    string DirectoryPath,
    int FileCount,
    long TotalSizeBytes);

public sealed record FileTreeScanRequest(
    string RootDirectory,
    string RelativeRoot = ".",
    string View = "tree", // "tree", "files", "summary"
    string? FileFilter = null,
    string? FolderFilter = null,
    int? MaxDepth = 3,
    int MaxResults = 100);

public sealed record FileTreeScanResult(
    string RootPath,
    IReadOnlyList<FileTreeEntry> Entries,
    IReadOnlyList<FileTreeSummaryEntry> Summaries,
    int TotalFiles,
    int TotalDirectories,
    bool IsTruncated,
    string FormattedText);
