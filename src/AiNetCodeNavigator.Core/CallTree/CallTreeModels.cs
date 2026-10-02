#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using AiNetCodeNavigator.Core.Assemblies;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.CallTree;

public enum CallTreeDirection
{
    Incoming,
    Outgoing,
    Both
}

public sealed record CallSiteInfo(
    string FilePath,
    int Line,
    int Column = 1,
    string EvidenceKind = AiNetCodeNavigator.Core.Symbols.RelationshipEvidence.Call);

public sealed record UnresolvedCallSiteInfo(
    string CallerNodeId,
    string FilePath,
    int Line,
    int Column,
    string EvidenceKind,
    IReadOnlyList<string> CandidateTargets);

public sealed record CallGraphNode(
    string NodeId,
    string SymbolId,
    string Name,
    string DisplayLine,
    string Kind,
    string? HandoffId = null,
    string? OwnerTargetPath = null,
    [property: JsonIgnore] AssemblyIdentityDto? ContainingAssemblyIdentity = null);

public sealed record CallGraphEdge(
    string FromNodeId,
    string ToNodeId,
    IReadOnlyList<CallSiteInfo> CallSites,
    string? DispatchKind = null);

public sealed record CallGraphMethodHint(
    string Name,
    string SymbolId,
    string DisplayLine);

public sealed record CallGraphPayload(
    string RootNodeId,
    IReadOnlyList<CallGraphNode> Nodes,
    IReadOnlyList<CallGraphEdge> Edges,
    IReadOnlyList<CallGraphMethodHint>? MethodHints = null,
    bool Truncated = false,
    int HiddenEdgeCount = 0,
    int PendingNodeCount = 0,
    IReadOnlyList<UnresolvedCallSiteInfo>? UnresolvedCallSites = null);

public sealed record CallTreeBuildRequest(
    Solution Solution,
    ISymbol SeedSymbol,
    int RequestedDepth = 2,
    int TopN = 10,
    CallTreeDirection Direction = CallTreeDirection.Incoming,
    bool IncludeBcl = false,
    AiNetCodeNavigator.Core.Symbols.SymbolScopeType Scope = AiNetCodeNavigator.Core.Symbols.SymbolScopeType.All,
    bool IncludeGenerated = false,
    Func<ISymbol, string?>? HandoffFormatter = null);
