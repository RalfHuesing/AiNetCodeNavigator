#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using AiNetCodeNavigator.Core.Symbols;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>Captures and rebinds physical PE references from the exact immutable bytes that are hashed.</summary>
internal static class MetadataReferenceImageCapture
{
    private static readonly StringComparer PhysicalPathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly ConditionalWeakTable<PortableExecutableReference, CapturedMetadataOwner> MetadataOwners = new();

    internal static CapturedMetadataReferences Capture(
        Solution solution,
        SourceIdentityValidatedInputs? previousInputs,
        CancellationToken cancellationToken = default,
        MetadataImageCaptureObserver? observer = null,
        int maxAttempts = 3,
        WorkspaceInputProvenance? provenance = null,
        CaptureContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(solution);

        context ??= new CaptureContext();
        Exception? lastFailure = null;
        if (maxAttempts is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Metadata capture permits one to three full attempts.");
        }

        provenance ??= WorkspaceInputProvenance.FindTestWorkspaceBuilderOutput(solution);

        var attemptsAvailable = Math.Min(maxAttempts, 3 - context.AttemptsUsed);
        for (var attempt = 0; attempt < attemptsAvailable; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.AttemptsUsed++;
            try
            {
                var captured = CaptureOnce(solution, previousInputs, provenance, context.ImagesByPath, context.AttemptsUsed - 1, cancellationToken, observer);
                return captured with { AttemptsUsed = context.AttemptsUsed };
            }
            catch (MetadataImageUnsupportedException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException)
            {
                lastFailure = exception;
                context.ImagesByPath.Clear();
            }
        }

        throw new IOException(
            $"Unable to capture all metadata-reference images after {context.AttemptsUsed} attempt(s): {lastFailure?.Message ?? "no capture attempts remain"}",
            lastFailure);
    }

    private static CapturedMetadataReferences CaptureOnce(
        Solution solution,
        SourceIdentityValidatedInputs? previousInputs,
        WorkspaceInputProvenance? provenance,
        Dictionary<string, CapturedImage> imagesByPath,
        int attempt,
        CancellationToken cancellationToken,
        MetadataImageCaptureObserver? observer)
    {
        var previousProjects = previousInputs?.Projects.ToDictionary(item => item.OwnerProjectId);
        var evidence = ImmutableArray.CreateBuilder<SourceIdentityMetadataReferenceEvidence>();
        var projects = ImmutableArray.CreateBuilder<SourceIdentityProjectProvenance>();
        var updated = solution;
        var solutionChanged = false;
        var requiresWorkspaceReload = false;

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bindingInputs = ImmutableArray.CreateBuilder<SourceIdentityCapturedInput>();
            string? unsupportedReason = null;
            var analyzerReferences = ImmutableArray.CreateBuilder<AnalyzerReference>(project.AnalyzerReferences.Count);
            var analyzerReferencesChanged = false;
            var projectSolutionChanged = false;
            foreach (var analyzerReference in project.AnalyzerReferences)
            {
                var analyzerPath = AnalyzerImageCapture.GetSourcePath(analyzerReference);
                if (analyzerPath is null)
                {
                    unsupportedReason ??= $"Project '{project.FilePath ?? project.Name}' has an analyzer reference whose source image path cannot be established.";
                    analyzerReferences.Add(analyzerReference);
                    continue;
                }

                var rootImages = CaptureManagedImageSet(
                    analyzerPath,
                    MetadataImageKind.Assembly,
                    imagesByPath,
                    attempt,
                    cancellationToken,
                    observer);
                var hasGenerators = AnalyzerImageCapture.ContainsSourceGenerators(rootImages, project.Language);

                // Ordinary analyzers are not executed when Roslyn constructs a Compilation. Only
                // source generators contribute generated source to declaration identity. Classify
                // the exact captured PE metadata so a reused AnalyzerFileReference cannot retain a
                // stale GetGenerators result after its path is replaced.
                if (!hasGenerators)
                {
                    var primaryAnalyzer = AnalyzerImageCapture.CapturePrimaryImage(analyzerReference, rootImages);
                    analyzerReferences.Add(primaryAnalyzer.Reference);
                    analyzerReferencesChanged |= !ReferenceEquals(primaryAnalyzer.Reference, analyzerReference);
                    if (primaryAnalyzer.UnsupportedReason is not null)
                    {
                        unsupportedReason ??= primaryAnalyzer.UnsupportedReason;
                    }

                    bindingInputs.AddRange(primaryAnalyzer.Inputs);
                    continue;
                }

                var capturedAnalyzer = AnalyzerImageCapture.Capture(
                    analyzerReference,
                    imagesByPath,
                    attempt,
                    cancellationToken,
                    observer);
                analyzerReferences.Add(capturedAnalyzer.Reference);
                analyzerReferencesChanged |= !ReferenceEquals(capturedAnalyzer.Reference, analyzerReference);
                if (capturedAnalyzer.UnsupportedReason is not null)
                {
                    unsupportedReason ??= $"Project '{project.FilePath ?? project.Name}' has source generator '{analyzerPath}' with unsupported captured binding inputs: {capturedAnalyzer.UnsupportedReason}";
                    continue;
                }

                bindingInputs.AddRange(capturedAnalyzer.Inputs);
            }

            if (analyzerReferencesChanged)
            {
                updated = updated.WithProjectAnalyzerReferences(project.Id, analyzerReferences.ToImmutable());
                solutionChanged = true;
                projectSolutionChanged = true;
            }

            if (unsupportedReason is null && provenance?.Matches(project) != true)
            {
                unsupportedReason = $"Project '{project.FilePath ?? project.Name}' has no matching workspace-owner proof for its analyzer configuration and binding providers.";
            }

            if (unsupportedReason is null
                && project.CompilationOptions?.MetadataReferenceResolver is { ResolveMissingAssemblies: true })
            {
                unsupportedReason = $"Project '{project.FilePath ?? project.Name}' enables metadata missing-assembly resolution, but those resolver-produced images are not captured.";
            }

            if (unsupportedReason is null
                && project.CompilationOptions?.StrongNameProvider is not null
                && (project.CompilationOptions.StrongNameProvider.GetType() != typeof(DesktopStrongNameProvider)
                    || !string.IsNullOrEmpty(project.CompilationOptions.CryptoKeyFile)
                    || !string.IsNullOrEmpty(project.CompilationOptions.CryptoKeyContainer)))
            {
                unsupportedReason = $"Project '{project.FilePath ?? project.Name}' has a strong-name provider or external signing key without captured effective signing inputs.";
            }

            var metadataReferences = project.MetadataReferences;
            var reboundReferences = ImmutableArray.CreateBuilder<MetadataReference>(metadataReferences.Count);
            var metadataReferencesChanged = false;
            for (var ordinal = 0; ordinal < metadataReferences.Count; ordinal++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (metadataReferences[ordinal] is not PortableExecutableReference reference)
                {
                    throw new MetadataImageUnsupportedException(
                        $"Project '{project.FilePath ?? project.Name}' has a metadata reference without a capturable portable executable image.");
                }

                var referenceImages = CaptureReferenceImages(reference, imagesByPath, attempt, cancellationToken, observer);
                var documentation = CaptureDocumentation(reference, imagesByPath, attempt, cancellationToken, observer);
                if (!documentation.IsStable)
                {
                    unsupportedReason ??= $"Project '{project.FilePath ?? project.Name}' has a metadata documentation provider without captured immutable XML bytes.";
                }
                else if (documentation.Hash != "missing")
                {
                    bindingInputs.Add(new SourceIdentityCapturedInput(
                        "xml-documentation-image",
                        documentation.CanonicalImageKey,
                        documentation.Hash));
                }

                var imageEvidence = referenceImages
                    .Select(image => new SourceIdentityImageEvidence(image.CanonicalPath, image.Sha256))
                    .ToImmutableArray();
                evidence.Add(new SourceIdentityMetadataReferenceEvidence(project.Id, ordinal, imageEvidence));

                var unchanged = MetadataOwners.TryGetValue(reference, out var priorOwner)
                    && ImagesEqual(priorOwner.Images, imageEvidence)
                    && StringComparer.Ordinal.Equals(priorOwner.DocumentationHash, documentation.Hash);
                if (unchanged)
                {
                    reboundReferences.Add(reference);
                    continue;
                }

                var replacement = CreateReferenceFromImages(
                    referenceImages,
                    reference.Properties,
                    documentation.Provider,
                    filePath: reference.FilePath,
                    display: reference.Display,
                    documentationHash: documentation.Hash,
                    documentationIsStable: documentation.IsStable,
                    documentationImageKey: documentation.CanonicalImageKey);
                reboundReferences.Add(replacement);
                solutionChanged = true;
                metadataReferencesChanged = true;
            }

            if (metadataReferencesChanged)
            {
                updated = updated.WithProjectMetadataReferences(project.Id, reboundReferences.ToImmutable());
                projectSolutionChanged = true;
            }

            if (unsupportedReason is null && provenance?.Matches(project) != true)
            {
                unsupportedReason = $"Project '{project.FilePath ?? project.Name}' has no matching workspace-owner proof for its analyzer configuration and binding providers.";
            }

            var projectIsSupported = unsupportedReason is null;
            var projectBindingInputs = bindingInputs.ToImmutable();
            if (previousProjects is not null
                && previousProjects.TryGetValue(project.Id, out var previousProject)
                && (!CapturedInputsEqual(
                    previousProject.BindingInputs.Where(input => input.Kind.StartsWith("analyzer-", StringComparison.Ordinal)).ToImmutableArray(),
                    projectBindingInputs.Where(input => input.Kind.StartsWith("analyzer-", StringComparison.Ordinal)).ToImmutableArray())
                    || (projectIsSupported
                        && !projectSolutionChanged
                        && !CapturedInputsEqual(previousProject.BindingInputs, projectBindingInputs))))
            {
                requiresWorkspaceReload = true;
            }
            projects.Add(new SourceIdentityProjectProvenance(
                project.Id,
                projectIsSupported,
                unsupportedReason,
                projectBindingInputs,
                OwnerCreatedAnalyzerConfigProvider: projectIsSupported,
                OwnerCreatedSyntaxTreeOptionsProvider: projectIsSupported && project.CompilationOptions?.SyntaxTreeOptionsProvider is not null,
                OwnerCreatedMetadataReferenceResolver: projectIsSupported && project.CompilationOptions?.MetadataReferenceResolver is not null,
                OwnerCreatedStrongNameProvider: projectIsSupported && project.CompilationOptions?.StrongNameProvider is not null));
        }

        var validatedInputs = new SourceIdentityValidatedInputs(projects.ToImmutable(), evidence.ToImmutable());
        var updatedProvenance = provenance?.CarryKnownMetadataReferenceChanges(solution, updated);
        return new CapturedMetadataReferences(updated, validatedInputs, solutionChanged, requiresWorkspaceReload, Provenance: updatedProvenance);
    }

    private static ImmutableArray<CapturedImage> CaptureReferenceImages(
        PortableExecutableReference reference,
        IDictionary<string, CapturedImage> imagesByPath,
        int attempt,
        CancellationToken cancellationToken,
        MetadataImageCaptureObserver? observer)
    {
        if (MetadataOwners.TryGetValue(reference, out var knownOwner) && !knownOwner.InMemoryImages.IsDefaultOrEmpty)
        {
            return knownOwner.InMemoryImages;
        }

        if (string.IsNullOrWhiteSpace(reference.FilePath) || !Path.IsPathRooted(reference.FilePath))
        {
            throw new MetadataImageUnsupportedException(
                $"Metadata reference '{reference.Display}' has no physical absolute path or immutable image provenance from its creating owner.");
        }

        return CaptureManagedImageSet(reference.FilePath!, reference.Properties.Kind, imagesByPath, attempt, cancellationToken, observer);
    }

    private static CapturedDocumentation CaptureDocumentation(
        PortableExecutableReference reference,
        IDictionary<string, CapturedImage> imagesByPath,
        int attempt,
        CancellationToken cancellationToken,
        MetadataImageCaptureObserver? observer)
    {
        if (MetadataOwners.TryGetValue(reference, out var knownOwner)
            && !knownOwner.InMemoryImages.IsDefaultOrEmpty)
        {
            return new CapturedDocumentation(
                knownOwner.DocumentationProvider,
                knownOwner.DocumentationHash,
                knownOwner.DocumentationIsStable,
                knownOwner.DocumentationImageKey);
        }

        if (string.IsNullOrWhiteSpace(reference.FilePath) || !Path.IsPathRooted(reference.FilePath))
        {
            return new CapturedDocumentation(null, "missing", true, "missing");
        }

        var documentationPath = Path.ChangeExtension(reference.FilePath, ".xml");
        if (!File.Exists(documentationPath))
        {
            return new CapturedDocumentation(null, "missing", true, "missing");
        }

        var image = CapturePhysicalImage(documentationPath, imagesByPath, attempt, cancellationToken, observer);
        return new CapturedDocumentation(
            XmlDocumentationProvider.CreateFromBytes(image.Bytes.ToArray()),
            image.Sha256,
            true,
            image.CanonicalPath);
    }

    internal static ImmutableArray<CapturedImage> CaptureManagedImageSet(
        string imagePath,
        MetadataImageKind kind,
        IDictionary<string, CapturedImage> imagesByPath,
        int attempt,
        CancellationToken cancellationToken,
        MetadataImageCaptureObserver? observer)
    {
        var primary = CapturePhysicalImage(imagePath, imagesByPath, attempt, cancellationToken, observer);
        if (kind == MetadataImageKind.Module)
        {
            return [primary];
        }

        using var stream = new MemoryStream(primary.Bytes.ToArray(), writable: false);
        using var peReader = new PEReader(stream);
        var metadataReader = peReader.GetMetadataReader();
        if (!metadataReader.IsAssembly)
        {
            throw new BadImageFormatException($"Metadata image '{imagePath}' is not a managed assembly manifest.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(imagePath))!;
        var result = ImmutableArray.CreateBuilder<CapturedImage>();
        result.Add(primary);
        foreach (var fileHandle in metadataReader.AssemblyFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assemblyFile = metadataReader.GetAssemblyFile(fileHandle);
            if (!assemblyFile.ContainsMetadata)
            {
                continue;
            }

            var modulePath = Path.GetFullPath(metadataReader.GetString(assemblyFile.Name), directory);
            result.Add(CapturePhysicalImage(modulePath, imagesByPath, attempt, cancellationToken, observer));
        }

        return result.ToImmutable();
    }

    internal static CapturedImage CapturePhysicalImage(
        string path,
        IDictionary<string, CapturedImage> imagesByPath,
        int attempt,
        CancellationToken cancellationToken,
        MetadataImageCaptureObserver? observer)
    {
        var fullPath = Path.GetFullPath(path);
        var key = GetCanonicalImageKey(fullPath);
        if (imagesByPath.TryGetValue(key, out var existing))
        {
            return existing;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var bytes = ReadStableImage(fullPath, attempt, cancellationToken, observer);
        var image = new CapturedImage(fullPath, key, bytes, Convert.ToHexString(SHA256.HashData(bytes.AsSpan())));
        imagesByPath.Add(key, image);
        observer?.OnImageCaptured?.Invoke(key);
        return image;
    }

    private static ImmutableArray<byte> ReadStableImage(
        string path,
        int attempt,
        CancellationToken cancellationToken,
        MetadataImageCaptureObserver? observer)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = new FileInfo(path);
        if (!before.Exists)
        {
            throw new IOException($"Metadata image does not exist: '{path}'.");
        }

        var length = before.Length;
        var writeTime = before.LastWriteTimeUtc;
        observer?.BeforeImageRead?.Invoke(path, attempt);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var buffer = new MemoryStream(length is > 0 and <= int.MaxValue ? (int)length : 0);
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var after = new FileInfo(path);
        if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != writeTime || bytes.LongLength != length)
        {
            throw new IOException($"Metadata image changed while it was being captured: '{path}'.");
        }

        return ImmutableArray.CreateRange(bytes);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Metadata ownership transfers to a weak-key owner associated with the returned reference; that owner remains alive while the reference is reachable and disposes metadata after its last owner is collected.")]
    private static PortableExecutableReference CreateReferenceFromImages(
        ImmutableArray<CapturedImage> images,
        MetadataReferenceProperties properties,
        DocumentationProvider? documentationProvider,
        string? filePath,
        string? display,
        string documentationHash = "missing",
        bool documentationIsStable = true,
        string documentationImageKey = "missing")
    {
        var inMemoryImages = images.All(image => image.CanonicalPath.StartsWith("in-memory:", StringComparison.Ordinal))
            ? images
            : ImmutableArray<CapturedImage>.Empty;
        if (properties.Kind == MetadataImageKind.Module)
        {
            var module = ModuleMetadata.CreateFromImage(images[0].Bytes);
            CapturedMetadataOwner? owner = null;
            try
            {
                var moduleReference = module.GetReference(documentationProvider, filePath, display).WithProperties(properties);
                owner = new CapturedMetadataOwner(module, CreateEvidence(images), inMemoryImages, documentationProvider,
                    documentationHash, documentationIsStable, documentationImageKey);
                MetadataOwners.Add(moduleReference, owner);
                owner = null;
                return moduleReference;
            }
            catch
            {
                if (owner is not null)
                {
                    owner.Dispose();
                }
                else
                {
                    module.Dispose();
                }

                throw;
            }
        }

        var modules = ImmutableArray.CreateBuilder<ModuleMetadata>(images.Length);
        AssemblyMetadata? assemblyMetadata = null;
        CapturedMetadataOwner? assemblyOwner = null;
        try
        {
            foreach (var image in images)
            {
                modules.Add(ModuleMetadata.CreateFromImage(image.Bytes));
            }

            assemblyMetadata = AssemblyMetadata.Create(modules.ToImmutable());
            modules.Clear();
            var reference = assemblyMetadata.GetReference(
                documentationProvider,
                properties.Aliases,
                properties.EmbedInteropTypes,
                filePath,
                display);
            assemblyOwner = new CapturedMetadataOwner(assemblyMetadata, CreateEvidence(images), inMemoryImages, documentationProvider,
                documentationHash, documentationIsStable, documentationImageKey);
            MetadataOwners.Add(reference, assemblyOwner);
            assemblyOwner = null;
            assemblyMetadata = null;
            return reference;
        }
        catch
        {
            if (assemblyOwner is not null)
            {
                assemblyOwner.Dispose();
            }
            else
            {
                assemblyMetadata?.Dispose();
            }

            foreach (var module in modules)
            {
                module.Dispose();
            }

            throw;
        }
    }

    private static bool ImagesEqual(
        ImmutableArray<SourceIdentityImageEvidence> left,
        ImmutableArray<SourceIdentityImageEvidence> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!StringComparer.Ordinal.Equals(left[index].CanonicalImageKey, right[index].CanonicalImageKey)
                || !StringComparer.Ordinal.Equals(left[index].Sha256, right[index].Sha256))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CapturedInputsEqual(
        ImmutableArray<SourceIdentityCapturedInput> left,
        ImmutableArray<SourceIdentityCapturedInput> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!StringComparer.Ordinal.Equals(left[index].Kind, right[index].Kind)
                || !StringComparer.Ordinal.Equals(left[index].LogicalKey, right[index].LogicalKey)
                || !StringComparer.Ordinal.Equals(left[index].Sha256, right[index].Sha256))
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableArray<SourceIdentityImageEvidence> CreateEvidence(ImmutableArray<CapturedImage> images) =>
        images.Select(image => new SourceIdentityImageEvidence(image.CanonicalPath, image.Sha256)).ToImmutableArray();

    internal static PortableExecutableReference CreateOwnedReferenceFromImages(
        ImmutableArray<ImmutableArray<byte>> images,
        MetadataReferenceProperties properties,
        DocumentationProvider? documentationProvider = null,
        string? filePath = null,
        string? display = null,
        ImmutableArray<byte> documentationXmlBytes = default)
    {
        if (images.IsDefaultOrEmpty || images.Any(image => image.IsDefaultOrEmpty))
        {
            throw new ArgumentException("At least one non-empty immutable metadata image is required.", nameof(images));
        }

        var capturedImages = images.Select(bytes =>
        {
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes.AsSpan()));
            return new CapturedImage(null, $"in-memory:{sha256}", bytes, sha256);
        }).ToImmutableArray();
        if (!documentationXmlBytes.IsDefault)
        {
            documentationProvider = XmlDocumentationProvider.CreateFromBytes(documentationXmlBytes.ToArray());
        }

        var documentationHash = documentationXmlBytes.IsDefault
            ? documentationProvider is null ? "missing" : "untracked-provider"
            : Convert.ToHexString(SHA256.HashData(documentationXmlBytes.AsSpan()));
        return CreateReferenceFromImages(
            capturedImages,
            properties,
            documentationProvider,
            filePath,
            display,
            documentationHash,
            documentationIsStable: documentationXmlBytes.IsDefault ? documentationProvider is null : true,
            documentationImageKey: documentationXmlBytes.IsDefault ? "missing" : $"in-memory-xml:{documentationHash}");
    }

    internal sealed class MetadataImageCaptureObserver
    {
        internal Action<string, int>? BeforeImageRead { get; init; }

        internal Action<string>? OnImageCaptured { get; init; }
    }

    internal sealed class CaptureContext
    {
        internal Dictionary<string, CapturedImage> ImagesByPath { get; } = new(PhysicalPathComparer);

        internal int AttemptsUsed { get; set; }
    }

    private static string GetCanonicalImageKey(string path)
    {
        if (!AnalysisPathIdentity.TryNormalize(Path.GetFullPath(path), out var normalized))
        {
            throw new MetadataImageUnsupportedException($"Physical metadata image path '{path}' could not be normalized for identity capture.");
        }

        return normalized;
    }

    internal sealed record CapturedMetadataReferences(
        Solution Solution,
        SourceIdentityValidatedInputs Inputs,
        bool SolutionChanged,
        bool RequiresWorkspaceReload,
        int AttemptsUsed = 1,
        WorkspaceInputProvenance? Provenance = null);

    internal sealed record CapturedImage(
        string? PhysicalPath,
        string CanonicalPath,
        ImmutableArray<byte> Bytes,
        string Sha256);

    private sealed record CapturedDocumentation(
        DocumentationProvider? Provider,
        string Hash,
        bool IsStable,
        string CanonicalImageKey);

    internal sealed class MetadataImageUnsupportedException(string message) : Exception(message);

    private sealed class CapturedMetadataOwner : IDisposable
    {
        private IDisposable? metadata;

        internal CapturedMetadataOwner(
            IDisposable metadata,
            ImmutableArray<SourceIdentityImageEvidence> images,
            ImmutableArray<CapturedImage> inMemoryImages,
            DocumentationProvider? documentationProvider,
            string documentationHash,
            bool documentationIsStable,
            string documentationImageKey)
        {
            this.metadata = metadata;
            Images = images;
            InMemoryImages = inMemoryImages;
            DocumentationProvider = documentationProvider;
            DocumentationHash = documentationHash;
            DocumentationIsStable = documentationIsStable;
            DocumentationImageKey = documentationImageKey;
        }

        internal ImmutableArray<SourceIdentityImageEvidence> Images { get; }

        internal ImmutableArray<CapturedImage> InMemoryImages { get; }

        internal DocumentationProvider? DocumentationProvider { get; }

        internal string DocumentationHash { get; }

        internal bool DocumentationIsStable { get; }

        internal string DocumentationImageKey { get; }

        ~CapturedMetadataOwner() => Dispose(false);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            var owned = Interlocked.Exchange(ref metadata, null);
            if (owned is null)
            {
                return;
            }

            if (disposing)
            {
                owned.Dispose();
                return;
            }

            try
            {
                owned.Dispose();
            }
            catch
            {
                // Finalizer cleanup must not surface exceptions on the finalizer thread.
            }
        }
    }
}
