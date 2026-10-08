#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

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
            var export = ValidateGeneratedOutput(stage, result, fingerprint.Sha256, cancellationToken);
            ValidateSnapshot(source, expectedIdentity, expectedContentHash, provenReferences);
            return export;
        }
        catch (Exception ex) when (IsRecoverableFailure(ex))
        {
            diagnostics.Add(new("assembly-export-validation-failed", ex.Message, true));
            return new(false, null, [], null, AssemblyDecompilationOptions.CurrentDecompilerVersion, diagnostics);
        }
    }

    internal static AssemblyProjectExportResult ValidateGeneratedOutput(string stagingDirectory,
        DecompilationResult result, string contentHash, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (result.ProjectFilePath is null || result.Documents.Count == 0)
            throw new InvalidDataException("Decompilation produced no usable project or C# source files.");

        var stage = Path.GetFullPath(stagingDirectory);
        var projectPath = Path.GetRelativePath(stage, result.ProjectFilePath);
        var sourcePaths = result.Documents.Select(item => Path.GetRelativePath(stage, item.GeneratedPath)).ToArray();
        ValidateArtifacts(stage, projectPath, sourcePaths);
        var repairedProject = !IsValidProjectDocument(result.ProjectFilePath);
        if (repairedProject) WriteMinimalProject(result.ProjectFilePath, sourcePaths);

        var diagnostics = result.Diagnostics.Select(item => new AssemblyExportReferenceDiagnostic(
            item.Code, item.Message, item.Severity == AssemblyDiagnosticSeverity.Error)).ToList();
        if (repairedProject)
            diagnostics.Add(new("assembly-export-project-repaired",
                "The generated project file was empty or invalid XML and was replaced with a minimal project; decompiled C# files were retained.",
                false));
        var isComplete = result.IsComplete && diagnostics.Count == 0;
        return new(isComplete, projectPath, sourcePaths, contentHash,
            AssemblyDecompilationOptions.CurrentDecompilerVersion, diagnostics);
    }

    private static bool IsValidProjectDocument(string projectFilePath)
    {
        try
        {
            using var reader = XmlReader.Create(projectFilePath,
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return XDocument.Load(reader).Root?.Name.LocalName == "Project";
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static void WriteMinimalProject(string projectFilePath, IReadOnlyList<string> sourcePaths)
    {
        var project = new XDocument(new XElement("Project",
            new XElement("ItemGroup", sourcePaths.Select(path => new XElement("Compile",
                new XAttribute("Include", path.Replace('\\', '/')))))));
        using (var writer = new StreamWriter(projectFilePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            project.Save(writer);
        if (!IsValidProjectDocument(projectFilePath))
            throw new InvalidDataException("Repaired project file is not valid XML.");
    }

    /// <summary>Classifies per-assembly failures without hiding cancellation or fatal process errors.</summary>
    public static bool IsRecoverableFailure(Exception exception) => AssemblyDecompilationAdapter.IsRecoverableFailure(exception);

    /// <summary>Checks generated relative paths before returning or publishing a staged export.</summary>
    public static void ValidateArtifacts(string stagingDirectory, string projectRelativePath, IReadOnlyList<string> sourceRelativePaths)
    {
        var stage = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingDirectory));
        if (sourceRelativePaths.Count == 0) throw new InvalidDataException("No generated C# source files were reported.");
        ValidateFile(projectRelativePath, ".csproj");
        foreach (var source in sourceRelativePaths) ValidateFile(source, ".cs");

        void ValidateFile(string relativePath, string extension)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new InvalidDataException("Generated artifact must have a relative staging path.");
            var path = Path.GetFullPath(Path.Combine(stage, relativePath));
            if (!path.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Generated artifact escaped staging or has an invalid extension: {relativePath}");
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidDataException($"Generated artifact is not a regular file: {relativePath}");
            for (string? ancestor = Path.GetDirectoryName(path); ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
                if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Generated artifact has a reparse-point ancestor: {relativePath}");
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
