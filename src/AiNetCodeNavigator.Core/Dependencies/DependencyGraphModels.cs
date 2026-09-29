#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.Dependencies;

public sealed record ProjectDependency(
    string FromProject,
    string ToProject);

public sealed record NamespaceDependency(
    string FromNamespace,
    string ToNamespace,
    IReadOnlyList<string> ReferencedTypes);

public sealed record FileDependency(
    string FromFile,
    string ToFile,
    IReadOnlyList<string> CrossingTypes);

public sealed record DependencyGraphPayload(
    IReadOnlyList<ProjectDependency> ProjectDependencies,
    IReadOnlyList<NamespaceDependency> NamespaceDependencies,
    IReadOnlyList<FileDependency> FileDependencies);
