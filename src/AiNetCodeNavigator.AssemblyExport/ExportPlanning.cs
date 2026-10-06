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
internal sealed record ExportPlanIssue(string Input, string? SourcePath, string ChildRelativePath, string Error, bool BlocksAssembly = true);
internal sealed record ExportPlan(ExportArguments Arguments, string OutputDirectory,
    IReadOnlyList<string> ExplicitPaths, IReadOnlyList<PlannedAssembly> Assemblies,
    IReadOnlyList<ExportPlanIssue> Issues);

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
        var issues = expanded.Issues.ToList();
        var validIdentities = new Dictionary<string, AssemblyIdentityDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in explicitPaths)
        {
            try { validIdentities[path] = ReadManagedIdentity(path); }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
                                             or BadImageFormatException or InvalidOperationException)
            {
                issues.Add(new(path, path, Path.GetFileName(path), exception.Message));
            }
        }
        var validPaths = explicitPaths.Where(validIdentities.ContainsKey).ToArray();
        var expandedCandidates = expanded.RecursivePaths.Where(validIdentities.ContainsKey).ToArray();
        var directExplicitPaths = expanded.DirectPaths;
        var preferredVersions = expandedCandidates.Select(path => (Path: path, Identity: validIdentities[path]))
            .GroupBy(item => Path.GetFileName(item.Path), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var highest = group.Max(item => Version.Parse(item.Identity.Version));
                return group.Where(item => Version.Parse(item.Identity.Version) == highest).Select(item => item.Path);
            }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedExplicit = validPaths.Where(path => directExplicitPaths.Contains(path)
            || !expandedCandidates.Contains(path, StringComparer.OrdinalIgnoreCase)
            || preferredVersions.Contains(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(validPaths.Where(selectedExplicit.Contains));
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIdentities = new Dictionary<string, (string Path, string Hash)>(StringComparer.OrdinalIgnoreCase);
        var assemblies = new List<PlannedAssembly>();
        while (queue.TryDequeue(out var source))
        {
            try { source = Path.GetFullPath(source); }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                issues.Add(new(source, source, Path.GetFileName(source), exception.Message));
                continue;
            }
            if (!seenPaths.Add(source)) continue;
            AssemblyIdentityDto identity;
            string hash;
            try
            {
                ExportDumpOwnership.RejectReparseAncestors(source);
                if (ExportDumpOwnership.IsWithin(source, output))
                    throw new InvalidOperationException($"Source DLL is inside the output dump: {source}");
                identity = ReadManagedIdentity(source);
                using var sourceStream = File.OpenRead(source);
                hash = Convert.ToHexStringLower(SHA256.HashData(sourceStream));
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
                                             or BadImageFormatException or InvalidOperationException)
            {
                issues.Add(new(source, source, Path.GetFileName(source), exception.Message));
                continue;
            }
            var identityKey = string.Join("|", identity.Name, identity.Version, identity.Culture, identity.PublicKeyToken);
            var duplicateIdentity = seenIdentities.TryGetValue(identityKey, out var existing);
            if (!duplicateIdentity) seenIdentities[identityKey] = (source, hash);
            AssemblyExportReferenceClosure closure;
            try
            {
                closure = resolve(source, reference => AutomaticExportFilter.Match(reference.Name) is null);
                if (closure.Identity is null)
                    throw new InvalidOperationException($"Reference closure could not establish the root identity: {string.Join("; ", closure.Diagnostics.Select(item => item.Message))}");
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
                                             or BadImageFormatException or InvalidOperationException)
            {
                issues.Add(new(source, source, Path.GetFileName(source), exception.Message));
                continue;
            }
            if (!closure.IsComplete)
                issues.Add(new(source, source, Path.GetFileName(source),
                    $"Incomplete reference closure: {string.Join("; ", closure.Diagnostics.Select(item => item.Message))}", BlocksAssembly: false));
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
        var invalidAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in assemblies)
        {
            foreach (var reference in item.Closure.References.Where(edge => edge.ResolvedPath is not null)
                         .OrderBy(edge => edge.ResolvedPath, StringComparer.OrdinalIgnoreCase))
            {
                try
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
                        representativePath = assemblies.FirstOrDefault(candidate => candidate.Identity == identity && candidate.ContentHash == hash)?.SourcePath ?? path;
                        aliasRepresentatives[aliasKey] = representativePath;
                    }
                    canonicalReferences[path] = representativePath;
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
                                                 or BadImageFormatException or InvalidOperationException)
                {
                    invalidAssemblies.Add(item.SourcePath);
                    issues.Add(new(item.SourcePath, item.SourcePath, Path.GetFileName(item.SourcePath),
                        $"Resolved dependency could not be checked ({reference.Name}): {exception.Message}"));
                }
            }
        }
        var deduplicated = new List<PlannedAssembly>();
        var identityHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in assemblies.Where(item => !invalidAssemblies.Contains(item.SourcePath)))
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
        var plan = new ExportPlan(arguments, output, explicitPaths, normalizedAssemblies, issues);
        var owner = new ExportDumpOwnership(plan);
        owner.ValidateRootPreflight();
        foreach (var assembly in plan.Assemblies)
        {
            try { owner.ValidateSelectedChild(assembly.ChildPath); }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                issues.Add(new(assembly.SourcePath, assembly.SourcePath, assembly.ChildRelativePath, exception.Message));
            }
        }
        foreach (var group in plan.Assemblies.GroupBy(item => Path.GetFileName(item.SourcePath), StringComparer.OrdinalIgnoreCase))
        {
            if (!group.Any(item => item.ChildRelativePath.Contains(Path.DirectorySeparatorChar))) continue;
            try { owner.ValidateVariantLayout(group.ToArray()); }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                foreach (var assembly in group)
                    issues.Add(new(assembly.SourcePath, assembly.SourcePath, assembly.ChildRelativePath, exception.Message));
            }
        }
        return plan;
    }

    internal static IReadOnlyList<string> Expand(IReadOnlyList<string> patterns)
    {
        var expanded = ExpandDetailed(patterns, continueOnErrors: false);
        if (expanded.Issues.Count > 0) throw new ArgumentException(expanded.Issues[0].Error);
        return expanded.Paths;
    }

    private static (IReadOnlyList<string> Paths, IReadOnlySet<string> RecursivePaths, IReadOnlySet<string> DirectPaths, IReadOnlyList<ExportPlanIssue> Issues) ExpandDetailed(IReadOnlyList<string> patterns, bool continueOnErrors = true)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recursivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<ExportPlanIssue>();
        for (var index = 0; index < patterns.Count; index++)
        {
            var pattern = patterns[index];
            var relatedPatterns = new List<string>();
            if (IsExistingDirectory(pattern))
            {
                while (index + 1 < patterns.Count && IsBareFilenamePattern(patterns[index + 1]))
                    relatedPatterns.Add(patterns[++index]);
            }
            var sourcesForInput = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
            var full = Path.GetFullPath(pattern);
            var recursiveSelection = false;
            IReadOnlyList<string> matches;
            if (Directory.Exists(full))
            {
                recursiveSelection = true;
                var filenamePatterns = new List<string>();
                foreach (var filenamePattern in relatedPatterns)
                {
                    if (filenamePattern.Contains("**", StringComparison.Ordinal))
                        throw new ArgumentException($"Only final filename segment wildcards * and ? are supported: {filenamePattern}");
                    filenamePatterns.Add(filenamePattern);
                }
                Action<string, string>? reportSearchIssue = continueOnErrors
                    ? (path, message) => issues.Add(new(path, null, path, message))
                    : null;
                matches = filenamePatterns.Count == 0
                    ? FindManagedFiles(full, ["*"], reportSearchIssue)
                    : FindManagedFiles(full, filenamePatterns, reportSearchIssue);
                foreach (var filenamePattern in filenamePatterns)
                    if (!matches.Any(match => FileSystemName.MatchesSimpleExpression(filenamePattern, Path.GetFileName(match), ignoreCase: true)))
                        issues.Add(new(filenamePattern, null, filenamePattern, $"Source pattern has no managed DLL or EXE matches: {filenamePattern}"));
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
                    Action<string, string>? reportSearchIssue = continueOnErrors
                        ? (path, message) => issues.Add(new(path, null, path, message))
                        : null;
                    matches = Directory.Exists(directory)
                        ? FindManagedFiles(directory, [filePattern], reportSearchIssue)
                        : [];
                }
                else if (File.Exists(full))
                {
                    ExportDumpOwnership.RejectReparseAncestors(full);
                    ReadManagedIdentity(full);
                    matches = [full];
                }
                else matches = [];
            }
            if (matches.Count == 0 && relatedPatterns.Count == 0)
                throw new ArgumentException($"Source path or pattern has no managed DLL or EXE matches: {pattern}");
            foreach (var match in matches)
            {
                sourcesForInput.Add(match);
                paths.Add(match);
                if (recursiveSelection) recursivePaths.Add(Path.GetFullPath(match));
                else directPaths.Add(Path.GetFullPath(match));
            }
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
                                             or BadImageFormatException or InvalidOperationException)
            {
                if (!continueOnErrors) throw;
                issues.Add(new(pattern, null, pattern, exception.Message));
            }
            foreach (var relatedPattern in relatedPatterns)
            {
                if (issues.Any(issue => issue.Input.Equals(relatedPattern, StringComparison.OrdinalIgnoreCase))) continue;
                if (!sourcesForInput.Any(path => FileSystemName.MatchesSimpleExpression(relatedPattern, Path.GetFileName(path), ignoreCase: true)))
                    issues.Add(new(relatedPattern, null, relatedPattern, $"Source pattern has no managed DLL or EXE matches: {relatedPattern}"));
            }
        }
        return (paths.Order(StringComparer.OrdinalIgnoreCase).ToArray(), recursivePaths, directPaths, issues);
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

    private static bool IsExistingDirectory(string path)
    {
        try { return Directory.Exists(Path.GetFullPath(path)); }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return false; }
    }

    private static IReadOnlyList<string> FindManagedFiles(string root, IReadOnlyList<string> filePatterns,
        Action<string, string>? reportIssue = null)
    {
        var matches = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            try { ExportDumpOwnership.RejectReparseAncestors(directory); }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                if (reportIssue is null) throw;
                reportIssue?.Invoke(directory, exception.Message);
                continue;
            }
            string[] entries;
            try { entries = Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase).ToArray(); }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                if (reportIssue is null) throw;
                reportIssue?.Invoke(directory, exception.Message);
                continue;
            }
            foreach (var entry in entries)
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    reportIssue?.Invoke(entry, exception.Message);
                    continue;
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        if (reportIssue is null)
                            throw new InvalidOperationException($"Reparse-point source directory cannot be searched: {entry}");
                        reportIssue?.Invoke(entry, $"Reparse-point source directory cannot be searched: {entry}");
                        continue;
                    }
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
                try
                {
                    ExportDumpOwnership.RejectReparseAncestors(entry);
                    ReadManagedIdentity(entry);
                }
                catch (BadImageFormatException) { continue; }
                catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    if (reportIssue is null) throw;
                    reportIssue?.Invoke(entry, exception.Message);
                    continue;
                }
                matches.Add(Path.GetFullPath(entry));
            }
        }
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
