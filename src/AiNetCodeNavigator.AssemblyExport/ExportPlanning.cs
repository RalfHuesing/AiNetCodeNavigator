using System.Reflection;
using System.Security.Cryptography;
using System.IO.Enumeration;
using AiNetCodeNavigator.Core.Assemblies;

namespace AiNetCodeNavigator.AssemblyExport;

internal sealed record FilteredExportReference(AssemblyReferenceDto Reference, string Rule);
internal sealed record PlannedAssembly(string SourcePath, string ChildPath, AssemblyIdentityDto Identity,
    bool IsExplicit, AssemblyExportReferenceClosure Closure, IReadOnlyList<FilteredExportReference> FilteredReferences)
{
    // Original closure edges retain their provenance; decompilation gets one proven path per identity.
    internal IReadOnlyList<AssemblyReferenceDto> DecompilationReferences { get; init; } = Closure.References;
    internal string ContentHash { get; init; } = "";
    internal string ChildRelativePath { get; init; } = "";
}
internal sealed record ExportPlan(ExportArguments Arguments, string OutputDirectory,
    IReadOnlyList<string> ExplicitPaths, IReadOnlyList<PlannedAssembly> Assemblies);

internal static class AutomaticExportFilter
{
    private static readonly string[] ExactNames = ["mscorlib", "netstandard", "System", "Microsoft", "WindowsBase",
        "PresentationCore", "PresentationFramework", "Accessibility", "UIAutomationClient", "UIAutomationTypes", "UIAutomationProvider"];
    private static readonly string[] Prefixes = ["System.", "Microsoft.", "Windows."];

    internal static string? Match(string name)
    {
        var exact = ExactNames.FirstOrDefault(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return "simple-name:" + exact;
        var prefix = Prefixes.FirstOrDefault(value => name.StartsWith(value, StringComparison.OrdinalIgnoreCase));
        return prefix is null ? null : "prefix:" + prefix;
    }
}

internal static class ExportPlanner
{
    internal static ExportPlan Create(ExportArguments arguments,
        Func<string, Func<AssemblyReferenceDto, bool>, AssemblyExportReferenceClosure>? resolve = null)
    {
        resolve ??= (path, traverse) => AssemblyExportReferenceResolver.Resolve(path, traverse);
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(arguments.OutputDirectory));
        var expanded = ExpandDetailed(arguments.Sources);
        var explicitPaths = expanded.Paths;
        var expandedCandidates = expanded.RecursivePaths;
        var directExplicitPaths = expanded.DirectPaths;
        var preferredVersions = expandedCandidates.Select(path => (Path: path, Identity: ReadManagedIdentity(path)))
            .GroupBy(item => Path.GetFileName(item.Path), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var highest = group.Max(item => Version.Parse(item.Identity.Version));
                return group.Where(item => Version.Parse(item.Identity.Version) == highest).Select(item => item.Path);
            }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedExplicit = explicitPaths.Where(path => directExplicitPaths.Contains(path)
            || !expandedCandidates.Contains(path, StringComparer.OrdinalIgnoreCase)
            || preferredVersions.Contains(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(explicitPaths.Where(selectedExplicit.Contains));
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIdentities = new Dictionary<string, (string Path, string Hash)>(StringComparer.OrdinalIgnoreCase);
        var assemblies = new List<PlannedAssembly>();
        while (queue.TryDequeue(out var source))
        {
            source = Path.GetFullPath(source);
            if (!seenPaths.Add(source)) continue;
            ExportDumpOwnership.RejectReparseAncestors(source);
            if (ExportDumpOwnership.IsWithin(source, output))
                throw new InvalidOperationException($"Source DLL is inside the output dump: {source}");
            var identity = ReadManagedIdentity(source);
            var identityKey = string.Join("|", identity.Name, identity.Version, identity.Culture, identity.PublicKeyToken);
            using var sourceStream = File.OpenRead(source);
            var hash = Convert.ToHexStringLower(SHA256.HashData(sourceStream));
            var duplicateIdentity = seenIdentities.TryGetValue(identityKey, out var existing);
            if (!duplicateIdentity) seenIdentities[identityKey] = (source, hash);
            var closure = resolve(source, reference => AutomaticExportFilter.Match(reference.Name) is null);
            if (closure.Identity is null || !closure.IsComplete)
                throw new InvalidOperationException($"Incomplete reference closure for {source}: {string.Join("; ", closure.Diagnostics.Select(item => item.Message))}");
            var filtered = closure.References.Select(reference => (reference, rule: AutomaticExportFilter.Match(reference.Name)))
                .Where(item => item.rule is not null).Select(item => new FilteredExportReference(item.reference, item.rule!)).ToArray();
            if (!duplicateIdentity || existing.Hash != hash)
                assemblies.Add(new(source, "", identity, selectedExplicit.Contains(source), closure, filtered) { ContentHash = hash });
            foreach (var reference in closure.References.Where(reference => reference.Resolved && reference.ResolvedPath is not null
                         && AutomaticExportFilter.Match(reference.Name) is null).OrderBy(reference => reference.ResolvedPath, StringComparer.OrdinalIgnoreCase))
                queue.Enqueue(reference.ResolvedPath!);
        }
        var canonicalReferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var aliasRepresentatives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in assemblies.SelectMany(item => item.Closure.References).Where(edge => edge.ResolvedPath is not null)
                     .OrderBy(edge => edge.ResolvedPath, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.GetFullPath(reference.ResolvedPath!);
            ExportDumpOwnership.RejectReparseAncestors(path);
            if (ExportDumpOwnership.IsWithin(path, output))
                throw new InvalidOperationException($"Resolved dependency is inside the output dump: {path}");
            var identity = ReadManagedIdentity(path);
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            var aliasKey = string.Join("|", identity.Name, identity.Version, identity.Culture, identity.PublicKeyToken, hash);
            if (!aliasRepresentatives.TryGetValue(aliasKey, out var representativePath))
            {
                representativePath = assemblies.FirstOrDefault(item => item.Identity == identity && item.ContentHash == hash)?.SourcePath ?? path;
                aliasRepresentatives[aliasKey] = representativePath;
            }
            canonicalReferences[path] = representativePath;
        }
        var deduplicated = new List<PlannedAssembly>();
        var identityHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in assemblies)
        {
            var key = string.Join("|", item.Identity.Name, item.Identity.Version, item.Identity.Culture, item.Identity.PublicKeyToken, item.ContentHash);
            if (identityHashes.Add(key)) deduplicated.Add(item);
        }

        var basenameGroups = deduplicated.GroupBy(item => Path.GetFileName(item.SourcePath), StringComparer.OrdinalIgnoreCase).ToArray();
        var collisions = basenameGroups.Where(group => group.Count() > 1
                || ExportDumpOwnership.HasVariantChildren(output, group.Key))
            .SelectMany(group => group).ToHashSet();
        var normalizedAssemblies = deduplicated.Select(item => item with
        {
            ChildPath = collisions.Contains(item) ? Path.Combine(output, Path.GetFileName(item.SourcePath), VariantKey(item))
                : Path.Combine(output, Path.GetFileName(item.SourcePath)),
            DecompilationReferences = item.Closure.References.Select(edge => edge.ResolvedPath is not null
                ? edge with { ResolvedPath = canonicalReferences[Path.GetFullPath(edge.ResolvedPath)] } : edge).ToArray(),
        }).Select(item => item with { ChildRelativePath = Path.GetRelativePath(output, item.ChildPath) })
            .OrderBy(item => item.ChildPath, StringComparer.OrdinalIgnoreCase).ToArray();
        var plan = new ExportPlan(arguments, output, explicitPaths, normalizedAssemblies);
        new ExportDumpOwnership(plan).ValidatePreflight();
        return plan;
    }

    internal static IReadOnlyList<string> Expand(IReadOnlyList<string> patterns)
        => ExpandDetailed(patterns).Paths;

    private static (IReadOnlyList<string> Paths, IReadOnlySet<string> RecursivePaths, IReadOnlySet<string> DirectPaths) ExpandDetailed(IReadOnlyList<string> patterns)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recursivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < patterns.Count; index++)
        {
            var pattern = patterns[index];
            var full = Path.GetFullPath(pattern);
            var recursiveSelection = false;
            IReadOnlyList<string> matches;
            if (Directory.Exists(full))
            {
                recursiveSelection = true;
                var filenamePatterns = new List<string>();
                while (index + 1 < patterns.Count && IsBareFilenamePattern(patterns[index + 1]))
                {
                    var filenamePattern = patterns[++index];
                    if (filenamePattern.Contains("**", StringComparison.Ordinal))
                        throw new ArgumentException($"Only final filename segment wildcards * and ? are supported: {filenamePattern}");
                    filenamePatterns.Add(filenamePattern);
                }
                matches = filenamePatterns.Count == 0
                    ? FindManagedFiles(full, ["*"])
                    : FindManagedFiles(full, filenamePatterns, requireEveryPattern: true);
            }
            else
            {
                var directory = Path.GetDirectoryName(full)!;
                var filePattern = Path.GetFileName(full);
                if (directory.IndexOfAny(['*', '?']) >= 0 || filePattern.Contains("**", StringComparison.Ordinal))
                    throw new ArgumentException($"Only final filename segment wildcards * and ? are supported: {pattern}");
                if (filePattern.IndexOfAny(['*', '?']) >= 0)
                {
                    recursiveSelection = true;
                    matches = Directory.Exists(directory) ? FindManagedFiles(directory, [filePattern]) : [];
                }
                else if (File.Exists(full))
                {
                    ExportDumpOwnership.RejectReparseAncestors(full);
                    ReadManagedIdentity(full);
                    matches = [full];
                }
                else matches = [];
            }
            if (matches.Count == 0) throw new ArgumentException($"Source path or pattern has no managed DLL or EXE matches: {pattern}");
            foreach (var match in matches)
            {
                paths.Add(match);
                if (recursiveSelection) recursivePaths.Add(Path.GetFullPath(match));
                else directPaths.Add(Path.GetFullPath(match));
            }
        }
        return (paths.Order(StringComparer.OrdinalIgnoreCase).ToArray(), recursivePaths, directPaths);
    }

    private static string VariantKey(PlannedAssembly item)
    {
        var origin = item.SourcePath.Contains(Path.Combine("assembly", "GAC_32"), StringComparison.OrdinalIgnoreCase) ? "gac32"
                : item.SourcePath.Contains(Path.Combine("assembly", "GAC_64"), StringComparison.OrdinalIgnoreCase) ? "gac64"
                : item.SourcePath.Contains("GAC_MSIL", StringComparison.OrdinalIgnoreCase) ? "gacmsil" : "local";
        var hashInput = System.Text.Encoding.UTF8.GetBytes(string.Join("|", item.Identity.Name, item.Identity.Version,
            item.Identity.Culture, item.Identity.PublicKeyToken, item.ContentHash));
        return origin + "-" + Convert.ToHexStringLower(SHA256.HashData(hashInput));
    }

    private static bool IsBareFilenamePattern(string pattern) =>
        pattern.IndexOfAny(['*', '?']) >= 0 && string.IsNullOrEmpty(Path.GetDirectoryName(pattern));

    private static IReadOnlyList<string> FindManagedFiles(string root, IReadOnlyList<string> filePatterns, bool requireEveryPattern = false)
    {
        var matches = new List<string>();
        var matchedPatterns = new bool[filePatterns.Count];
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            ExportDumpOwnership.RejectReparseAncestors(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException($"Reparse-point source directory cannot be searched: {entry}");
                    pending.Push(entry);
                    continue;
                }
                if (!IsSupportedExtension(entry)) continue;
                var name = Path.GetFileName(entry);
                var matchingPatterns = new List<int>();
                for (var patternIndex = 0; patternIndex < filePatterns.Count; patternIndex++)
                    if (FileSystemName.MatchesSimpleExpression(filePatterns[patternIndex], name, ignoreCase: true))
                        matchingPatterns.Add(patternIndex);
                if (matchingPatterns.Count == 0) continue;
                ExportDumpOwnership.RejectReparseAncestors(entry);
                try { ReadManagedIdentity(entry); }
                catch (BadImageFormatException) { continue; }
                matches.Add(Path.GetFullPath(entry));
                foreach (var patternIndex in matchingPatterns) matchedPatterns[patternIndex] = true;
            }
        }
        if (requireEveryPattern)
            for (var index = 0; index < filePatterns.Count; index++)
                if (!matchedPatterns[index])
                    throw new ArgumentException($"Source path or pattern has no managed DLL or EXE matches: {filePatterns[index]}");
        return matches;
    }

    private static bool IsSupportedExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static AssemblyIdentityDto ReadManagedIdentity(string path)
    {
        if (!IsSupportedExtension(path))
            throw new ArgumentException($"Input must be a managed DLL or EXE: {path}");
        var name = AssemblyName.GetAssemblyName(path);
        return new(name.Name!, name.Version!.ToString(), string.IsNullOrEmpty(name.CultureName) ? "neutral" : name.CultureName,
            Convert.ToHexStringLower(name.GetPublicKeyToken() ?? []));
    }
}
