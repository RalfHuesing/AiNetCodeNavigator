#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record ReferenceLocationEntry(
    string FilePath,
    int Line,
    int Column,
    string Snippet,
    string EnclosingSymbolName,
    string? EnclosingSymbolHandoffId,
    string ProjectName);

public sealed record FindReferencesResult(
    string TargetSymbolName,
    string TargetKind,
    IReadOnlyList<ReferenceLocationEntry> References,
    int TotalCount,
    bool IsTruncated);

public sealed record ImplementationLocationEntry(
    string SymbolName,
    string Kind,
    string FilePath,
    int Line,
    string Signature,
    string ProjectName,
    string? HandoffId = null);

public sealed record FindImplementationsResult(
    string TargetSymbolName,
    string TargetKind,
    IReadOnlyList<ImplementationLocationEntry> Implementations,
    int TotalCount,
    bool IsTruncated = false);
