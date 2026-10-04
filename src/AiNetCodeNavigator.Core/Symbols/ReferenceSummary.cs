using System;
using System.Collections.Generic;
using System.Linq;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record ReferenceProjectIdentity(string? OwnerTargetPath, string? ProjectPath, string? ProjectIdentity, string ProjectName);
public sealed record ReferenceFileIdentity(string? OwnerTargetPath, string? ProjectPath, string? ProjectIdentity, string FilePath);

/// <summary>Aggregates the discovered reference sites before display paging, not unique callers or runtime consequences.</summary>
public sealed record ReferenceSummary(
    int DirectReferenceSiteCount,
    int DeeperReferenceSiteCount,
    int TotalReferenceSiteCount,
    int MaxDepthReached,
    int ProjectCount,
    int FileCount,
    IReadOnlyList<ReferenceProjectIdentity> Projects,
    IReadOnlyList<ReferenceFileIdentity> Files,
    bool AnalysisComplete,
    IReadOnlyList<string> Omissions)
{
    public static ReferenceSummary Create(IReadOnlyList<ReferenceLocationEntry> sites, IReadOnlyList<string> omissions, string? ownerTargetPath = null)
    {
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(omissions);
        var projects = sites.Select(site => new ReferenceProjectIdentity(site.OwnerTargetPath ?? ownerTargetPath, site.ProjectPath, site.ProjectIdentity, site.ProjectName))
            .Distinct().OrderBy(item => item.OwnerTargetPath, StringComparer.Ordinal)
            .ThenBy(item => item.ProjectIdentity, StringComparer.Ordinal).ThenBy(item => item.ProjectName, StringComparer.Ordinal).ToArray();
        var files = sites.Select(site => new ReferenceFileIdentity(site.OwnerTargetPath ?? ownerTargetPath, site.ProjectPath, site.ProjectIdentity, site.FilePath))
            .Distinct().OrderBy(item => item.OwnerTargetPath, StringComparer.Ordinal)
            .ThenBy(item => item.ProjectIdentity, StringComparer.Ordinal).ThenBy(item => item.FilePath, StringComparer.Ordinal).ToArray();
        return new(sites.Count(site => site.Depth == 1), sites.Count(site => site.Depth > 1), sites.Count,
            sites.Count == 0 ? 0 : sites.Max(site => site.Depth), projects.Length, files.Length, projects, files,
            omissions.Count == 0, omissions);
    }
}
