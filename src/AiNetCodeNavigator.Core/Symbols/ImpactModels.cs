#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record ImpactCallSiteEntry(
    string FilePath,
    int Line,
    int Column,
    string CallingMember,
    string? CallingMemberHandoffId,
    string ProjectName,
    int Depth,
    string ReachedFromSymbolId = "",
    string? ReachedFromSymbolHandoffId = null,
    string? OwnerTargetPath = null);

public sealed record SymbolImpactPayload(
    string TargetSymbol,
    string TargetKind,
    int DirectCallersCount,
    int TransitiveImpactCount,
    int MaxDepthReached,
    IReadOnlyList<ImpactCallSiteEntry> CallSites,
    IReadOnlyList<string> AffectedProjects,
    IReadOnlyList<string> AffectedFiles,
    bool IsTruncated,
    int RequestedDepth = 1,
    int EffectiveDepth = 1,
    int VisitedSymbolCount = 1,
    bool IsTruncatedByNodeLimit = false,
    bool IsDepthClamped = false,
    int EffectiveNodeLimit = 200,
    int TransitiveCallSitesCount = 0)
{
    public bool IsComplete => !IsTruncated && !IsTruncatedByNodeLimit && !IsDepthClamped;
}
