namespace AiNetCodeNavigator.Core.Models;

/// <summary>Stable identity and explicit limits of the analysis that produced a navigation result.</summary>
public sealed record NavigationAnalysisMetadata(
    string SnapshotId,
    string AnalyzedScope,
    IReadOnlyList<string> OmissionReasons,
    string AnalysisCompleteness = "complete",
    bool ResultContinuationAvailable = false)
{
    public static string CreateSnapshotId(string ownerKind, string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        var shortHash = contentHash.Length <= 24 ? contentHash : contentHash[..24];
        return $"{ownerKind}:{shortHash}";
    }
}
