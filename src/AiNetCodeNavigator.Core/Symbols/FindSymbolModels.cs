#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using AiNetCodeNavigator.Core.Models;

namespace AiNetCodeNavigator.Core.Symbols;

public enum SymbolScopeType
{
    All,
    Production,
    Tests
}

public enum SymbolKindFilter
{
    All,
    Class,
    Struct,
    Interface,
    Enum,
    Delegate,
    Record,
    RecordClass,
    RecordStruct,
    Method,
    Property,
    Field,
    Event
}

public sealed record SymbolLocationEntry(
    string Name,
    string Kind,
    string? DocCommentId,
    string? HandoffId,
    string FilePath,
    int Line,
    int EndLine,
    string ProjectName,
    string Signature,
    IReadOnlyList<SymbolLocationItem>? Locations = null,
    string? OwnerTargetPath = null);

public sealed record SymbolLocationItem(
    string FilePath,
    int Line,
    int EndLine,
    string ProjectName);

public sealed record FindSymbolScanRequest(
    Solution Solution,
    string NamePattern,
    SymbolKindFilter Kind = SymbolKindFilter.All,
    SymbolScopeType ScopeType = SymbolScopeType.All,
    int MaxResults = 50,
    AnalysisSymbolIdentity? AssemblyIdentity = null,
    AnalysisSymbolIdentity? SourceIdentity = null,
    bool IncludeGenerated = false,
    string? ResultCursor = null);

public sealed record FindSymbolScanResult(
    string Text,
    IReadOnlyList<SymbolLocationEntry> Entries,
    int TotalMatches,
    int ReturnedMatches,
    bool IsTruncated,
    IReadOnlyList<string> TruncatedBy,
    IReadOnlyList<string> KindAlternatives,
    ResultError? Error = null,
    string? ResultCursor = null);
