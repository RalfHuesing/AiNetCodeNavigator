#nullable enable

using System.Collections.Generic;
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
    int Line);

public sealed record CallGraphNode(
    string NodeId,
    string SymbolId,
    string Name,
    string DisplayLine,
    string Kind,
    string? HandoffId = null);

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
    int PendingNodeCount = 0);

public sealed record CallTreeBuildRequest(
    Solution Solution,
    ISymbol SeedSymbol,
    int RequestedDepth = 2,
    int TopN = 10,
    CallTreeDirection Direction = CallTreeDirection.Incoming,
    bool IncludeBcl = false);
