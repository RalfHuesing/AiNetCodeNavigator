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
    private const int MaxReferenceDepth = 128;
    private const int MaxReferenceNodes = 4096;
    private const int MaxAssemblyChildrenPerDirectory = 64;

    internal static ExportPlan Create(ExportArguments arguments,
        Func<string, Func<AssemblyReferenceDto, bool>, AssemblyExportReferenceClosure>? resolve = null)
    {
        resolve ??= (path, traverse) => AssemblyExportReferenceResolver.Resolve(path, traverse);
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(arguments.OutputDirectory));
        ValidateSourceOutputOverlap(arguments.Sources, output);
        var fileInfo = new Dictionary<string, AssemblyFileInfo>(StringComparer.OrdinalIgnoreCase);
        var expanded = ExpandDetailed(arguments.Sources, fileInfo);
        var explicitPaths = expanded.Paths;
        var issues = expanded.Issues.ToList();
        foreach (var path in explicitPaths)
        {
            try { _ = GetFileInfo(path, fileInfo); }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
                                             or BadImageFormatException or InvalidOperationException)
            {
                issues.Add(new(path, path, Path.GetFileName(path), exception.Message));
            }
        }
        var validPaths = explicitPaths.Where(fileInfo.ContainsKey).ToArray();
        var expandedCandidates = expanded.RecursivePaths.Where(fileInfo.ContainsKey).ToArray();
        var directExplicitPaths = expanded.DirectPaths;
        var preferredVersions = expandedCandidates.Select(path => (Path: path, Identity: fileInfo[path].Identity))
            .GroupBy(item => Path.GetFileName(item.Path), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var highest = group.Max(item => Version.Parse(item.Identity.Version));
                return group.Where(item => Version.Parse(item.Identity.Version) == highest).Select(item => item.Path);
            }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedExplicit = validPaths.Where(path => directExplicitPaths.Contains(path)
            || !expandedCandidates.Contains(path, StringComparer.OrdinalIgnoreCase)
            || preferredVersions.Contains(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, int Depth)>(validPaths.Where(selectedExplicit.Contains).Select(path => (path, 0)));
        var nodeDepths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nodes = new Dictionary<string, PlannedNode>(StringComparer.OrdinalIgnoreCase);
        var blockedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (queue.TryDequeue(out var queued))
        {
            string source;
            try { source = Path.GetFullPath(queued.Path); }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                issues.Add(new(queued.Path, queued.Path, Path.GetFileName(queued.Path), exception.Message));
                continue;
            }
            if (nodeDepths.TryGetValue(source, out var priorDepth) && priorDepth <= queued.Depth) continue;
            if (queued.Depth > MaxReferenceDepth)
            {
                issues.Add(new(source, source, Path.GetFileName(source), $"Reference closure reached the maximum depth of {MaxReferenceDepth}.", BlocksAssembly: false));
                blockedPaths.Add(source);
                continue;
            }
            if (!nodes.ContainsKey(source) && nodes.Count >= MaxReferenceNodes)
            {
                issues.Add(new(source, source, Path.GetFileName(source), $"Reference closure reached the maximum of {MaxReferenceNodes} assemblies.", BlocksAssembly: false));
                blockedPaths.Add(source);
                continue;
            }
            nodeDepths[source] = queued.Depth;
            try
            {
                ExportDumpOwnership.RejectReparseAncestors(source);
                if (ExportDumpOwnership.IsWithin(source, output))
                    throw new InvalidOperationException($"Source DLL is inside the output dump: {source}");
                var info = GetFileInfo(source, fileInfo, includeHash: true);
                var closure = resolve(source, _ => false);
                if (closure.Identity is null)
                    throw new InvalidOperationException($"Reference closure could not establish the root identity: {string.Join("; ", closure.Diagnostics.Select(item => item.Message))}");
                nodes[source] = new(source, info.Identity, info.ContentHash!, closure);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
                                             or BadImageFormatException or InvalidOperationException)
            {
                issues.Add(new(source, source, Path.GetFileName(source), exception.Message));
                continue;
            }
            foreach (var reference in nodes[source].DirectClosure.References.Where(reference => reference.Resolved && reference.ResolvedPath is not null
                         && AutomaticExportFilter.Match(reference.Name) is null).OrderBy(reference => reference.ResolvedPath, StringComparer.OrdinalIgnoreCase))
                queue.Enqueue((reference.ResolvedPath!, queued.Depth + 1));
        }

        foreach (var source in blockedPaths)
            foreach (var parent in nodes.Values)
                foreach (var edge in parent.DirectClosure.References.Where(edge => edge.ResolvedPath is not null))
                    if (Path.GetFullPath(edge.ResolvedPath!).Equals(source, StringComparison.OrdinalIgnoreCase)) parent.LimitEdges.Add(source);

        var assemblies = new List<PlannedAssembly>();
        foreach (var node in nodes.Values.OrderBy(node => node.SourcePath, StringComparer.OrdinalIgnoreCase))
        {
            var references = node.DirectClosure.References.Select(edge =>
                edge.ResolvedPath is not null && node.LimitEdges.Contains(Path.GetFullPath(edge.ResolvedPath))
                    ? edge with { Resolved = false, ResolvedPath = null, ResolutionState = "boundary_limit",
                        Diagnostic = $"Reference closure reached the configured depth or node limit at '{edge.ResolvedPath}'." }
                    : edge).ToArray();
            var closureDiagnostics = node.DirectClosure.Diagnostics.ToList();
            if (node.LimitEdges.Count > 0)
                closureDiagnostics.Add(new("assembly-reference-boundary", $"Reference closure reached the configured depth or node limit from '{node.SourcePath}'.", true));
            var closure = new AssemblyExportReferenceClosure(node.Identity, references, closureDiagnostics,
                node.DirectClosure.IsComplete && node.LimitEdges.Count == 0);
            if (!closure.IsComplete)
                issues.Add(new(node.SourcePath, node.SourcePath, Path.Combine(GetOwnerDirectory(node.SourcePath), Path.GetFileName(node.SourcePath)),
                    $"Incomplete reference closure: {string.Join("; ", closure.Diagnostics.Select(item => item.Message))}", BlocksAssembly: false));
            var filtered = references.Select(reference => (reference, rule: AutomaticExportFilter.Match(reference.Name)))
                .Where(item => item.rule is not null).Select(item => new FilteredExportReference(item.reference, item.rule!)).ToArray();
            assemblies.Add(new(node.SourcePath, "", node.Identity, selectedExplicit.Contains(node.SourcePath), closure, filtered)
                { ContentHash = node.ContentHash });
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
                    var info = GetFileInfo(path, fileInfo, includeHash: true);
                    var identity = info.Identity;
                    var hash = info.ContentHash!;
                    var aliasKey = IdentityHashKey(identity, hash);
                    if (!aliasRepresentatives.TryGetValue(aliasKey, out var representativePath))
                    {
                        representativePath = assemblies.FirstOrDefault(candidate => IdentityHashKey(candidate.Identity, candidate.ContentHash) == aliasKey)?.SourcePath ?? path;
                        aliasRepresentatives[aliasKey] = representativePath;
                    }
                    canonicalReferences[path] = representativePath;
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
                                                 or BadImageFormatException or InvalidOperationException)
                {
                    invalidAssemblies.Add(item.SourcePath);
                    issues.Add(new(item.SourcePath, item.SourcePath, Path.Combine(GetOwnerDirectory(item.SourcePath), Path.GetFileName(item.SourcePath)),
                        $"Resolved dependency could not be checked ({reference.Name}): {exception.Message}"));
                }
            }
        }
        var deduplicated = new List<PlannedAssembly>();
        var identityHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in assemblies.Where(item => !invalidAssemblies.Contains(item.SourcePath)))
        {
            var key = IdentityHashKey(item.Identity, item.ContentHash);
            if (identityHashes.Add(key)) deduplicated.Add(item);
        }

        var collisions = deduplicated.GroupBy(item => Path.GetFileName(item.SourcePath), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).SelectMany(group => group).ToHashSet();
        var relativePaths = PlanChildRelativePaths(deduplicated, collisions);
        var normalizedAssemblies = deduplicated.Select(item => item with
        {
            ChildPath = Path.Combine(output, relativePaths[item.SourcePath]),
            DecompilationReferences = item.Closure.References.Select(edge => edge.ResolvedPath is not null
                && canonicalReferences.TryGetValue(Path.GetFullPath(edge.ResolvedPath), out var canonicalPath)
                ? edge with { ResolvedPath = canonicalPath } : edge).ToArray(),
        }).Select(item => item with { ChildRelativePath = relativePaths[item.SourcePath] })
            .OrderBy(item => item.ChildPath, StringComparer.OrdinalIgnoreCase).ToArray();
        var finalPathsBySource = normalizedAssemblies.ToDictionary(item => item.SourcePath, item => item.ChildRelativePath,
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < issues.Count; index++)
            if (issues[index].SourcePath is { } source && finalPathsBySource.TryGetValue(source, out var relativePath))
                issues[index] = issues[index] with { ChildRelativePath = relativePath };
        if (normalizedAssemblies.Select(item => item.ChildPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedAssemblies.Length)
            throw new InvalidOperationException("Assembly export planning produced colliding child paths.");
        var plan = new ExportPlan(arguments, output, explicitPaths, normalizedAssemblies, issues);
        var owner = new ExportDumpOwnership(plan);
        owner.ValidateRootPreflight();
        return plan;
    }

    internal static IReadOnlyList<string> Expand(IReadOnlyList<string> patterns)
    {
        var expanded = ExpandDetailed(patterns, new Dictionary<string, AssemblyFileInfo>(StringComparer.OrdinalIgnoreCase), continueOnErrors: false);
        if (expanded.Issues.Count > 0) throw new ArgumentException(expanded.Issues[0].Error);
        return expanded.Paths;
    }

    private static (IReadOnlyList<string> Paths, IReadOnlySet<string> RecursivePaths, IReadOnlySet<string> DirectPaths, IReadOnlyList<ExportPlanIssue> Issues) ExpandDetailed(
        IReadOnlyList<string> patterns, IDictionary<string, AssemblyFileInfo> fileInfo, bool continueOnErrors = true)
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
                    ? FindManagedFiles(full, ["*"], fileInfo, reportSearchIssue)
                    : FindManagedFiles(full, filenamePatterns, fileInfo, reportSearchIssue);
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
                        ? FindManagedFiles(directory, [filePattern], fileInfo, reportSearchIssue)
                        : [];
                }
                else if (File.Exists(full))
                {
                    ExportDumpOwnership.RejectReparseAncestors(full);
                    _ = GetFileInfo(full, fileInfo);
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

    internal const string MiscOwnerDirectory = "_misc";

    internal static string GetOwnerDirectory(string sourcePath)
    {
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var dotIndex = stem.IndexOf('.');
        if (dotIndex > 0)
        {
            var owner = stem[..dotIndex].Trim();
            if (owner.Length > 0 && IsSafeDirectorySegment(owner)) return owner;
        }
        return MiscOwnerDirectory;
    }

    private static Dictionary<string, string> PlanChildRelativePaths(IReadOnlyList<PlannedAssembly> assemblies,
        IReadOnlySet<PlannedAssembly> basenameCollisions)
    {
        var owners = assemblies.GroupBy(item => GetOwnerDirectory(item.SourcePath), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        var relativePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var owner in owners)
        {
            var ownerName = owner.Select(item => GetOwnerDirectory(item.SourcePath))
                .Order(StringComparer.OrdinalIgnoreCase).ThenBy(value => value, StringComparer.Ordinal).First();
            var entries = owner.Select(item => new LayoutEntry(item, GetAdditionalSegments(item.SourcePath))).ToArray();
            Assign(entries, ownerName, 0);
        }
        return relativePaths;

        void Assign(IReadOnlyList<LayoutEntry> entries, string parent, int startSegment)
        {
            if (entries.Count <= MaxAssemblyChildrenPerDirectory)
            {
                AddLeaves(entries, parent);
                return;
            }

            var maxSegment = entries.Max(entry => entry.Segments.Length);
            for (var segmentIndex = startSegment; segmentIndex < maxSegment; segmentIndex++)
            {
                var groups = entries.Where(entry => segmentIndex < entry.Segments.Length)
                    .GroupBy(entry => entry.Segments[segmentIndex], StringComparer.OrdinalIgnoreCase)
                    .Select(group => new SegmentGroup(CanonicalSegment(group.Select(entry => entry.Segments[segmentIndex])),
                        group.ToList())).ToArray();
                var missing = entries.Where(entry => segmentIndex >= entry.Segments.Length).ToList();

                // A shared segment does not add a useful directory, so inspect the next one.
                if (groups.Length == 1 && missing.Count == 0) continue;

                // The first differing segment bounds the prefix. Do not skip all-singleton
                // buckets and group unrelated entries by a later suffix.
                foreach (var group in groups.OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(group => group.Name, StringComparer.Ordinal))
                {
                    if (IsSafeDirectorySegment(group.Name))
                    {
                        if (group.Entries.Count == 1)
                        {
                            AddLeaf(group.Entries[0], parent);
                            continue;
                        }
                        var groupPath = Path.Combine(parent, group.Name);
                        if (group.Entries.Count > MaxAssemblyChildrenPerDirectory)
                            Assign(group.Entries, groupPath, segmentIndex + 1);
                        else
                            AddLeaves(group.Entries, groupPath);
                    }
                    else if (group.Entries.Count > MaxAssemblyChildrenPerDirectory)
                    {
                        // Unsafe tokens cannot name directories; recurse within that token's
                        // own bucket so later safe segments can still reduce a large group.
                        Assign(group.Entries, parent, segmentIndex + 1);
                    }
                    else
                    {
                        AddLeaves(group.Entries, parent);
                    }
                }
                AddLeaves(missing, parent);
                return;
            }

            AddLeaves(entries, parent);
        }

        void AddLeaves(IEnumerable<LayoutEntry> entries, string parent)
        {
            foreach (var entry in entries) AddLeaf(entry, parent);
        }

        void AddLeaf(LayoutEntry entry, string parent)
        {
            var relativePath = basenameCollisions.Contains(entry.Assembly)
                ? Path.Combine(parent, Path.GetFileName(entry.Assembly.SourcePath), VariantKey(entry.Assembly))
                : Path.Combine(parent, Path.GetFileName(entry.Assembly.SourcePath));
            relativePaths.Add(entry.Assembly.SourcePath, relativePath);
        }
    }

    private static string[] GetAdditionalSegments(string sourcePath) =>
        Path.GetFileNameWithoutExtension(sourcePath).Split('.').Skip(1).ToArray();

    private static string CanonicalSegment(IEnumerable<string> segments) =>
        segments.Order(StringComparer.OrdinalIgnoreCase).ThenBy(value => value, StringComparer.Ordinal).First();

    private static bool IsSafeDirectorySegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".."
            || segment.EndsWith(' ') || segment.EndsWith('.')
            || segment.Any(character => character <= '\u001f')
            || segment.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0)
            return false;

        var deviceName = segment.Split('.')[0];
        if (deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return false;
        if (deviceName.Length == 4 && (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && (deviceName[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3')) return false;
        return true;
    }

    private sealed record LayoutEntry(PlannedAssembly Assembly, string[] Segments);
    private sealed record SegmentGroup(string Name, List<LayoutEntry> Entries);

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
        IDictionary<string, AssemblyFileInfo> fileInfo,
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
                    _ = GetFileInfo(entry, fileInfo);
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

    private static AssemblyFileInfo GetFileInfo(string path, IDictionary<string, AssemblyFileInfo> fileInfo, bool includeHash = false)
    {
        var canonicalPath = Path.GetFullPath(path);
        if (!fileInfo.TryGetValue(canonicalPath, out var info))
        {
            info = new(ReadManagedIdentity(canonicalPath));
            fileInfo.Add(canonicalPath, info);
        }
        if (includeHash && info.ContentHash is null)
        {
            using var stream = File.OpenRead(canonicalPath);
            info.ContentHash = Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        return info;
    }

    private static string IdentityHashKey(AssemblyIdentityDto identity, string hash) =>
        string.Join("|", identity.Name, identity.Version, identity.Culture, identity.PublicKeyToken, hash);

    private static void ValidateSourceOutputOverlap(IReadOnlyList<string> patterns, string output)
    {
        for (var index = 0; index < patterns.Count; index++)
        {
            var full = Path.GetFullPath(patterns[index]);
            if (Directory.Exists(full))
            {
                if (ExportDumpOwnership.IsWithin(output, full) || ExportDumpOwnership.IsWithin(full, output))
                    throw new InvalidOperationException($"Source directory overlaps the output dump: {full}");
                while (index + 1 < patterns.Count && IsBareFilenamePattern(patterns[index + 1])) index++;
                continue;
            }

            var directory = Path.GetDirectoryName(full)!;
            var filename = Path.GetFileName(full);
            if (filename.IndexOfAny(['*', '?']) >= 0)
            {
                if (ExportDumpOwnership.IsWithin(output, directory) || ExportDumpOwnership.IsWithin(directory, output))
                    throw new InvalidOperationException($"Source search directory overlaps the output dump: {directory}");
            }
            else if (ExportDumpOwnership.IsWithin(full, output))
            {
                throw new InvalidOperationException($"Source file is inside the output dump: {full}");
            }
        }
    }

    private sealed class AssemblyFileInfo(AssemblyIdentityDto identity)
    {
        internal AssemblyIdentityDto Identity { get; } = identity;
        internal string? ContentHash { get; set; }
    }

    private sealed class PlannedNode(string sourcePath, AssemblyIdentityDto identity, string contentHash,
        AssemblyExportReferenceClosure directClosure)
    {
        internal string SourcePath { get; } = sourcePath;
        internal AssemblyIdentityDto Identity { get; } = identity;
        internal string ContentHash { get; } = contentHash;
        internal AssemblyExportReferenceClosure DirectClosure { get; } = directClosure;
        internal HashSet<string> LimitEdges { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
