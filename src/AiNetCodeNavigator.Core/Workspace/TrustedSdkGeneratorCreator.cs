#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using AiNetCodeNavigator.Core.Symbols;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Narrow creator contract for source generators shipped by the registered Microsoft .NET SDK.
/// These generators consume Roslyn's project/document/configuration context and their captured
/// private managed closure; the registered distribution, exact image bytes and public-key-token
/// identity are retained as snapshot inputs. The distribution is the trust boundary.
/// </summary>
internal static class TrustedSdkGeneratorCreator
{
    private static readonly Dictionary<string, SupportedGenerator> SupportedAssemblies = new(StringComparer.Ordinal)
    {
        ["Microsoft.Interop.LibraryImportGenerator"] = new("Microsoft.NETCore.App.Ref", "B03F5F7F11D50A3A"),
        ["Microsoft.Interop.ComInterfaceGenerator"] = new("Microsoft.NETCore.App.Ref", "B03F5F7F11D50A3A"),
        ["Microsoft.Interop.JavaScript.JSImportGenerator"] = new("Microsoft.NETCore.App.Ref", "B03F5F7F11D50A3A"),
        ["System.Text.Json.SourceGeneration"] = new("Microsoft.NETCore.App.Ref", "CC7B13FFCD2DDD51"),
        ["System.Text.RegularExpressions.Generator"] = new("Microsoft.NETCore.App.Ref", "B03F5F7F11D50A3A"),
        ["Microsoft.Extensions.Logging.Generators"] = new("Microsoft.AspNetCore.App.Ref", "ADB9793829DDAE60"),
    };

    internal static GeneratorCreatorInputContract? CreateContract(
        Project project,
        string analyzerPath,
        ImmutableArray<SourceIdentityCapturedInput> capturedInputs,
        IDictionary<string, MetadataReferenceImageCapture.CapturedImage> imagesByPath,
        string? registeredDistributionPath,
        int attempt,
        CancellationToken cancellationToken,
        MetadataReferenceImageCapture.MetadataImageCaptureObserver? observer)
    {
        if (!TryGetRegisteredSdk(registeredDistributionPath, out var dotnetRoot, out var bundledVersionsPath)
            || project.FilePath is null
            || !AnalysisPathIdentity.TryNormalize(analyzerPath, out var canonicalAnalyzerPath))
        {
            return null;
        }

        var rootInput = capturedInputs.SingleOrDefault(input =>
            StringComparer.Ordinal.Equals(input.Kind, "analyzer-image")
            && PathComparer.Equals(input.LogicalKey, canonicalAnalyzerPath));
        if (rootInput is null
            || !imagesByPath.TryGetValue(rootInput.LogicalKey, out var rootImage)
            || !StringComparer.Ordinal.Equals(rootInput.Sha256, rootImage.Sha256)
            || !IsSupportedMicrosoftSdkAssembly(rootImage.Bytes, dotnetRoot, canonicalAnalyzerPath, bundledVersionsPath,
                imagesByPath, attempt, cancellationToken, observer, out var bundledVersionsImage))
        {
            return null;
        }

        var inputs = ImmutableArray.CreateBuilder<GeneratorCreatorInputImage>(capturedInputs.Length);
        foreach (var input in capturedInputs)
        {
            if (!imagesByPath.TryGetValue(input.LogicalKey, out var image)
                || !StringComparer.Ordinal.Equals(input.Sha256, image.Sha256))
            {
                return null;
            }

            inputs.Add(GeneratorCreatorInputImage.FromBytes(image.PhysicalPath!, image.Bytes.AsSpan()));
        }

        inputs.Add(GeneratorCreatorInputImage.FromBytes(
            bundledVersionsImage.PhysicalPath!,
            bundledVersionsImage.Bytes.AsSpan()));

        return GeneratorCreatorInputContract.CreateFromProducedImages(
            project.FilePath,
            analyzerPath,
            inputs.ToImmutable());
    }

    private static bool IsSupportedMicrosoftSdkAssembly(
        ImmutableArray<byte> bytes,
        string dotnetRoot,
        string analyzerPath,
        string bundledVersionsPath,
        IDictionary<string, MetadataReferenceImageCapture.CapturedImage> imagesByPath,
        int attempt,
        CancellationToken cancellationToken,
        MetadataReferenceImageCapture.MetadataImageCaptureObserver? observer,
        out MetadataReferenceImageCapture.CapturedImage bundledVersionsImage)
    {
        bundledVersionsImage = null!;
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        if (!metadata.IsAssembly)
        {
            return false;
        }

        var assembly = metadata.GetAssemblyDefinition();
        var name = metadata.GetString(assembly.Name);
        if (!SupportedAssemblies.TryGetValue(name, out var supportedGenerator))
        {
            return false;
        }

        var publicKey = metadata.GetBlobBytes(assembly.PublicKey);
        if (!assembly.PublicKey.IsNil && publicKey.Length > 0)
        {
            var assemblyName = new AssemblyName { Name = name };
            assemblyName.SetPublicKey(publicKey);
            var token = Convert.ToHexString(assemblyName.GetPublicKeyToken() ?? []);
            if (!StringComparer.Ordinal.Equals(token, supportedGenerator.PublicKeyToken))
            {
                return false;
            }

            var relativePackPath = Path.GetRelativePath(dotnetRoot, analyzerPath);
            var packSegments = relativePackPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (packSegments.Length != 7
                || !PathComparer.Equals(packSegments[0], "packs")
                || !PathComparer.Equals(packSegments[1], supportedGenerator.PackName)
                || !PathComparer.Equals(packSegments[3], "analyzers")
                || !PathComparer.Equals(packSegments[4], "dotnet")
                || !PathComparer.Equals(packSegments[5], "cs")
                || !PathComparer.Equals(packSegments[6], name + ".dll"))
            {
                return false;
            }

            bundledVersionsImage = MetadataReferenceImageCapture.CapturePhysicalImage(
                bundledVersionsPath, imagesByPath, attempt, cancellationToken, observer);
            try
            {
                using var versionsStream = new MemoryStream(bundledVersionsImage.Bytes.ToArray(), writable: false);
                var bundledVersions = XDocument.Load(versionsStream, LoadOptions.None);
                return bundledVersions
                    .Descendants()
                    .Where(element => element.Name.LocalName == "KnownFrameworkReference")
                    .Any(element =>
                        StringComparer.Ordinal.Equals((string?)element.Attribute("TargetingPackName"), supportedGenerator.PackName)
                        && StringComparer.Ordinal.Equals((string?)element.Attribute("TargetingPackVersion"), packSegments[2]));
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryGetRegisteredSdk(
        string? registeredDistributionPath,
        out string dotnetRoot,
        out string bundledVersionsPath)
    {
        dotnetRoot = string.Empty;
        bundledVersionsPath = string.Empty;
        if (string.IsNullOrWhiteSpace(registeredDistributionPath))
        {
            return false;
        }

        var sdkPath = Path.GetFullPath(registeredDistributionPath);
        var sdkDirectory = new DirectoryInfo(sdkPath);
        if (!StringComparer.OrdinalIgnoreCase.Equals(sdkDirectory.Parent?.Name, "sdk")
            || !Version.TryParse(sdkDirectory.Name, out _))
        {
            return false;
        }

        var rootDirectory = sdkDirectory.Parent?.Parent;
        if (rootDirectory is null)
        {
            return false;
        }

        var dotnetHostPath = Path.Combine(rootDirectory.FullName, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        var propsPath = Path.Combine(sdkDirectory.FullName, "Microsoft.NETCoreSdk.BundledVersions.props");
        if (!File.Exists(dotnetHostPath) || !File.Exists(propsPath))
        {
            return false;
        }

        dotnetRoot = rootDirectory.FullName;
        bundledVersionsPath = propsPath;
        return true;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record SupportedGenerator(string PackName, string PublicKeyToken);
}
