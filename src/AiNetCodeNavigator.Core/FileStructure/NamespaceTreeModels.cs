#nullable enable

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.FileStructure;

public sealed class NamespaceNode
{
    public string Name { get; }
    public string FullName { get; }
    public int TypeCount { get; set; }
    public List<NamespaceNode> Children { get; } = [];
    public List<NamespaceTypeEntry> Types { get; } = [];

    public NamespaceNode(string name, string fullName, int typeCount = 0)
    {
        Name = name;
        FullName = fullName;
        TypeCount = typeCount;
    }
}

public sealed record NamespaceTypeEntry(string Name, string Kind, string FilePath, int Line, string? HandoffId = null);

public sealed record NamespaceTreePayload(
    string SolutionName,
    string? ProjectName,
    IReadOnlyList<NamespaceNode> RootNamespaces,
    int TotalNamespaces,
    int TotalTypes,
    string FormattedText,
    int ShownNamespaces = 0,
    bool Truncated = false,
    IReadOnlyList<string>? TruncatedBy = null,
    string? Error = null,
    int RequestedMaxDepth = 32,
    int EffectiveMaxDepth = 32,
    int RequestedMaxResults = NamespaceTreeScanner.DefaultMaxResults,
    int EffectiveMaxResults = NamespaceTreeScanner.DefaultMaxResults,
    bool BoundsWereClamped = false,
    string? NextAction = null,
    bool IncludeGenerated = false,
    IReadOnlyList<NamespaceProjectOverviewEntry>? Projects = null,
    int TotalProjects = 0,
    string? ErrorCode = null);

public sealed record NamespaceProjectOverviewEntry(
    string ProjectName,
    string ProjectType,
    int NamespaceCount,
    int TypeCount,
    string? ProjectPath = null);

public sealed record NamespaceTreeScanOptions(
    int MaxDepth = NamespaceTreeScanner.MaxDepthCap,
    int MaxResults = NamespaceTreeScanner.DefaultMaxResults,
    bool IncludeGenerated = false,
    string? NamespacePrefix = null,
    string Kind = "all",
    bool IncludeTypes = true,
    bool IncludeProjectOverview = false,
    System.Func<INamedTypeSymbol, string?>? FormatTypeHandoff = null,
    bool CollectAllInventory = false,
    bool AllowProjectSelectionRecovery = true);
