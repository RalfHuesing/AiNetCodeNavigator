#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace AiNetCodeNavigator.Core.Dependencies;

/// <summary>
/// Combines bounded scanner windows and traverses the resulting type graph.
/// </summary>
public static class DependencyGraphTraversal
{
    public static DependencyGraphPayload MergeAndTraverse(
        IEnumerable<DependencyGraphPayload> scanPages,
        DependencyGraphTraversalOptions options)
    {
        ArgumentNullException.ThrowIfNull(scanPages);
        ArgumentNullException.ThrowIfNull(options);
        if (options.TargetFilePath is null && options.TargetTypeName is null)
            throw new ArgumentException("A target file or type is required.", nameof(options));
        if (options.TargetFilePath is not null && options.TargetTypeName is not null)
            throw new ArgumentException("Specify either TargetFilePath or TargetTypeName, not both.", nameof(options));
        if (options.Offset < 0) throw new ArgumentOutOfRangeException(nameof(options), "Offset must be zero or greater.");
        if (options.PageSize < 1) throw new ArgumentOutOfRangeException(nameof(options), "PageSize must be at least one.");
        if (options.MaxNodes < 1) throw new ArgumentOutOfRangeException(nameof(options), "MaxNodes must be at least one.");
        if (!Enum.IsDefined(options.Direction)) throw new ArgumentOutOfRangeException(nameof(options), "Direction is invalid.");

        var pages = scanPages.ToList();
        if (pages.Count == 0) throw new ArgumentException("At least one scanner result is required.", nameof(scanPages));
        if (pages.Any(page => page.IsTargeted))
            throw new ArgumentException("Merge unfiltered scanner pages so traversal can span document windows.", nameof(scanPages));

        var pageSize = Math.Min(options.PageSize, DependencyGraphScanner.MaximumPageSize);
        var nodeLimit = Math.Min(options.MaxNodes, DependencyGraphScanner.MaximumNodes);
        var windows = pages.GroupBy(page => page.DocumentOffset).OrderBy(group => group.Key).ToList();
        var rawEdges = new Dictionary<(string From, string To), DependencyTypeReference>();
        var projectDependencies = pages.SelectMany(page => page.ProjectDependencies).Distinct()
            .OrderBy(edge => edge.FromProject, StringComparer.Ordinal).ThenBy(edge => edge.ToProject, StringComparer.Ordinal).ToList();
        var errors = pages.SelectMany(page => page.Errors ?? []).Distinct().ToList();
        var totalDocumentCount = pages.Max(page => page.TotalDocumentCount);
        var scannedDocumentCount = 0;
        var continuationInputIncomplete =
            !ArePagesComplete(pages, page => page.TotalProjectDependencyCount, page => page.ProjectDependencies.Count) ||
            !ArePagesComplete(pages, page => page.TotalNamespaceDependencyCount, page => page.NamespaceDependencies.Count) ||
            !ArePagesComplete(pages, page => page.TotalFileDependencyCount, page => page.FileDependencies.Count) ||
            !ArePagesComplete(pages, page => page.TotalTypeDependencyCount, page => page.TypeDependencies?.Count ?? 0);
        int? nextDocumentOffset = null;
        int? missingDocumentOffset = null;
        int? finalDocumentCursor = null;
        var expectedDocumentOffset = 0;

        foreach (var window in windows)
        {
            var orderedPages = window.OrderBy(page => page.Offset).ToList();
            var firstPage = orderedPages[0];
            if (window.Key != expectedDocumentOffset || orderedPages.Any(page => page.ScannedDocumentCount != firstPage.ScannedDocumentCount || page.TotalDocumentCount != firstPage.TotalDocumentCount || page.NextDocumentOffset != firstPage.NextDocumentOffset))
            {
                continuationInputIncomplete = true;
                missingDocumentOffset ??= expectedDocumentOffset;
            }

            var expectedRelationshipOffset = 0;
            var expectedRelationshipTotal = firstPage.TotalTypeDependencyCount;
            var lastPage = firstPage;
            foreach (var page in orderedPages)
            {
                if (page.TotalTypeDependencyCount != expectedRelationshipTotal || page.Offset != expectedRelationshipOffset)
                    continuationInputIncomplete = true;
                var edges = page.TypeDependencies ?? [];
                foreach (var edge in edges)
                {
                    var key = (edge.FromTypeId, edge.ToTypeId);
                    if (!rawEdges.ContainsKey(key)) rawEdges.Add(key, edge with { Depth = 1 });
                }
                expectedRelationshipOffset = page.Offset + edges.Count;
                lastPage = page;
            }
            if (expectedRelationshipOffset != expectedRelationshipTotal) continuationInputIncomplete = true;

            scannedDocumentCount += firstPage.ScannedDocumentCount;
            if (firstPage.NextDocumentOffset is int next)
            {
                expectedDocumentOffset = next;
                finalDocumentCursor = next;
            }
            else
            {
                expectedDocumentOffset = firstPage.DocumentOffset + firstPage.ScannedDocumentCount;
                finalDocumentCursor = null;
                if (lastPage.NextDocumentOffset is not null) continuationInputIncomplete = true;
            }
        }

        if (windows[0].Key != 0 || scannedDocumentCount != totalDocumentCount)
            continuationInputIncomplete = true;
        nextDocumentOffset = missingDocumentOffset ?? finalDocumentCursor;
        if (scannedDocumentCount < totalDocumentCount && nextDocumentOffset is null)
            nextDocumentOffset = expectedDocumentOffset;

        var edgesForTraversal = rawEdges.Values
            .OrderBy(edge => edge.FromProject, StringComparer.Ordinal).ThenBy(edge => edge.FromFile, StringComparer.Ordinal)
            .ThenBy(edge => edge.FromTypeId, StringComparer.Ordinal).ThenBy(edge => edge.ToTypeId, StringComparer.Ordinal).ToList();
        var requestedDepth = options.Depth;
        var effectiveDepth = Math.Clamp(requestedDepth, 1, DependencyGraphScanner.MaximumDepth);
        var traversal = DependencyGraphScanner.Traverse(
            edgesForTraversal,
            options.TargetFilePath,
            options.TargetTypeName,
            options.TargetProject,
            options.Direction,
            string.Empty,
            effectiveDepth,
            nodeLimit);
        var selectedEdges = traversal.Edges;

        var namespaceDependencies = selectedEdges
            .Where(edge => !string.IsNullOrEmpty(edge.FromNamespace) && !string.IsNullOrEmpty(edge.ToNamespace) && edge.FromNamespace != edge.ToNamespace)
            .GroupBy(edge => (edge.FromProject, edge.FromNamespace, edge.ToProject, edge.ToNamespace))
            .Select(group => new NamespaceDependency(group.Key.FromNamespace, group.Key.ToNamespace,
                group.Select(edge => edge.ToTypeName).Distinct(StringComparer.Ordinal).OrderBy(type => type, StringComparer.Ordinal).ToList(),
                group.Key.FromProject, group.Key.ToProject))
            .OrderBy(edge => edge.FromProject, StringComparer.Ordinal).ThenBy(edge => edge.FromNamespace, StringComparer.Ordinal)
            .ThenBy(edge => edge.ToProject, StringComparer.Ordinal).ThenBy(edge => edge.ToNamespace, StringComparer.Ordinal).ToList();
        var fileDependencies = selectedEdges
            .GroupBy(edge => (edge.FromProject, edge.FromFile, edge.ToProject, edge.ToFile))
            .Select(group => new FileDependency(group.Key.FromFile, group.Key.ToFile,
                group.Select(edge => edge.ToTypeName).Distinct(StringComparer.Ordinal).OrderBy(type => type, StringComparer.Ordinal).ToList(),
                group.Key.FromProject, group.Key.ToProject))
            .OrderBy(edge => edge.FromProject, StringComparer.Ordinal).ThenBy(edge => edge.FromFile, StringComparer.Ordinal)
            .ThenBy(edge => edge.ToProject, StringComparer.Ordinal).ThenBy(edge => edge.ToFile, StringComparer.Ordinal).ToList();

        var pagedProjects = Page(projectDependencies, options.Offset, pageSize);
        var pagedNamespaces = Page(namespaceDependencies, options.Offset, pageSize);
        var pagedFiles = Page(fileDependencies, options.Offset, pageSize);
        var pagedTypes = Page(selectedEdges, options.Offset, pageSize);
        var pageHadClamp = pageSize != options.PageSize;
        return new DependencyGraphPayload(
            ProjectDependencies: pagedProjects,
            NamespaceDependencies: pagedNamespaces,
            FileDependencies: pagedFiles,
            TotalProjectDependencyCount: projectDependencies.Count,
            TotalNamespaceDependencyCount: namespaceDependencies.Count,
            TotalFileDependencyCount: fileDependencies.Count,
            Offset: options.Offset,
            PageSize: pageSize,
            ScannedDocumentCount: scannedDocumentCount,
            TotalDocumentCount: totalDocumentCount,
            DocumentLimitReached: nextDocumentOffset is not null,
            PageSizeWasClamped: pageHadClamp,
            Errors: errors,
            TypeDependencies: pagedTypes,
            TotalTypeDependencyCount: selectedEdges.Count,
            DocumentOffset: 0,
            NextDocumentOffset: nextDocumentOffset,
            Direction: options.Direction,
            RequestedDepth: requestedDepth,
            EffectiveDepth: effectiveDepth,
            IsDepthClamped: requestedDepth != effectiveDepth,
            IsTargeted: true,
            TargetFilePath: options.TargetFilePath,
            TargetTypeName: options.TargetTypeName,
            VisitedTypeCount: traversal.VisitedTypeCount,
            EffectiveNodeLimit: nodeLimit,
            IsNodeLimitClamped: options.MaxNodes != nodeLimit,
            NodeLimitReached: traversal.NodeLimitReached,
            HiddenTypeDependencyCount: traversal.HiddenTypeDependencyCount,
            ContinuationInputIncomplete: continuationInputIncomplete);
    }

    private static IReadOnlyList<T> Page<T>(IReadOnlyList<T> items, int offset, int pageSize) =>
        items.Skip(offset).Take(pageSize).ToList();

    private static bool ArePagesComplete(
        IReadOnlyList<DependencyGraphPayload> pages,
        Func<DependencyGraphPayload, int> totalSelector,
        Func<DependencyGraphPayload, int> countSelector)
    {
        foreach (var window in pages.GroupBy(page => page.DocumentOffset))
        {
            var ordered = window.OrderBy(page => page.Offset).ToList();
            var total = totalSelector(ordered[0]);
            if (total < 0) return false;

            long expectedPageOffset = 0;
            var coveredThrough = 0;
            foreach (var page in ordered)
            {
                if (totalSelector(page) != total || page.Offset != expectedPageOffset || page.PageSize < 1)
                    return false;

                var expectedCount = (int)Math.Min(page.PageSize, Math.Max(0L, (long)total - page.Offset));
                if (countSelector(page) != expectedCount) return false;

                coveredThrough = Math.Max(coveredThrough, (int)Math.Min(total, (long)page.Offset + page.PageSize));
                expectedPageOffset = (long)page.Offset + page.PageSize;
            }
            if (coveredThrough != total) return false;
        }
        return true;
    }
}
