#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record ReferenceLocationEntry(
    string FilePath,
    int Line,
    int Column,
    string Snippet,
    string EnclosingSymbolName,
    string? EnclosingSymbolHandoffId,
    string ProjectName,
    int Depth = 1,
    string? ReachedFromSymbolName = null,
    string? ReachedFromSymbolHandoffId = null,
    string? OwnerTargetPath = null,
    [property: JsonIgnore] string ReachedFromSymbolId = "",
    string EvidenceKind = RelationshipEvidence.Unresolved,
    [property: JsonIgnore] string? ProjectPath = null,
    [property: JsonIgnore] string? ProjectIdentity = null);

public sealed record FindReferencesResult(
    string TargetSymbolName,
    string TargetKind,
    IReadOnlyList<ReferenceLocationEntry> References,
    int TotalCount,
    bool IsTruncated,
    int RequestedDepth = 1,
    int EffectiveDepth = 1,
    int VisitedSymbolCount = 1,
    bool IsTruncatedByNodeLimit = false,
    bool IsDepthClamped = false,
    int EffectiveNodeLimit = 200,
    ReferenceSummary? Summary = null)
{
    public bool IsComplete => !IsTruncated && !IsTruncatedByNodeLimit && !IsDepthClamped;
}

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
    bool IsTruncated = false,
    string? ErrorMessage = null)
{
    public bool IsSuccess => ErrorMessage is null;
}
