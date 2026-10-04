using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Core.Models;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Mcp;

internal sealed record NavigationOperationProgressSnapshot(
    long ElapsedMilliseconds, NavigationAnalysisPhase Phase, long? ProcessedDocuments = null, long? TotalDocuments = null);

/// <summary>Request-owned adapter for measured Core events and immutable running snapshots.</summary>
internal sealed class NavigationOperationProgress(TimeProvider timeProvider)
{
    private static readonly AsyncLocal<NavigationOperationProgress?> Ambient = new();
    private readonly object gate = new();
    private readonly long started = timeProvider.GetTimestamp();
    private readonly HashSet<(long Ticket, ProjectId Project, DocumentId Document)> satisfied = [];
    private NavigationAnalysisPhase phase = NavigationAnalysisPhase.Loading;
    private long elapsedMilliseconds;
    private long? totalDocuments;
    private bool documentsStarted;

    internal static NavigationOperationProgress? Current => Ambient.Value;

    internal static IDisposable Enter(NavigationOperationProgress progress)
    {
        var previous = Ambient.Value;
        Ambient.Value = progress;
        return new Scope(previous);
    }

    internal void Advance(NavigationAnalysisPhase next)
    {
        lock (gate)
            if (next > phase) phase = next;
    }

    internal void Report(DependencyGraphProgress progress)
    {
        lock (gate)
        {
            documentsStarted = true;
            switch (progress)
            {
                case DependencyRequiredDocumentsKnown known:
                    if (known.TotalDocuments < satisfied.Count || totalDocuments is { } total && total != known.TotalDocuments)
                        throw new InvalidOperationException("A published dependency document total cannot change or undercount coverage.");
                    totalDocuments = known.TotalDocuments;
                    break;
                case DependencyDocumentSatisfied document:
                    var need = (document.SnapshotTicket, document.ProjectId, document.DocumentId);
                    if (!satisfied.Contains(need) && totalDocuments is { } limit && satisfied.Count >= limit)
                        throw new InvalidOperationException("Satisfied dependency needs cannot exceed the published total.");
                    satisfied.Add(need);
                    break;
            }
        }
    }

    internal NavigationOperationProgressSnapshot Snapshot()
    {
        lock (gate)
        {
            var ticks = unchecked((ulong)(timeProvider.GetTimestamp() - started));
            var measured = (decimal)ticks * 1000 / timeProvider.TimestampFrequency;
            var milliseconds = measured >= long.MaxValue ? long.MaxValue : (long)measured;
            elapsedMilliseconds = Math.Max(elapsedMilliseconds, milliseconds);
            return new(elapsedMilliseconds, phase, documentsStarted ? satisfied.Count : null, totalDocuments);
        }
    }

    private sealed class Scope(NavigationOperationProgress? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
