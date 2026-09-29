#nullable enable

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record FeatureContextDeclaration(
    string SymbolName,
    string Kind,
    string Signature,
    string Modifiers,
    string FilePath,
    int Line,
    int EndLine,
    string? DocCommentId = null,
    string? HandoffId = null);

public sealed record FeatureContextCallerEntry(
    string CallerName,
    string CallerKind,
    string? CallerHandoffId,
    string FilePath,
    int Line,
    string ProjectName);

public sealed record FeatureContextTestRecommendation(
    string FixtureName,
    string TestMethod,
    string FilePath,
    int Line,
    string Framework,
    string? HandoffId = null);

public sealed record FeatureContextPayload(
    FeatureContextDeclaration Declaration,
    IReadOnlyList<FeatureContextCallerEntry> Callers,
    IReadOnlyList<FeatureContextTestRecommendation> Tests,
    int TotalCallers,
    int TotalTests,
    bool CallersTruncated,
    bool TestsTruncated);

public sealed record FeatureContextRequest(
    Solution Solution,
    string SymbolIdentifier,
    int MaxCallers = 20,
    int MaxTests = 20,
    SymbolScopeType Scope = SymbolScopeType.All);
