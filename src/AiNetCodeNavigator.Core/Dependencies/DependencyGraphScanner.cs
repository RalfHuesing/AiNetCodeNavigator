#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiNetCodeNavigator.Core.Dependencies;

/// <summary>
/// Analysiert Projekt- und Namespace-Abhängigkeiten sowie typbasierte Datei-Beziehungen.
/// </summary>
public static class DependencyGraphScanner
{
    public const int MaximumPageSize = 500;
    public const int MaximumDocuments = 1000;

    public static async Task<DependencyGraphPayload> ScanSolutionAsync(
        Solution solution,
        CancellationToken ct = default,
        DependencyGraphScanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        options ??= new DependencyGraphScanOptions();
        if (options.Offset < 0) throw new ArgumentOutOfRangeException(nameof(options), "Offset must be zero or greater.");
        if (options.PageSize < 1) throw new ArgumentOutOfRangeException(nameof(options), "PageSize must be at least one.");
        if (options.MaxDocuments < 1) throw new ArgumentOutOfRangeException(nameof(options), "MaxDocuments must be at least one.");

        var pageSize = Math.Min(options.PageSize, MaximumPageSize);
        var maxDocuments = Math.Min(options.MaxDocuments, MaximumDocuments);
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        var allDocuments = solution.Projects
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ThenBy(project => project.Id.Id)
            .SelectMany(project => project.Documents
                .OrderBy(document => document.FilePath ?? document.Name, StringComparer.Ordinal)
                .Select(document => (Project: project, Document: document)))
            .ToList();
        var documents = allDocuments.Take(maxDocuments).ToList();
        var errors = new List<DependencyGraphScanError>();

        var projectDeps = solution.Projects
            .SelectMany(project => project.ProjectReferences.Select(reference =>
            {
                var target = solution.GetProject(reference.ProjectId);
                return target is null ? null : new ProjectDependency(project.Name, target.Name);
            }))
            .Where(dependency => dependency is not null)
            .Cast<ProjectDependency>()
            .Distinct()
            .OrderBy(dependency => dependency.FromProject, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.ToProject, StringComparer.Ordinal)
            .ToList();

        var nsMap = new Dictionary<(string FromProject, string From, string ToProject, string To), HashSet<string>>();
        var fileMap = new Dictionary<(string FromProject, string From, string ToProject, string To), HashSet<string>>();

        // Roslyn symbols retain assembly identity, unlike display strings. This keeps
        // same-named types from separate project assemblies mapped to their own files.
        var solutionTypes = new Dictionary<ISymbol, TypeLocation>(SymbolEqualityComparer.Default);
        foreach (var (project, doc) in documents)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null)
            {
                errors.Add(new DependencyGraphScanError(project.Name, doc.Name, "Compilation was unavailable."));
                continue;
            }

            var syntaxTree = await doc.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            if (syntaxTree is null)
            {
                errors.Add(new DependencyGraphScanError(project.Name, doc.Name, "Source was unavailable."));
                continue;
            }

            var model = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(ct).ConfigureAwait(false);
            var relPath = PathNormalizer.ToRelative(solutionDir, doc.FilePath ?? doc.Name);
            foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeDecl, ct) is not INamedTypeSymbol namedType) continue;
                var key = namedType.OriginalDefinition;
                var namespaceName = namedType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                var candidate = new TypeLocation(project.Name, namespaceName, relPath);
                if (!solutionTypes.TryGetValue(key, out var current) ||
                    string.CompareOrdinal(candidate.FilePath, current.FilePath) < 0)
                {
                    solutionTypes[key] = candidate;
                }
            }
        }

        foreach (var (project, doc) in documents)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            var syntaxTree = await doc.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            if (compilation is null || syntaxTree is null) continue;
            var model = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(ct).ConfigureAwait(false);
            var currentFilePath = PathNormalizer.ToRelative(solutionDir, doc.FilePath ?? doc.Name);

            foreach (var node in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                var typeSymbol = model.GetTypeInfo(node, ct).Type as INamedTypeSymbol;
                if (typeSymbol is null || !solutionTypes.TryGetValue(typeSymbol.OriginalDefinition, out var targetInfo)) continue;
                var enclosingSymbol = model.GetEnclosingSymbol(node.SpanStart, ct);
                var currentNamespace = enclosingSymbol?.ContainingNamespace?.ToDisplayString() ?? string.Empty;

                if (!string.IsNullOrEmpty(currentNamespace) && !string.IsNullOrEmpty(targetInfo.Namespace) && currentNamespace != targetInfo.Namespace)
                {
                    var key = (project.Name, currentNamespace, targetInfo.ProjectName, targetInfo.Namespace);
                    if (!nsMap.TryGetValue(key, out var types)) nsMap[key] = types = new HashSet<string>(StringComparer.Ordinal);
                    types.Add(typeSymbol.Name);
                }

                if (!string.IsNullOrEmpty(currentFilePath) && !string.IsNullOrEmpty(targetInfo.FilePath) &&
                    (project.Name != targetInfo.ProjectName || currentFilePath != targetInfo.FilePath))
                {
                    var key = (project.Name, currentFilePath, targetInfo.ProjectName, targetInfo.FilePath);
                    if (!fileMap.TryGetValue(key, out var types)) fileMap[key] = types = new HashSet<string>(StringComparer.Ordinal);
                    types.Add(typeSymbol.Name);
                }
            }
        }

        var allNamespaceDeps = nsMap
            .OrderBy(pair => pair.Key.FromProject, StringComparer.Ordinal).ThenBy(pair => pair.Key.From, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.ToProject, StringComparer.Ordinal).ThenBy(pair => pair.Key.To, StringComparer.Ordinal)
            .Select(pair => new NamespaceDependency(pair.Key.From, pair.Key.To,
                pair.Value.OrderBy(type => type, StringComparer.Ordinal).ToList(), pair.Key.FromProject, pair.Key.ToProject))
            .ToList();
        var allFileDeps = fileMap
            .OrderBy(pair => pair.Key.FromProject, StringComparer.Ordinal).ThenBy(pair => pair.Key.From, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.ToProject, StringComparer.Ordinal).ThenBy(pair => pair.Key.To, StringComparer.Ordinal)
            .Select(pair => new FileDependency(pair.Key.From, pair.Key.To,
                pair.Value.OrderBy(type => type, StringComparer.Ordinal).ToList(), pair.Key.FromProject, pair.Key.ToProject))
            .ToList();

        var pagedProjects = Page(projectDeps, options.Offset, pageSize);
        var pagedNamespaces = Page(allNamespaceDeps, options.Offset, pageSize);
        var pagedFiles = Page(allFileDeps, options.Offset, pageSize);
        var documentLimitReached = allDocuments.Count > documents.Count;
        return new DependencyGraphPayload(
            ProjectDependencies: pagedProjects,
            NamespaceDependencies: pagedNamespaces,
            FileDependencies: pagedFiles,
            TotalProjectDependencyCount: projectDeps.Count,
            TotalNamespaceDependencyCount: allNamespaceDeps.Count,
            TotalFileDependencyCount: allFileDeps.Count,
            Offset: options.Offset,
            PageSize: pageSize,
            ScannedDocumentCount: documents.Count,
            TotalDocumentCount: allDocuments.Count,
            DocumentLimitReached: documentLimitReached,
            PageSizeWasClamped: pageSize != options.PageSize,
            DocumentLimitWasClamped: maxDocuments != options.MaxDocuments,
            Errors: errors);
    }

    private static IReadOnlyList<T> Page<T>(IReadOnlyList<T> items, int offset, int pageSize) =>
        items.Skip(offset).Take(pageSize).ToList();

    private sealed record TypeLocation(string ProjectName, string Namespace, string FilePath);
}
