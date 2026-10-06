using System.Reflection;
using System.Security.Cryptography;
using AiNetCodeNavigator.Core.Assemblies;

namespace AiNetCodeNavigator.AssemblyExport;

internal sealed record FilteredExportReference(AssemblyReferenceDto Reference, string Rule);
internal sealed record PlannedAssembly(string SourcePath, string ChildPath, AssemblyIdentityDto Identity,
    bool IsExplicit, AssemblyExportReferenceClosure Closure, IReadOnlyList<FilteredExportReference> FilteredReferences)
{
    // Original closure edges retain their provenance; decompilation gets one proven path per identity.
    internal IReadOnlyList<AssemblyReferenceDto> DecompilationReferences { get; init; } = Closure.References;
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
        var explicitPaths = Expand(arguments.Sources);
        var queue = new Queue<string>(explicitPaths);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIdentities = new Dictionary<string, (string Path, string Hash)>(StringComparer.OrdinalIgnoreCase);
        var children = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assemblies = new List<PlannedAssembly>();
        while (queue.TryDequeue(out var source))
        {
            source = Path.GetFullPath(source);
            if (!seenPaths.Add(source)) continue;
            ExportDumpOwnership.RejectReparseAncestors(source);
            if (ExportDumpOwnership.IsWithin(source, output))
                throw new InvalidOperationException($"Source DLL is inside the output dump: {source}");
            var identity = ReadManagedIdentity(source);
            var child = Path.Combine(output, Path.GetFileName(source));
            if (children.TryGetValue(child, out var previous) && !previous.Equals(source, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Different source files map to the same output child: {previous}, {source}");
            children[child] = source;
            var identityKey = string.Join("|", identity.Name, identity.Version, identity.Culture, identity.PublicKeyToken);
            using var sourceStream = File.OpenRead(source);
            var hash = Convert.ToHexStringLower(SHA256.HashData(sourceStream));
            var duplicateIdentity = seenIdentities.TryGetValue(identityKey, out var existing);
            if (duplicateIdentity)
            {
                if (existing.Hash != hash)
                    throw new InvalidOperationException($"Assembly identity resolves to different content: {existing.Path}, {source}");
            }
            else seenIdentities[identityKey] = (source, hash);
            var closure = resolve(source, reference => AutomaticExportFilter.Match(reference.Name) is null);
            if (closure.Identity is null || !closure.IsComplete)
                throw new InvalidOperationException($"Incomplete reference closure for {source}: {string.Join("; ", closure.Diagnostics.Select(item => item.Message))}");
            var filtered = closure.References.Select(reference => (reference, rule: AutomaticExportFilter.Match(reference.Name)))
                .Where(item => item.rule is not null).Select(item => new FilteredExportReference(item.reference, item.rule!)).ToArray();
            if (!duplicateIdentity)
                assemblies.Add(new(source, child, identity, explicitPaths.Contains(source, StringComparer.OrdinalIgnoreCase), closure, filtered));
            foreach (var reference in closure.References.Where(reference => reference.Resolved && reference.ResolvedPath is not null
                         && AutomaticExportFilter.Match(reference.Name) is null).OrderBy(reference => reference.ResolvedPath, StringComparer.OrdinalIgnoreCase))
                queue.Enqueue(reference.ResolvedPath!);
        }
        var canonicalReferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in assemblies.SelectMany(item => item.Closure.References).Where(edge => edge.ResolvedPath is not null)
                     .OrderBy(edge => edge.ResolvedPath, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.GetFullPath(reference.ResolvedPath!);
            ExportDumpOwnership.RejectReparseAncestors(path);
            if (ExportDumpOwnership.IsWithin(path, output))
                throw new InvalidOperationException($"Resolved dependency is inside the output dump: {path}");
            var identity = ReadManagedIdentity(path);
            var key = string.Join("|", identity.Name, identity.Version, identity.Culture, identity.PublicKeyToken);
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (seenIdentities.TryGetValue(key, out var representative))
            {
                if (representative.Hash != hash)
                    throw new InvalidOperationException($"Assembly identity resolves to different content: {representative.Path}, {path}");
            }
            else seenIdentities[key] = representative = (path, hash);
            canonicalReferences[path] = representative.Path;
        }
        var normalizedAssemblies = assemblies.Select(item => item with
        {
            DecompilationReferences = item.Closure.References.Select(edge => edge.ResolvedPath is not null
                ? edge with { ResolvedPath = canonicalReferences[Path.GetFullPath(edge.ResolvedPath)] } : edge).ToArray(),
        }).OrderBy(item => item.ChildPath, StringComparer.OrdinalIgnoreCase).ToArray();
        var plan = new ExportPlan(arguments, output, explicitPaths, normalizedAssemblies);
        new ExportDumpOwnership(plan).ValidatePreflight();
        return plan;
    }

    internal static IReadOnlyList<string> Expand(IReadOnlyList<string> patterns)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in patterns)
        {
            var full = Path.GetFullPath(pattern);
            var directory = Path.GetDirectoryName(full)!;
            var filePattern = Path.GetFileName(full);
            if (directory.IndexOfAny(['*', '?']) >= 0 || filePattern.Contains("**", StringComparison.Ordinal))
                throw new ArgumentException($"Only final filename segment wildcards * and ? are supported: {pattern}");
            var matches = filePattern.IndexOfAny(['*', '?']) >= 0
                ? Directory.Exists(directory) ? Directory.GetFiles(directory, filePattern, SearchOption.TopDirectoryOnly) : []
                : File.Exists(full) ? [full] : [];
            if (matches.Length == 0) throw new ArgumentException($"Source path or pattern has no matches: {pattern}");
            foreach (var match in matches)
            {
                ExportDumpOwnership.RejectReparseAncestors(match);
                ReadManagedIdentity(match);
                paths.Add(Path.GetFullPath(match));
            }
        }
        return paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static AssemblyIdentityDto ReadManagedIdentity(string path)
    {
        if (!Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Input must be a managed DLL: {path}");
        var name = AssemblyName.GetAssemblyName(path);
        return new(name.Name!, name.Version!.ToString(), string.IsNullOrEmpty(name.CultureName) ? "neutral" : name.CultureName,
            Convert.ToHexStringLower(name.GetPublicKeyToken() ?? []));
    }
}
