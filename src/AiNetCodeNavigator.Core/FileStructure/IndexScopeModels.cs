#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.FileStructure;

public sealed record ProjectScopeEntry(
    string Name,
    int DocumentCount,
    bool IsTestProject);

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
    string FormattedText);
