#nullable enable

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using AiNetCodeNavigator.Core.Workspace;

namespace AiNetCodeNavigator.TestKit.Builders;

/// <summary>Creates references whose immutable image provenance can be carried into resident test snapshots.</summary>
public static class CapturedMetadataReference
{
    /// <summary>Creates a reference from images retained by the creating test fixture.</summary>
    public static PortableExecutableReference CreateFromImages(
        ImmutableArray<ImmutableArray<byte>> images,
        MetadataReferenceProperties? properties = null,
        DocumentationProvider? documentationProvider = null,
        string? filePath = null,
        string? display = null,
        ImmutableArray<byte> documentationXmlBytes = default) =>
        MetadataReferenceImageCapture.CreateOwnedReferenceFromImages(
            images,
            properties ?? MetadataReferenceProperties.Assembly,
            documentationProvider,
            filePath,
            display,
            documentationXmlBytes);

    /// <summary>Creates a single-image assembly reference from immutable bytes retained by the creating fixture.</summary>
    public static PortableExecutableReference CreateFromImage(
        ImmutableArray<byte> image,
        MetadataReferenceProperties? properties = null,
        DocumentationProvider? documentationProvider = null,
        string? filePath = null,
        string? display = null,
        ImmutableArray<byte> documentationXmlBytes = default) =>
        CreateFromImages([image], properties, documentationProvider, filePath, display, documentationXmlBytes);
}
