#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record AnalysisSymbolIdentity(string ContentHash, long Generation)
{
    public string CanonicalPath { get; init; } = string.Empty;
    public bool IsAssembly { get; init; } = true;
    public IReadOnlyDictionary<ProjectId, string>? SourceProjectMarkers { get; init; }

    private string? Format(string? symbolId) =>
        symbolId is not null
        && SymbolHandoffIdentifier.TryCreate(
            new SymbolHandoffCreationRequest(
                IsAssembly ? SymbolHandoffOrigin.Assembly : SymbolHandoffOrigin.Source,
                CanonicalPath,
                ContentHash,
                symbolId),
            out var identifier)
            ? identifier.Format()
            : null;

    private string? Format(string? symbolId, ProjectId projectId) =>
        symbolId is not null
        && SourceProjectMarkers is not null
        && SourceProjectMarkers.TryGetValue(projectId, out var projectMarker)
            ? Format($"{symbolId}~p:{projectMarker}")
            : null;

    public string? FormatHandoff(ISymbol symbol)
    {
        if (!IsAssembly)
        {
            return null;
        }

        var declarationId = DocumentationCommentId.CreateDeclarationId(symbol);
        return IsCanonicalHandoffSymbol(symbol, declarationId)
            ? Format(declarationId)
            : null;
    }

    private string? FormatHandoff(ISymbol symbol, ProjectId projectId)
    {
        if (IsAssembly
            || SourceProjectMarkers is null
            || !SourceProjectMarkers.ContainsKey(projectId))
        {
            return null;
        }

        var declarationId = DocumentationCommentId.CreateDeclarationId(symbol);
        return IsCanonicalHandoffSymbol(symbol, declarationId)
            ? Format(declarationId, projectId)
            : null;
    }

    public string? FormatHandoff(ISymbol symbol, Solution solution)
    {
        if (IsAssembly)
        {
            return FormatHandoff(symbol);
        }

        var projectIds = symbol.Locations
            .Where(location => location.IsInSource && location.SourceTree is not null)
            .Select(location => solution.GetDocument(location.SourceTree!)?.Project.Id)
            .Where(projectId => projectId is not null)
            .Select(projectId => projectId!)
            .Distinct()
            .Take(2)
            .ToArray();

        return projectIds.Length == 1
            ? FormatHandoff(symbol, projectIds[0])
            : null;
    }

    public static bool IsCanonicalHandoffSymbol(ISymbol symbol, string? declarationId = null) =>
        symbol is not IMethodSymbol { MethodKind: MethodKind.LocalFunction }
        && !string.IsNullOrWhiteSpace(declarationId)
        && HasKnownDocumentationCommentIdPrefix(declarationId!);

    /// <summary>
    /// Creates a stable source identity from a declaration ID, or from its unique source location
    /// when Roslyn does not provide a documentation comment ID (for example, a local function).
    /// </summary>
    public static string? CreateCanonicalSymbolIdentifier(ISymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        var declarationId = DocumentationCommentId.CreateDeclarationId(symbol);
        if (symbol is not IMethodSymbol { MethodKind: MethodKind.LocalFunction }
            && HasKnownDocumentationCommentIdPrefix(declarationId ?? string.Empty))
        {
            return declarationId;
        }

        var locations = symbol.Locations
            .Where(location => location.IsInSource && location.SourceTree is not null)
            .Take(2)
            .ToArray();
        if (locations.Length != 1)
        {
            return null;
        }

        var location = locations[0];
        var filePath = location.SourceTree!.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathFullyQualified(filePath))
        {
            return null;
        }

        string canonicalPath;
        try
        {
            canonicalPath = Path.GetFullPath(filePath);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            canonicalPath = canonicalPath.ToUpperInvariant();
        }

        var linePosition = location.SourceTree.GetLineSpan(location.SourceSpan).StartLinePosition;
        return $"L:{canonicalPath}:{linePosition.Line + 1}:{linePosition.Character + 1}";
    }

    public bool Matches(AnalysisSymbolIdentity other) =>
        IsAssembly == other.IsAssembly
        && SymbolHandoffToken.TryNormalizeTargetPath(CanonicalPath, out var canonicalPath)
        && SymbolHandoffToken.TryNormalizeTargetPath(other.CanonicalPath, out var otherCanonicalPath)
        && string.Equals(
            canonicalPath,
            otherCanonicalPath,
            StringComparison.Ordinal)
        && string.Equals(ContentHash, other.ContentHash, StringComparison.OrdinalIgnoreCase);

    public static AnalysisSymbolIdentity ForAssembly(string canonicalPath, string contentHash, long generation = 0) =>
        new(contentHash, generation)
        {
            CanonicalPath = canonicalPath,
            IsAssembly = true,
        };

    public static AnalysisSymbolIdentity ForSource(string canonicalPath, string snapshotHash, Solution? solution = null) =>
        new(snapshotHash, 0)
        {
            CanonicalPath = canonicalPath,
            IsAssembly = false,
            SourceProjectMarkers = solution is null ? null : BuildSourceProjectMarkers(solution),
        };

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

        if (!SymbolHandoffToken.TryNormalizeTargetPath(projectPath, out var canonicalPath))
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
        if (!SymbolHandoffToken.TryNormalizeTargetPath(canonicalPath, out var normalizedTargetPath))
        {
            throw new ArgumentException("The solution path must be absolute and normalizable.", nameof(canonicalPath));
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, normalizedTargetPath);
        foreach (var file in fileState.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var normalizedFilePath = SymbolHandoffToken.TryNormalizeTargetPath(file.Key, out var fullFilePath)
                ? fullFilePath
                : file.Key;
            Append(hash, normalizedFilePath);
            Append(hash, file.Value.Hash);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static bool HasKnownDocumentationCommentIdPrefix(string value) =>
        SymbolHandoffIdentifier.IsCanonicalDocumentationCommentId(value);

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
            || !SymbolHandoffToken.TryNormalizeTargetPath(projectPath, out var canonicalPath))
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
            || !SymbolHandoffToken.TryNormalizeTargetPath(referencePath, out var canonicalPath))
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
