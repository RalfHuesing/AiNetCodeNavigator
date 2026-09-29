#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.FileStructure;

public sealed class NamespaceNode
{
    public string Name { get; }
    public string FullName { get; }
    public int TypeCount { get; set; }
    public List<NamespaceNode> Children { get; } = [];

    public NamespaceNode(string name, string fullName, int typeCount = 0)
    {
        Name = name;
        FullName = fullName;
        TypeCount = typeCount;
    }
}

public sealed record NamespaceTreePayload(
    string SolutionName,
    string? ProjectName,
    IReadOnlyList<NamespaceNode> RootNamespaces,
    int TotalNamespaces,
    int TotalTypes,
    string FormattedText);
