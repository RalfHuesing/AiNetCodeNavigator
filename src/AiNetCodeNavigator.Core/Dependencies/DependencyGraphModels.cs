#nullable enable

using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using AiNetCodeNavigator.Core.Symbols;

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
    int MaxNodes = DependencyGraphScanner.MaximumNodes,
    string? TargetTypeId = null,
    IReadOnlyCollection<string>? TargetTypeIds = null,
    SymbolScopeType ScopeType = SymbolScopeType.All,
    bool IncludeGenerated = false);

internal sealed record DependencyGraphCollectionOptions(
    SymbolScopeType ScopeType = SymbolScopeType.All,
    bool IncludeGenerated = false,
    int DocumentOffset = 0,
    int? MaxDocuments = null);

internal sealed record DependencyGraphProjectionOptions(
    int Offset = 0,
    int PageSize = 100,
    string? TargetFilePath = null,
    string? TargetTypeName = null,
    string? TargetProject = null,
    DependencyGraphDirection Direction = DependencyGraphDirection.Both,
    int Depth = 1,
    int MaxNodes = DependencyGraphScanner.MaximumNodes,
    string? TargetTypeId = null,
    IReadOnlyCollection<string>? TargetTypeIds = null);

internal sealed record DependencyGraphCollection(
    ImmutableArray<DependencyTypeReference> TypeDependencies,
    ImmutableArray<ProjectDependency> ProjectDependencies,
    ImmutableArray<DependencyGraphScanError> Errors,
    ImmutableArray<DependencyDocumentIdentity> EligibleDocuments,
    ImmutableArray<DependencyDocumentIdentity> RequiredDocuments,
    ImmutableArray<DependencyDocumentIdentity> AttemptedDocuments,
    ImmutableArray<DependencyDocumentIdentity> CoveredDocuments,
    ImmutableArray<DependencyTypeDeclaration> TypeDeclarations,
    int EligibleDocumentCount,
    int RequiredDocumentCount,
    int CoveredDocumentCount,
    int NewSemanticScanCount,
    int DocumentOffset,
    int? NextDocumentOffset,
    bool DocumentLimitWasClamped,
    SymbolScopeType ScopeType,
    bool IncludeGenerated,
    string SolutionDirectory,
    bool ContinuationInputIncomplete = false);

internal sealed record DependencyDocumentIdentity(
    string OwnerProjectPath,
    string OwnerContextFingerprint,
    string DocumentPath,
    string Name,
    ImmutableArray<string> Folders,
    string SourceCodeKind,
    string TextHash,
    int DuplicateOrdinal);

internal sealed record DependencyTypeDeclaration(
    string TypeId,
    string DisplayName,
    string Name,
    string Namespace,
    string Project,
    string File,
    string OwnerProjectPath,
    string OwnerContextFingerprint,
    string OriginalTypeId,
    ImmutableArray<DependencyDocumentIdentity> DeclarationDocuments);

internal sealed record DependencyGraphCollectionObserver(
    Action<Project>? CompilationAcquired = null,
    Action<Document>? DocumentCollected = null,
    Action<string, ISymbol>? SymbolDiscovered = null);

public sealed record DependencyGraphTraversalOptions(
    string? TargetFilePath = null,
    string? TargetTypeName = null,
    string? TargetProject = null,
    DependencyGraphDirection Direction = DependencyGraphDirection.Both,
    int Depth = 1,
    int Offset = 0,
    int PageSize = 100,
    int MaxNodes = 200,
    string? TargetTypeId = null,
    IReadOnlyCollection<string>? TargetTypeIds = null);

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
    int Depth = 1,
    string? FromHandoffId = null,
    string? ToHandoffId = null);

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
    internal ImmutableArray<DependencyTypeDeclaration> TypeDeclarations { get; init; } = ImmutableArray<DependencyTypeDeclaration>.Empty;

    public bool HasMoreProjectDependencies => (long)Offset + ProjectDependencies.Count < TotalProjectDependencyCount;
    public bool HasMoreNamespaceDependencies => (long)Offset + NamespaceDependencies.Count < TotalNamespaceDependencyCount;
    public bool HasMoreFileDependencies => (long)Offset + FileDependencies.Count < TotalFileDependencyCount;
    public bool HasMoreTypeDependencies => (long)Offset + (TypeDependencies?.Count ?? 0) < TotalTypeDependencyCount;
    public bool IsTruncated => Offset > 0 || DocumentOffset > 0 || NextDocumentOffset is not null || IsDepthClamped || NodeLimitReached || ContinuationInputIncomplete || (Errors?.Count ?? 0) > 0 || HasMoreProjectDependencies || HasMoreNamespaceDependencies || HasMoreFileDependencies || HasMoreTypeDependencies;
    public bool IsComplete => !IsTruncated;
}
