#nullable enable

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record ClassStructureMemberEntry(
    string Kind,
    string Name,
    string Visibility,
    int StartLine,
    int EndLine,
    int LineCount,
    string Signature,
    string FilePath,
    string? HandoffId);

public sealed record ClassStructurePayload(
    string TypeName,
    string Kind,
    IReadOnlyList<string> Files,
    int TotalLines,
    int TotalMemberCount,
    int ShownMemberCount,
    bool Truncated,
    IReadOnlyList<ClassStructureMemberEntry> Members,
    IReadOnlyList<string> TruncatedBy);

public sealed record ClassStructureScanRequest(
    Solution Solution,
    string SymbolIdentifier,
    string? SortBy = "lines",
    int MaxMembers = 50,
    string? KindFilter = null,
    string? NameFilter = null,
    AnalysisSymbolIdentity? HandoffIdentity = null);
