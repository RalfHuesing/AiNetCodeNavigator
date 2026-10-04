#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>Encodes the complete validated C# source snapshot identity contract.</summary>
internal static class SourceAnalysisIdentityEncoder
{
    private static readonly string[] FrameworkPropertyKeys =
    [
        "build_property.TargetFramework",
        "build_property.TargetFrameworkIdentifier",
        "build_property.TargetFrameworkVersion",
        "build_property.TargetPlatformIdentifier",
        "build_property.TargetPlatformVersion",
        "build_property.RuntimeIdentifier",
        "build_property.Configuration",
        "build_property.Platform",
    ];

    internal static byte[] EncodeStringFrameForTesting(string value)
    {
        var writer = new Writer();
        writer.String(value);
        return writer.ToArray();
    }

    internal static byte[] EncodeNullableStringFrameForTesting(string? value)
    {
        var writer = new Writer();
        writer.NullableString(value);
        return writer.ToArray();
    }

    internal static byte[] EncodeStringListFrameForTesting(IEnumerable<string> values)
    {
        var writer = new Writer();
        writer.StringList(values);
        return writer.ToArray();
    }

    internal static async Task<Result<SourceIdentityFingerprintData>> ComputeAsync(
        SourceIdentityValidatedSnapshot validated,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validated);
        var solution = validated.Solution;
        if (!AnalysisPathIdentity.TryNormalize(solution.FilePath, out var solutionPath))
            return Failure("The loaded solution has no canonical absolute solution path.");

        try
        {
            var projects = solution.Projects.ToArray();
            var byId = projects.ToDictionary(project => project.Id);
            var provenance = validated.Inputs.Projects.ToDictionary(item => item.OwnerProjectId);
            if (provenance.Count != projects.Length || projects.Any(project => !provenance.ContainsKey(project.Id)))
                return Failure("Validated provenance does not cover every loaded project.");

            var evidence = new Dictionary<(ProjectId, int), SourceIdentityMetadataReferenceEvidence>();
            foreach (var item in validated.Inputs.MetadataReferences)
                if (item.ReferenceOrdinal < 0 || !evidence.TryAdd((item.OwnerProjectId, item.ReferenceOrdinal), item))
                    return Failure("Validated metadata evidence has an invalid or duplicate owner/reference coordinate.");

            var canonicalPaths = new Dictionary<ProjectId, string>();
            var category2 = new Dictionary<ProjectId, byte[]>();
            var category3 = new Dictionary<ProjectId, byte[]>();
            var compilations = new Dictionary<ProjectId, Compilation>();
            foreach (var project in projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (project.Language != LanguageNames.CSharp
                    || project.ParseOptions is not CSharpParseOptions parse
                    || parse.GetType() != typeof(CSharpParseOptions)
                    || project.CompilationOptions is not CSharpCompilationOptions compilationOptions
                    || compilationOptions.GetType() != typeof(CSharpCompilationOptions))
                    return Failure($"Project '{project.Name}' does not use supported C# option types.");
                if (!provenance[project.Id].IsSupported)
                    return Failure(provenance[project.Id].UnsupportedReason ?? $"Project '{project.Name}' has unsupported loader provenance.");
                if (project.AnalyzerOptions.AnalyzerConfigOptionsProvider is null
                    || !provenance[project.Id].OwnerCreatedAnalyzerConfigProvider)
                    return Failure($"Project '{project.Name}' has an analyzer-config options provider without matching creator provenance.");
                if (project.FilePath is not { Length: > 0 } path || !Path.IsPathFullyQualified(path)
                    || !AnalysisPathIdentity.TryNormalize(path, out var canonicalPath))
                    return Failure($"Project '{project.Name}' has no canonical absolute project path.");
                canonicalPaths.Add(project.Id, canonicalPath);

                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                if (compilation is null) return Failure($"Project '{project.Name}' produced no C# compilation.");
                compilations.Add(project.Id, compilation);
                if (ContainsActiveReferenceDirective(compilation, cancellationToken))
                    return Failure($"Project '{project.Name}' contains an active #r directive, but the workspace owner has not captured the resolved reference inputs.");

                var identity = compilation.Assembly.Identity;
                var projectRecord = new Writer();
                projectRecord.String(canonicalPath);
                projectRecord.String(project.Name);
                projectRecord.NullableString(project.AssemblyName);
                projectRecord.String(project.Language);
                foreach (var property in FrameworkPropertyKeys)
                {
                    var value = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(property, out var configured)
                        ? configured : "unknown";
                    projectRecord.String(value);
                }
                projectRecord.String(identity.Name);
                projectRecord.String(identity.Version.ToString());
                projectRecord.NullableString(identity.CultureName);
                projectRecord.String(Convert.ToHexString(identity.PublicKey.AsSpan()));
                projectRecord.Boolean(identity.IsRetargetable);
                projectRecord.String(((int)identity.ContentType).ToString(CultureInfo.InvariantCulture));
                category2.Add(project.Id, projectRecord.ToArray());

                var optionRecord = new Writer();
                optionRecord.String(((int)parse.LanguageVersion).ToString(CultureInfo.InvariantCulture));
                optionRecord.String(((int)parse.SpecifiedLanguageVersion).ToString(CultureInfo.InvariantCulture));
                optionRecord.String(((int)parse.Kind).ToString(CultureInfo.InvariantCulture));
                optionRecord.String(((int)parse.SpecifiedKind).ToString(CultureInfo.InvariantCulture));
                optionRecord.String(((int)parse.DocumentationMode).ToString(CultureInfo.InvariantCulture));
                optionRecord.StringList(parse.PreprocessorSymbolNames.Order(StringComparer.Ordinal));
                optionRecord.StringPairs(parse.Features.OrderBy(pair => pair.Key, StringComparer.Ordinal));
                WriteCompilationOptions(optionRecord, compilationOptions, provenance[project.Id]);
                category3.Add(project.Id, optionRecord.ToArray());
            }

            var metadataRecords = new Dictionary<ProjectId, List<byte[]>>();
            var metadataSortKeys = new Dictionary<(ProjectId, int), string[]>();
            var category5 = new List<KeyedRecord>();
            var category6 = new List<KeyedRecord>();
            foreach (var project in projects)
            {
                var ownerPath = canonicalPaths[project.Id];
                var ownMetadata = new List<byte[]>();
                for (var index = 0; index < project.MetadataReferences.Count; index++)
                {
                    var reference = project.MetadataReferences[index];
                    if (reference is not PortableExecutableReference portable
                        || !evidence.TryGetValue((project.Id, index), out var imageEvidence)
                        || imageEvidence.Images.IsDefaultOrEmpty)
                        return Failure($"Project '{project.Name}' metadata reference {index} lacks captured immutable image evidence.");
                    var images = new List<KeyedRecord>();
                    foreach (var image in imageEvidence.Images)
                    {
                        if (!IsSha256(image.Sha256) || !IsCanonicalImageKey(image.CanonicalImageKey))
                            return Failure($"Project '{project.Name}' metadata reference {index} has invalid image evidence.");
                        var imageRecord = new Writer();
                        imageRecord.String(image.CanonicalImageKey);
                        imageRecord.String(image.Sha256.ToUpperInvariant());
                        images.Add(new KeyedRecord([image.CanonicalImageKey, image.Sha256.ToUpperInvariant()], [], imageRecord.ToArray()));
                    }
                    images.Sort(KeyedRecordComparer.Instance);
                    var record = new Writer();
                    record.String(ownerPath);
                    record.String(Sha256(Combine(category2[project.Id], category3[project.Id])));
                    record.String(((int)portable.Properties.Kind).ToString(CultureInfo.InvariantCulture));
                    record.Boolean(portable.Properties.EmbedInteropTypes);
                    record.StringList(portable.Properties.Aliases.Order(StringComparer.Ordinal));
                    record.Records(images.Select(image => image.Bytes));
                    var bytes = record.ToArray();
                    ownMetadata.Add(bytes);
                    metadataSortKeys[(project.Id, index)] = MetadataSortKey(images, portable.Properties);
                }
                metadataRecords.Add(project.Id, ownMetadata);
            }
            if (evidence.Count != projects.Sum(project => project.MetadataReferences.Count))
                return Failure("Validated metadata evidence contains references which are not present in the Solution.");

            var contexts = ComputeOwnerContextFingerprints(projects, byId, canonicalPaths, category2, category3,
                metadataRecords, metadataSortKeys, cancellationToken);

            foreach (var project in projects)
            {
                foreach (var reference in project.ProjectReferences)
                {
                    var referencedProject = byId[reference.ProjectId];
                    var edge = new Writer();
                    edge.String(canonicalPaths[project.Id]);
                    edge.String(contexts[project.Id]);
                    edge.String(canonicalPaths[referencedProject.Id]);
                    edge.String(contexts[referencedProject.Id]);
                    edge.Boolean(reference.EmbedInteropTypes);
                    edge.StringList(reference.Aliases.Order(StringComparer.Ordinal));
                    category5.Add(new KeyedRecord([canonicalPaths[referencedProject.Id], contexts[referencedProject.Id],
                        ((int)(reference.EmbedInteropTypes ? 1 : 0)).ToString(CultureInfo.InvariantCulture),
                        .. reference.Aliases.Order(StringComparer.Ordinal), canonicalPaths[project.Id], contexts[project.Id]], [], edge.ToArray()));
                }
                for (var index = 0; index < project.MetadataReferences.Count; index++)
                {
                    var metadata = metadataRecords[project.Id][index];
                    var bound = new Writer();
                    bound.String(canonicalPaths[project.Id]);
                    bound.String(contexts[project.Id]);
                    bound.Record(metadata);
                    category6.Add(new KeyedRecord([.. metadataSortKeys[(project.Id, index)], canonicalPaths[project.Id], contexts[project.Id]], [], bound.ToArray()));
                }
            }

            var sourceDocuments = new List<KeyedRecord>();
            var additionalDocuments = new List<KeyedRecord>();
            var analyzerConfigDocuments = new List<KeyedRecord>();
            var capturedInputs = new List<KeyedRecord>();
            foreach (var project in projects)
            {
                var context = contexts[project.Id];
                if (ContainsActiveLoadDirective(compilations[project.Id], cancellationToken))
                    return Failure($"Project '{project.Name}' contains an active #load directive whose external source closure was not captured by the workspace owner.");
                foreach (var document in project.Documents)
                    sourceDocuments.Add(await EncodeDocumentAsync(document, "source", canonicalPaths[project.Id], context, cancellationToken).ConfigureAwait(false));
                var generatedDocuments = await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false);
                foreach (var analyzerReference in project.AnalyzerReferences)
                {
                    var bindingFailure = AnalyzerImageCapture.GetBindingFailure(analyzerReference);
                    if (bindingFailure is not null)
                    {
                        var analyzerPath = AnalyzerImageCapture.GetSourcePath(analyzerReference) ?? analyzerReference.Display;
                        return Failure($"Project '{project.FilePath ?? project.Name}' source generator '{analyzerPath}' could not load a captured binding input: {bindingFailure}");
                    }
                }
                foreach (var document in generatedDocuments)
                    sourceDocuments.Add(await EncodeDocumentAsync(document, "source", canonicalPaths[project.Id], context, cancellationToken).ConfigureAwait(false));
                foreach (var document in project.AdditionalDocuments)
                    additionalDocuments.Add(await EncodeDocumentAsync(document, "additional", canonicalPaths[project.Id], context, cancellationToken).ConfigureAwait(false));
                foreach (var document in project.AnalyzerConfigDocuments)
                    analyzerConfigDocuments.Add(await EncodeDocumentAsync(document, "analyzer-config", canonicalPaths[project.Id], context, cancellationToken).ConfigureAwait(false));

                foreach (var input in provenance[project.Id].BindingInputs)
                {
                    if (string.IsNullOrWhiteSpace(input.Kind) || string.IsNullOrWhiteSpace(input.LogicalKey) || !IsSha256(input.Sha256))
                        return Failure($"Project '{project.Name}' has invalid captured analyzer/provider input evidence.");
                    var provider = new Writer();
                    provider.String(canonicalPaths[project.Id]);
                    provider.String(context);
                    provider.String(input.Kind);
                    provider.String(input.LogicalKey);
                    provider.String(input.Sha256.ToUpperInvariant());
                    capturedInputs.Add(new KeyedRecord([canonicalPaths[project.Id], context, input.Kind, input.LogicalKey], [input.Sha256.ToUpperInvariant()],
                        provider.ToArray()));
                }
            }
            sourceDocuments.Sort(KeyedRecordComparer.Instance);
            additionalDocuments.Sort(KeyedRecordComparer.Instance);
            analyzerConfigDocuments.Sort(KeyedRecordComparer.Instance);
            capturedInputs.Sort(KeyedRecordComparer.Instance);

            var duplicateMarkers = contexts.Values.GroupBy(Marker, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            var contextByPath = projects.Select(project => new SourceIdentityProjectMarker(
                contexts[project.Id], duplicateMarkers.Contains(Marker(contexts[project.Id])) ? null : Marker(contexts[project.Id]))).ToImmutableArray();

            var rootRecord = new Writer();
            rootRecord.String("source-analysis-v1");
            rootRecord.String(solutionPath);
            var all = new Writer();
            all.Record(rootRecord.ToArray());
            var sortedProjectIds = projects.Select(project => project.Id)
                .OrderBy(projectId => canonicalPaths[projectId], StringComparer.Ordinal)
                .ThenBy(projectId => contexts[projectId], StringComparer.Ordinal)
                .ToArray();
            all.Records(sortedProjectIds.Select(projectId => category2[projectId]));
            all.Records(sortedProjectIds.Select(projectId => category3[projectId]));
            var category4 = new Writer();
            category4.Records(sourceDocuments.Select(record => record.Bytes));
            category4.Records(additionalDocuments.Select(record => record.Bytes));
            category4.Records(analyzerConfigDocuments.Select(record => record.Bytes));
            category4.Records(capturedInputs.Select(record => record.Bytes));
            all.Record(category4.ToArray());
            category5.Sort(KeyedRecordComparer.Instance);
            all.Records(category5.Select(record => record.Bytes));
            category6.Sort(KeyedRecordComparer.Instance);
            all.Records(category6.Select(record => record.Bytes));

            return Result<SourceIdentityFingerprintData>.Success(new SourceIdentityFingerprintData(
                solutionPath, Sha256(all.WrittenSpan), contextByPath, 0));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException
                                           or IOException or EncoderFallbackException or CryptographicException)
        {
            return Failure($"Source snapshot identity could not be validated: {exception.Message}");
        }
    }

    private static async Task<KeyedRecord> EncodeDocumentAsync(
        TextDocument document,
        string category,
        string ownerPath,
        string ownerContext,
        CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var textHash = Sha256(new UTF8Encoding(false, true).GetBytes(text.ToString()));
        var path = document.FilePath;
        var record = new Writer();
        record.String(ownerPath);
        record.String(ownerContext);
        record.String(category);
        string[] logicalKey;
        var hasPath = !string.IsNullOrEmpty(path);
        var hasPhysicalPath = hasPath && Path.IsPathFullyQualified(path!);
        var isGeneratedDocument = document is SourceGeneratedDocument;
        if (hasPhysicalPath)
        {
            if (!AnalysisPathIdentity.TryNormalize(path, out var canonicalPath))
                throw new InvalidOperationException($"Document '{document.Name}' has a noncanonical physical path.");
            record.Byte(1);
            record.String(canonicalPath);
            logicalKey = ["1", canonicalPath];
        }
        else
        {
            // Roslyn source-generated documents may expose virtual relative paths; their loaded identity is name/folders/kind.
            if (hasPath && !isGeneratedDocument)
                throw new InvalidOperationException($"Document '{document.Name}' has a noncanonical physical path.");
            record.Byte(0);
            record.String(document.Name);
            record.StringList(document.Folders);
            logicalKey = ["0", document.Name, .. document.Folders];
        }
        var sourceCodeKind = document is Document sourceDocument
            ? ((int)sourceDocument.SourceCodeKind).ToString(CultureInfo.InvariantCulture)
            : null;
        record.NullableString(sourceCodeKind);
        logicalKey = [.. logicalKey, sourceCodeKind is null ? "0" : "1", sourceCodeKind ?? string.Empty];
        record.String(textHash);
        var key = new[] { ownerPath, ownerContext }.Concat(logicalKey).ToArray();
        return new KeyedRecord(key, [textHash], record.ToArray());
    }

    private static string[] MetadataSortKey(IReadOnlyList<KeyedRecord> images, MetadataReferenceProperties properties)
    {
        var key = new List<string>();
        foreach (var image in images)
        {
            key.Add(image.Key[0]);
            key.Add(image.Key[1]);
        }
        key.Add(((int)properties.Kind).ToString(CultureInfo.InvariantCulture));
        key.Add(properties.EmbedInteropTypes ? "1" : "0");
        key.AddRange(properties.Aliases.Order(StringComparer.Ordinal));
        return key.ToArray();
    }

    private static void WriteCompilationOptions(Writer writer, CSharpCompilationOptions options, SourceIdentityProjectProvenance provenance)
    {
        writer.String(((int)options.OutputKind).ToString(CultureInfo.InvariantCulture));
        writer.NullableString(options.ModuleName);
        writer.NullableString(options.MainTypeName);
        writer.NullableString(options.ScriptClassName);
        writer.String(((int)options.OptimizationLevel).ToString(CultureInfo.InvariantCulture));
        writer.Boolean(options.CheckOverflow);
        writer.Boolean(options.AllowUnsafe);
        writer.String(((int)options.NullableContextOptions).ToString(CultureInfo.InvariantCulture));
        writer.String(((int)options.Platform).ToString(CultureInfo.InvariantCulture));
        writer.String(((int)options.MetadataImportOptions).ToString(CultureInfo.InvariantCulture));
        writer.StringList(options.Usings);
        writer.String(options.WarningLevel.ToString(CultureInfo.InvariantCulture));
        writer.String(((int)options.GeneralDiagnosticOption).ToString(CultureInfo.InvariantCulture));
        writer.StringPairs(options.SpecificDiagnosticOptions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new KeyValuePair<string, string>(pair.Key, ((int)pair.Value).ToString(CultureInfo.InvariantCulture))));
        writer.Boolean(options.ReportSuppressedDiagnostics);
        writer.Boolean(options.Deterministic);
        writer.String(Convert.ToHexString(options.CryptoPublicKey.AsSpan()));
        writer.NullableBoolean(options.DelaySign);
        writer.Boolean(options.PublicSign);
        WriteSigningInput(writer, options.CryptoKeyFile, "crypto-key-file", provenance);
        writer.NullableString(options.CryptoKeyContainer);
        if (!string.IsNullOrEmpty(options.CryptoKeyContainer)
            && !provenance.BindingInputs.Any(input => input.Kind == "crypto-key-container" && input.LogicalKey == options.CryptoKeyContainer))
            throw new InvalidOperationException("The signing key container lacks complete loader-captured provenance.");
        var comparer = options.AssemblyIdentityComparer;
        if (ReferenceEquals(comparer, AssemblyIdentityComparer.Default))
            writer.String("default");
        else if (comparer?.GetType() == typeof(DesktopAssemblyIdentityComparer))
            writer.String("desktop");
        else
            throw new InvalidOperationException("The assembly identity comparer is unsupported or lacks creator provenance.");

        switch (options.SourceReferenceResolver)
        {
            case null:
                writer.String("source-resolver:none");
                break;
            case SourceFileResolver resolver when resolver.GetType() == typeof(SourceFileResolver):
                writer.String("source-file-resolver");
                writer.String(CanonicalResolverPath(resolver.BaseDirectory));
                writer.StringList(resolver.SearchPaths.Select(CanonicalResolverPath));
                break;
            default:
                throw new InvalidOperationException("The project uses an unsupported source reference resolver.");
        }
        if (options.MetadataReferenceResolver is null)
            writer.String("metadata-resolver:none");
        else if (provenance.OwnerCreatedMetadataReferenceResolver)
        {
            if (options.MetadataReferenceResolver.ResolveMissingAssemblies)
                throw new InvalidOperationException("The metadata reference resolver enables missing-assembly resolution, but those resolver-produced images are not captured.");
            writer.String("metadata-resolver:owner-created-captured-references");
            writer.Boolean(false);
        }
        else
            throw new InvalidOperationException("The project uses a metadata reference resolver without matching creator provenance.");
        switch (options.XmlReferenceResolver)
        {
            case null:
                writer.String("xml-resolver:none");
                break;
            case XmlFileResolver resolver when resolver.GetType() == typeof(XmlFileResolver):
                writer.String("xml-file-resolver");
                writer.String(CanonicalResolverPath(resolver.BaseDirectory));
                break;
            default:
                throw new InvalidOperationException("The project uses an unsupported XML reference resolver.");
        }
        switch (options.StrongNameProvider)
        {
            case null:
                writer.String("strong-name-provider:none");
                break;
            case DesktopStrongNameProvider provider when provider.GetType() == typeof(DesktopStrongNameProvider):
                if (!provenance.OwnerCreatedStrongNameProvider)
                    throw new InvalidOperationException("The strong-name provider has no matching creator provenance.");
                if (!string.IsNullOrEmpty(options.CryptoKeyFile) || !string.IsNullOrEmpty(options.CryptoKeyContainer))
                    throw new InvalidOperationException("The strong-name provider uses external signing inputs without complete captured provenance.");
                writer.String("strong-name-provider:desktop-no-external-key");
                break;
            default:
                throw new InvalidOperationException("The project uses an unsupported strong-name provider.");
        }
        if (options.SyntaxTreeOptionsProvider is null)
            writer.String("syntax-tree-options:none");
        else if (provenance.OwnerCreatedSyntaxTreeOptionsProvider)
            writer.String("syntax-tree-options:owner-created-analyzer-config-documents");
        else
            throw new InvalidOperationException("The project uses a syntax-tree options provider without matching creator provenance.");
    }

    private static bool ContainsActiveLoadDirective(Compilation compilation, CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = tree.GetRoot(cancellationToken);
            foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
            {
                if (trivia.GetStructure() is LoadDirectiveTriviaSyntax directive && directive.IsActive)
                    return true;
            }
        }
        return false;
    }

    private static bool ContainsActiveReferenceDirective(Compilation compilation, CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = tree.GetRoot(cancellationToken);
            foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
            {
                if (trivia.GetStructure() is ReferenceDirectiveTriviaSyntax directive && directive.IsActive)
                    return true;
            }
        }
        return false;
    }

    private static void WriteSigningInput(Writer writer, string? value, string kind, SourceIdentityProjectProvenance provenance)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.NullableString(value);
            writer.NullableString(null);
            return;
        }
        if (!Path.IsPathFullyQualified(value) || !AnalysisPathIdentity.TryNormalize(value, out var canonical))
            throw new InvalidOperationException("The signing key path is not canonical.");
        var captured = provenance.BindingInputs.SingleOrDefault(input => input.Kind == kind && input.LogicalKey == canonical);
        if (captured is null || !IsSha256(captured.Sha256))
            throw new InvalidOperationException("The signing key path has no complete loader-captured hash.");
        writer.NullableString(canonical);
        writer.NullableString(captured.Sha256.ToUpperInvariant());
    }

    private static Dictionary<ProjectId, string> ComputeOwnerContextFingerprints(
        IReadOnlyList<Project> projects,
        IReadOnlyDictionary<ProjectId, Project> byId,
        IReadOnlyDictionary<ProjectId, string> canonicalPaths,
        IReadOnlyDictionary<ProjectId, byte[]> category2,
        IReadOnlyDictionary<ProjectId, byte[]> category3,
        IReadOnlyDictionary<ProjectId, List<byte[]>> metadataRecords,
        IReadOnlyDictionary<(ProjectId, int), string[]> metadataSortKeys,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<ProjectId, string>();
        foreach (var root in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reachable = GetReachable(root, byId, cancellationToken);
            var labels = reachable.ToDictionary(project => project.Id,
                project => ComputeProjectLocalLabel(project, canonicalPaths, category2, category3, metadataRecords, metadataSortKeys));

            // Iterative labels carry binding ownership across cycles without serializing ProjectIds.
            // A round propagates one more project edge; |V| rounds cover every reachable path.
            for (var round = 0; round < reachable.Count; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var next = new Dictionary<ProjectId, string>();
                foreach (var project in reachable)
                {
                    var record = new Writer();
                    record.String("source-project-context-node-v1");
                    record.String(canonicalPaths[project.Id]);
                    record.Record(category2[project.Id]);
                    record.Record(category3[project.Id]);
                    record.Records(GetSortedMetadataRecords(project, metadataRecords, metadataSortKeys));
                    var edges = new List<KeyedRecord>();
                    foreach (var reference in project.ProjectReferences)
                    {
                        if (!byId.TryGetValue(reference.ProjectId, out var target) || !labels.TryGetValue(target.Id, out var targetLabel))
                            throw new InvalidOperationException("A reachable project reference has no context label.");
                        var edge = new Writer();
                        edge.String(canonicalPaths[target.Id]);
                        edge.String(targetLabel);
                        edge.Boolean(reference.EmbedInteropTypes);
                        edge.StringList(reference.Aliases.Order(StringComparer.Ordinal));
                        edges.Add(new KeyedRecord(
                            [canonicalPaths[target.Id], targetLabel, reference.EmbedInteropTypes ? "1" : "0", .. reference.Aliases.Order(StringComparer.Ordinal)],
                            [], edge.ToArray()));
                    }
                    edges.Sort(KeyedRecordComparer.Instance);
                    record.Records(edges.Select(edge => edge.Bytes));
                    next.Add(project.Id, Sha256(record.WrittenSpan));
                }
                labels = next;
            }

            var context = new Writer();
            context.String("source-owner-context-v1");
            var nodes = reachable.Select(project =>
            {
                var node = new Writer();
                node.String(canonicalPaths[project.Id]);
                node.String(labels[project.Id]);
                node.Record(category2[project.Id]);
                node.Record(category3[project.Id]);
                node.Records(GetSortedMetadataRecords(project, metadataRecords, metadataSortKeys));
                return new KeyedRecord([canonicalPaths[project.Id], labels[project.Id]], [], node.ToArray());
            }).ToList();
            nodes.Sort(KeyedRecordComparer.Instance);
            context.Records(nodes.Select(node => node.Bytes));

            var graphEdges = new List<KeyedRecord>();
            foreach (var project in reachable)
            {
                foreach (var reference in project.ProjectReferences)
                {
                    var target = byId[reference.ProjectId];
                    var edge = new Writer();
                    edge.String(canonicalPaths[project.Id]);
                    edge.String(labels[project.Id]);
                    edge.String(canonicalPaths[target.Id]);
                    edge.String(labels[target.Id]);
                    edge.Boolean(reference.EmbedInteropTypes);
                    edge.StringList(reference.Aliases.Order(StringComparer.Ordinal));
                    graphEdges.Add(new KeyedRecord(
                        [canonicalPaths[project.Id], labels[project.Id], canonicalPaths[target.Id], labels[target.Id],
                            reference.EmbedInteropTypes ? "1" : "0", .. reference.Aliases.Order(StringComparer.Ordinal)],
                        [], edge.ToArray()));
                }
            }
            graphEdges.Sort(KeyedRecordComparer.Instance);
            context.Records(graphEdges.Select(edge => edge.Bytes));
            result.Add(root.Id, Sha256(context.WrittenSpan));
        }
        return result;
    }

    private static string ComputeProjectLocalLabel(
        Project project,
        IReadOnlyDictionary<ProjectId, string> canonicalPaths,
        IReadOnlyDictionary<ProjectId, byte[]> category2,
        IReadOnlyDictionary<ProjectId, byte[]> category3,
        IReadOnlyDictionary<ProjectId, List<byte[]>> metadataRecords,
        IReadOnlyDictionary<(ProjectId, int), string[]> metadataSortKeys)
    {
        var record = new Writer();
        record.String("source-project-context-node-v1");
        record.String(canonicalPaths[project.Id]);
        record.Record(category2[project.Id]);
        record.Record(category3[project.Id]);
        record.Records(GetSortedMetadataRecords(project, metadataRecords, metadataSortKeys));
        return Sha256(record.WrittenSpan);
    }

    private static IEnumerable<byte[]> GetSortedMetadataRecords(
        Project project,
        IReadOnlyDictionary<ProjectId, List<byte[]>> metadataRecords,
        IReadOnlyDictionary<(ProjectId, int), string[]> metadataSortKeys)
    {
        var records = metadataRecords[project.Id]
            .Select((bytes, index) => new KeyedRecord(metadataSortKeys[(project.Id, index)], [], bytes))
            .ToList();
        records.Sort(KeyedRecordComparer.Instance);
        return records.Select(record => record.Bytes);
    }

    private static List<Project> GetReachable(Project root, IReadOnlyDictionary<ProjectId, Project> byId, CancellationToken token)
    {
        var found = new Dictionary<ProjectId, Project>();
        var pending = new Stack<Project>();
        pending.Push(root);
        while (pending.TryPop(out var project))
        {
            token.ThrowIfCancellationRequested();
            if (!found.TryAdd(project.Id, project)) continue;
            foreach (var reference in project.ProjectReferences)
                if (byId.TryGetValue(reference.ProjectId, out var dependency)) pending.Push(dependency);
                else throw new InvalidOperationException("A project reference is unresolved.");
        }
        return found.Values.ToList();
    }

    private static string CanonicalResolverPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        return AnalysisPathIdentity.TryNormalize(path, out var canonical)
            ? canonical
            : throw new InvalidOperationException("A resolver path is not a canonical absolute path.");
    }

    private static string Marker(string contextHash) => Convert.ToHexString(Convert.FromHexString(contextHash).AsSpan(0, 16)).ToLowerInvariant();
    private static string Sha256(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value));
    private static bool IsSha256(string value) => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');
    private static bool IsCanonicalImageKey(string value)
    {
        if (value.StartsWith("in-memory:", StringComparison.Ordinal))
            return IsSha256(value["in-memory:".Length..])
                && string.Equals(value, "in-memory:" + value["in-memory:".Length..].ToUpperInvariant(), StringComparison.Ordinal);
        return AnalysisPathIdentity.TryNormalize(value, out var normalized)
            && string.Equals(value, normalized, StringComparison.Ordinal);
    }
    private static byte[] Combine(byte[] first, byte[] second) { var result = new byte[first.Length + second.Length]; first.CopyTo(result, 0); second.CopyTo(result, first.Length); return result; }
    private static Result<SourceIdentityFingerprintData> Failure(string message) =>
        Result<SourceIdentityFingerprintData>.Failure(NavigationErrorCodes.WorkspaceDiagnostic, message,
            "Repeat the query after correcting unsupported source project, option, provider, or metadata image inputs.");

    private sealed record KeyedRecord(string[] Key, string[] TieBreaker, byte[] Bytes);
    private sealed class KeyedRecordComparer : IComparer<KeyedRecord>
    {
        internal static KeyedRecordComparer Instance { get; } = new();
        public int Compare(KeyedRecord? left, KeyedRecord? right)
        {
            var leftKey = left?.Key ?? [];
            var rightKey = right?.Key ?? [];
            var length = Math.Min(leftKey.Length, rightKey.Length);
            for (var index = 0; index < length; index++)
            {
                var comparison = StringComparer.Ordinal.Compare(leftKey[index], rightKey[index]);
                if (comparison != 0) return comparison;
            }
            var keyLength = leftKey.Length.CompareTo(rightKey.Length);
            if (keyLength != 0) return keyLength;
            var leftTie = left?.TieBreaker ?? [];
            var rightTie = right?.TieBreaker ?? [];
            length = Math.Min(leftTie.Length, rightTie.Length);
            for (var index = 0; index < length; index++)
            {
                var comparison = StringComparer.Ordinal.Compare(leftTie[index], rightTie[index]);
                if (comparison != 0) return comparison;
            }
            return leftTie.Length.CompareTo(rightTie.Length);
        }
    }
    private sealed class Writer
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private readonly ArrayBufferWriter<byte> buffer = new();
        internal ReadOnlySpan<byte> WrittenSpan => buffer.WrittenSpan;
        internal void Byte(byte value) { var span = buffer.GetSpan(1); span[0] = value; buffer.Advance(1); }
        internal void Boolean(bool value) => Byte(value ? (byte)1 : (byte)0);
        internal void NullableBoolean(bool? value) { if (value is null) Byte(0); else { Byte(1); Boolean(value.Value); } }
        internal void String(string value) { var bytes = StrictUtf8.GetBytes(value); UInt32(checked((uint)bytes.Length)); Bytes(bytes); }
        internal void NullableString(string? value) { if (value is null) Byte(0); else { Byte(1); String(value); } }
        internal void StringList(IEnumerable<string> values) => Records(values.Select(value => { var item = new Writer(); item.String(value); return item.ToArray(); }));
        internal void StringPairs(IEnumerable<KeyValuePair<string, string>> values) => Records(values.Select(pair => { var item = new Writer(); item.String(pair.Key); item.String(pair.Value); return item.ToArray(); }));
        internal void Record(byte[] value) { UInt32(checked((uint)value.Length)); Bytes(value); }
        internal void Records(IEnumerable<byte[]> values) { var records = values.ToArray(); UInt32(checked((uint)records.Length)); foreach (var record in records) Record(record); }
        internal byte[] ToArray() => buffer.WrittenSpan.ToArray();
        private void UInt32(uint value) { var span = buffer.GetSpan(sizeof(uint)); BinaryPrimitives.WriteUInt32BigEndian(span, value); buffer.Advance(sizeof(uint)); }
        private void Bytes(ReadOnlySpan<byte> bytes) { bytes.CopyTo(buffer.GetSpan(bytes.Length)); buffer.Advance(bytes.Length); }
    }
}

internal sealed record SourceIdentityFingerprintData(
    string CanonicalPath,
    string ContentHash,
    ImmutableArray<SourceIdentityProjectMarker> ProjectMarkers,
    long Ticket);

internal sealed record SourceIdentityProjectMarker(
    string ContextFingerprint,
    string? Marker);
