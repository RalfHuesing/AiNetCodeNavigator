#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.FileStructure;

public sealed record ProjectScopeEntry(
    string Name,
    int DocumentCount,
    bool IsTestProject,
    int CSharpDocumentCount = 0,
    bool IsCSharpProject = true);

public sealed record FileTypeScopeEntry(
    string Extension,
    int Count,
    bool SymbolGraphCovered);

public sealed record IndexScopePayload(
    string SolutionPath,
    int ProjectCount,
    int TotalDocumentCount,
    int CSharpFileCount,
    int TestProjectCount,
    IReadOnlyList<ProjectScopeEntry> Projects,
    IReadOnlyList<FileTypeScopeEntry> FileTypes,
    string FormattedText,
    string? ScopeProjectName = null,
    int TotalFileTypeCount = 0,
    int ShownProjectCount = 0,
    int ShownFileTypeCount = 0,
    bool ScanCompleted = true,
    bool IsTruncated = false,
    IReadOnlyList<string>? TruncatedBy = null,
    string? Error = null,
    int RequestedMaxProjects = IndexScopeScanner.DefaultMaxProjects,
    int EffectiveMaxProjects = IndexScopeScanner.DefaultMaxProjects,
    int RequestedMaxFileTypes = IndexScopeScanner.DefaultMaxFileTypes,
    int EffectiveMaxFileTypes = IndexScopeScanner.DefaultMaxFileTypes,
    bool BoundsWereClamped = false,
    string? NextAction = null);

public sealed record IndexScopeScanOptions(
    string? ProjectName = null,
    int MaxProjects = IndexScopeScanner.DefaultMaxProjects,
    int MaxFileTypes = IndexScopeScanner.DefaultMaxFileTypes);
