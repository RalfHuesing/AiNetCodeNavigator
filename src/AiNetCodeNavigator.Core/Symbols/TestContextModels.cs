#nullable enable

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record TestMethodMatch(
    string MethodName,
    int Line,
    string? HandoffId = null,
    IReadOnlyList<TestCandidateEvidence>? Evidence = null)
{
    public string? QualifiedName { get; init; }
    public string? FilePath { get; init; }
    public int Column { get; init; }
}

/// <summary>One proven static source use in a test-to-helper-to-endpoint path.</summary>
public sealed record TestHelperPathStep(
    string Caller, string Target, string RelationshipKind, string ProjectPath,
    string FilePath, int Line, int Column, string? CallerHandoffId, string? TargetHandoffId)
{
    public string? DispatchLimitation { get; init; }
}

/// <summary>A source-located static reason why a test fixture or method was returned as a candidate.</summary>
public sealed record TestCandidateEvidence(
    string EvidenceType,
    string SourceSymbol,
    string FilePath,
    int Line,
    int Column,
    string? ProjectIdentity = null,
    string? HandoffId = null)
{
    public IReadOnlyList<TestHelperPathStep>? HelperPath { get; init; }
}

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
    string? ProjectName = null,
    IReadOnlyList<TestCandidateEvidence>? Evidence = null,
    string? ProjectIdentity = null)
{
    internal ProjectId? SourceProjectId { get; init; }
    public string? ProjectPath { get; init; }
    public string? QualifiedName { get; init; }
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

    public int ExpandedImplementationCount { get; init; }
    public bool ImplementationExpansionLimitReached { get; init; }
    public bool CandidateExpansionLimitReached { get; init; }
    public bool ReferenceInspectionLimitReached { get; init; }
    public int TestHelperDepth { get; init; }
    public int ExpandedHelperCount { get; init; }
    public bool HelperExpansionLimitReached { get; init; }
}
