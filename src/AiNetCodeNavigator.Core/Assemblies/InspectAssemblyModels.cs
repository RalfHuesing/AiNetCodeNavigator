#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using AiNetCodeNavigator.Core.Models;

namespace AiNetCodeNavigator.Core.Assemblies;

public sealed record InspectAssemblyRequest(
    string AssemblyPath,
    string? Namespace = null,
    string? TypeName = null,
    string? MemberName = null,
    bool PublicOnly = true,
    int MaxResults = 100,
    bool ExactTypeName = false,
    IReadOnlyList<string>? MemberNames = null,
    bool IncludeReferences = false,
    string? Cursor = null)
{
    public bool IncludeReferenceDetails => IncludeReferences;
}

public sealed record AssemblyTypeDto(
    string Namespace,
    string Name,
    string Kind,
    string Accessibility,
    IReadOnlyList<AssemblyMemberDto> Members,
    IReadOnlyList<string> Attributes,
    [property: JsonIgnore] string? Id = null,
    bool Handoff = false,
    IReadOnlyList<string>? AllowedFollowUpTools = null,
    string? HandoffId = null,
    string? OwnerTargetPath = null);

public sealed record AssemblyMemberDto(
    string Kind,
    string Name,
    string Accessibility,
    string Signature,
    IReadOnlyList<AssemblyParameterDto> Parameters,
    IReadOnlyList<string> GenericParameters,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<string> Attributes,
    [property: JsonIgnore] string? Id = null,
    bool Handoff = false,
    IReadOnlyList<string>? AllowedFollowUpTools = null,
    string? HandoffId = null,
    string? OwnerTargetPath = null);

public sealed record AssemblyParameterDto(
    string Name,
    string Type,
    string RefKind,
    bool IsOptional,
    string? DefaultValue);

public sealed record AssemblyReferenceSummary(
    int TotalReferenceCount,
    int ShownReferenceCount,
    bool ReferencesTruncated);

public sealed record InspectAssemblyPayload(
    string AssemblyPath,
    AssemblyIdentityDto? Identity,
    IReadOnlyList<string> Namespaces,
    IReadOnlyList<AssemblyReferenceDto> References,
    IReadOnlyList<AssemblyTypeDto> Types,
    IReadOnlyList<string> Diagnostics,
    string Completeness,
    bool Truncated,
    int TotalTypes,
    int ShownCount,
    IReadOnlyList<string> TruncatedBy,
    AssemblyOrigin? Origin = null,
    long Generation = 0,
    string SessionStatus = "complete",
    AssemblyReferenceSummary? ReferenceSummary = null,
    bool ReferenceDetailsIncluded = true,
    int TotalNamespaces = 0,
    string? DecompiledSourceRoot = null,
    [property: JsonPropertyName("resultCursor")] string? ResultCursor = null,
    NavigationAnalysisMetadata? Analysis = null)
{
    public int TotalCount => TotalTypes;
    public int ReturnedCount => ShownCount;
    public bool IsTruncated => Truncated;
}
