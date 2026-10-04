#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AiNetCodeNavigator.Core.Symbols;

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

        var windows = pages.GroupBy(page => page.DocumentOffset).OrderBy(group => group.Key).ToList();
        var rawEdges = new Dictionary<(string From, string To), DependencyTypeReference>();
        var projectDependencies = pages.SelectMany(page => page.ProjectDependencies).Distinct()
            .OrderBy(edge => edge.FromProject, StringComparer.Ordinal).ThenBy(edge => edge.ToProject, StringComparer.Ordinal).ToList();
        var typeDeclarations = windows
            .SelectMany(window => window.OrderBy(page => page.Offset).First().TypeDeclarations)
            .GroupBy(declaration => declaration.TypeId, StringComparer.Ordinal)
            .Select(group => group.First() with
            {
                DeclarationDocuments = group.SelectMany(declaration => declaration.DeclarationDocuments).ToImmutableArray()
            })
            .OrderBy(declaration => declaration.TypeId, StringComparer.Ordinal)
            .ToImmutableArray();
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

            foreach (var page in orderedPages)
            {
                var edges = page.TypeDependencies ?? [];
                foreach (var edge in edges)
                {
                    var key = (edge.FromTypeId, edge.ToTypeId);
                    if (!rawEdges.ContainsKey(key)) rawEdges.Add(key, edge with { Depth = 1 });
                }
            }

            scannedDocumentCount += firstPage.ScannedDocumentCount;
            if (firstPage.NextDocumentOffset is int next)
            {
                expectedDocumentOffset = next;
                finalDocumentCursor = next;
            }
            else
            {
                expectedDocumentOffset = totalDocumentCount;
                finalDocumentCursor = null;
            }
        }

        if (windows[0].Key != 0)
            continuationInputIncomplete = true;
        nextDocumentOffset = missingDocumentOffset ?? finalDocumentCursor;

        var edgesForTraversal = rawEdges.Values
            .OrderBy(edge => edge.FromProject, StringComparer.Ordinal).ThenBy(edge => edge.FromFile, StringComparer.Ordinal)
            .ThenBy(edge => edge.FromTypeId, StringComparer.Ordinal).ThenBy(edge => edge.ToTypeId, StringComparer.Ordinal).ToList();
        var collection = new DependencyGraphCollection(
            edgesForTraversal.ToImmutableArray(),
            projectDependencies.ToImmutableArray(),
            errors.ToImmutableArray(),
            ImmutableArray<DependencyDocumentIdentity>.Empty,
            ImmutableArray<DependencyDocumentIdentity>.Empty,
            ImmutableArray<DependencyDocumentIdentity>.Empty,
            ImmutableArray<DependencyDocumentIdentity>.Empty,
            typeDeclarations,
            totalDocumentCount,
            scannedDocumentCount,
            scannedDocumentCount,
            scannedDocumentCount,
            0,
            nextDocumentOffset,
            false,
            SymbolScopeType.All,
            false,
            string.Empty,
            continuationInputIncomplete);
        return DependencyGraphScanner.Project(collection, new DependencyGraphProjectionOptions(
            options.Offset,
            options.PageSize,
            options.TargetFilePath,
            options.TargetTypeName,
            options.TargetProject,
            options.Direction,
            options.Depth,
            options.MaxNodes,
            options.TargetTypeId,
            options.TargetTypeIds));
    }

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
