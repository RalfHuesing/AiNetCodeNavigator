#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AiNetCodeNavigator.Core.Assemblies;

public sealed record AssemblyProjectExportResult(bool IsComplete, string? ProjectRelativePath,
    IReadOnlyList<string> SourceRelativePaths, string? ContentHash, string DecompilerVersion,
    IReadOnlyList<AssemblyExportReferenceDiagnostic> Diagnostics);

/// <summary>Stages a validated whole project using only the caller's proven dependency snapshot.</summary>
public static class AssemblyProjectExporter
{
    public static async Task<AssemblyProjectExportResult> ExportAsync(string sourcePath, string stagingDirectory,
        AssemblyIdentityDto expectedIdentity, string expectedContentHash,
        IReadOnlyList<AssemblyReferenceDto> provenReferences, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<AssemblyExportReferenceDiagnostic>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.GetFullPath(sourcePath);
            var stage = Path.GetFullPath(stagingDirectory);
            if (!Directory.Exists(stage) || Directory.EnumerateFileSystemEntries(stage).Any()
                || source.StartsWith(Path.TrimEndingDirectorySeparator(stage) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Export requires an empty staging directory separate from its input.");
            ValidateSnapshot(source, expectedIdentity, expectedContentHash, provenReferences);
            var fingerprint = AssemblyFingerprintCalculator.Create(source);
            var options = AssemblyDecompilationOptions.Default;
            var resolution = new AssemblyReferenceResolution(expectedIdentity, provenReferences, [], [], UseOnlyProvenReferences: true);
            var request = new DecompilationRequest(source, fingerprint,
                AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options), options, cancellationToken, stage);
            var result = await new AssemblyDecompilationAdapter().DecompileAsync(request, resolution).ConfigureAwait(false);
            diagnostics.AddRange(result.Diagnostics.Select(item => new AssemblyExportReferenceDiagnostic(item.Code, item.Message,
                item.Severity == AssemblyDiagnosticSeverity.Error)));
            if (!result.IsComplete || result.ProjectFilePath is null || result.Documents.Count == 0
                || result.Diagnostics.Any(item => item.Code is "assembly-type-decompilation-empty" or "assembly-type-decompilation-failed")
                || result.Documents.Any(item => string.IsNullOrWhiteSpace(item.CSharpSource)
                    || CSharpSyntaxTree.ParseText(item.CSharpSource).GetDiagnostics(cancellationToken)
                        .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)))
                throw new InvalidDataException("Generated project or C# documents did not pass export validation.");
            using (var reader = XmlReader.Create(result.ProjectFilePath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                var project = XDocument.Load(reader);
                if (project.Root?.Name.LocalName != "Project") throw new InvalidDataException("Generated .csproj has no Project root.");
            }
            ValidateSnapshot(source, expectedIdentity, expectedContentHash, provenReferences);
            return new(true, Path.GetRelativePath(stage, result.ProjectFilePath),
                result.Documents.Select(item => Path.GetRelativePath(stage, item.GeneratedPath)).ToArray(), fingerprint.Sha256,
                AssemblyDecompilationOptions.CurrentDecompilerVersion, diagnostics);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or BadImageFormatException
                                  or InvalidOperationException or ArgumentException or XmlException)
        {
            diagnostics.Add(new("assembly-export-validation-failed", ex.Message, true));
            return new(false, null, [], null, AssemblyDecompilationOptions.CurrentDecompilerVersion, diagnostics);
        }
    }

    private static void ValidateSnapshot(string source, AssemblyIdentityDto expectedIdentity, string expectedHash,
        IReadOnlyList<AssemblyReferenceDto> references)
    {
        using (var stream = File.OpenRead(source))
        using (var pe = new System.Reflection.PortableExecutable.PEReader(stream))
        {
            if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null) throw new BadImageFormatException("Source is not a managed PE image.");
            var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            if (!reader.IsAssembly) throw new BadImageFormatException("Source has no assembly definition.");
            var actual = AssemblyReferenceResolver.ReadIdentity(reader);
            var expected = new AssemblyReferenceDto(expectedIdentity.Name, expectedIdentity.Version, expectedIdentity.Culture,
                true, PublicKeyToken: expectedIdentity.PublicKeyToken);
            if (!AssemblyGacCandidateSource.ExactIdentityMatches(expected, actual))
                throw new InvalidDataException("Source identity changed after export preflight.");
        }
        if (!AssemblyFingerprintCalculator.Create(source).Sha256.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source content changed after export preflight.");
        foreach (var reference in references.Where(item => item.Resolved && item.ResolvedPath is not null))
            if (reference.ContentHash is null || !AssemblyFingerprintCalculator.Create(reference.ResolvedPath!).Sha256
                    .Equals(reference.ContentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Dependency content changed after export preflight: {reference.ResolvedPath}");
    }
}
