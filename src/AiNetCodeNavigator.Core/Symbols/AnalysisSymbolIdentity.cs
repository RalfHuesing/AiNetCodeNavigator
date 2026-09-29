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

    public string? Format(string? symbolId) =>
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

    public string? Format(string? symbolId, ProjectId projectId) =>
        symbolId is null
            ? null
            : Format($"{symbolId}~p:{(SourceProjectMarkers?.GetValueOrDefault(projectId) ?? projectId.Id.ToString("N"))}");

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

    public string? FormatHandoff(ISymbol symbol, ProjectId projectId)
    {
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

    public bool Matches(AnalysisSymbolIdentity other) =>
        IsAssembly == other.IsAssembly
        && (string.IsNullOrEmpty(CanonicalPath)
            || string.IsNullOrEmpty(other.CanonicalPath)
            || string.Equals(CanonicalPath, other.CanonicalPath, StringComparison.OrdinalIgnoreCase))
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
            SourceProjectMarkers = solution?.Projects.ToDictionary(project => project.Id, GetStableProjectMarker),
        };

    public static string GetStableProjectMarker(Project project)
    {
        if (project.FilePath is not { Length: > 0 } projectPath)
        {
            return project.Id.Id.ToString("N");
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
}
