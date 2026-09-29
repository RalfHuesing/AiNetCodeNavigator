#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.Skeletons;

public enum SkeletonMemberKind
{
    Field,
    Constructor,
    Property,
    PublicMethod,
    InternalMethod,
    PrivateMethod,
    Event
}

public sealed record SkeletonMemberInfo(
    SkeletonMemberKind Kind,
    string Signature,
    string? MetaComment = null,
    string? Id = null);

public sealed record SkeletonTypeInfo(
    string Namespace,
    string Kind,
    string Modifiers,
    string Name,
    string? BaseTypes,
    string RelativePath,
    IReadOnlyList<SkeletonMemberInfo> Members,
    string? Id = null);
