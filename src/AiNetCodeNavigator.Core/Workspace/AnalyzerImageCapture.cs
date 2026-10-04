#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using AiNetCodeNavigator.Core.Symbols;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>Captures file-backed analyzer inputs and rebinds them to immutable captured copies.</summary>
internal static class AnalyzerImageCapture
{
    private static readonly ConditionalWeakTable<AnalyzerReference, CapturedAnalyzerOwner> Owners = new();
    private static readonly Lazy<Dictionary<string, string>> SharedAssemblyPaths = new(CreateSharedAssemblyPaths);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    internal static CapturedAnalyzerReference Capture(
        AnalyzerReference analyzerReference,
        IDictionary<string, MetadataReferenceImageCapture.CapturedImage> imagesByPath,
        int attempt,
        CancellationToken cancellationToken,
        MetadataReferenceImageCapture.MetadataImageCaptureObserver? observer)
    {
        if (analyzerReference is not AnalyzerFileReference fileReference
            || string.IsNullOrWhiteSpace(fileReference.FullPath)
            || !Path.IsPathRooted(fileReference.FullPath))
        {
            return Unsupported(analyzerReference,
                $"Analyzer reference '{analyzerReference.Display}' has no capturable absolute file image.");
        }

        var sourcePath = Owners.TryGetValue(analyzerReference, out var priorOwner)
            ? priorOwner.SourcePath
            : Path.GetFullPath(fileReference.FullPath);
        var rootImages = MetadataReferenceImageCapture.CaptureManagedImageSet(
            sourcePath,
            MetadataImageKind.Assembly,
            imagesByPath,
            attempt,
            cancellationToken,
            observer);
        if (rootImages.IsDefaultOrEmpty)
        {
            return Unsupported(analyzerReference, $"Analyzer image '{sourcePath}' has no managed assembly image.");
        }

        var allImages = new Dictionary<string, MetadataReferenceImageCapture.CapturedImage>(PathComparer);
        var assembliesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var identitiesByName = new Dictionary<string, AssemblyName>(StringComparer.OrdinalIgnoreCase);
        var defaultContextDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dependencyUnits = new Dictionary<string, ImmutableArray<MetadataReferenceImageCapture.CapturedImage>>(StringComparer.OrdinalIgnoreCase);
        var satellitesByParent = new Dictionary<string, ImmutableArray<CapturedSatelliteResource>>(PathComparer);
        var work = new Queue<AnalyzerAssemblyUnit>();
        var dependencyReasons = new List<string>();
        var manifestInputs = new Dictionary<string, MetadataReferenceImageCapture.CapturedImage>(PathComparer);
        var rootIdentity = ReadAssemblyIdentity(rootImages[0]);
        AddUnit(sourcePath, rootIdentity, rootImages);
        if (rootIdentity.Name is not null)
        {
            assembliesByName.Add(rootIdentity.Name, sourcePath);
            identitiesByName.Add(rootIdentity.Name, rootIdentity);
        }

        while (work.TryDequeue(out var unit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var satellites = CaptureSatelliteResources(
                unit.AssemblyPath,
                unit.Identity.Name ?? throw new MetadataReferenceImageCapture.MetadataImageUnsupportedException(
                    $"Captured analyzer component '{unit.AssemblyPath}' has no assembly simple name."),
                imagesByPath,
                attempt,
                cancellationToken,
                observer);
            if (!satellites.IsDefaultOrEmpty)
            {
                satellitesByParent[Path.GetFullPath(unit.AssemblyPath)] = satellites;
                foreach (var satellite in satellites.SelectMany(resource => resource.Images))
                {
                    allImages.TryAdd(satellite.CanonicalPath, satellite);
                }
            }

            var resolver = CreateResolver(unit.AssemblyPath, imagesByPath, manifestInputs, attempt, cancellationToken, observer);
            foreach (var image in unit.Images)
            {
                foreach (var reference in ReadAssemblyReferences(image))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryGetSharedAssemblyPath(reference, out _))
                    {
                        continue;
                    }

                    if (assembliesByName.ContainsKey(reference.Name!))
                    {
                        var existingPath = assembliesByName[reference.Name!];
                        var existingIdentity = identitiesByName[reference.Name!];
                        var matchesExisting = MatchesReference(reference, existingIdentity);
                        if (!matchesExisting
                            && defaultContextDependencies.Contains(reference.Name!)
                            && TryResolveDefaultAssembly(
                                reference,
                                imagesByPath,
                                attempt,
                                cancellationToken,
                                observer,
                                out var selectedDefault,
                                out _)
                            && selectedDefault is not null
                            && PathComparer.Equals(existingPath, selectedDefault.Path)
                            && SameAssemblyIdentity(existingIdentity, selectedDefault.Identity))
                        {
                            matchesExisting = true;
                        }

                        if (!matchesExisting)
                        {
                            dependencyReasons.Add($"Analyzer '{sourcePath}' has ambiguous dependency binding for '{reference.FullName}'.");
                        }

                        continue;
                    }

                    var resolvedDependency = ResolveDependencyPath(
                        reference,
                        unit.AssemblyPath,
                        resolver,
                        imagesByPath,
                        attempt,
                        cancellationToken,
                        observer,
                        out var resolutionFailure);
                    if (resolvedDependency is null)
                    {
                        dependencyReasons.Add(resolutionFailure ??
                            $"Analyzer '{sourcePath}' has an untracked private dependency '{reference.FullName}' (expected '{reference.Name}.dll' or '{reference.Name}.exe').");
                        continue;
                    }

                    var dependencyPath = resolvedDependency.Path;
                    var dependencyImages = MetadataReferenceImageCapture.CaptureManagedImageSet(
                        dependencyPath,
                        MetadataImageKind.Assembly,
                        imagesByPath,
                        attempt,
                        cancellationToken,
                        observer);
                    var dependencyIdentity = ReadAssemblyIdentity(dependencyImages[0]);
                    if (!SameAssemblyIdentity(resolvedDependency.Identity, dependencyIdentity))
                    {
                        dependencyReasons.Add($"Analyzer '{sourcePath}' resolved dependency '{reference.FullName}' to incompatible captured image '{dependencyPath}'.");
                        continue;
                    }

                    assembliesByName.Add(reference.Name!, dependencyPath);
                    identitiesByName.Add(reference.Name!, dependencyIdentity);
                    if (resolvedDependency.IsDefaultContextSelection)
                    {
                        defaultContextDependencies.Add(reference.Name!);
                    }

                    dependencyUnits.Add(reference.Name!, dependencyImages);
                    AddImages(dependencyImages);
                    AddUnit(dependencyPath, dependencyIdentity, dependencyImages);
                }

                if (HasNativeImports(image))
                {
                    dependencyReasons.Add($"Analyzer '{sourcePath}' has native imports whose complete image closure cannot be proven by its managed dependency manifest.");
                }
            }
        }

        var inputs = BuildInputs(rootImages, allImages.Values, manifestInputs.Values);
        if (dependencyReasons.Count > 0)
        {
            return new CapturedAnalyzerReference(
                analyzerReference,
                inputs,
                string.Join(" ", dependencyReasons.Distinct(StringComparer.Ordinal)));
        }

        if (priorOwner is not null && InputsEqual(priorOwner.Inputs, inputs))
        {
            return new CapturedAnalyzerReference(analyzerReference, inputs, null);
        }

        return CreateCapturedReference(sourcePath, rootImages, inputs, dependencyUnits, satellitesByParent);

        void AddUnit(
            string assemblyPath,
            AssemblyName identity,
            ImmutableArray<MetadataReferenceImageCapture.CapturedImage> images)
        {
            AddImages(images);
            work.Enqueue(new AnalyzerAssemblyUnit(assemblyPath, identity, images));
        }

        void AddImages(IEnumerable<MetadataReferenceImageCapture.CapturedImage> images)
        {
            foreach (var image in images)
            {
                allImages.TryAdd(image.CanonicalPath, image);
            }
        }
    }

    internal static CapturedAnalyzerReference CapturePrimaryImage(
        AnalyzerReference analyzerReference,
        ImmutableArray<MetadataReferenceImageCapture.CapturedImage> rootImages)
    {
        if (analyzerReference is not AnalyzerFileReference fileReference
            || string.IsNullOrWhiteSpace(fileReference.FullPath)
            || rootImages.IsDefaultOrEmpty)
        {
            return Unsupported(analyzerReference, $"Analyzer reference '{analyzerReference.Display}' has no capturable primary image.");
        }

        var sourcePath = GetSourcePath(analyzerReference) ?? Path.GetFullPath(fileReference.FullPath);
        var inputs = BuildInputs(rootImages, [], []);
        if (Owners.TryGetValue(analyzerReference, out var priorOwner)
            && PrimaryInputsEqual(priorOwner.Inputs, inputs))
        {
            return new CapturedAnalyzerReference(analyzerReference, priorOwner.Inputs, null);
        }

        return CreateCapturedReference(
            sourcePath,
            rootImages,
            inputs,
            ImmutableDictionary<string, ImmutableArray<MetadataReferenceImageCapture.CapturedImage>>.Empty,
            ImmutableDictionary<string, ImmutableArray<CapturedSatelliteResource>>.Empty);
    }

    internal static bool ContainsSourceGenerators(
        ImmutableArray<MetadataReferenceImageCapture.CapturedImage> images,
        string projectLanguage)
    {
        foreach (var image in images)
        {
            using var stream = new MemoryStream(image.Bytes.ToArray(), writable: false);
            using var peReader = new PEReader(stream);
            var metadata = peReader.GetMetadataReader();
            foreach (var typeHandle in metadata.TypeDefinitions)
            {
                var type = metadata.GetTypeDefinition(typeHandle);
                foreach (var attributeHandle in type.GetCustomAttributes())
                {
                    var attribute = metadata.GetCustomAttribute(attributeHandle);
                    if (TryGetAttributeType(metadata, attribute.Constructor, out var name, out var @namespace)
                        && StringComparer.Ordinal.Equals(name, "GeneratorAttribute")
                        && StringComparer.Ordinal.Equals(@namespace, "Microsoft.CodeAnalysis"))
                    {
                        if (AppliesToLanguage(metadata, attribute, projectLanguage))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static bool AppliesToLanguage(
        MetadataReader metadata,
        CustomAttribute attribute,
        string projectLanguage)
    {
        // GeneratorAttribute(string firstLanguage, params string[] additionalLanguages)
        // stores the language selection in the custom-attribute fixed arguments.
        try
        {
            var value = metadata.GetBlobReader(attribute.Value);
            if (value.ReadUInt16() != 1)
            {
                return false;
            }

            if (value.RemainingBytes == sizeof(ushort))
            {
                // The parameterless GeneratorAttribute constructor applies to every language.
                return true;
            }

            var firstLanguage = value.ReadSerializedString();
            if (StringComparer.Ordinal.Equals(firstLanguage, projectLanguage))
            {
                return true;
            }

            var additionalLanguageCount = value.ReadInt32();
            if (additionalLanguageCount < 0 || additionalLanguageCount > value.RemainingBytes)
            {
                return false;
            }

            for (var index = 0; index < additionalLanguageCount; index++)
            {
                if (StringComparer.Ordinal.Equals(value.ReadSerializedString(), projectLanguage))
                {
                    return true;
                }
            }
        }
        catch (BadImageFormatException)
        {
            return false;
        }

        return false;
    }

    private static AssemblyDependencyResolver? CreateResolver(
        string assemblyPath,
        IDictionary<string, MetadataReferenceImageCapture.CapturedImage> imagesByPath,
        IDictionary<string, MetadataReferenceImageCapture.CapturedImage> manifestInputs,
        int attempt,
        CancellationToken cancellationToken,
        MetadataReferenceImageCapture.MetadataImageCaptureObserver? observer)
    {
        var manifestPath = Path.ChangeExtension(assemblyPath, ".deps.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var manifest = MetadataReferenceImageCapture.CapturePhysicalImage(
            manifestPath,
            imagesByPath,
            attempt,
            cancellationToken,
            observer);
        manifestInputs.TryAdd(manifest.CanonicalPath, manifest);

        // Keep the captured manifest stable while the public resolver reads it to construct its binding map.
        using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var currentHash = Convert.ToHexString(SHA256.HashData(stream));
        if (!StringComparer.Ordinal.Equals(currentHash, manifest.Sha256))
        {
            throw new IOException($"Analyzer dependency manifest changed while it was being captured: '{manifestPath}'.");
        }

        return new AssemblyDependencyResolver(assemblyPath);
    }

    private static ImmutableArray<CapturedSatelliteResource> CaptureSatelliteResources(
        string componentPath,
        string componentAssemblyName,
        IDictionary<string, MetadataReferenceImageCapture.CapturedImage> imagesByPath,
        int attempt,
        CancellationToken cancellationToken,
        MetadataReferenceImageCapture.MetadataImageCaptureObserver? observer)
    {
        var componentDirectory = Path.GetDirectoryName(Path.GetFullPath(componentPath));
        if (componentDirectory is null)
        {
            return [];
        }

        var satelliteFileName = $"{componentAssemblyName}.resources.dll";
        var satellites = ImmutableArray.CreateBuilder<CapturedSatelliteResource>();
        foreach (var cultureDirectory in Directory.EnumerateDirectories(componentDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cultureName = Path.GetFileName(cultureDirectory);
            if (!IsCanonicalCultureName(cultureName))
            {
                continue;
            }

            var satellitePath = Path.Combine(cultureDirectory, satelliteFileName);
            if (!File.Exists(satellitePath))
            {
                continue;
            }

            var images = MetadataReferenceImageCapture.CaptureManagedImageSet(
                satellitePath,
                MetadataImageKind.Assembly,
                imagesByPath,
                attempt,
                cancellationToken,
                observer);
            if (!images.IsDefaultOrEmpty)
            {
                satellites.Add(new CapturedSatelliteResource(cultureName, images));
            }
        }

        return satellites.ToImmutable();
    }

    private static bool IsCanonicalCultureName(string name)
    {
        try
        {
            return StringComparer.OrdinalIgnoreCase.Equals(CultureInfo.GetCultureInfo(name).Name, name);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static ResolvedAnalyzerDependency? ResolveDependencyPath(
        AssemblyName reference,
        string componentPath,
        AssemblyDependencyResolver? resolver,
        IDictionary<string, MetadataReferenceImageCapture.CapturedImage> imagesByPath,
        int attempt,
        CancellationToken cancellationToken,
        MetadataReferenceImageCapture.MetadataImageCaptureObserver? observer,
        out string? failure)
    {
        failure = null;
        var fromManifest = resolver?.ResolveAssemblyToPath(reference);
        if (!string.IsNullOrWhiteSpace(fromManifest))
        {
            var manifestPath = Path.GetFullPath(fromManifest);
            if (!File.Exists(manifestPath))
            {
                failure = $"Analyzer dependency manifest for '{componentPath}' resolves '{reference.FullName}' to missing image '{manifestPath}'.";
                return null;
            }

            var manifestImage = MetadataReferenceImageCapture.CapturePhysicalImage(
                manifestPath,
                imagesByPath,
                attempt,
                cancellationToken,
                observer);
            try
            {
                var manifestIdentity = ReadAssemblyIdentity(manifestImage);
                if (MatchesReference(reference, manifestIdentity))
                {
                    return new ResolvedAnalyzerDependency(manifestPath, manifestIdentity);
                }
            }
            catch (BadImageFormatException)
            {
                // The declared asset is present but is not a managed assembly matching the request.
            }

            failure = $"Analyzer dependency manifest for '{componentPath}' resolves '{reference.FullName}' to incompatible image '{manifestPath}'.";
            return null;
        }

        var candidates = new HashSet<string>(PathComparer);
        var directory = Path.GetDirectoryName(Path.GetFullPath(componentPath))!;
        foreach (var extension in new[] { ".dll", ".exe" })
        {
            var adjacent = Path.Combine(directory, reference.Name + extension);
            if (File.Exists(adjacent))
            {
                candidates.Add(Path.GetFullPath(adjacent));
            }
        }

        var matching = new List<ResolvedAnalyzerDependency>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidateImage = MetadataReferenceImageCapture.CapturePhysicalImage(
                candidate,
                imagesByPath,
                attempt,
                cancellationToken,
                observer);
            AssemblyName candidateIdentity;
            try
            {
                candidateIdentity = ReadAssemblyIdentity(candidateImage);
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            if (MatchesReference(reference, candidateIdentity))
            {
                matching.Add(new ResolvedAnalyzerDependency(candidate, candidateIdentity));
            }
        }
        if (matching.Count > 1)
        {
            failure = $"Analyzer '{componentPath}' has ambiguous private dependency binding for '{reference.FullName}' across {matching.Count} images.";
            return null;
        }

        if (matching.Count == 0 && candidates.Count > 0)
        {
            if (TryResolveDefaultAssembly(reference, imagesByPath, attempt, cancellationToken, observer, out var defaultDependency, out var defaultFailure))
            {
                return defaultDependency;
            }

            failure = defaultFailure
                ?? $"Analyzer '{componentPath}' has no compatible captured candidate for private dependency '{reference.FullName}' (expected '{reference.Name}.dll' or '{reference.Name}.exe').";
            return null;
        }

        if (matching.Count == 1)
        {
            return matching[0];
        }

        if (TryResolveDefaultAssembly(reference, imagesByPath, attempt, cancellationToken, observer, out var resolvedDefault, out var defaultResolutionFailure))
        {
            return resolvedDefault;
        }

        failure = defaultResolutionFailure;
        return null;
    }

    private static bool TryResolveDefaultAssembly(
        AssemblyName reference,
        IDictionary<string, MetadataReferenceImageCapture.CapturedImage> imagesByPath,
        int attempt,
        CancellationToken cancellationToken,
        MetadataReferenceImageCapture.MetadataImageCaptureObserver? observer,
        out ResolvedAnalyzerDependency? dependency,
        out string? failure)
    {
        dependency = null;
        failure = null;
        var expectedImages = $"expected '{reference.Name}.dll' or '{reference.Name}.exe'";
        Assembly selected;
        try
        {
            selected = AssemblyLoadContext.Default.LoadFromAssemblyName(reference);
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            failure = $"Default load context could not resolve private analyzer dependency '{reference.FullName}' ({expectedImages}): {exception.Message}";
            return false;
        }

        if (selected.IsDynamic || string.IsNullOrWhiteSpace(selected.Location) || !Path.IsPathRooted(selected.Location))
        {
            failure = $"Default load context resolved private analyzer dependency '{reference.FullName}' to a non-file image that cannot be captured ({expectedImages}).";
            return false;
        }

        var selectedIdentity = selected.GetName();
        if (!MatchesDefaultReference(reference, selectedIdentity))
        {
            failure = $"Default load context selected incompatible private analyzer dependency '{selectedIdentity.FullName}' for '{reference.FullName}' ({expectedImages}).";
            return false;
        }

        var selectedPath = Path.GetFullPath(selected.Location);
        var selectedImage = MetadataReferenceImageCapture.CapturePhysicalImage(
            selectedPath,
            imagesByPath,
            attempt,
            cancellationToken,
            observer);
        var capturedIdentity = ReadAssemblyIdentity(selectedImage);
        if (!SameAssemblyIdentity(selectedIdentity, capturedIdentity))
        {
            failure = $"Default load context selected '{selectedIdentity.FullName}' from '{selectedPath}', but the current image at that path has changed identity ({expectedImages}).";
            return false;
        }

        dependency = new ResolvedAnalyzerDependency(selectedPath, capturedIdentity, IsDefaultContextSelection: true);
        return true;
    }

    private static IEnumerable<AssemblyName> ReadAssemblyReferences(MetadataReferenceImageCapture.CapturedImage image)
    {
        using var stream = new MemoryStream(image.Bytes.ToArray(), writable: false);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        foreach (var handle in metadata.AssemblyReferences)
        {
            var row = metadata.GetAssemblyReference(handle);
            var identity = new AssemblyName(metadata.GetString(row.Name))
            {
                Version = row.Version,
                CultureName = row.Culture.IsNil ? null : metadata.GetString(row.Culture),
            };
            if (!row.PublicKeyOrToken.IsNil)
            {
                var key = metadata.GetBlobBytes(row.PublicKeyOrToken);
                if ((row.Flags & AssemblyFlags.PublicKey) != 0)
                {
                    identity.SetPublicKey(key);
                }
                else
                {
                    identity.SetPublicKeyToken(key);
                }
            }

            yield return identity;
        }
    }

    private static AssemblyName ReadAssemblyIdentity(MetadataReferenceImageCapture.CapturedImage image)
    {
        using var stream = new MemoryStream(image.Bytes.ToArray(), writable: false);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        if (!metadata.IsAssembly)
        {
            throw new BadImageFormatException($"Analyzer image '{image.PhysicalPath ?? image.CanonicalPath}' is not an assembly manifest.");
        }

        var definition = metadata.GetAssemblyDefinition();
        var identity = new AssemblyName(metadata.GetString(definition.Name))
        {
            Version = definition.Version,
            CultureName = definition.Culture.IsNil ? null : metadata.GetString(definition.Culture),
            Flags = (AssemblyNameFlags)definition.Flags,
        };
        if (!definition.PublicKey.IsNil)
        {
            identity.SetPublicKey(metadata.GetBlobBytes(definition.PublicKey));
        }

        return identity;
    }

    private static bool HasNativeImports(MetadataReferenceImageCapture.CapturedImage image)
    {
        using var stream = new MemoryStream(image.Bytes.ToArray(), writable: false);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        return metadata.MethodDefinitions.Any(handle =>
            (metadata.GetMethodDefinition(handle).Attributes & MethodAttributes.PinvokeImpl) != 0);
    }

    private static bool TryGetSharedAssemblyPath(AssemblyName reference, out string? path)
    {
        if (reference.Name is not null && SharedAssemblyPaths.Value.TryGetValue(reference.Name, out var candidate)
            && MatchesDefaultReference(reference, AssemblyName.GetAssemblyName(candidate)))
        {
            path = candidate;
            return true;
        }

        path = null;
        return false;
    }

    private static Dictionary<string, string> CreateSharedAssemblyPaths()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var runtimeDirectory = string.IsNullOrWhiteSpace(typeof(object).Assembly.Location)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(typeof(object).Assembly.Location));
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedAssemblies)
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            {
                if (runtimeDirectory is null
                    || !PathComparer.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), runtimeDirectory))
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Length > 0)
                {
                    result.TryAdd(name, path);
                }
            }
        }

        AddTrustedRoslynAssembly(typeof(Compilation).Assembly);
        AddTrustedRoslynAssembly(typeof(CSharpCompilation).Assembly);
        AddTrustedRoslynAssembly(typeof(AnalyzerReference).Assembly);
        AddTrustedRoslynAssembly(typeof(global::Microsoft.CodeAnalysis.Workspace).Assembly);

        return result;

        void AddTrustedRoslynAssembly(Assembly assembly)
        {
            if (!assembly.IsDynamic
                && !string.IsNullOrWhiteSpace(assembly.Location)
                && assembly.GetName().Name is { Length: > 0 } name)
            {
                result[name] = assembly.Location;
            }
        }
    }

    private static bool MatchesReference(AssemblyName reference, AssemblyName candidate)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(reference.Name, candidate.Name)
            || (reference.Version is not null && candidate.Version != reference.Version)
            || !StringComparer.OrdinalIgnoreCase.Equals(reference.CultureName ?? string.Empty, candidate.CultureName ?? string.Empty))
        {
            return false;
        }

        var referenceToken = reference.GetPublicKeyToken() ?? [];
        var candidateToken = candidate.GetPublicKeyToken() ?? [];
        return referenceToken.AsSpan().SequenceEqual(candidateToken);
    }

    private static bool MatchesDefaultReference(AssemblyName reference, AssemblyName candidate)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(reference.Name, candidate.Name)
            || !StringComparer.OrdinalIgnoreCase.Equals(reference.CultureName ?? string.Empty, candidate.CultureName ?? string.Empty))
        {
            return false;
        }

        var referenceToken = reference.GetPublicKeyToken() ?? [];
        var candidateToken = candidate.GetPublicKeyToken() ?? [];
        if (!referenceToken.AsSpan().SequenceEqual(candidateToken))
        {
            return false;
        }

        return reference.Version is null || candidate.Version is not null && candidate.Version >= reference.Version;
    }

    private static bool SameAssemblyIdentity(AssemblyName left, AssemblyName right) =>
        MatchesReference(left, right) && MatchesReference(right, left);

    private static ImmutableArray<SourceIdentityCapturedInput> BuildInputs(
        ImmutableArray<MetadataReferenceImageCapture.CapturedImage> rootImages,
        IEnumerable<MetadataReferenceImageCapture.CapturedImage> dependencyImages,
        IEnumerable<MetadataReferenceImageCapture.CapturedImage> manifests)
    {
        var inputs = ImmutableArray.CreateBuilder<SourceIdentityCapturedInput>();
        for (var index = 0; index < rootImages.Length; index++)
        {
            var image = rootImages[index];
            inputs.Add(new SourceIdentityCapturedInput(
                index == 0 ? "analyzer-image" : "analyzer-module-image",
                image.CanonicalPath,
                image.Sha256));
        }

        foreach (var image in dependencyImages.OrderBy(image => image.CanonicalPath, StringComparer.Ordinal))
        {
            if (!rootImages.Any(root => PathComparer.Equals(root.CanonicalPath, image.CanonicalPath)))
            {
                inputs.Add(new SourceIdentityCapturedInput("analyzer-dependency-image", image.CanonicalPath, image.Sha256));
            }
        }

        foreach (var image in manifests.OrderBy(image => image.CanonicalPath, StringComparer.Ordinal))
        {
            inputs.Add(new SourceIdentityCapturedInput("analyzer-dependency-manifest", image.CanonicalPath, image.Sha256));
        }

        return inputs.OrderBy(input => input.Kind, StringComparer.Ordinal)
            .ThenBy(input => input.LogicalKey, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static bool InputsEqual(
        ImmutableArray<SourceIdentityCapturedInput> left,
        ImmutableArray<SourceIdentityCapturedInput> right) =>
        left.SequenceEqual(right);

    private static bool PrimaryInputsEqual(
        ImmutableArray<SourceIdentityCapturedInput> previous,
        ImmutableArray<SourceIdentityCapturedInput> current)
    {
        var previousPrimary = previous.Where(input =>
            input.Kind is "analyzer-image" or "analyzer-module-image").ToImmutableArray();
        var currentPrimary = current.Where(input =>
            input.Kind is "analyzer-image" or "analyzer-module-image").ToImmutableArray();
        return InputsEqual(previousPrimary, currentPrimary);
    }

    private static bool TryGetAttributeType(
        MetadataReader metadata,
        EntityHandle constructor,
        out string name,
        out string @namespace)
    {
        EntityHandle attributeType = constructor.Kind switch
        {
            HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            _ => default,
        };

        switch (attributeType.Kind)
        {
            case HandleKind.TypeDefinition:
                var definition = metadata.GetTypeDefinition((TypeDefinitionHandle)attributeType);
                name = metadata.GetString(definition.Name);
                @namespace = metadata.GetString(definition.Namespace);
                return true;
            case HandleKind.TypeReference:
                var reference = metadata.GetTypeReference((TypeReferenceHandle)attributeType);
                name = metadata.GetString(reference.Name);
                @namespace = metadata.GetString(reference.Namespace);
                return true;
            default:
                name = string.Empty;
                @namespace = string.Empty;
                return false;
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "After successful registration, ConditionalWeakTable transfers the owner lifetime to the returned AnalyzerFileReference key; the owner disposes its loader and shadow files when that key is collected. The catch path disposes the owner and loader before registration succeeds.")]
    private static CapturedAnalyzerReference CreateCapturedReference(
        string sourcePath,
        ImmutableArray<MetadataReferenceImageCapture.CapturedImage> rootImages,
        ImmutableArray<SourceIdentityCapturedInput> inputs,
        IReadOnlyDictionary<string, ImmutableArray<MetadataReferenceImageCapture.CapturedImage>> dependencyUnits,
        IReadOnlyDictionary<string, ImmutableArray<CapturedSatelliteResource>> satellitesByParent)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator", "captured-analyzers", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        CapturedAnalyzerOwner? owner = null;
        CapturedAnalyzerAssemblyLoader? loader = null;
        try
        {
            var primaryOriginalDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
            var primaryShadowPath = Path.Combine(directory, Path.GetFileName(sourcePath));
            File.WriteAllBytes(primaryShadowPath, rootImages[0].Bytes.ToArray());
            var shadowDirectories = new Dictionary<string, string>(PathComparer)
            {
                [Path.GetFullPath(sourcePath)] = directory,
            };

            foreach (var module in rootImages.Skip(1))
            {
                var relativePath = Path.GetRelativePath(primaryOriginalDirectory, module.PhysicalPath!);
                if (Path.IsPathRooted(relativePath) || relativePath == ".."
                    || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    throw new MetadataReferenceImageCapture.MetadataImageUnsupportedException(
                        $"Analyzer '{sourcePath}' has a netmodule outside its captured owner directory: '{module.PhysicalPath}'.");
                }

                var moduleShadowPath = Path.Combine(directory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(moduleShadowPath)!);
                File.WriteAllBytes(moduleShadowPath, module.Bytes.ToArray());
            }

            var privatePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, unitImages) in dependencyUnits)
            {
                var unitRoot = unitImages[0];
                var originalDirectory = Path.GetDirectoryName(unitRoot.PhysicalPath!)!;
                var unitDirectory = Path.Combine(directory, "dependencies", name);
                shadowDirectories[Path.GetFullPath(unitRoot.PhysicalPath!)] = unitDirectory;
                foreach (var image in unitImages)
                {
                    var relativePath = Path.GetRelativePath(originalDirectory, image.PhysicalPath!);
                    if (Path.IsPathRooted(relativePath) || relativePath == ".."
                        || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    {
                        throw new MetadataReferenceImageCapture.MetadataImageUnsupportedException(
                            $"Analyzer dependency '{name}' has a netmodule outside its captured owner directory: '{image.PhysicalPath}'.");
                    }

                    var shadowPath = Path.Combine(unitDirectory, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(shadowPath)!);
                    File.WriteAllBytes(shadowPath, image.Bytes.ToArray());
                }

                privatePaths.Add(name, Path.Combine(unitDirectory, Path.GetFileName(unitRoot.PhysicalPath!)));
            }

            foreach (var (parentAssemblyPath, resources) in satellitesByParent)
            {
                if (!shadowDirectories.TryGetValue(Path.GetFullPath(parentAssemblyPath), out var parentShadowDirectory))
                {
                    throw new MetadataReferenceImageCapture.MetadataImageUnsupportedException(
                        $"Captured satellite resources have no shadow owner for analyzer component '{parentAssemblyPath}'.");
                }

                foreach (var resource in resources)
                {
                    var resourceRoot = resource.Images[0];
                    var originalResourceDirectory = Path.GetDirectoryName(resourceRoot.PhysicalPath!)!;
                    foreach (var image in resource.Images)
                    {
                        var relativePath = Path.GetRelativePath(originalResourceDirectory, image.PhysicalPath!);
                        if (Path.IsPathRooted(relativePath) || relativePath == ".."
                            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                        {
                            throw new MetadataReferenceImageCapture.MetadataImageUnsupportedException(
                                $"Satellite resource image '{image.PhysicalPath}' is outside its culture directory.");
                        }

                        var shadowPath = Path.Combine(parentShadowDirectory, resource.CultureName, relativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(shadowPath)!);
                        File.WriteAllBytes(shadowPath, image.Bytes.ToArray());
                    }
                }
            }

            loader = new CapturedAnalyzerAssemblyLoader(directory, privatePaths);
            var reference = new AnalyzerFileReference(primaryShadowPath, loader);
            owner = new CapturedAnalyzerOwner(sourcePath, inputs, directory, loader);
            loader = null;
            Owners.Add(reference, owner);
            owner = null;
            return new CapturedAnalyzerReference(reference, inputs, null);
        }
        catch
        {
            owner?.Dispose();
            loader?.Dispose();
            DeleteDirectory(directory);
            throw;
        }
    }

    private static CapturedAnalyzerReference Unsupported(AnalyzerReference reference, string reason) =>
        new(reference, ImmutableArray<SourceIdentityCapturedInput>.Empty, reason);

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal sealed record CapturedAnalyzerReference(
        AnalyzerReference Reference,
        ImmutableArray<SourceIdentityCapturedInput> Inputs,
        string? UnsupportedReason);

    internal static string? GetBindingFailure(AnalyzerReference analyzerReference) =>
        Owners.TryGetValue(analyzerReference, out var owner) ? owner.BindingFailure : null;

    internal static string? GetSourcePath(AnalyzerReference analyzerReference) =>
        analyzerReference is AnalyzerFileReference fileReference
            ? Owners.TryGetValue(analyzerReference, out var owner) ? owner.SourcePath : Path.GetFullPath(fileReference.FullPath)
            : null;

    private sealed record AnalyzerAssemblyUnit(
        string AssemblyPath,
        AssemblyName Identity,
        ImmutableArray<MetadataReferenceImageCapture.CapturedImage> Images);
    private sealed record CapturedSatelliteResource(
        string CultureName,
        ImmutableArray<MetadataReferenceImageCapture.CapturedImage> Images);
    private sealed record ResolvedAnalyzerDependency(
        string Path,
        AssemblyName Identity,
        bool IsDefaultContextSelection = false);

    private sealed class CapturedAnalyzerOwner : IDisposable
    {
        private CapturedAnalyzerAssemblyLoader? loader;

        internal CapturedAnalyzerOwner(
            string sourcePath,
            ImmutableArray<SourceIdentityCapturedInput> inputs,
            string directory,
            CapturedAnalyzerAssemblyLoader loader)
        {
            SourcePath = Path.GetFullPath(sourcePath);
            Inputs = inputs;
            Directory = directory;
            this.loader = loader;
        }

        internal string SourcePath { get; }
        internal ImmutableArray<SourceIdentityCapturedInput> Inputs { get; }
        internal string Directory { get; }
        internal string? BindingFailure => Volatile.Read(ref loader)?.BindingFailure;

        ~CapturedAnalyzerOwner() => Dispose(false);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            Interlocked.Exchange(ref loader, null)?.Dispose();
            DeleteDirectory(Directory);
        }
    }

    private sealed class CapturedAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader, IDisposable
    {
        private readonly string rootDirectory;
        private readonly Dictionary<string, string> dependencyPaths;
        private readonly CapturedAnalyzerLoadContext loadContext;
        private string? bindingFailure;

        internal CapturedAnalyzerAssemblyLoader(string rootDirectory, Dictionary<string, string> dependencyPaths)
        {
            this.rootDirectory = Path.GetFullPath(rootDirectory);
            this.dependencyPaths = dependencyPaths;
            loadContext = new CapturedAnalyzerLoadContext(dependencyPaths, RecordBindingFailure);
        }

        public Assembly LoadFromPath(string fullPath) => loadContext.LoadFromAssemblyPath(Path.GetFullPath(fullPath));

        public void AddDependencyLocation(string fullPath)
        {
            var canonical = Path.GetFullPath(fullPath);
            if (!canonical.StartsWith(this.rootDirectory + Path.DirectorySeparatorChar, PathComparison)
                && !PathComparer.Equals(canonical, this.rootDirectory))
            {
                var message = $"Analyzer requested uncaptured dependency location '{canonical}'.";
                RecordBindingFailure(message);
                throw new FileNotFoundException(message, canonical);
            }
        }

        internal string? BindingFailure => Volatile.Read(ref bindingFailure);

        public void Dispose() => loadContext.Unload();

        private void RecordBindingFailure(string message) => Interlocked.CompareExchange(ref bindingFailure, message, null);

        private sealed class CapturedAnalyzerLoadContext(
            Dictionary<string, string> dependencyPaths,
            Action<string> recordBindingFailure) : AssemblyLoadContext(isCollectible: true)
        {
            protected override Assembly? Load(AssemblyName assemblyName)
            {
                if (assemblyName.Name is null)
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(assemblyName.CultureName)
                    && assemblyName.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                {
                    // Missing culture-specific satellites are optional. Returning null lets the runtime
                    // probe the captured culture directories and apply normal parent-culture fallback.
                    return null;
                }

                if (dependencyPaths.TryGetValue(assemblyName.Name, out var path))
                {
                    try
                    {
                        return LoadFromAssemblyPath(path);
                    }
                    catch (Exception exception) when (exception is FileLoadException or FileNotFoundException or BadImageFormatException)
                    {
                        var message = $"Captured analyzer dependency '{assemblyName.FullName}' failed to load from '{path}': {exception.Message}";
                        recordBindingFailure(message);
                        throw new FileLoadException(message, path, exception);
                    }
                }

                if (SharedAssemblyPaths.Value.TryGetValue(assemblyName.Name, out var sharedPath)
                    && MatchesDefaultReference(assemblyName, AssemblyName.GetAssemblyName(sharedPath)))
                {
                    try
                    {
                        var sharedAssembly = Default.LoadFromAssemblyName(assemblyName);
                        if (sharedAssembly.IsDynamic
                            || string.IsNullOrWhiteSpace(sharedAssembly.Location)
                            || !PathComparer.Equals(Path.GetFullPath(sharedAssembly.Location), Path.GetFullPath(sharedPath))
                            || !MatchesDefaultReference(assemblyName, sharedAssembly.GetName()))
                        {
                            var mismatch = $"Default load context selected an unpinned host image for '{assemblyName.FullName}'.";
                            recordBindingFailure(mismatch);
                            throw new FileLoadException(mismatch, sharedPath);
                        }

                        return sharedAssembly;
                    }
                    catch (Exception exception) when (exception is FileLoadException or FileNotFoundException or BadImageFormatException)
                    {
                        var message = $"Pinned host dependency '{assemblyName.FullName}' failed to bind: {exception.Message}";
                        recordBindingFailure(message);
                        throw new FileLoadException(message, sharedPath, exception);
                    }
                }

                var failure = $"Analyzer dependency '{assemblyName.FullName}' was not part of the captured binding closure.";
                recordBindingFailure(failure);
                throw new FileNotFoundException(failure);
            }
        }
    }
}
