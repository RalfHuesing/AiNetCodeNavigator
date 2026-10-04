#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record AnalysisSymbolIdentity(string ContentHash, long Generation)
{
    public string CanonicalPath { get; init; } = string.Empty;
    public bool IsAssembly { get; init; } = true;
    public IReadOnlyDictionary<ProjectId, string>? SourceProjectMarkers { get; init; }

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

    public static AnalysisSymbolIdentity ForSource(string canonicalPath, string snapshotHash) =>
        new(snapshotHash, 0)
        {
            CanonicalPath = canonicalPath,
            IsAssembly = false,
        };

    public static bool HasKnownDocumentationCommentIdPrefix(string value) =>
        !string.IsNullOrEmpty(value)
        && value.Length >= 3
        && value[1] == ':'
        && value[0] is 'T' or 'M' or 'P' or 'F' or 'E' or '!'
        && !value.Contains("#lf:", StringComparison.Ordinal)
        && !value.Contains('?', StringComparison.Ordinal)
        && !value.Any(char.IsWhiteSpace);


}
