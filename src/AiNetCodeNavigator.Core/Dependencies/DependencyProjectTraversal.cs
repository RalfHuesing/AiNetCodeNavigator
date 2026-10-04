#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Dependencies;

public sealed record DependencyProjectIdentity(string ProjectId, string Name, string? ProjectPath, string? OwnerContextFingerprint);
public sealed record DependencyProjectReference(string FromProjectId, string ToProjectId, int Depth, string Origin = "ProjectReference");
public sealed record DependencyProjectGraph(DependencyProjectIdentity Root, IReadOnlyList<DependencyProjectIdentity> Projects,
    IReadOnlyList<DependencyProjectReference> ProjectDependencies, int TotalProjectDependencyCount,
    DependencyGraphDirection Direction, int RequestedDepth, int PageSize, int VisitedProjectCount,
    int EffectiveNodeLimit, bool NodeLimitReached, int HiddenProjectDependencyCount)
{
    public string Level => "project";
    public string RootSemantics => "Loaded project references from the exact owning project; build relationships, not observed calls.";
    public bool HasMore => ProjectDependencies.Count < TotalProjectDependencyCount;
    public bool IsTruncated => HasMore || NodeLimitReached;
    public bool IsComplete => !IsTruncated;
}

/// <summary>Traverses loaded ProjectReference facts without collecting semantic document dependencies.</summary>
public static class DependencyProjectTraversal
{
    public static DependencyProjectGraph Traverse(Solution solution, ProjectId rootProjectId,
        DependencyGraphDirection direction, int depth, int pageSize,
        IReadOnlyDictionary<ProjectId, string>? ownerContextFingerprints = null,
        int maxNodes = DependencyGraphScanner.MaximumNodes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        if (solution.GetProject(rootProjectId) is null) throw new ArgumentException("The root must be a loaded project.", nameof(rootProjectId));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (depth is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(depth));
        if (pageSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (maxNodes < 1) throw new ArgumentOutOfRangeException(nameof(maxNodes));
        var nodeLimit = Math.Min(maxNodes, DependencyGraphScanner.MaximumNodes);
        var projects = solution.Projects.ToDictionary(project => project.Id, project => new DependencyProjectIdentity(
            project.Id.Id.ToString("D"), project.Name,
            string.IsNullOrWhiteSpace(project.FilePath) ? null : CanonicalPath(project.FilePath),
            ownerContextFingerprints?.GetValueOrDefault(project.Id)));
        var references = solution.Projects.SelectMany(project => project.ProjectReferences
                .Where(reference => projects.ContainsKey(reference.ProjectId))
                .Select(reference => (From: project.Id, To: reference.ProjectId)))
            .Distinct().OrderBy(edge => projects[edge.From].ProjectPath, StringComparer.Ordinal)
            .ThenBy(edge => projects[edge.From].OwnerContextFingerprint, StringComparer.Ordinal)
            .ThenBy(edge => projects[edge.From].ProjectId, StringComparer.Ordinal)
            .ThenBy(edge => projects[edge.To].ProjectPath, StringComparer.Ordinal)
            .ThenBy(edge => projects[edge.To].OwnerContextFingerprint, StringComparer.Ordinal)
            .ThenBy(edge => projects[edge.To].ProjectId, StringComparer.Ordinal).ToArray();
        var admitted = new HashSet<ProjectId> { rootProjectId };
        var edges = new Dictionary<(ProjectId From, ProjectId To), int>();
        var hidden = new HashSet<(ProjectId From, ProjectId To)>();
        if (direction is DependencyGraphDirection.Outgoing or DependencyGraphDirection.Both) Walk(incoming: false);
        if (direction is DependencyGraphDirection.Incoming or DependencyGraphDirection.Both) Walk(incoming: true);
        var page = edges.OrderBy(edge => edge.Value)
            .ThenBy(edge => projects[edge.Key.From].ProjectPath, StringComparer.Ordinal)
            .ThenBy(edge => projects[edge.Key.From].ProjectId, StringComparer.Ordinal)
            .ThenBy(edge => projects[edge.Key.To].ProjectPath, StringComparer.Ordinal)
            .ThenBy(edge => projects[edge.Key.To].ProjectId, StringComparer.Ordinal)
            .Take(pageSize).Select(edge => new DependencyProjectReference(projects[edge.Key.From].ProjectId, projects[edge.Key.To].ProjectId, edge.Value)).ToArray();
        var endpoints = page.SelectMany(edge => new[] { edge.FromProjectId, edge.ToProjectId }).ToHashSet(StringComparer.Ordinal);
        return new DependencyProjectGraph(projects[rootProjectId], projects.Values.Where(project => endpoints.Contains(project.ProjectId))
                .OrderBy(project => project.ProjectPath, StringComparer.Ordinal).ThenBy(project => project.ProjectId, StringComparer.Ordinal).ToArray(),
            page, edges.Count, direction, depth, pageSize, admitted.Count, nodeLimit, hidden.Count > 0, hidden.Count);

        void Walk(bool incoming)
        {
            var seen = new HashSet<ProjectId> { rootProjectId };
            var queue = new Queue<(ProjectId Owner, int Depth)>();
            queue.Enqueue((rootProjectId, 0));
            while (queue.TryDequeue(out var current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (current.Depth == depth) continue;
                foreach (var edge in references.Where(edge => incoming ? edge.To == current.Owner : edge.From == current.Owner))
                {
                    var next = incoming ? edge.From : edge.To;
                    if (!admitted.Contains(next))
                    {
                        if (admitted.Count == nodeLimit) { hidden.Add(edge); continue; }
                        admitted.Add(next);
                    }
                    hidden.Remove(edge);
                    var edgeDepth = current.Depth + 1;
                    if (!edges.TryGetValue(edge, out var previous) || edgeDepth < previous) edges[edge] = edgeDepth;
                    if (seen.Add(next)) queue.Enqueue((next, edgeDepth));
                }
            }
        }
    }

    private static string CanonicalPath(string path)
    {
        var canonical = Path.GetFullPath(path).Replace('\\', '/');
        return OperatingSystem.IsWindows() ? canonical.ToUpperInvariant() : canonical;
    }
}
