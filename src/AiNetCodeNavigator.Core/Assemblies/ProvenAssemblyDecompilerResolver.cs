#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using ICSharpCode.Decompiler.Metadata;

namespace AiNetCodeNavigator.Core.Assemblies;

/// <summary>Uses the metadata resolver's proven file before ILSpy search-directory binding.</summary>
internal sealed class ProvenAssemblyDecompilerResolver(
    IAssemblyResolver fallback, IReadOnlyList<AssemblyReferenceDto> references, bool useOnlyProvenReferences = false) : IAssemblyResolver, IDisposable
{
    private readonly Dictionary<string, PEFile> files = new(StringComparer.OrdinalIgnoreCase);

    public MetadataFile? Resolve(IAssemblyReference reference) => ResolveCore(reference);

    private MetadataFile? ResolveCore(IAssemblyReference reference)
    {
        var paths = references.Where(candidate => candidate.Resolved && candidate.ResolvedPath is not null
            && string.Equals(candidate.Name, reference.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Version, reference.Version?.ToString(), StringComparison.Ordinal)
            && string.Equals(candidate.Culture == "neutral" ? "" : candidate.Culture, reference.Culture ?? "", StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.PublicKeyToken, Convert.ToHexString(reference.PublicKeyToken ?? []), StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.ResolvedPath!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0) return useOnlyProvenReferences ? null : fallback.Resolve(reference);
        if (paths.Count != 1) return null;
        lock (files)
        {
            if (!files.TryGetValue(paths[0], out var file))
            {
                file = new PEFile(paths[0], PEStreamOptions.PrefetchEntireImage);
                files.Add(paths[0], file);
            }
            return file;
        }
    }

    public MetadataFile? ResolveModule(MetadataFile mainModule, string moduleName) => fallback.ResolveModule(mainModule, moduleName);
    public Task<MetadataFile?> ResolveAsync(IAssemblyReference reference) => Task.FromResult(ResolveCore(reference));
    public Task<MetadataFile?> ResolveModuleAsync(MetadataFile mainModule, string moduleName) => fallback.ResolveModuleAsync(mainModule, moduleName);

    public void Dispose()
    {
        foreach (var file in files.Values) file.Dispose();
        files.Clear();
    }
}
