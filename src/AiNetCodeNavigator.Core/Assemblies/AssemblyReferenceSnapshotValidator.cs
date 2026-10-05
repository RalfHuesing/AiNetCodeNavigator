#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;

namespace AiNetCodeNavigator.Core.Assemblies;

internal static class AssemblyReferenceSnapshotValidator
{
    internal enum OwnerValidationStatus
    {
        Matches,
        Incomplete,
        Stale,
    }

    internal static OwnerValidationStatus ValidateOwner(
        AssemblyContext root,
        string ownerPath,
        AssemblyContext owner,
        out ResultError? error)
    {
        error = null;
        var fullOwnerPath = Path.GetFullPath(ownerPath);
        var expectedReference = root.References.FirstOrDefault(reference =>
            reference.ResolvedPath is not null
            && string.Equals(Path.GetFullPath(reference.ResolvedPath), fullOwnerPath, StringComparison.OrdinalIgnoreCase));
        if (expectedReference is null
            || owner.Identity is null
            || !AssemblyReferenceResolver.IdentityMatches(expectedReference, owner.Identity)
            || !string.Equals(expectedReference.ContentHash, owner.Origin.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            error = Stale(fullOwnerPath, "does not match the captured root reference identity or PE content");
            return OwnerValidationStatus.Stale;
        }

        var (matches, rootHasBoundary) = ReferenceSubgraphMatches(root.References, fullOwnerPath, owner.References);
        if (matches) return OwnerValidationStatus.Matches;
        if (rootHasBoundary) return OwnerValidationStatus.Incomplete;
        error = Stale(fullOwnerPath, "has a different transitive reference snapshot than the root assembly");
        return OwnerValidationStatus.Stale;
    }

    internal static (bool Matches, bool RootHasBoundary) ReferenceSubgraphMatches(
        IReadOnlyList<AssemblyReferenceDto> rootReferences,
        string ownerPath,
        IReadOnlyList<AssemblyReferenceDto> ownerReferences)
    {
        static (HashSet<string> Edges, bool HasBoundary) GetEdges(
            IReadOnlyList<AssemblyReferenceDto> references,
            string rootPath)
        {
            var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(rootPath) };
            var edges = new HashSet<string>(StringComparer.Ordinal);
            var hasBoundary = false;
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var reference in references)
                {
                    if (reference.SourceAssemblyPath is null
                        || !reachable.Contains(Path.GetFullPath(reference.SourceAssemblyPath))) continue;
                    var source = Path.GetFullPath(reference.SourceAssemblyPath);
                    var resolvedPath = reference.ResolvedPath is null ? string.Empty : Path.GetFullPath(reference.ResolvedPath);
                    // These labels describe how this root traversed an already proven immutable binding.
                    var bindingState = reference.Resolved && reference.ResolvedPath is not null
                        && !string.IsNullOrWhiteSpace(reference.ContentHash)
                        && reference.ResolutionState is "resolved" or "deduplicated" or "cycle"
                            ? "resolved"
                            : reference.ResolutionState;
                    edges.Add(string.Join("|", source, reference.Name, reference.Version, reference.Culture,
                        reference.PublicKeyToken, bindingState, resolvedPath, reference.ContentHash ?? string.Empty));
                    hasBoundary |= reference.ResolutionState is "depth_limit" or "invalid";
                    if (reference.ResolvedPath is not null && reachable.Add(resolvedPath)) changed = true;
                }
            }

            return (edges, hasBoundary);
        }

        var root = GetEdges(rootReferences, ownerPath);
        var owner = GetEdges(ownerReferences, ownerPath);
        return (root.Edges.SetEquals(owner.Edges), root.HasBoundary);
    }

    private static ResultError Stale(string ownerPath, string reason) => new(
        NavigationErrorCodes.StaleSnapshot,
        $"Referenced assembly '{Path.GetFileName(ownerPath)}' changed after the root assembly snapshot was captured; it {reason}.",
        "Repeat the query so the root and referenced assembly snapshots can be acquired together.");
}
