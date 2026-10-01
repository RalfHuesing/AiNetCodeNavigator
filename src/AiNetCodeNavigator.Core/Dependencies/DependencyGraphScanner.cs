#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Symbols;
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
    public const int MaximumNodes = 200;

    public static async Task<DependencyGraphPayload> ScanSolutionAsync(
        Solution solution,
        CancellationToken ct = default,
        DependencyGraphScanOptions? options = null,
        Func<ISymbol, string?>? handoffFormatter = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        options ??= new DependencyGraphScanOptions();
        ValidateOptions(options);

        var pageSize = Math.Min(options.PageSize, MaximumPageSize);
        var maxDocuments = Math.Min(options.MaxDocuments, MaximumDocuments);
        var maxNodes = Math.Min(options.MaxNodes, MaximumNodes);
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        var documentSelection = await SelectDocumentsAsync(solution, options, maxDocuments, ct).ConfigureAwait(false);
        var projectDependencies = GetProjectDependencies(solution);
        var typeScan = await CollectTypeReferencesAsync(solution, documentSelection.Documents, solutionDir, ct).ConfigureAwait(false);

        var rawEdges = typeScan.Edges;
        var isTargeted = options.TargetFilePath is not null || options.TargetTypeName is not null || options.TargetTypeId is not null || options.TargetTypeIds is not null;
        var requestedDepth = isTargeted ? options.Depth : 1;
        var effectiveDepth = Math.Clamp(requestedDepth, 1, MaximumDepth);
        var traversal = isTargeted
            ? Traverse(rawEdges, options.TargetFilePath, options.TargetTypeName, options.TargetProject, options.TargetTypeId, options.TargetTypeIds, options.Direction, solutionDir, effectiveDepth, maxNodes)
            : new DependencyGraphTraversalOutcome(rawEdges.Select(edge => edge with { Depth = 1 }).ToList(), 0, false, 0);

        return CreatePayload(
            options, pageSize, maxDocuments, maxNodes, documentSelection, projectDependencies,
            typeScan, traversal, isTargeted, requestedDepth, effectiveDepth, handoffFormatter);
    }

    private static void ValidateOptions(DependencyGraphScanOptions options)
    {
        if (options.Offset < 0) throw new ArgumentOutOfRangeException(nameof(options), "Offset must be zero or greater.");
        if (options.DocumentOffset < 0) throw new ArgumentOutOfRangeException(nameof(options), "DocumentOffset must be zero or greater.");
        if (options.PageSize < 1) throw new ArgumentOutOfRangeException(nameof(options), "PageSize must be at least one.");
        if (options.MaxDocuments < 1) throw new ArgumentOutOfRangeException(nameof(options), "MaxDocuments must be at least one.");
        if (options.MaxNodes < 1) throw new ArgumentOutOfRangeException(nameof(options), "MaxNodes must be at least one.");
        if (!Enum.IsDefined(options.Direction)) throw new ArgumentOutOfRangeException(nameof(options), "Direction is invalid.");
        if (options.TargetFilePath is not null && options.TargetTypeName is not null)
            throw new ArgumentException("Specify either TargetFilePath or TargetTypeName, not both.", nameof(options));
        if (options.TargetFilePath is not null && string.IsNullOrWhiteSpace(options.TargetFilePath))
            throw new ArgumentException("TargetFilePath must not be empty.", nameof(options));
        if (options.TargetTypeName is not null && string.IsNullOrWhiteSpace(options.TargetTypeName))
            throw new ArgumentException("TargetTypeName must not be empty.", nameof(options));
        if (options.TargetTypeId is not null && string.IsNullOrWhiteSpace(options.TargetTypeId))
            throw new ArgumentException("TargetTypeId must not be empty.", nameof(options));
        if (options.TargetTypeIds is not null && options.TargetTypeIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("TargetTypeIds must not contain empty values.", nameof(options));
        if (!Enum.IsDefined(options.ScopeType))
            throw new ArgumentOutOfRangeException(nameof(options), "ScopeType is invalid.");
        if (options.TargetProject is not null && options.TargetFilePath is null && options.TargetTypeName is null && options.TargetTypeId is null && options.TargetTypeIds is null)
            throw new ArgumentException("TargetProject requires a file or type target.", nameof(options));
    }

    private static async Task<DocumentSelection> SelectDocumentsAsync(
        Solution solution,
        DependencyGraphScanOptions options,
        int maxDocuments,
        CancellationToken ct)
    {
        var candidateDocuments = solution.Projects
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ThenBy(project => project.Id.Id)
            .SelectMany(project => project.Documents
                .OrderBy(document => document.FilePath ?? document.Name, StringComparer.Ordinal)
                .Select(document => (Project: project, Document: document)))
            .ToList();
        var allDocuments = new List<(Project Project, Document Document)>(candidateDocuments.Count);
        foreach (var candidate in candidateDocuments)
        {
            ct.ThrowIfCancellationRequested();
            var isTestDocument = TestDetector.IsTestProject(candidate.Project)
                || TestDetector.IsTestFile(candidate.Document.FilePath ?? candidate.Document.Name);
            if (options.ScopeType == SymbolScopeType.Production && isTestDocument
                || options.ScopeType == SymbolScopeType.Tests && !isTestDocument)
                continue;
            if (!options.IncludeGenerated && await GeneratedDocumentDetector.IsGeneratedDocumentAsync(candidate.Document, ct).ConfigureAwait(false))
                continue;
            allDocuments.Add(candidate);
        }
        if (options.DocumentOffset > allDocuments.Count)
            throw new ArgumentOutOfRangeException(nameof(options), "DocumentOffset exceeds the number of solution documents.");

        var documents = allDocuments.Skip(options.DocumentOffset).Take(maxDocuments).ToList();
        var nextDocumentOffset = options.DocumentOffset + documents.Count < allDocuments.Count
            ? options.DocumentOffset + documents.Count
            : (int?)null;
        return new DocumentSelection(allDocuments.Count, documents, nextDocumentOffset);
    }

    private static List<ProjectDependency> GetProjectDependencies(Solution solution) => solution.Projects
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

    private static async Task<TypeReferenceScan> CollectTypeReferencesAsync(
        Solution solution,
        IReadOnlyList<(Project Project, Document Document)> documents,
        string solutionDir,
        CancellationToken ct)
    {
        var errors = new List<DependencyGraphScanError>();
        var rawTypeEdges = new Dictionary<(string FromTypeId, string ToTypeId), DependencyTypeReference>();
        var symbolsByTypeId = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
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

            await CollectDocumentTypeReferencesAsync(
                solution, project, document, compilation, solutionDir, rawTypeEdges, symbolsByTypeId, errors, ct).ConfigureAwait(false);
        }

        var rawEdges = rawTypeEdges.Values
            .OrderBy(edge => edge.FromProject, StringComparer.Ordinal).ThenBy(edge => edge.FromFile, StringComparer.Ordinal)
            .ThenBy(edge => edge.FromTypeId, StringComparer.Ordinal).ThenBy(edge => edge.ToTypeId, StringComparer.Ordinal)
            .ToList();
        return new TypeReferenceScan(rawEdges, symbolsByTypeId, errors);
    }

    private static async Task CollectDocumentTypeReferencesAsync(
        Solution solution,
        Project project,
        Document document,
        Compilation compilation,
        string solutionDir,
        Dictionary<(string FromTypeId, string ToTypeId), DependencyTypeReference> rawTypeEdges,
        Dictionary<string, INamedTypeSymbol> symbolsByTypeId,
        List<DependencyGraphScanError> errors,
        CancellationToken ct)
    {
        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (tree is null)
        {
            errors.Add(new DependencyGraphScanError(project.Name, document.Name, "Source was unavailable."));
            return;
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

            var sourceOwner = solution.GetDocument(sourceLocation.SourceTree!)?.Project ?? project;
            var targetOwner = solution.GetDocument(targetLocation.SourceTree!)?.Project ?? project;
            var source = ToTypeReferenceEnd(enclosingType, sourceOwner.Name, sourceFile, GetProjectIdentity(sourceOwner));
            var target = ToTypeReferenceEnd(targetType, targetOwner.Name, targetFile, GetProjectIdentity(targetOwner));
            symbolsByTypeId.TryAdd(source.TypeId, enclosingType.OriginalDefinition);
            symbolsByTypeId.TryAdd(target.TypeId, targetType.OriginalDefinition);
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

    private static DependencyGraphPayload CreatePayload(
        DependencyGraphScanOptions options,
        int pageSize,
        int maxDocuments,
        int maxNodes,
        DocumentSelection documentSelection,
        List<ProjectDependency> projectDeps,
        TypeReferenceScan typeScan,
        DependencyGraphTraversalOutcome traversal,
        bool isTargeted,
        int requestedDepth,
        int effectiveDepth,
        Func<ISymbol, string?>? handoffFormatter)
    {
        var selectedEdges = traversal.Edges;

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
        if (handoffFormatter is not null)
        {
            pagedTypeEdges = pagedTypeEdges.Select(edge => edge with
            {
                FromHandoffId = typeScan.SymbolsByTypeId.TryGetValue(edge.FromTypeId, out var fromSymbol)
                    ? handoffFormatter(fromSymbol) : null,
                ToHandoffId = typeScan.SymbolsByTypeId.TryGetValue(edge.ToTypeId, out var toSymbol)
                    ? handoffFormatter(toSymbol) : null,
            }).ToList();
        }
        return new DependencyGraphPayload(
            ProjectDependencies: pagedProjects,
            NamespaceDependencies: pagedNamespaces,
            FileDependencies: pagedFiles,
            TotalProjectDependencyCount: projectDeps.Count,
            TotalNamespaceDependencyCount: allNamespaceDeps.Count,
            TotalFileDependencyCount: allFileDeps.Count,
            Offset: options.Offset,
            PageSize: pageSize,
            ScannedDocumentCount: documentSelection.Documents.Count,
            TotalDocumentCount: documentSelection.TotalDocumentCount,
            DocumentLimitReached: documentSelection.NextDocumentOffset is not null,
            PageSizeWasClamped: pageSize != options.PageSize,
            DocumentLimitWasClamped: maxDocuments != options.MaxDocuments,
            Errors: typeScan.Errors,
            TypeDependencies: pagedTypeEdges,
            TotalTypeDependencyCount: selectedEdges.Count,
            DocumentOffset: options.DocumentOffset,
            NextDocumentOffset: documentSelection.NextDocumentOffset,
            Direction: isTargeted ? options.Direction : DependencyGraphDirection.Both,
            RequestedDepth: requestedDepth,
            EffectiveDepth: effectiveDepth,
            IsDepthClamped: requestedDepth != effectiveDepth,
            IsTargeted: isTargeted,
            TargetFilePath: options.TargetFilePath,
            TargetTypeName: options.TargetTypeName,
            VisitedTypeCount: traversal.VisitedTypeCount,
            EffectiveNodeLimit: maxNodes,
            IsNodeLimitClamped: options.MaxNodes != maxNodes,
            NodeLimitReached: traversal.NodeLimitReached,
            HiddenTypeDependencyCount: traversal.HiddenTypeDependencyCount);
    }

    private sealed record DocumentSelection(
        int TotalDocumentCount,
        IReadOnlyList<(Project Project, Document Document)> Documents,
        int? NextDocumentOffset);

    private sealed record TypeReferenceScan(
        IReadOnlyList<DependencyTypeReference> Edges,
        IReadOnlyDictionary<string, INamedTypeSymbol> SymbolsByTypeId,
        IReadOnlyList<DependencyGraphScanError> Errors);

    internal static DependencyGraphTraversalOutcome Traverse(
        IReadOnlyList<DependencyTypeReference> edges,
        string? targetFilePath,
        string? targetTypeName,
        string? targetProject,
        string? targetTypeId,
        IReadOnlyCollection<string>? targetTypeIds,
        DependencyGraphDirection direction,
        string solutionDir,
        int maxDepth,
        int maxNodes)
    {
        var targetPath = NormalizeTargetPath(targetFilePath, solutionDir);
        var targetType = NormalizeTypeName(targetTypeName);
        var exactTypeIds = targetTypeIds is null ? null : new HashSet<string>(targetTypeIds, StringComparer.Ordinal);
        bool CanTraverse(DependencyTypeReference edge, string node) =>
            (edge.FromTypeId == node && direction is DependencyGraphDirection.Outgoing or DependencyGraphDirection.Both) ||
            (edge.ToTypeId == node && direction is DependencyGraphDirection.Incoming or DependencyGraphDirection.Both);
        bool Matches(string type, string project, string file) =>
            (targetFilePath is not null && MatchesTargetPath(file, targetPath) &&
             (targetProject is null || string.Equals(project, targetProject, StringComparison.Ordinal))) ||
            (targetTypeName is not null && MatchesTypeName(type, targetType) &&
             (targetProject is null || string.Equals(project, targetProject, StringComparison.Ordinal)));

        var seeds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (targetTypeId is not null || exactTypeIds is not null)
            {
                if (string.Equals(edge.FromTypeId, targetTypeId, StringComparison.Ordinal) || exactTypeIds?.Contains(edge.FromTypeId) == true) seeds.Add(edge.FromTypeId);
                if (string.Equals(edge.ToTypeId, targetTypeId, StringComparison.Ordinal) || exactTypeIds?.Contains(edge.ToTypeId) == true) seeds.Add(edge.ToTypeId);
            }
            else
            {
                if (Matches(edge.FromType, edge.FromProject, edge.FromFile)) seeds.Add(edge.FromTypeId);
                if (Matches(edge.ToType, edge.ToProject, edge.ToFile)) seeds.Add(edge.ToTypeId);
            }
        }

        var discovered = new Dictionary<(string From, string To), DependencyTypeReference>();
        var hiddenEdges = new HashSet<(string From, string To)>();
        var nodeLimitReached = false;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new List<string>();
        foreach (var seed in seeds.OrderBy(seed => seed, StringComparer.Ordinal))
        {
            if (visited.Count < maxNodes)
            {
                visited.Add(seed);
                frontier.Add(seed);
            }
            else
            {
                nodeLimitReached = true;
                foreach (var edge in edges.Where(edge => CanTraverse(edge, seed)))
                    hiddenEdges.Add((edge.FromTypeId, edge.ToTypeId));
            }
        }
        for (var depth = 1; depth <= maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var node in frontier)
            {
                foreach (var edge in edges)
                {
                    if (!CanTraverse(edge, node)) continue;
                    var outgoing = edge.FromTypeId == node;
                    var key = (edge.FromTypeId, edge.ToTypeId);
                    if (!discovered.ContainsKey(key)) discovered[key] = edge with { Depth = depth };
                    var neighbor = outgoing ? edge.ToTypeId : edge.FromTypeId;
                    if (visited.Contains(neighbor)) continue;
                    if (visited.Count < maxNodes)
                    {
                        visited.Add(neighbor);
                        next.Add(neighbor);
                    }
                    else
                    {
                        nodeLimitReached = true;
                        foreach (var hiddenEdge in edges.Where(candidate => CanTraverse(candidate, neighbor)))
                            hiddenEdges.Add((hiddenEdge.FromTypeId, hiddenEdge.ToTypeId));
                    }
                }
            }
            frontier = next;
        }

        var sorted = discovered.Values
            .OrderBy(edge => edge.Depth)
            .ThenBy(edge => edge.FromProject, StringComparer.Ordinal).ThenBy(edge => edge.FromFile, StringComparer.Ordinal)
            .ThenBy(edge => edge.FromTypeId, StringComparer.Ordinal).ThenBy(edge => edge.ToTypeId, StringComparer.Ordinal)
            .ToList();
        hiddenEdges.ExceptWith(discovered.Keys);
        return new DependencyGraphTraversalOutcome(sorted, visited.Count, nodeLimitReached, hiddenEdges.Count);
    }

    internal static string NormalizeTargetPath(string? targetFilePath, string solutionDir)
    {
        if (string.IsNullOrWhiteSpace(targetFilePath)) return string.Empty;
        if (!Path.IsPathRooted(targetFilePath)) return PathNormalizer.NormalizeSeparators(targetFilePath.TrimStart('.', '/', '\\'));
        if (string.IsNullOrWhiteSpace(solutionDir)) return PathNormalizer.NormalizeSeparators(targetFilePath);
        return PathNormalizer.ToRelative(solutionDir, targetFilePath);
    }

    internal static bool MatchesTargetPath(string candidatePath, string targetPath)
    {
        if (string.Equals(candidatePath, targetPath, StringComparison.OrdinalIgnoreCase)) return true;
        if (!Path.IsPathRooted(targetPath)) return false;
        return targetPath.EndsWith("/" + candidatePath, StringComparison.OrdinalIgnoreCase) ||
               targetPath.EndsWith("\\" + candidatePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeTypeName(string? typeName) =>
        (typeName ?? string.Empty).Replace("global::", string.Empty, StringComparison.Ordinal);

    internal static bool MatchesTypeName(string typeName, string targetTypeName)
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

    public static string GetSourceTypeId(Solution solution, ISymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(symbol);
        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType
            ?? throw new ArgumentException("The symbol has no containing source type.", nameof(symbol));
        var location = type.Locations.FirstOrDefault(candidate => candidate.IsInSource && candidate.SourceTree is not null)
            ?? throw new ArgumentException("The symbol has no source declaration.", nameof(symbol));
        var project = solution.GetDocument(location.SourceTree!)?.Project
            ?? throw new ArgumentException("The symbol's source project is not in the solution.", nameof(symbol));
        return CreateTypeId(type, GetProjectIdentity(project));
    }

    public static async Task<IReadOnlyCollection<string>> GetDocumentTypeIdsAsync(Document document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Source document was unavailable.");
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Source semantic model was unavailable.");
        var projectIdentity = GetProjectIdentity(document.Project);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in root.DescendantNodes().Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
        {
            ct.ThrowIfCancellationRequested();
            if (model.GetDeclaredSymbol(declaration, ct) is INamedTypeSymbol type)
                ids.Add(CreateTypeId(type, projectIdentity));
        }
        return ids;
    }

    private static string GetProjectIdentity(Project project)
    {
        if (string.IsNullOrWhiteSpace(project.FilePath)) return project.Name;
        try { return Path.GetFullPath(project.FilePath); }
        catch (ArgumentException) { return project.FilePath; }
    }

    private static string CreateTypeId(INamedTypeSymbol type, string projectIdentity)
    {
        var definition = type.OriginalDefinition;
        var displayName = definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return $"{displayName}|{definition.ContainingAssembly?.Identity}|{projectIdentity}";
    }

    private static TypeReferenceEnd ToTypeReferenceEnd(INamedTypeSymbol type, string project, string file, string projectIdentity)
    {
        var definition = type.OriginalDefinition;
        var displayName = definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new TypeReferenceEnd(
            CreateTypeId(definition, projectIdentity),
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

internal sealed record DependencyGraphTraversalOutcome(
    IReadOnlyList<DependencyTypeReference> Edges,
    int VisitedTypeCount,
    bool NodeLimitReached,
    int HiddenTypeDependencyCount);
