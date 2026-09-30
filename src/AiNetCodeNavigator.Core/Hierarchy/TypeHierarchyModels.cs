#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.Hierarchy;

public sealed record TypeHierarchyEntry(
    string Name,
    string Kind,
    string FilePath,
    int Line,
    string? HandoffId = null);

public sealed record TypeHierarchyPayload(
    string TypeName,
    IReadOnlyList<TypeHierarchyEntry> BaseTypes,
    IReadOnlyList<TypeHierarchyEntry> Interfaces,
    string SubtypesHeading,
    IReadOnlyList<TypeHierarchyEntry> Subtypes,
    int TotalSubtypes,
    bool IsTruncated,
    string? ErrorMessage = null)
{
    public bool IsSuccess => ErrorMessage is null;
}
