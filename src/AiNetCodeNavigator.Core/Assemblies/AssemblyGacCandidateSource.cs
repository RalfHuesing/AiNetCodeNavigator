#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace AiNetCodeNavigator.Core.Assemblies;

/// <summary>Reads only the named assembly's modern and legacy GAC buckets; never loads code.</summary>
internal sealed class AssemblyGacCandidateSource
{
    private readonly IReadOnlyList<string> roots;

    internal AssemblyGacCandidateSource(IReadOnlyList<string>? roots = null)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        this.roots = roots ?? (string.IsNullOrEmpty(windows) ? [] :
            [Path.Combine(windows, "Microsoft.NET", "assembly"), Path.Combine(windows, "assembly")]);
    }

    internal IReadOnlyList<string> Find(AssemblyReferenceDto reference, string referringPath,
        ICollection<AssemblySessionDiagnostic> diagnostics)
    {
        if (reference.Name.IndexOfAny(['/', '\\', '*', '?']) >= 0) return [];
        var matches = new List<(string Path, Machine? Architecture)>();
        var referringArchitecture = ReadArchitecture(referringPath);
        foreach (var root in roots)
        foreach (var bucket in new[] { "GAC_MSIL", "GAC_32", "GAC_64", "GAC" })
        {
            var nameDirectory = Path.Combine(root, bucket, reference.Name);
            if (!Directory.Exists(nameDirectory)) continue;
            try
            {
                foreach (var versionDirectory in Directory.EnumerateDirectories(nameDirectory))
                {
                    var path = Path.GetFullPath(Path.Combine(versionDirectory, reference.Name + ".dll"));
                    if (!File.Exists(path)) continue;
                    try
                    {
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var pe = new PEReader(stream);
                        if (!pe.HasMetadata) continue;
                        var reader = pe.GetMetadataReader();
                        if (!reader.IsAssembly) continue;
                        var actual = AssemblyReferenceResolver.ReadIdentity(reader);
                        if (!ExactIdentityMatches(reference, actual)) continue;
                        var architecture = GetArchitecture(pe);
                        if (architecture is not null && architecture != referringArchitecture) continue;
                        matches.Add((path, architecture));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException or InvalidOperationException)
                    {
                        diagnostics.Add(new("assembly-gac-candidate-failed", $"GAC candidate could not be checked: {path}: {ex.Message}", AssemblyDiagnosticSeverity.Warning));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException or InvalidOperationException)
            {
                diagnostics.Add(new("assembly-gac-candidate-failed", $"GAC candidates could not be checked: {nameDirectory}: {ex.Message}", AssemblyDiagnosticSeverity.Warning));
            }
        }

        var preferred = matches.Any(match => match.Architecture is null)
            ? matches.Where(match => match.Architecture is null) : matches;
        return preferred.Select(match => match.Path).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static bool ExactIdentityMatches(AssemblyReferenceDto expected, AssemblyIdentityDto actual) =>
        AssemblyReferenceResolver.IdentityMatches(expected, actual)
        && string.Equals(expected.Version, actual.Version, StringComparison.Ordinal);

    private static Machine? ReadArchitecture(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var pe = new PEReader(stream);
        return GetArchitecture(pe);
    }

    private static Machine? GetArchitecture(PEReader pe) =>
        pe.PEHeaders.CoffHeader.Machine == Machine.I386
        && pe.PEHeaders.CorHeader is { } cor
        && (cor.Flags & CorFlags.ILOnly) != 0
        && (cor.Flags & CorFlags.Requires32Bit) == 0
            ? null : pe.PEHeaders.CoffHeader.Machine;
}
