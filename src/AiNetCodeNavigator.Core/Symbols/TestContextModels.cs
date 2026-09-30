#nullable enable

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record TestMethodMatch(
    string MethodName,
    int Line,
    string? HandoffId = null);

/// <summary>
/// A statically inferred test fixture candidate. The match is heuristic evidence, not proof of execution or coverage.
/// </summary>
public sealed record TestFixtureMatch(
    string ClassName,
    string FilePath,
    int Line,
    string Framework,
    IReadOnlyList<TestMethodMatch> Methods,
    string? HandoffId = null,
    string? ProjectName = null)
{
    internal ProjectId? SourceProjectId { get; init; }
}

/// <summary>
/// Statically inferred test candidates for a symbol; this payload is not evidence of test execution or coverage.
/// </summary>
public sealed record TestContextPayload(
    string TargetSymbol,
    string TargetKind,
    IReadOnlyList<TestFixtureMatch> TestFixtures,
    int TotalTestFixtures,
    int TotalTestMethods)
{
    public const string StaticTestCandidatesOnlyEvidenceMode = "static-test-candidates-only";

    /// <summary>Identifies the payload as static heuristic candidates, not execution or coverage evidence.</summary>
    public string EvidenceMode { get; } = StaticTestCandidatesOnlyEvidenceMode;
}
