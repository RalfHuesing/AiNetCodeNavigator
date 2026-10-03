#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Models;
using Microsoft.CodeAnalysis.CSharp;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record AnalysisSymbolIdentity(string ContentHash, long Generation)
{
    public string CanonicalPath { get; init; } = string.Empty;
    public bool IsAssembly { get; init; } = true;
    public IReadOnlyDictionary<ProjectId, string>? SourceProjectMarkers { get; init; }
    internal SourceReferenceFormattingContext? SourceReferences { get; init; }

    public string? FormatHandoff(ISymbol symbol, Solution solution)
    {
        if (IsAssembly) return null;
        return SourceReferences is { } context && context.IsForSolution(solution)
            ? context.Format(symbol) : null;
    }

    public bool Matches(AnalysisSymbolIdentity other) =>
        IsAssembly == other.IsAssembly
        && StableTargetPath.TryNormalizeTargetPath(CanonicalPath, out var canonicalPath)
        && StableTargetPath.TryNormalizeTargetPath(other.CanonicalPath, out var otherCanonicalPath)
        && string.Equals(
            canonicalPath,
            otherCanonicalPath,
            StringComparison.Ordinal)
        && string.Equals(ContentHash, other.ContentHash, StringComparison.OrdinalIgnoreCase)
        && (IsAssembly || HaveSameSourceProjectMarkers(other));

    internal static ResultError? SourceMismatch(AnalysisSymbolIdentity supplied, AnalysisSymbolIdentity? current)
    {
        if (current is not null && supplied.Matches(current)) return null;

        var sameTarget = current is not null
            && !supplied.IsAssembly
            && StableTargetPath.TryCreateTarget(supplied.CanonicalPath, out var suppliedTarget)
            && StableTargetPath.TryCreateTarget(current.CanonicalPath, out var actualTarget)
            && string.Equals(suppliedTarget, actualTarget, StringComparison.Ordinal);
        var sameContent = current is not null
            && string.Equals(supplied.ContentHash, current.ContentHash, StringComparison.OrdinalIgnoreCase);
        var code = sameTarget && !sameContent ? NavigationErrorCodes.StaleSnapshot : NavigationErrorCodes.TargetMismatch;
        var message = code == NavigationErrorCodes.StaleSnapshot
            ? "The supplied source identity does not match the current solution snapshot."
            : "The supplied source identity does not match this target and project context.";
        return new ResultError(code, message);
    }

    private bool HaveSameSourceProjectMarkers(AnalysisSymbolIdentity other)
    {
        if (SourceProjectMarkers is null || other.SourceProjectMarkers is null)
        {
            return false;
        }

        // ProjectIds are workspace-local; compare stable marker values across reloads.
        return SourceProjectMarkers.Values.OrderBy(marker => marker, StringComparer.Ordinal)
            .SequenceEqual(other.SourceProjectMarkers.Values.OrderBy(marker => marker, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    public static AnalysisSymbolIdentity ForAssembly(
        string canonicalPath,
        string contentHash,
        long generation = 0,
        string referenceSnapshotHash = "") =>
        new(CreateAssemblyHandoffContentHash(contentHash, referenceSnapshotHash, generation), generation)
        {
            CanonicalPath = canonicalPath,
            IsAssembly = true,
        };

    public static string CreateAssemblyHandoffContentHash(string contentHash, string referenceSnapshotHash, long generation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        if (string.IsNullOrWhiteSpace(referenceSnapshotHash)) return contentHash;
        var material = string.Join("\0", contentHash, referenceSnapshotHash, generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    public static AnalysisSymbolIdentity ForSource(string canonicalPath, string snapshotHash, Solution? solution = null) =>
        new(snapshotHash, 0)
        {
            CanonicalPath = canonicalPath,
            IsAssembly = false,
            SourceProjectMarkers = solution is null ? null : BuildSourceProjectMarkers(solution),
        };

    public static async Task<AnalysisSymbolIdentity?> ForSourceAsync(Solution solution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        if (!StableTargetPath.TryNormalizeTargetPath(solution.FilePath, out var canonicalPath))
        {
            return null;
        }

        var documents = new List<(string Key, string Hash)>();
        foreach (var project in solution.Projects.OrderBy(project => project.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(project => project.Name, StringComparer.Ordinal))
        {
            string projectMarker;
            try
            {
                projectMarker = GetStableProjectMarker(project);
            }
            catch (ArgumentException)
            {
                continue;
            }
            foreach (var document in project.Documents.OrderBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(document => document.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                var textHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
                var path = document.FilePath is { Length: > 0 } filePath
                    && StableTargetPath.TryNormalizeTargetPath(filePath, out var normalizedFilePath)
                        ? normalizedFilePath
                        : document.FilePath ?? document.Name;
                documents.Add(($"{projectMarker}\\0{path}", textHash));
            }
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, canonicalPath);
        foreach (var document in documents.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, document.Key);
            Append(hash, document.Hash);
        }

        var identity = ForSource(canonicalPath, Convert.ToHexString(hash.GetHashAndReset()), solution);
        return identity with { SourceReferences = await SourceReferenceFormattingContext.CreateAsync(solution, cancellationToken).ConfigureAwait(false) };
    }

    private static IReadOnlyDictionary<ProjectId, string> BuildSourceProjectMarkers(Solution solution)
    {
        var candidates = new List<(ProjectId ProjectId, string Marker)>();
        foreach (var project in solution.Projects)
        {
            if (project.FilePath is not { Length: > 0 } projectPath || !Path.IsPathFullyQualified(projectPath))
            {
                continue;
            }

            try
            {
                candidates.Add((project.Id, GetStableProjectMarker(project)));
            }
            catch (ArgumentException)
            {
                // Projects without a stable reference context cannot safely receive handoff IDs.
            }
        }

        return candidates
            .GroupBy(candidate => candidate.Marker, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Single().ProjectId, group => group.Key);
    }

    public static string GetStableProjectMarker(Project project)
    {
        if (project.FilePath is not { Length: > 0 } projectPath || !Path.IsPathFullyQualified(projectPath))
        {
            throw new ArgumentException("A stable project marker requires an absolute project file path.", nameof(project));
        }

        if (!StableTargetPath.TryNormalizeTargetPath(projectPath, out var canonicalPath))
        {
            throw new ArgumentException("The project file path could not be normalized.", nameof(project));
        }

        var projectContext = GetStableProjectContext(project);
        var identityMaterial = $"{canonicalPath}\0{projectContext}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identityMaterial));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    public static string CreateSourceSnapshotHash(
        string canonicalPath,
        IReadOnlyDictionary<string, DocumentFileState> fileState)
    {
        if (!StableTargetPath.TryNormalizeTargetPath(canonicalPath, out var normalizedTargetPath))
        {
            throw new ArgumentException("The solution path must be absolute and normalizable.", nameof(canonicalPath));
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, normalizedTargetPath);
        foreach (var file in fileState.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var normalizedFilePath = StableTargetPath.TryNormalizeTargetPath(file.Key, out var fullFilePath)
                ? fullFilePath
                : file.Key;
            Append(hash, normalizedFilePath);
            Append(hash, file.Value.Hash);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static bool HasKnownDocumentationCommentIdPrefix(string value) =>
        !string.IsNullOrEmpty(value)
        && value.Length >= 3
        && value[1] == ':'
        && value[0] is 'T' or 'M' or 'P' or 'F' or 'E' or '!'
        && !value.Contains("#lf:", StringComparison.Ordinal)
        && !value.Contains('?', StringComparison.Ordinal)
        && !value.Any(char.IsWhiteSpace);

    private static void Append(IncrementalHash hash, string value) =>
        hash.AppendData(Encoding.UTF8.GetBytes(value));

    private static string GetStableProjectContext(Project project)
    {
        var nodes = new List<string>();
        var edges = new List<string>();
        var pending = new Queue<Project>();
        var visited = new HashSet<ProjectId>();
        pending.Enqueue(project);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current.Id))
            {
                continue;
            }

            var node = GetStableProjectNode(current);
            nodes.Add(node);
            foreach (var projectReference in current.ProjectReferences)
            {
                var referencedProject = current.Solution.GetProject(projectReference.ProjectId);
                if (referencedProject is null)
                {
                    throw new ArgumentException("A stable project marker requires all project references to resolve.", nameof(project));
                }

                var targetPath = GetCanonicalProjectPath(referencedProject);
                var targetNode = GetStableProjectNode(referencedProject);
                var aliases = string.Join(";", projectReference.Aliases.OrderBy(alias => alias, StringComparer.Ordinal));
                edges.Add($"{node}=>{targetPath}\0{targetNode}\0{projectReference.EmbedInteropTypes}\0{aliases}");
                pending.Enqueue(referencedProject);
            }
        }

        return string.Join("\n", nodes.OrderBy(value => value, StringComparer.Ordinal))
            + "\n--references--\n"
            + string.Join("\n", edges.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static string GetCanonicalProjectPath(Project project)
    {
        if (project.FilePath is not { Length: > 0 } projectPath
            || !Path.IsPathFullyQualified(projectPath)
            || !StableTargetPath.TryNormalizeTargetPath(projectPath, out var canonicalPath))
        {
            throw new ArgumentException("A stable project marker requires absolute paths for the full project reference graph.", nameof(project));
        }

        return canonicalPath;
    }

    private static string GetStableProjectNode(Project project)
    {
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["assembly"] = project.AssemblyName ?? string.Empty,
            ["language"] = project.Language,
            ["name"] = project.Name,
        };

        if (project.ParseOptions is CSharpParseOptions csharpParseOptions)
        {
            properties["languageVersion"] = csharpParseOptions.LanguageVersion.ToString();
            properties["preprocessorSymbols"] = string.Join(
                ";",
                csharpParseOptions.PreprocessorSymbolNames.OrderBy(symbol => symbol, StringComparer.Ordinal));
        }

        if (project.CompilationOptions is { } compilationOptions)
        {
            properties["outputKind"] = compilationOptions.OutputKind.ToString();
            properties["moduleName"] = compilationOptions.ModuleName ?? string.Empty;
        }

        var analyzerOptions = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
        foreach (var propertyName in new[]
        {
            "build_property.TargetFramework",
            "build_property.TargetFrameworkIdentifier",
            "build_property.TargetFrameworkVersion",
            "build_property.TargetPlatformIdentifier",
            "build_property.TargetPlatformVersion",
            "build_property.RuntimeIdentifier",
            "build_property.Configuration",
            "build_property.Platform",
        })
        {
            if (analyzerOptions.TryGetValue(propertyName, out var value))
            {
                properties[propertyName] = value;
            }
        }

        var metadataReferences = new List<string>();
        foreach (var metadataReference in project.MetadataReferences)
        {
            if (metadataReference is not PortableExecutableReference portableReference
                || !TryGetStableMetadataReferenceDescriptor(portableReference, out var descriptor))
            {
                throw new ArgumentException("A stable project marker requires stable metadata reference identities.", nameof(project));
            }

            metadataReferences.Add(descriptor);
        }

        properties["metadataReferences"] = string.Join("\n", metadataReferences.OrderBy(value => value, StringComparer.Ordinal));

        return string.Join("\n", properties.Select(property => $"{property.Key}={property.Value}"));
    }

    private static bool TryGetStableMetadataReferenceDescriptor(
        PortableExecutableReference reference,
        out string descriptor)
    {
        descriptor = string.Empty;
        var referencePath = reference.FilePath;
        if (string.IsNullOrWhiteSpace(referencePath) || !Path.IsPathFullyQualified(referencePath)
            || !StableTargetPath.TryNormalizeTargetPath(referencePath, out var canonicalPath))
        {
            return false;
        }

        var moduleIds = reference.GetMetadata() switch
        {
            AssemblyMetadata assemblyMetadata => assemblyMetadata.GetModules()
                .Select(module => module.GetModuleVersionId().ToString("D")),
            ModuleMetadata moduleMetadata => [moduleMetadata.GetModuleVersionId().ToString("D")],
            _ => [],
        };
        var stableModuleIds = moduleIds.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (stableModuleIds.Length == 0 || stableModuleIds.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        var properties = reference.Properties;
        var aliases = string.Join(";", properties.Aliases.OrderBy(alias => alias, StringComparer.Ordinal));
        descriptor = $"{canonicalPath}\0{string.Join(";", stableModuleIds)}\0{properties.Kind}\0{properties.EmbedInteropTypes}\0{aliases}";
        return true;
    }
}
