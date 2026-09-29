#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record ImpactCallSiteEntry(
    string FilePath,
    int Line,
    string CallingMember,
    string? CallingMemberHandoffId,
    string ProjectName,
    int Depth);

public sealed record SymbolImpactPayload(
    string TargetSymbol,
    string TargetKind,
    int DirectCallersCount,
    int TransitiveImpactCount,
    int MaxDepthReached,
    IReadOnlyList<ImpactCallSiteEntry> CallSites,
    IReadOnlyList<string> AffectedProjects,
    IReadOnlyList<string> AffectedFiles,
    bool IsTruncated);
