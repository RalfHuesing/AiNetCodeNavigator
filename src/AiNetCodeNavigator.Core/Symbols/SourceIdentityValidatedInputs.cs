#nullable enable

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Immutable evidence captured by the workspace owner while validating a fresh source snapshot.
/// It intentionally contains no Solution, Project, MetadataReference, Compilation, or byte buffer.
/// </summary>
internal sealed record SourceIdentityValidatedInputs(
    ImmutableArray<SourceIdentityProjectProvenance> Projects,
    ImmutableArray<SourceIdentityMetadataReferenceEvidence> MetadataReferences);

/// <summary>
/// A request-lived proof that captured image/provenance evidence belongs with this exact immutable Solution.
/// Memoized values must retain neither this pair nor either Roslyn project identifier.
/// </summary>
internal sealed record SourceIdentityValidatedSnapshot(
    Solution Solution,
    SourceIdentityValidatedInputs Inputs);

/// <summary>Loader-established provenance for binding inputs which are not represented by ordinary documents.</summary>
internal sealed record SourceIdentityProjectProvenance(
    ProjectId OwnerProjectId,
    bool IsSupported,
    string? UnsupportedReason,
    ImmutableArray<SourceIdentityCapturedInput> BindingInputs,
    bool OwnerCreatedAnalyzerConfigProvider = false,
    bool OwnerCreatedSyntaxTreeOptionsProvider = false,
    bool OwnerCreatedMetadataReferenceResolver = false,
    bool OwnerCreatedStrongNameProvider = false);

/// <summary>A captured analyzer, generator, or provider input that can affect source binding.</summary>
internal sealed record SourceIdentityCapturedInput(
    string Kind,
    string LogicalKey,
    string Sha256);

/// <summary>
/// SHA-256 evidence for one metadata reference and all of its referenced module images.
/// Owner and ordinal identify the exact reference in the paired immutable Solution.
/// </summary>
internal sealed record SourceIdentityMetadataReferenceEvidence(
    ProjectId OwnerProjectId,
    int ReferenceOrdinal,
    ImmutableArray<SourceIdentityImageEvidence> Images);

/// <summary>
/// A SHA-256 computed from the same immutable image bytes used to construct the paired Roslyn reference.
/// The key is a canonical physical path or <c>in-memory:&lt;SHA-256&gt;</c>.
/// </summary>
internal sealed record SourceIdentityImageEvidence(
    string CanonicalImageKey,
    string Sha256);
