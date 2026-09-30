#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.FileStructure;

public sealed record FileTreeEntry(
    string RelativePath,
    string Name,
    bool IsDirectory,
    long? Size,
    string? Extension,
    int? LineCount = null,
    int Depth = 0);

public sealed record FileTreeSummaryEntry(
    string DirectoryPath,
    int FileCount,
    long TotalSizeBytes);

public sealed record FileTreeScanRequest(
    string RootDirectory,
    string RelativeRoot = ".",
    string View = "tree",
    string? FileFilter = null,
    string? FolderFilter = null,
    int? MaxDepth = null,
    int MaxResults = 100,
    IReadOnlyList<string>? IncludeExtensions = null,
    IReadOnlyList<string>? ExcludePatterns = null,
    int? TreeDepth = 2,
    string SortBy = "path",
    bool IncludeMetadata = true,
    bool IncludeLineCount = false);

public sealed record FileTreeScanResult(
    string RootPath,
    IReadOnlyList<FileTreeEntry> Entries,
    IReadOnlyList<FileTreeSummaryEntry> Summaries,
    int TotalFiles,
    int TotalDirectories,
    bool IsTruncated,
    string FormattedText,
    bool ScanCompleted = true,
    IReadOnlyList<string>? TruncatedBy = null,
    IReadOnlyList<string>? Warnings = null,
    int ExcludedFileCount = 0,
    int ScannedFileCount = 0,
    long TotalBytes = 0,
    string? Error = null,
    FileTreeNext? Next = null);

public sealed record FileTreeNext(string Kind, string Action);
