#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AiNetCodeNavigator.Core.Dependencies;

/// <summary>Delivers one projection of the admitted traversal and its shared coverage and bounds.</summary>
public sealed record DependencyGraphView([property: JsonIgnore] DependencyGraphLevel Level, [property: JsonIgnore] DependencyGraphPayload Graph)
{
    [JsonPropertyName("level")]
    public string LevelName => Level.ToString().ToLowerInvariant();
    public string RootSemantics => "Type dependencies; a member root selects its owning type.";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DependencyTypeReference>? TypeDependencies => Level == DependencyGraphLevel.Type ? Graph.TypeDependencies : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FileDependency>? FileDependencies => Level == DependencyGraphLevel.File ? Graph.FileDependencies : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<NamespaceDependency>? NamespaceDependencies => Level == DependencyGraphLevel.Namespace ? Graph.NamespaceDependencies : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalTypeDependencyCount => Level == DependencyGraphLevel.Type ? Graph.TotalTypeDependencyCount : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalFileDependencyCount => Level == DependencyGraphLevel.File ? Graph.TotalFileDependencyCount : null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalNamespaceDependencyCount => Level == DependencyGraphLevel.Namespace ? Graph.TotalNamespaceDependencyCount : null;
    public bool HasMore => Graph.HasMoreTypeDependencies || Graph.HasMoreFileDependencies || Graph.HasMoreNamespaceDependencies;
    public int Offset => Graph.Offset;
    public int PageSize => Graph.PageSize;
    public int ScannedDocumentCount => Graph.ScannedDocumentCount;
    public int TotalDocumentCount => Graph.TotalDocumentCount;
    public bool DocumentLimitReached => Graph.DocumentLimitReached;
    public bool PageSizeWasClamped => Graph.PageSizeWasClamped;
    public bool DocumentLimitWasClamped => Graph.DocumentLimitWasClamped;
    public IReadOnlyList<DependencyGraphScanError>? Errors => Graph.Errors;
    public int DocumentOffset => Graph.DocumentOffset;
    public int? NextDocumentOffset => Graph.NextDocumentOffset;
    public DependencyGraphDirection Direction => Graph.Direction;
    public int RequestedDepth => Graph.RequestedDepth;
    public int EffectiveDepth => Graph.EffectiveDepth;
    public bool IsDepthClamped => Graph.IsDepthClamped;
    public bool IsTargeted => Graph.IsTargeted;
    public string? TargetFilePath => Graph.TargetFilePath;
    public string? TargetTypeName => Graph.TargetTypeName;
    public int VisitedTypeCount => Graph.VisitedTypeCount;
    public int EffectiveNodeLimit => Graph.EffectiveNodeLimit;
    public bool IsNodeLimitClamped => Graph.IsNodeLimitClamped;
    public bool NodeLimitReached => Graph.NodeLimitReached;
    public int HiddenTypeDependencyCount => Graph.HiddenTypeDependencyCount;
    public bool ContinuationInputIncomplete => Graph.ContinuationInputIncomplete;
    public bool IsTruncated => Graph.IsTruncated;
    public bool IsComplete => Graph.IsComplete;
}
