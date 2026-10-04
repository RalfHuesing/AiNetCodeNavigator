#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Dependencies;

/// <summary>Schedules eligible declaration documents for admitted outgoing frontiers.</summary>
internal static class DependencyGraphOutgoingCollector
{
    internal static async Task<DependencyGraphCollection> CollectAsync(
        DependencyGraphCache cache,
        Solution solution,
        DependencyGraphCollectionOptions collectionOptions,
        DependencyGraphProjectionOptions projectionOptions,
        IReadOnlyList<INamedTypeSymbol> roots,
        string targetPath,
        long snapshotTicket,
        IReadOnlyDictionary<ProjectId, string>? ownerContextFingerprints = null,
        CancellationToken cancellationToken = default)
    {
        var plan = await DependencyGraphScanner.PrepareCollectionPlanAsync(solution, collectionOptions,
            ownerContextFingerprints, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (cache.GetFullRetainedCollection(plan, targetPath, snapshotTicket) is { } full) return full;

        var symbols = roots.GroupBy(type => DependencyGraphScanner.GetSourceTypeId(solution, type,
                plan.OwnerContextFingerprints, plan.GeneratedDocumentOwners), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var seedIds = symbols.Keys.ToArray();
        var required = new HashSet<DocumentId>();
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        var facts = new List<DependencyDocumentFact>();
        var errors = new List<DependencyGraphScanError>();
        var compilations = new Dictionary<ProjectId, Compilation?>();
        var newScans = 0;
        var incomplete = false;
        var depth = Math.Clamp(projectionOptions.Depth, 1, DependencyGraphScanner.MaximumDepth);
        var nodeLimit = Math.Min(projectionOptions.MaxNodes, DependencyGraphScanner.MaximumNodes);
        var collection = Merge();

        for (var distance = 0; distance < depth; distance++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var traversal = DependencyGraphScanner.Traverse(collection.TypeDependencies, null, null, null,
                null, seedIds, DependencyGraphDirection.Outgoing, plan.SolutionDirectory, depth, nodeLimit);
            var needs = new HashSet<DocumentId>();
            foreach (var typeId in traversal.AdmittedDistances.Where(pair => pair.Value == distance)
                         .Select(pair => pair.Key).OrderBy(value => value, StringComparer.Ordinal))
            {
                if (!expanded.Add(typeId)) continue;
                var type = symbols.GetValueOrDefault(typeId);
                if (type is null)
                {
                    var declaration = collection.TypeDeclarations.FirstOrDefault(candidate => candidate.TypeId == typeId);
                    type = declaration is null ? null : await ResolveAsync(declaration).ConfigureAwait(false);
                }
                if (type is null || type.DeclaringSyntaxReferences.IsEmpty)
                {
                    incomplete = true;
                    continue;
                }

                foreach (var syntax in type.OriginalDefinition.DeclaringSyntaxReferences)
                {
                    var document = solution.GetDocument(syntax.SyntaxTree)
                        ?? plan.GeneratedDocumentOwners.GetValueOrDefault(syntax.SyntaxTree);
                    if (document is null)
                    {
                        incomplete = true;
                        continue;
                    }
                    if (plan.RequiredDocuments.Any(item => item.Document.Id == document.Id)
                        && required.Add(document.Id)) needs.Add(document.Id);
                }
            }

            if (needs.Count > 0)
            {
                var selected = plan with
                {
                    RequiredDocuments = plan.RequiredDocuments.Where(item => needs.Contains(item.Document.Id)).ToImmutableArray(),
                    DocumentOffset = 0,
                    NextDocumentOffset = null
                };
                var collected = await cache.CollectPlanAsync(selected, targetPath, snapshotTicket,
                    cancellationToken).ConfigureAwait(false);
                facts.AddRange(collected.DocumentFacts);
                errors.AddRange(collected.Errors);
                newScans += collected.NewSemanticScanCount;
            }
            collection = Merge();
        }
        return collection;

        DependencyGraphCollection Merge() => DependencyGraphScanner.CreateCollectionFromFacts(plan with
        {
            RequiredDocuments = plan.RequiredDocuments.Where(item => required.Contains(item.Document.Id)).ToImmutableArray(),
            DocumentOffset = 0,
            NextDocumentOffset = null,
            ContinuationInputIncomplete = incomplete
        }, facts, errors, newScans);

        async Task<INamedTypeSymbol?> ResolveAsync(DependencyTypeDeclaration declaration)
        {
            var owners = solution.Projects.Where(project =>
                AnalysisPathIdentity.TryNormalize(project.FilePath, out var path)
                && path == declaration.OwnerProjectPath
                && (plan.OwnerContextFingerprints.GetValueOrDefault(project.Id) ?? string.Empty) == declaration.OwnerContextFingerprint)
                .ToArray();
            if (owners.Length != 1 || declaration.DocumentationCommentId.Length == 0) return null;
            var owner = owners[0];
            if (!compilations.TryGetValue(owner.Id, out var compilation))
            {
                compilation = await owner.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                compilations.Add(owner.Id, compilation);
            }
            if (compilation is null) return null;
            var matches = DocumentationCommentId.GetSymbolsForDeclarationId(declaration.DocumentationCommentId, compilation)
                .OfType<INamedTypeSymbol>().Where(type => type.Locations.Any(location => location.IsInSource))
                .Where(type => DependencyGraphScanner.GetSourceTypeId(solution, type, plan.OwnerContextFingerprints,
                    plan.GeneratedDocumentOwners) == declaration.TypeId).ToArray();
            return matches.Length == 1 ? matches[0].OriginalDefinition : null;
        }
    }
}
