#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;
using AiNetCodeNavigator.Core.Models;

namespace AiNetCodeNavigator.Core.Assemblies;

public sealed record AssemblyContextRequest(string AssemblyPath, int MaxResults = 100, bool IncludeReferences = false);

public sealed record AssemblyContextPayload(
    string AssemblyPath,
    AssemblyIdentityDto? Identity,
    AssemblyOrigin Origin,
    string Status,
    int TotalTypes,
    int TotalNamespaces,
    IReadOnlyList<string> Namespaces,
    IReadOnlyList<string> Types,
    IReadOnlyList<AssemblyReferenceDto> References,
    IReadOnlyList<string> Diagnostics,
    int ShownCount,
    bool Truncated,
    int TotalReferenceCount,
    bool ReferencesTruncated);

public sealed record AssemblySearchRequest(
    string AssemblyPath,
    string? Query = null,
    bool CaseSensitive = false,
    bool UseRegex = false,
    string? FileFilter = null,
    bool DeclarationOnly = false,
    int ContextLines = 0,
    int MaxResults = 100,
    int MaxFiles = 0,
    string? Kind = null,
    string? Cursor = null);

public sealed record AssemblySearchHit(
    string FilePath,
    int LineNumber,
    string Text,
    string? Symbol = null,
    IReadOnlyList<string>? Context = null,
    string? HandoffId = null,
    string? OwnerTargetPath = null);

public sealed record AssemblySearchPayload(
    string AssemblyPath,
    string Query,
    IReadOnlyList<AssemblySearchHit> Results,
    int TotalCount,
    bool Truncated,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string>? TruncatedBy = null,
    [property: JsonPropertyName("resultCursor")] string? ResultCursor = null,
    NavigationAnalysisMetadata? Analysis = null);

public sealed record FindAssemblyExtensionsRequest(
    string AssemblyPath,
    string? ReceiverType = null,
    string? ExtensionName = null,
    string? Namespace = null,
    bool IncludeReferences = false,
    int MaxResults = 100,
    string? Cursor = null);

public sealed record AssemblyExtensionDto(
    string Namespace,
    string ContainingType,
    string Name,
    string Signature,
    string ReceiverType,
    string ReturnType,
    string AssemblyName,
    [property: JsonIgnore] string? SymbolId = null,
    string? HandoffId = null,
    string? OwnerTargetPath = null);

public sealed record FindAssemblyExtensionsPayload(
    string AssemblyPath,
    IReadOnlyList<AssemblyExtensionDto> Extensions,
    int TotalCount,
    bool Truncated,
    IReadOnlyList<string> Diagnostics,
    NavigationAnalysisMetadata? Analysis = null,
    [property: JsonPropertyName("resultCursor")] string? ResultCursor = null);

public sealed record ResolveTypeOriginRequest(
    string AssemblyPath,
    string TypeName,
    bool IncludeReferences = true);

public sealed record ResolveTypeOriginPayload(
    string TypeName,
    string OriginKind,
    string? AssemblyName,
    string? AssemblyPath,
    string? PackageId,
    string? PackageVersion,
    bool IsAmbiguous,
    IReadOnlyList<string> CandidatePaths,
    IReadOnlyList<string> Diagnostics,
    AiNetCodeNavigator.Core.Models.NavigationAnalysisMetadata? Analysis = null);

public sealed record SourceTypeOriginLocation(string FilePath, int Line, int Column);

public sealed record SourceTypeOriginPayload(
    string TypeName,
    bool Found,
    string TargetPath,
    string? ProjectName,
    IReadOnlyList<SourceTypeOriginLocation> SourceLocations,
    string? AssemblyOrigin,
    string? OutputAssembly,
    string Namespace,
    IReadOnlyList<string> SearchedAssemblies,
    bool IsAmbiguous = false,
    IReadOnlyList<string>? CandidatePaths = null);
