#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        && TryNormalizeTargetPath(CanonicalPath, out var canonicalPath)
        && TryNormalizeTargetPath(other.CanonicalPath, out var otherCanonicalPath)
        && string.Equals(
            canonicalPath,
            otherCanonicalPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
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
            SourceProjectMarkers = solution?.Projects
                .Where(project => project.FilePath is { Length: > 0 } projectPath && Path.IsPathFullyQualified(projectPath))
                .ToDictionary(project => project.Id, GetStableProjectMarker),
        };

    public static string GetStableProjectMarker(Project project)
    {
        if (project.FilePath is not { Length: > 0 } projectPath || !Path.IsPathFullyQualified(projectPath))
        {
            throw new ArgumentException("A stable project marker requires an absolute project file path.", nameof(project));
        }

        var canonicalPath = Path.GetFullPath(projectPath);
        if (OperatingSystem.IsWindows())
        {
            canonicalPath = canonicalPath.ToUpperInvariant();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPath));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    public static string CreateSourceSnapshotHash(
        string canonicalPath,
        IReadOnlyDictionary<string, DocumentFileState> fileState)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, canonicalPath);
        foreach (var file in fileState.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, file.Key);
            Append(hash, file.Value.Hash);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static bool HasKnownDocumentationCommentIdPrefix(string value) =>
        SymbolHandoffIdentifier.IsCanonicalDocumentationCommentId(value);

    private static void Append(IncrementalHash hash, string value) =>
        hash.AppendData(Encoding.UTF8.GetBytes(value));

    private static bool TryNormalizeTargetPath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return normalizedPath.Length > 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
