#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
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
        var symbols = new Dictionary<string, ISymbol>(StringComparer.Ordinal);
        var collection = await CollectAsync(
            solution,
            new DependencyGraphCollectionOptions(options.ScopeType, options.IncludeGenerated, options.DocumentOffset, options.MaxDocuments),
            ct,
            observer: new DependencyGraphCollectionObserver(SymbolDiscovered: (id, symbol) => symbols.TryAdd(id, symbol)))
            .ConfigureAwait(false);
        return Project(collection, ToProjectionOptions(options), id => symbols.GetValueOrDefault(id), handoffFormatter);
    }

    internal static async Task<DependencyGraphCollection> CollectAsync(
        Solution solution,
        DependencyGraphCollectionOptions options,
        CancellationToken ct = default,
        IReadOnlyDictionary<ProjectId, string>? ownerContextFingerprints = null,
        DependencyGraphCollectionObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(options);
        if (options.DocumentOffset < 0) throw new ArgumentOutOfRangeException(nameof(options), "DocumentOffset must be zero or greater.");
        if (options.MaxDocuments is < 1) throw new ArgumentOutOfRangeException(nameof(options), "MaxDocuments must be at least one when provided.");
        if (!Enum.IsDefined(options.ScopeType)) throw new ArgumentOutOfRangeException(nameof(options), "ScopeType is invalid.");
        var maxDocuments = options.MaxDocuments is null ? int.MaxValue : Math.Min(options.MaxDocuments.Value, MaximumDocuments);
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        var generatedDocumentOwners = await ExactSourceSymbolResolver.GetSourceGeneratedDocumentOwnersAsync(solution, ct).ConfigureAwait(false);
        var documentSelection = await SelectDocumentsAsync(
            solution, options, maxDocuments, ownerContextFingerprints, generatedDocumentOwners, ct).ConfigureAwait(false);
        var projectDependencies = GetProjectDependencies(solution);
        var typeScan = await CollectTypeReferencesAsync(
            solution, documentSelection.Documents, solutionDir, ownerContextFingerprints, generatedDocumentOwners, ct, observer).ConfigureAwait(false);

        return new DependencyGraphCollection(
            typeScan.Edges.ToImmutableArray(),
            projectDependencies.ToImmutableArray(),
            typeScan.Errors.ToImmutableArray(),
            documentSelection.EligibleDocuments.Select(item => item.ToIdentity()).ToImmutableArray(),
            documentSelection.Documents.Select(item => item.ToIdentity()).ToImmutableArray(),
            typeScan.AttemptedDocuments.ToImmutableArray(),
            typeScan.CoveredDocuments.ToImmutableArray(),
            typeScan.TypeDeclarations.ToImmutableArray(),
            documentSelection.TotalDocumentCount,
            documentSelection.Documents.Count,
            typeScan.CollectedDocumentCount,
            typeScan.NewSemanticScanCount,
            options.DocumentOffset,
            documentSelection.NextDocumentOffset,
            options.MaxDocuments is not null && options.MaxDocuments.Value != maxDocuments,
            options.ScopeType,
            options.IncludeGenerated,
            solutionDir);
    }

    internal static DependencyGraphPayload Project(
        DependencyGraphCollection collection,
        DependencyGraphProjectionOptions options,
        Func<string, ISymbol?>? symbolResolver = null,
        Func<ISymbol, string?>? handoffFormatter = null)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Offset < 0) throw new ArgumentOutOfRangeException(nameof(options), "Offset must be zero or greater.");
        if (options.PageSize < 1) throw new ArgumentOutOfRangeException(nameof(options), "PageSize must be at least one.");
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
        if (options.TargetProject is not null && options.TargetFilePath is null && options.TargetTypeName is null && options.TargetTypeId is null && options.TargetTypeIds is null)
            throw new ArgumentException("TargetProject requires a file or type target.", nameof(options));

        var pageSize = Math.Min(options.PageSize, MaximumPageSize);
        var maxNodes = Math.Min(options.MaxNodes, MaximumNodes);
        var solutionDir = collection.SolutionDirectory;

        var rawEdges = collection.TypeDependencies;
        var isTargeted = options.TargetFilePath is not null || options.TargetTypeName is not null || options.TargetTypeId is not null || options.TargetTypeIds is not null;
        var requestedDepth = isTargeted ? options.Depth : 1;
        var effectiveDepth = Math.Clamp(requestedDepth, 1, MaximumDepth);
        var traversal = isTargeted
            ? Traverse(rawEdges, options.TargetFilePath, options.TargetTypeName, options.TargetProject, options.TargetTypeId, options.TargetTypeIds,
                options.Direction, solutionDir, effectiveDepth, maxNodes, collection.TypeDeclarations)
            : new DependencyGraphTraversalOutcome(rawEdges.Select(edge => edge with { Depth = 1 }).ToList(), 0, false, 0);

        return CreatePayload(
            options, pageSize, maxNodes, collection, traversal, isTargeted, requestedDepth, effectiveDepth,
            symbolResolver, handoffFormatter, collection.DocumentLimitWasClamped, pageSize != options.PageSize);
    }

    private static DependencyGraphProjectionOptions ToProjectionOptions(DependencyGraphScanOptions options) => new(
        options.Offset, options.PageSize, options.TargetFilePath, options.TargetTypeName, options.TargetProject,
        options.Direction, options.Depth, options.MaxNodes, options.TargetTypeId, options.TargetTypeIds);

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
        DependencyGraphCollectionOptions options,
        int maxDocuments,
        IReadOnlyDictionary<ProjectId, string>? ownerContextFingerprints,
        IReadOnlyDictionary<SyntaxTree, SourceGeneratedDocument> generatedDocumentOwners,
        CancellationToken ct)
    {
        var allDocuments = new List<OrderedDocument>();
        foreach (var project in solution.Projects)
        {
            IEnumerable<Document> projectDocuments = project.Documents;
            if (options.IncludeGenerated)
                projectDocuments = projectDocuments.Concat(generatedDocumentOwners.Values.Where(document => document.Project.Id == project.Id));
            foreach (var document in projectDocuments)
            {
                ct.ThrowIfCancellationRequested();
                var isTestDocument = TestDetector.IsTestProject(project)
                    || TestDetector.IsTestFile(document.FilePath ?? document.Name);
                if (options.ScopeType == SymbolScopeType.Production && isTestDocument
                    || options.ScopeType == SymbolScopeType.Tests && !isTestDocument)
                    continue;
                if (!options.IncludeGenerated && await GeneratedDocumentDetector.IsGeneratedDocumentAsync(document, ct).ConfigureAwait(false))
                    continue;
                var textHash = await GetTextHashAsync(document, ct).ConfigureAwait(false);
                allDocuments.Add(new OrderedDocument(
                    project,
                    document,
                    CanonicalPath(project.FilePath),
                    ownerContextFingerprints?.GetValueOrDefault(project.Id) ?? string.Empty,
                    CanonicalPath(document.FilePath),
                    textHash.Hash,
                    textHash.Error,
                    DuplicateOrdinal: 0));
            }
        }
        allDocuments = allDocuments
            .OrderBy(item => item.ProjectPath, StringComparer.Ordinal)
            .ThenBy(item => item.ContextFingerprint, StringComparer.Ordinal)
            .ThenBy(item => item.DocumentPath, StringComparer.Ordinal)
            .ThenBy(item => item.Document.Name, StringComparer.Ordinal)
            .ThenBy(item => item, OrderedDocumentFolderComparer.Instance)
            .ThenBy(item => item.Document.SourceCodeKind)
            .ThenBy(item => item.TextHash, StringComparer.Ordinal)
            .ToList();
        var duplicateOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < allDocuments.Count; index++)
        {
            var item = allDocuments[index];
            var key = GetDocumentIdentityGroupKey(item);
            duplicateOrdinals.TryGetValue(key, out var ordinal);
            duplicateOrdinals[key] = ordinal + 1;
            allDocuments[index] = item with { DuplicateOrdinal = ordinal };
        }
        if (options.DocumentOffset > allDocuments.Count)
            throw new ArgumentOutOfRangeException(nameof(options), "DocumentOffset exceeds the number of solution documents.");

        var documents = allDocuments.Skip(options.DocumentOffset).Take(maxDocuments).ToList();
        var nextDocumentOffset = options.DocumentOffset + documents.Count < allDocuments.Count
            ? options.DocumentOffset + documents.Count
            : (int?)null;
        return new DocumentSelection(allDocuments, documents, nextDocumentOffset);
    }

    private static string CanonicalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (ArgumentException) { return string.Empty; }
        return AnalysisPathIdentity.TryNormalize(fullPath, out var canonicalPath) ? canonicalPath : string.Empty;
    }

    private static async Task<(string Hash, string? Error)> GetTextHashAsync(Document document, CancellationToken ct)
    {
        try
        {
            var text = await document.GetTextAsync(ct).ConfigureAwait(false);
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetBytes(text.ToString());
            return (Convert.ToHexString(SHA256.HashData(bytes)), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
            return (string.Empty, "Source text could not be read for deterministic document identity.");
        }
    }

    private static string GetDocumentIdentityGroupKey(OrderedDocument document)
    {
        var builder = new StringBuilder();
        AppendKeyPart(builder, document.ProjectPath);
        AppendKeyPart(builder, document.ContextFingerprint);
        AppendKeyPart(builder, document.DocumentPath);
        AppendKeyPart(builder, document.Document.Name);
        builder.Append(document.Document.Folders.Count).Append(':');
        foreach (var folder in document.Document.Folders) AppendKeyPart(builder, folder);
        AppendKeyPart(builder, document.Document.SourceCodeKind.ToString());
        AppendKeyPart(builder, document.TextHash);
        return builder.ToString();
    }

    private static void AppendKeyPart(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value);

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
        IReadOnlyList<OrderedDocument> documents,
        string solutionDir,
        IReadOnlyDictionary<ProjectId, string>? ownerContextFingerprints,
        IReadOnlyDictionary<SyntaxTree, SourceGeneratedDocument> generatedDocumentOwners,
        CancellationToken ct,
        DependencyGraphCollectionObserver? observer)
    {
        var errors = new List<DependencyGraphScanError>();
        var rawTypeEdges = new Dictionary<(string FromTypeId, string ToTypeId), DependencyTypeReference>();
        var symbolsByTypeId = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        var typeDocumentsById = new Dictionary<string, (INamedTypeSymbol Symbol, List<DependencyDocumentIdentity> Documents)>(StringComparer.Ordinal);
        var compilations = new Dictionary<ProjectId, Compilation?>();
        var collectedDocumentCount = 0;
        var newSemanticScanCount = 0;
        var attemptedDocuments = new List<DependencyDocumentIdentity>();
        var coveredDocuments = new List<DependencyDocumentIdentity>();
        foreach (var batch in documents.Chunk(MaximumDocuments))
        {
            foreach (var orderedDocument in batch)
            {
                var project = orderedDocument.Project;
                var document = orderedDocument.Document;
                var documentIdentity = orderedDocument.ToIdentity();
                attemptedDocuments.Add(documentIdentity);
                ct.ThrowIfCancellationRequested();
                if (orderedDocument.TextError is not null)
                {
                    errors.Add(new DependencyGraphScanError(project.Name, document.Name, orderedDocument.TextError));
                    continue;
                }
                if (!compilations.TryGetValue(project.Id, out var compilation))
                {
                    compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                    compilations[project.Id] = compilation;
                    observer?.CompilationAcquired?.Invoke(project);
                }
                if (compilation is null)
                {
                    errors.Add(new DependencyGraphScanError(project.Name, document.Name, "Compilation was unavailable."));
                    continue;
                }

                observer?.DocumentCollected?.Invoke(document);
                newSemanticScanCount++;
                if (await CollectDocumentTypeReferencesAsync(
                        solution, project, document, compilation, solutionDir, ownerContextFingerprints, generatedDocumentOwners, documentIdentity,
                        rawTypeEdges, symbolsByTypeId, typeDocumentsById, errors, ct).ConfigureAwait(false))
                {
                    collectedDocumentCount++;
                    coveredDocuments.Add(orderedDocument.ToIdentity());
                }
            }
        }

        if (observer?.SymbolDiscovered is { } symbolDiscovered)
            foreach (var (typeId, symbol) in symbolsByTypeId)
                symbolDiscovered(typeId, symbol);

        var typeDeclarations = typeDocumentsById.Select(pair =>
        {
            var symbol = pair.Value.Symbol;
            var type = symbol.OriginalDefinition;
            var location = type.Locations.Where(candidate => candidate.IsInSource)
                .OrderBy(candidate => CanonicalPath(candidate.SourceTree?.FilePath ?? candidate.GetLineSpan().Path), StringComparer.Ordinal)
                .FirstOrDefault();
            var ownerProject = location?.SourceTree is { } tree
                ? GetDocumentOwner(solution, tree, generatedDocumentOwners)?.Project
                : null;
            var ownerPath = ownerProject is null ? string.Empty : CanonicalPath(ownerProject.FilePath);
            var ownerContext = ownerProject is null ? string.Empty : ownerContextFingerprints?.GetValueOrDefault(ownerProject.Id) ?? string.Empty;
            return new DependencyTypeDeclaration(
                pair.Key,
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                type.Name,
                type.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                ownerProject?.Name ?? string.Empty,
                location is null ? string.Empty : PathNormalizer.ToRelative(solutionDir, location.SourceTree?.FilePath ?? location.GetLineSpan().Path),
                ownerPath,
                ownerContext,
                pair.Key,
                pair.Value.Documents.ToImmutableArray());
        }).OrderBy(declaration => declaration.TypeId, StringComparer.Ordinal).ToList();

        var rawEdges = rawTypeEdges.Values
            .OrderBy(edge => edge.FromProject, StringComparer.Ordinal).ThenBy(edge => edge.FromFile, StringComparer.Ordinal)
            .ThenBy(edge => edge.FromTypeId, StringComparer.Ordinal).ThenBy(edge => edge.ToTypeId, StringComparer.Ordinal)
            .ToList();
        return new TypeReferenceScan(rawEdges, errors, collectedDocumentCount, newSemanticScanCount,
            attemptedDocuments, coveredDocuments, typeDeclarations);
    }

    private static async Task<bool> CollectDocumentTypeReferencesAsync(
        Solution solution,
        Project project,
        Document document,
        Compilation compilation,
        string solutionDir,
        IReadOnlyDictionary<ProjectId, string>? ownerContextFingerprints,
        IReadOnlyDictionary<SyntaxTree, SourceGeneratedDocument> generatedDocumentOwners,
        DependencyDocumentIdentity documentIdentity,
        Dictionary<(string FromTypeId, string ToTypeId), DependencyTypeReference> rawTypeEdges,
        Dictionary<string, INamedTypeSymbol> symbolsByTypeId,
        Dictionary<string, (INamedTypeSymbol Symbol, List<DependencyDocumentIdentity> Documents)> typeDocumentsById,
        List<DependencyGraphScanError> errors,
        CancellationToken ct)
    {
        var documentEdges = new Dictionary<(string FromTypeId, string ToTypeId), DependencyTypeReference>();
        var documentSymbols = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        var documentTypeDocuments = new Dictionary<string, (INamedTypeSymbol Symbol, List<DependencyDocumentIdentity> Documents)>(StringComparer.Ordinal);
        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (tree is null)
        {
            errors.Add(new DependencyGraphScanError(project.Name, document.Name, "Source was unavailable."));
            return false;
        }

        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
        foreach (var typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
        {
            if (model.GetTypeInfo(typeSyntax, ct).Type is not INamedTypeSymbol targetType || !HasSourceLocation(targetType)) continue;
            var containingDeclaration = typeSyntax.AncestorsAndSelf()
                .FirstOrDefault(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);
            var enclosingType = containingDeclaration is null
                ? null
                : model.GetDeclaredSymbol(containingDeclaration, ct) as INamedTypeSymbol;
            if (enclosingType is null)
            {
                var enclosingSymbol = model.GetEnclosingSymbol(typeSyntax.SpanStart, ct);
                enclosingType = enclosingSymbol as INamedTypeSymbol ?? enclosingSymbol?.ContainingType;
            }
            if (enclosingType is null || !HasSourceLocation(enclosingType)) continue;

            var sourceLocation = SelectLocation(enclosingType, tree.FilePath);
            var targetLocation = SelectLocation(targetType, tree.FilePath);
            if (sourceLocation is null || targetLocation is null) continue;
            var sourceFile = PathNormalizer.ToRelative(solutionDir, sourceLocation.SourceTree?.FilePath ?? tree.FilePath);
            var targetFile = PathNormalizer.ToRelative(solutionDir, targetLocation.SourceTree?.FilePath ?? targetLocation.GetLineSpan().Path);
            if (string.IsNullOrEmpty(sourceFile) || string.IsNullOrEmpty(targetFile)) continue;

            var sourceOwner = GetDocumentOwner(solution, sourceLocation.SourceTree!, generatedDocumentOwners)?.Project;
            var targetOwner = GetDocumentOwner(solution, targetLocation.SourceTree!, generatedDocumentOwners)?.Project;
            if (sourceOwner is null || targetOwner is null)
            {
                errors.Add(new DependencyGraphScanError(project.Name, document.Name,
                    "A source dependency endpoint did not have an exact loaded document owner."));
                return false;
            }
            var source = ToTypeReferenceEnd(enclosingType, sourceOwner.Name, sourceFile,
                GetProjectIdentity(sourceOwner, ownerContextFingerprints?.GetValueOrDefault(sourceOwner.Id)));
            var target = ToTypeReferenceEnd(targetType, targetOwner.Name, targetFile,
                GetProjectIdentity(targetOwner, ownerContextFingerprints?.GetValueOrDefault(targetOwner.Id)));
            documentSymbols.TryAdd(source.TypeId, enclosingType.OriginalDefinition);
            documentSymbols.TryAdd(target.TypeId, targetType.OriginalDefinition);
            if (source.TypeId == target.TypeId) continue;

            var key = (source.TypeId, target.TypeId);
            if (!documentEdges.ContainsKey(key))
            {
                documentEdges.Add(key, new DependencyTypeReference(
                    source.TypeId, target.TypeId,
                    source.Type, target.Type,
                    source.TypeName, target.TypeName,
                    source.Namespace, target.Namespace,
                    source.Project, target.Project,
                    source.File, target.File));
            }
        }

        var declaredTypesInDocument = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in root.DescendantNodes().Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
        {
            ct.ThrowIfCancellationRequested();
            if (model.GetDeclaredSymbol(declaration, ct) is not INamedTypeSymbol type || !HasSourceLocation(type)) continue;
            var owner = GetDocumentOwner(solution, declaration.SyntaxTree, generatedDocumentOwners)?.Project;
            if (owner is null)
            {
                errors.Add(new DependencyGraphScanError(project.Name, document.Name,
                    "A declared source type did not have an exact loaded document owner."));
                return false;
            }
            var typeId = CreateTypeId(type, GetProjectIdentity(owner, ownerContextFingerprints?.GetValueOrDefault(owner.Id)));
            if (!declaredTypesInDocument.Add(typeId)) continue;
            documentSymbols.TryAdd(typeId, type.OriginalDefinition);
            if (!documentTypeDocuments.TryGetValue(typeId, out var typeDocuments))
            {
                typeDocuments = (type.OriginalDefinition, []);
                documentTypeDocuments.Add(typeId, typeDocuments);
            }
            typeDocuments.Documents.Add(documentIdentity);
        }

        foreach (var (key, edge) in documentEdges)
            if (!rawTypeEdges.ContainsKey(key)) rawTypeEdges.Add(key, edge);
        foreach (var (typeId, symbol) in documentSymbols)
            symbolsByTypeId.TryAdd(typeId, symbol);
        foreach (var (typeId, typeDocuments) in documentTypeDocuments)
        {
            if (!typeDocumentsById.TryGetValue(typeId, out var existing))
            {
                typeDocumentsById.Add(typeId, typeDocuments);
                continue;
            }
            existing.Documents.AddRange(typeDocuments.Documents);
        }
        return true;
    }

    private static DependencyGraphPayload CreatePayload(
        DependencyGraphProjectionOptions options,
        int pageSize,
        int maxNodes,
        DependencyGraphCollection collection,
        DependencyGraphTraversalOutcome traversal,
        bool isTargeted,
        int requestedDepth,
        int effectiveDepth,
        Func<string, ISymbol?>? symbolResolver,
        Func<ISymbol, string?>? handoffFormatter,
        bool documentLimitWasClamped,
        bool pageSizeWasClamped)
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

        var pagedProjects = Page(collection.ProjectDependencies, options.Offset, pageSize);
        var pagedNamespaces = Page(allNamespaceDeps, options.Offset, pageSize);
        var pagedFiles = Page(allFileDeps, options.Offset, pageSize);
        var pagedTypeEdges = Page(selectedEdges, options.Offset, pageSize);
        if (handoffFormatter is not null && symbolResolver is not null)
        {
            pagedTypeEdges = pagedTypeEdges.Select(edge => edge with
            {
                FromHandoffId = symbolResolver(edge.FromTypeId) is { } fromSymbol ? handoffFormatter(fromSymbol) : null,
                ToHandoffId = symbolResolver(edge.ToTypeId) is { } toSymbol ? handoffFormatter(toSymbol) : null,
            }).ToList();
        }
        return new DependencyGraphPayload(
            ProjectDependencies: pagedProjects,
            NamespaceDependencies: pagedNamespaces,
            FileDependencies: pagedFiles,
            TotalProjectDependencyCount: collection.ProjectDependencies.Length,
            TotalNamespaceDependencyCount: allNamespaceDeps.Count,
            TotalFileDependencyCount: allFileDeps.Count,
            Offset: options.Offset,
            PageSize: pageSize,
            ScannedDocumentCount: collection.CoveredDocumentCount,
            TotalDocumentCount: collection.EligibleDocumentCount,
            DocumentLimitReached: collection.NextDocumentOffset is not null,
            PageSizeWasClamped: pageSizeWasClamped,
            DocumentLimitWasClamped: documentLimitWasClamped || collection.DocumentLimitWasClamped,
            Errors: collection.Errors,
            TypeDependencies: pagedTypeEdges,
            TotalTypeDependencyCount: selectedEdges.Count,
            DocumentOffset: collection.DocumentOffset,
            NextDocumentOffset: collection.NextDocumentOffset,
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
            HiddenTypeDependencyCount: traversal.HiddenTypeDependencyCount,
            ContinuationInputIncomplete: collection.ContinuationInputIncomplete)
        {
            TypeDeclarations = collection.TypeDeclarations
        };
    }

    private sealed record DocumentSelection(
        IReadOnlyList<OrderedDocument> EligibleDocuments,
        IReadOnlyList<OrderedDocument> Documents,
        int? NextDocumentOffset)
    {
        public int TotalDocumentCount => EligibleDocuments.Count;
    }

    private sealed record OrderedDocument(
        Project Project,
        Document Document,
        string ProjectPath,
        string ContextFingerprint,
        string DocumentPath,
        string TextHash,
        string? TextError,
        int DuplicateOrdinal)
    {
        public DependencyDocumentIdentity ToIdentity() => new(
            ProjectPath,
            ContextFingerprint,
            DocumentPath,
            Document.Name,
            Document.Folders.ToImmutableArray(),
            Document.SourceCodeKind.ToString(),
            TextHash,
            DuplicateOrdinal);
    }

    private sealed class OrderedDocumentFolderComparer : IComparer<OrderedDocument>
    {
        public static OrderedDocumentFolderComparer Instance { get; } = new();

        public int Compare(OrderedDocument? left, OrderedDocument? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var leftFolders = left.Document.Folders;
            var rightFolders = right.Document.Folders;
            var commonCount = Math.Min(leftFolders.Count, rightFolders.Count);
            for (var index = 0; index < commonCount; index++)
            {
                var comparison = StringComparer.Ordinal.Compare(leftFolders[index], rightFolders[index]);
                if (comparison != 0) return comparison;
            }
            return leftFolders.Count.CompareTo(rightFolders.Count);
        }
    }

    private sealed record TypeReferenceScan(
        IReadOnlyList<DependencyTypeReference> Edges,
        IReadOnlyList<DependencyGraphScanError> Errors,
        int CollectedDocumentCount,
        int NewSemanticScanCount,
        IReadOnlyList<DependencyDocumentIdentity> AttemptedDocuments,
        IReadOnlyList<DependencyDocumentIdentity> CoveredDocuments,
        IReadOnlyList<DependencyTypeDeclaration> TypeDeclarations);

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
        int maxNodes,
        IReadOnlyList<DependencyTypeDeclaration>? typeDeclarations = null)
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
        if (typeDeclarations is not null)
        {
            foreach (var declaration in typeDeclarations)
            {
                if (targetTypeId is not null || exactTypeIds is not null)
                {
                    if (string.Equals(declaration.TypeId, targetTypeId, StringComparison.Ordinal) || exactTypeIds?.Contains(declaration.TypeId) == true)
                        seeds.Add(declaration.TypeId);
                }
                else if ((targetFilePath is not null &&
                          (MatchesTargetPath(declaration.File, targetPath) || declaration.DeclarationDocuments.Any(document =>
                              MatchesTargetPath(PathNormalizer.ToRelative(solutionDir, document.DocumentPath), targetPath)))
                          && (targetProject is null || string.Equals(declaration.Project, targetProject, StringComparison.Ordinal)))
                         || (targetTypeName is not null && MatchesTypeName(declaration.DisplayName, targetType)
                             && (targetProject is null || string.Equals(declaration.Project, targetProject, StringComparison.Ordinal))))
                {
                    seeds.Add(declaration.TypeId);
                }
            }
        }
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
        if (targetTypeId is not null) seeds.Add(targetTypeId);
        if (exactTypeIds is not null) seeds.UnionWith(exactTypeIds);

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
            }
        }
        for (var depth = 1; depth <= maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var node in frontier)
            {
                foreach (var edge in edges.Where(edge => CanTraverse(edge, node))
                             .OrderBy(edge => edge.FromTypeId, StringComparer.Ordinal)
                             .ThenBy(edge => edge.ToTypeId, StringComparer.Ordinal)
                             .ThenBy(edge => edge.FromFile, StringComparer.Ordinal)
                             .ThenBy(edge => edge.ToFile, StringComparer.Ordinal))
                {
                    var outgoing = edge.FromTypeId == node;
                    var key = (edge.FromTypeId, edge.ToTypeId);
                    var neighbor = outgoing ? edge.ToTypeId : edge.FromTypeId;
                    if (visited.Contains(neighbor))
                    {
                        if (!discovered.ContainsKey(key)) discovered[key] = edge with { Depth = depth };
                        continue;
                    }

                    if (visited.Count >= maxNodes)
                    {
                        nodeLimitReached = true;
                        hiddenEdges.Add(key);
                        continue;
                    }

                    visited.Add(neighbor);
                    next.Add(neighbor);
                    if (!discovered.ContainsKey(key)) discovered[key] = edge with { Depth = depth };
                }
            }
            frontier = next.OrderBy(node => node, StringComparer.Ordinal).ToList();
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
            .OrderBy(location => string.Equals(
                CanonicalPath(location.SourceTree?.FilePath ?? location.GetLineSpan().Path), CanonicalPath(preferredPath), StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(location => CanonicalPath(location.SourceTree?.FilePath ?? location.GetLineSpan().Path), StringComparer.Ordinal)
            .FirstOrDefault();

    private static Document? GetDocumentOwner(
        Solution solution,
        SyntaxTree tree,
        IReadOnlyDictionary<SyntaxTree, SourceGeneratedDocument> generatedDocumentOwners) =>
        solution.GetDocument(tree) ?? (generatedDocumentOwners.TryGetValue(tree, out var generated) ? generated : null);

    public static string GetSourceTypeId(Solution solution, ISymbol symbol) =>
        GetSourceTypeId(solution, symbol, null, null);

    internal static string GetSourceTypeId(
        Solution solution,
        ISymbol symbol,
        IReadOnlyDictionary<ProjectId, string>? ownerContextFingerprints,
        IReadOnlyDictionary<SyntaxTree, SourceGeneratedDocument>? generatedDocumentOwners = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(symbol);
        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType
            ?? throw new ArgumentException("The symbol has no containing source type.", nameof(symbol));
        var location = type.Locations.FirstOrDefault(candidate => candidate.IsInSource && candidate.SourceTree is not null)
            ?? throw new ArgumentException("The symbol has no source declaration.", nameof(symbol));
        var project = GetDocumentOwner(solution, location.SourceTree!, generatedDocumentOwners ?? ImmutableDictionary<SyntaxTree, SourceGeneratedDocument>.Empty)?.Project
            ?? throw new ArgumentException("The symbol's source project is not in the solution.", nameof(symbol));
        return CreateTypeId(type, GetProjectIdentity(project, ownerContextFingerprints?.GetValueOrDefault(project.Id)));
    }

    public static Task<IReadOnlyCollection<string>> GetDocumentTypeIdsAsync(Document document, CancellationToken ct = default) =>
        GetDocumentTypeIdsAsync(document, ct, null);

    internal static async Task<IReadOnlyCollection<string>> GetDocumentTypeIdsAsync(
        Document document,
        CancellationToken ct,
        IReadOnlyDictionary<ProjectId, string>? ownerContextFingerprints)
    {
        ArgumentNullException.ThrowIfNull(document);
        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Source document was unavailable.");
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Source semantic model was unavailable.");
        var projectIdentity = GetProjectIdentity(document.Project, ownerContextFingerprints?.GetValueOrDefault(document.Project.Id));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in root.DescendantNodes().Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
        {
            ct.ThrowIfCancellationRequested();
            if (model.GetDeclaredSymbol(declaration, ct) is INamedTypeSymbol type)
                ids.Add(CreateTypeId(type, projectIdentity));
        }
        return ids;
    }

    private static string GetProjectIdentity(Project project, string? ownerContextFingerprint = null)
    {
        var path = string.IsNullOrWhiteSpace(project.FilePath) ? project.Name : CanonicalPath(project.FilePath);
        return string.IsNullOrEmpty(ownerContextFingerprint) ? path : $"{path}|context:{ownerContextFingerprint}";
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
