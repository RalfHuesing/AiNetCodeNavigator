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
    int MaxDocuments = DependencyGraphScanner.MaximumDocuments,
    int DocumentOffset = 0,
    string? TargetFilePath = null,
    string? TargetTypeName = null,
    string? TargetProject = null,
    DependencyGraphDirection Direction = DependencyGraphDirection.Both,
    int Depth = 1,
    int MaxNodes = DependencyGraphScanner.MaximumNodes);

public sealed record DependencyGraphTraversalOptions(
    string? TargetFilePath = null,
    string? TargetTypeName = null,
    string? TargetProject = null,
    DependencyGraphDirection Direction = DependencyGraphDirection.Both,
    int Depth = 1,
    int Offset = 0,
    int PageSize = 100,
    int MaxNodes = 200);

public enum DependencyGraphDirection
{
    Outgoing,
    Incoming,
    Both
}

public sealed record DependencyTypeReference(
    string FromTypeId,
    string ToTypeId,
    string FromType,
    string ToType,
    string FromTypeName,
    string ToTypeName,
    string FromNamespace,
    string ToNamespace,
    string FromProject,
    string ToProject,
    string FromFile,
    string ToFile,
    int Depth = 1);

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
    IReadOnlyList<DependencyGraphScanError>? Errors = null,
    IReadOnlyList<DependencyTypeReference>? TypeDependencies = null,
    int TotalTypeDependencyCount = 0,
    int DocumentOffset = 0,
    int? NextDocumentOffset = null,
    DependencyGraphDirection Direction = DependencyGraphDirection.Both,
    int RequestedDepth = 1,
    int EffectiveDepth = 1,
    bool IsDepthClamped = false,
    bool IsTargeted = false,
    string? TargetFilePath = null,
    string? TargetTypeName = null,
    int VisitedTypeCount = 0,
    int EffectiveNodeLimit = 200,
    bool IsNodeLimitClamped = false,
    bool NodeLimitReached = false,
    int HiddenTypeDependencyCount = 0,
    bool ContinuationInputIncomplete = false)
{
    public bool HasMoreProjectDependencies => (long)Offset + ProjectDependencies.Count < TotalProjectDependencyCount;
    public bool HasMoreNamespaceDependencies => (long)Offset + NamespaceDependencies.Count < TotalNamespaceDependencyCount;
    public bool HasMoreFileDependencies => (long)Offset + FileDependencies.Count < TotalFileDependencyCount;
    public bool HasMoreTypeDependencies => (long)Offset + (TypeDependencies?.Count ?? 0) < TotalTypeDependencyCount;
    public bool IsTruncated => Offset > 0 || DocumentOffset > 0 || NextDocumentOffset is not null || IsDepthClamped || NodeLimitReached || ContinuationInputIncomplete || (Errors?.Count ?? 0) > 0 || HasMoreProjectDependencies || HasMoreNamespaceDependencies || HasMoreFileDependencies || HasMoreTypeDependencies;
    public bool IsComplete => !IsTruncated;
}
