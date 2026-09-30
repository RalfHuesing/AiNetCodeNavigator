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
/// Analyzes bounded source type dependencies and project references in a solution.
/// </summary>
public static class DependencyGraphScanner
{
    public const int MaximumPageSize = 500;
    public const int MaximumDocuments = 1000;
    public const int MaximumDepth = 3;

    public static async Task<DependencyGraphPayload> ScanSolutionAsync(
        Solution solution,
        CancellationToken ct = default,
        DependencyGraphScanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        options ??= new DependencyGraphScanOptions();
        if (options.Offset < 0) throw new ArgumentOutOfRangeException(nameof(options), "Offset must be zero or greater.");
        if (options.DocumentOffset < 0) throw new ArgumentOutOfRangeException(nameof(options), "DocumentOffset must be zero or greater.");
        if (options.PageSize < 1) throw new ArgumentOutOfRangeException(nameof(options), "PageSize must be at least one.");
        if (options.MaxDocuments < 1) throw new ArgumentOutOfRangeException(nameof(options), "MaxDocuments must be at least one.");
        if (!Enum.IsDefined(options.Direction)) throw new ArgumentOutOfRangeException(nameof(options), "Direction is invalid.");
        if (options.TargetFilePath is not null && options.TargetTypeName is not null)
            throw new ArgumentException("Specify either TargetFilePath or TargetTypeName, not both.", nameof(options));
        if (options.TargetFilePath is not null && string.IsNullOrWhiteSpace(options.TargetFilePath))
            throw new ArgumentException("TargetFilePath must not be empty.", nameof(options));
        if (options.TargetTypeName is not null && string.IsNullOrWhiteSpace(options.TargetTypeName))
            throw new ArgumentException("TargetTypeName must not be empty.", nameof(options));
        if (options.TargetProject is not null && options.TargetFilePath is null && options.TargetTypeName is null)
            throw new ArgumentException("TargetProject requires a file or type target.", nameof(options));

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
        if (options.DocumentOffset > allDocuments.Count)
            throw new ArgumentOutOfRangeException(nameof(options), "DocumentOffset exceeds the number of solution documents.");

        var documents = allDocuments.Skip(options.DocumentOffset).Take(maxDocuments).ToList();
        var nextDocumentOffset = options.DocumentOffset + documents.Count < allDocuments.Count
            ? options.DocumentOffset + documents.Count
            : (int?)null;
        var documentPathProjects = allDocuments
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Document.FilePath))
            .GroupBy(pair => pair.Document.FilePath!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Project.Name, StringComparer.OrdinalIgnoreCase);
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

        var rawTypeEdges = new Dictionary<(string FromTypeId, string ToTypeId), DependencyTypeReference>();
        var compilations = new Dictionary<ProjectId, Compilation?>();
        foreach (var (project, document) in documents)
        {
            ct.ThrowIfCancellationRequested();
            if (!compilations.TryGetValue(project.Id, out var compilation))
            {
                compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                compilations[project.Id] = compilation;
            }
            if (compilation is null)
            {
                errors.Add(new DependencyGraphScanError(project.Name, document.Name, "Compilation was unavailable."));
                continue;
            }

            var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            if (tree is null)
            {
                errors.Add(new DependencyGraphScanError(project.Name, document.Name, "Source was unavailable."));
                continue;
            }

            var model = compilation.GetSemanticModel(tree);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
            foreach (var typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
            {
                if (model.GetTypeInfo(typeSyntax, ct).Type is not INamedTypeSymbol targetType || !HasSourceLocation(targetType)) continue;
                var enclosingSymbol = model.GetEnclosingSymbol(typeSyntax.SpanStart, ct);
                var enclosingType = enclosingSymbol as INamedTypeSymbol ?? enclosingSymbol?.ContainingType;
                if (enclosingType is null || !HasSourceLocation(enclosingType)) continue;

                var sourceLocation = SelectLocation(enclosingType, tree.FilePath);
                var targetLocation = SelectLocation(targetType, tree.FilePath);
                if (sourceLocation is null || targetLocation is null) continue;
                var sourceFile = PathNormalizer.ToRelative(solutionDir, sourceLocation.SourceTree?.FilePath ?? tree.FilePath);
                var targetFile = PathNormalizer.ToRelative(solutionDir, targetLocation.SourceTree?.FilePath ?? targetLocation.GetLineSpan().Path);
                if (string.IsNullOrEmpty(sourceFile) || string.IsNullOrEmpty(targetFile)) continue;

                var sourceProject = GetProjectName(sourceLocation, project.Name, documentPathProjects);
                var targetProject = GetProjectName(targetLocation, project.Name, documentPathProjects);
                var source = ToTypeReferenceEnd(enclosingType, sourceProject, sourceFile);
                var target = ToTypeReferenceEnd(targetType, targetProject, targetFile);
                if (source.TypeId == target.TypeId) continue;

                var key = (source.TypeId, target.TypeId);
                if (!rawTypeEdges.ContainsKey(key))
                {
                    rawTypeEdges.Add(key, new DependencyTypeReference(
                        source.TypeId, target.TypeId,
                        source.Type, target.Type,
                        source.TypeName, target.TypeName,
                        source.Namespace, target.Namespace,
                        source.Project, target.Project,
                        source.File, target.File));
                }
            }
        }

        var rawEdges = rawTypeEdges.Values
            .OrderBy(edge => edge.FromProject, StringComparer.Ordinal).ThenBy(edge => edge.FromFile, StringComparer.Ordinal)
            .ThenBy(edge => edge.FromTypeId, StringComparer.Ordinal).ThenBy(edge => edge.ToTypeId, StringComparer.Ordinal)
            .ToList();
        var isTargeted = options.TargetFilePath is not null || options.TargetTypeName is not null;
        var requestedDepth = isTargeted ? options.Depth : 1;
        var effectiveDepth = Math.Clamp(requestedDepth, 1, MaximumDepth);
        var selectedEdges = isTargeted
            ? Traverse(rawEdges, options, solutionDir, effectiveDepth)
            : rawEdges.Select(edge => edge with { Depth = 1 }).ToList();

        var allNamespaceDeps = selectedEdges
            .Where(edge => !string.IsNullOrEmpty(edge.FromNamespace) && !string.IsNullOrEmpty(edge.ToNamespace) && edge.FromNamespace != edge.ToNamespace)
            .GroupBy(edge => (edge.FromProject, edge.FromNamespace, edge.ToProject, edge.ToNamespace))
            .Select(group => new NamespaceDependency(group.Key.FromNamespace, group.Key.ToNamespace,
                group.Select(edge => edge.ToTypeName).Distinct(StringComparer.Ordinal).OrderBy(type => type, StringComparer.Ordinal).ToList(),
                group.Key.FromProject, group.Key.ToProject))
            .OrderBy(edge => edge.FromProject, StringComparer.Ordinal).ThenBy(edge => edge.FromNamespace, StringComparer.Ordinal)
            .ThenBy(edge => edge.ToProject, StringComparer.Ordinal).ThenBy(edge => edge.ToNamespace, StringComparer.Ordinal)
            .ToList();
        var allFileDeps = selectedEdges
            .GroupBy(edge => (edge.FromProject, edge.FromFile, edge.ToProject, edge.ToFile))
            .Select(group => new FileDependency(group.Key.FromFile, group.Key.ToFile,
                group.Select(edge => edge.ToTypeName).Distinct(StringComparer.Ordinal).OrderBy(type => type, StringComparer.Ordinal).ToList(),
                group.Key.FromProject, group.Key.ToProject))
            .OrderBy(edge => edge.FromProject, StringComparer.Ordinal).ThenBy(edge => edge.FromFile, StringComparer.Ordinal)
            .ThenBy(edge => edge.ToProject, StringComparer.Ordinal).ThenBy(edge => edge.ToFile, StringComparer.Ordinal)
            .ToList();

        var pagedProjects = Page(projectDeps, options.Offset, pageSize);
        var pagedNamespaces = Page(allNamespaceDeps, options.Offset, pageSize);
        var pagedFiles = Page(allFileDeps, options.Offset, pageSize);
        var pagedTypeEdges = Page(selectedEdges, options.Offset, pageSize);
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
            DocumentLimitReached: nextDocumentOffset is not null,
            PageSizeWasClamped: pageSize != options.PageSize,
            DocumentLimitWasClamped: maxDocuments != options.MaxDocuments,
            Errors: errors,
            TypeDependencies: pagedTypeEdges,
            TotalTypeDependencyCount: selectedEdges.Count,
            DocumentOffset: options.DocumentOffset,
            NextDocumentOffset: nextDocumentOffset,
            Direction: isTargeted ? options.Direction : DependencyGraphDirection.Both,
            RequestedDepth: requestedDepth,
            EffectiveDepth: effectiveDepth,
            IsDepthClamped: requestedDepth != effectiveDepth);
    }

    private static List<DependencyTypeReference> Traverse(
        IReadOnlyList<DependencyTypeReference> edges,
        DependencyGraphScanOptions options,
        string solutionDir,
        int maxDepth)
    {
        var targetPath = NormalizeTargetPath(options.TargetFilePath, solutionDir);
        var targetType = NormalizeTypeName(options.TargetTypeName);
        bool Matches(string type, string project, string file) =>
            (options.TargetFilePath is not null && string.Equals(file, targetPath, StringComparison.OrdinalIgnoreCase) &&
             (options.TargetProject is null || string.Equals(project, options.TargetProject, StringComparison.Ordinal))) ||
            (options.TargetTypeName is not null && MatchesTypeName(type, targetType) &&
             (options.TargetProject is null || string.Equals(project, options.TargetProject, StringComparison.Ordinal)));

        var seeds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (Matches(edge.FromType, edge.FromProject, edge.FromFile)) seeds.Add(edge.FromTypeId);
            if (Matches(edge.ToType, edge.ToProject, edge.ToFile)) seeds.Add(edge.ToTypeId);
        }

        var discovered = new Dictionary<(string From, string To), DependencyTypeReference>();
        var visited = new HashSet<string>(seeds, StringComparer.Ordinal);
        var frontier = seeds.ToList();
        for (var depth = 1; depth <= maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var node in frontier)
            {
                foreach (var edge in edges)
                {
                    var outgoing = edge.FromTypeId == node && options.Direction is DependencyGraphDirection.Outgoing or DependencyGraphDirection.Both;
                    var incoming = edge.ToTypeId == node && options.Direction is DependencyGraphDirection.Incoming or DependencyGraphDirection.Both;
                    if (!outgoing && !incoming) continue;
                    var key = (edge.FromTypeId, edge.ToTypeId);
                    if (!discovered.ContainsKey(key)) discovered[key] = edge with { Depth = depth };
                    var neighbor = outgoing ? edge.ToTypeId : edge.FromTypeId;
                    if (visited.Add(neighbor)) next.Add(neighbor);
                }
            }
            frontier = next;
        }

        return discovered.Values
            .OrderBy(edge => edge.Depth)
            .ThenBy(edge => edge.FromProject, StringComparer.Ordinal).ThenBy(edge => edge.FromFile, StringComparer.Ordinal)
            .ThenBy(edge => edge.FromTypeId, StringComparer.Ordinal).ThenBy(edge => edge.ToTypeId, StringComparer.Ordinal)
            .ToList();
    }

    private static string NormalizeTargetPath(string? targetFilePath, string solutionDir)
    {
        if (string.IsNullOrWhiteSpace(targetFilePath)) return string.Empty;
        if (!Path.IsPathRooted(targetFilePath)) return PathNormalizer.NormalizeSeparators(targetFilePath.TrimStart('.', '/', '\\'));
        return PathNormalizer.ToRelative(solutionDir, targetFilePath);
    }

    private static string NormalizeTypeName(string? typeName) =>
        (typeName ?? string.Empty).Replace("global::", string.Empty, StringComparison.Ordinal);

    private static bool MatchesTypeName(string typeName, string targetTypeName)
    {
        var normalizedType = NormalizeTypeName(typeName);
        if (string.Equals(normalizedType, targetTypeName, StringComparison.Ordinal)) return true;
        var genericStart = normalizedType.IndexOf('<');
        return genericStart > 0 && string.Equals(normalizedType[..genericStart], targetTypeName, StringComparison.Ordinal);
    }

    private static bool HasSourceLocation(INamedTypeSymbol type) => type.Locations.Any(location => location.IsInSource);

    private static Location? SelectLocation(INamedTypeSymbol type, string preferredPath) =>
        type.Locations.Where(location => location.IsInSource)
            .OrderByDescending(location => string.Equals(location.SourceTree?.FilePath, preferredPath, StringComparison.OrdinalIgnoreCase))
            .ThenBy(location => location.SourceTree?.FilePath, StringComparer.Ordinal)
            .FirstOrDefault();

    private static string GetProjectName(Location location, string fallback, IReadOnlyDictionary<string, string> documentPathProjects)
    {
        var path = location.SourceTree?.FilePath;
        return path is not null && documentPathProjects.TryGetValue(path, out var project) ? project : fallback;
    }

    private static TypeReferenceEnd ToTypeReferenceEnd(INamedTypeSymbol type, string project, string file)
    {
        var definition = type.OriginalDefinition;
        var displayName = definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new TypeReferenceEnd(
            $"{displayName}|{definition.ContainingAssembly?.Identity}",
            displayName,
            definition.Name,
            definition.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            project,
            file);
    }

    private static IReadOnlyList<T> Page<T>(IReadOnlyList<T> items, int offset, int pageSize) =>
        items.Skip(offset).Take(pageSize).ToList();

    private sealed record TypeReferenceEnd(string TypeId, string Type, string TypeName, string Namespace, string Project, string File);
}
