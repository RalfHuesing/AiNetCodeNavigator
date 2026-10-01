#nullable enable

using System;

namespace AiNetCodeNavigator.Core.Workspace;

public enum AnalysisTargetType
{
    Project,
    Assembly,
}

public enum AnalysisTargetOrigin
{
    Source,
    Decompiled,
}

/// <summary>
/// Request model for the unified targetPath-only contract.
/// </summary>
public sealed record AnalysisTargetRequest(string? TargetPath);

/// <summary>
/// Structured error during target resolution.
/// </summary>
public sealed record AnalysisTargetError(
    string Code,
    string Message,
    string? Hint = null,
    string? Context = null,
    string? FieldPath = null)
{
    public string FormattedMessage =>
        NavigatorErrorFormatter.Format(Code, Message, Context, Hint, FieldPath);

    public string StatusLine => "Status: operation=error, completeness=not_applicable";

    public string FullMessage => $"{FormattedMessage}\n{StatusLine}";
}

/// <summary>
/// Result of target resolution.
/// </summary>
public sealed record AnalysisTargetResolution(
    AnalysisTarget? Target,
    AnalysisTargetError? Error)
{
    public bool Succeeded => Target is not null;
}

/// <summary>
/// Resolved analysis target (either a source project or a compiled assembly).
/// </summary>
public sealed record AnalysisTarget(
    AnalysisTargetType TargetType,
    string CanonicalPath,
    AnalysisTargetRequest Request)
{
    public AnalysisTargetOrigin Origin =>
        TargetType == AnalysisTargetType.Project
            ? AnalysisTargetOrigin.Source
            : AnalysisTargetOrigin.Decompiled;

    public string AnalysisRoot { get; init; } = string.Empty;

    public string Fingerprint { get; init; } = string.Empty;

    public string? AnalysisSnapshotFingerprint { get; init; }

    public string AnalysisSnapshotKind { get; init; } = "target-file";

    public bool AnalysisSnapshotFresh { get; init; }
}
