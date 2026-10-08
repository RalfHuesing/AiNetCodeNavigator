#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace AiNetCodeNavigator.Core.Assemblies;

/// <summary>Reads named, identity-compatible assemblies from installed legacy framework directories.</summary>
internal sealed class AssemblyFrameworkCandidateSource
{
    private readonly IReadOnlyList<string> directories;

    internal AssemblyFrameworkCandidateSource(IReadOnlyList<string>? directories = null)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        this.directories = directories ?? (string.IsNullOrEmpty(windows) ? [] :
        [
            Path.Combine(windows, "Microsoft.NET", "Framework", "v4.0.30319"),
            Path.Combine(windows, "Microsoft.NET", "Framework64", "v4.0.30319"),
            Path.Combine(windows, "Microsoft.NET", "Framework", "v2.0.50727"),
            Path.Combine(windows, "Microsoft.NET", "Framework64", "v2.0.50727"),
        ]);
    }

    internal string? Find(AssemblyReferenceDto reference, string referringPath,
        ICollection<AssemblySessionDiagnostic> diagnostics)
    {
        if (reference.Name.IndexOfAny(['/', '\\', '*', '?']) >= 0
            || reference.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        var referringArchitecture = AssemblyGacCandidateSource.ReadArchitecture(referringPath);
        foreach (var directory in directories)
        {
            var path = directory;
            try
            {
                path = Path.GetFullPath(Path.Combine(directory, reference.Name + ".dll"));
                if (!File.Exists(path)) continue;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata) continue;
                var reader = pe.GetMetadataReader();
                if (!reader.IsAssembly || !AssemblyGacCandidateSource.ExactIdentityMatches(reference,
                        AssemblyReferenceResolver.ReadIdentity(reader))) continue;
                var architecture = AssemblyGacCandidateSource.GetArchitecture(pe);
                if (referringArchitecture is not null && architecture is not null && architecture != referringArchitecture) continue;
                return path;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or BadImageFormatException or ArgumentException or InvalidOperationException)
            {
                diagnostics.Add(new("assembly-framework-candidate-failed",
                    $"Framework candidate could not be checked: {path}: {exception.Message}", AssemblyDiagnosticSeverity.Warning));
            }
        }
        return null;
    }
}
