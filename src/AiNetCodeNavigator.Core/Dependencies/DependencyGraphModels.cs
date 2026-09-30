#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.Dependencies;

public sealed record ProjectDependency(
    string FromProject,
    string ToProject);

public sealed record NamespaceDependency(
    string FromNamespace,
    string ToNamespace,
    IReadOnlyList<string> ReferencedTypes,
    string? FromProject = null,
    string? ToProject = null);

public sealed record FileDependency(
    string FromFile,
    string ToFile,
    IReadOnlyList<string> CrossingTypes,
    string? FromProject = null,
    string? ToProject = null);

public sealed record DependencyGraphScanOptions(
    int Offset = 0,
    int PageSize = 100,
    int MaxDocuments = DependencyGraphScanner.MaximumDocuments);

public sealed record DependencyGraphScanError(
    string Project,
    string Document,
    string Message);

public sealed record DependencyGraphPayload(
    IReadOnlyList<ProjectDependency> ProjectDependencies,
    IReadOnlyList<NamespaceDependency> NamespaceDependencies,
    IReadOnlyList<FileDependency> FileDependencies,
    int TotalProjectDependencyCount = 0,
    int TotalNamespaceDependencyCount = 0,
    int TotalFileDependencyCount = 0,
    int Offset = 0,
    int PageSize = 100,
    int ScannedDocumentCount = 0,
    int TotalDocumentCount = 0,
    bool DocumentLimitReached = false,
    bool PageSizeWasClamped = false,
    bool DocumentLimitWasClamped = false,
    IReadOnlyList<DependencyGraphScanError>? Errors = null)
{
    public bool HasMoreProjectDependencies => (long)Offset + ProjectDependencies.Count < TotalProjectDependencyCount;
    public bool HasMoreNamespaceDependencies => (long)Offset + NamespaceDependencies.Count < TotalNamespaceDependencyCount;
    public bool HasMoreFileDependencies => (long)Offset + FileDependencies.Count < TotalFileDependencyCount;
    public bool IsTruncated => DocumentLimitReached || (Errors?.Count ?? 0) > 0 || HasMoreProjectDependencies || HasMoreNamespaceDependencies || HasMoreFileDependencies;
    public bool IsComplete => !DocumentLimitReached && (Errors?.Count ?? 0) == 0;
}
