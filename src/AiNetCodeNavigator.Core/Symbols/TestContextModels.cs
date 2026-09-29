#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record TestMethodMatch(
    string MethodName,
    int Line,
    string? HandoffId = null);

public sealed record TestFixtureMatch(
    string ClassName,
    string FilePath,
    int Line,
    string Framework,
    IReadOnlyList<TestMethodMatch> Methods,
    string? HandoffId = null);

public sealed record TestContextPayload(
    string TargetSymbol,
    string TargetKind,
    IReadOnlyList<TestFixtureMatch> TestFixtures,
    int TotalTestFixtures,
    int TotalTestMethods);
