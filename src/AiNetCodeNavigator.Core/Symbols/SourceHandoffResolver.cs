#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>Resolves source handoffs only within the target and snapshot that produced them.</summary>
public static class SourceHandoffResolver
{
    public static async Task<Result<AnalysisSymbolIdentity>> ValidateIdentityAsync(
        Solution solution,
        AnalysisSymbolIdentity? suppliedIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        var currentIdentity = await AnalysisSymbolIdentity.ForSourceAsync(solution, cancellationToken).ConfigureAwait(false);
        if (currentIdentity is null)
        {
            return Result<AnalysisSymbolIdentity>.Failure(
                NavigationErrorCodes.InvalidHandoff,
                "A canonical source identity could not be created for this solution.");
        }

        if (suppliedIdentity is null || suppliedIdentity.Matches(currentIdentity))
        {
            return Result<AnalysisSymbolIdentity>.Success(currentIdentity);
        }

        var sameTarget = !suppliedIdentity.IsAssembly
            && SymbolHandoffToken.TryCreateTarget(suppliedIdentity.CanonicalPath, out var suppliedTarget)
            && SymbolHandoffToken.TryCreateTarget(solution.FilePath ?? string.Empty, out var currentTarget)
            && string.Equals(suppliedTarget, currentTarget, StringComparison.Ordinal);
        if (!sameTarget)
        {
            return Result<AnalysisSymbolIdentity>.Failure(
                NavigationErrorCodes.TargetMismatch,
                "The supplied source identity belongs to a different analysis target.");
        }

        if (!string.Equals(suppliedIdentity.ContentHash, currentIdentity.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            return Result<AnalysisSymbolIdentity>.Failure(
                NavigationErrorCodes.StaleSnapshot,
                "The supplied source identity does not match the current solution snapshot.");
        }

        return Result<AnalysisSymbolIdentity>.Failure(
            NavigationErrorCodes.TargetMismatch,
            "The supplied source identity has a different project context.");
    }

    public static async Task<Result<ISymbol?>> ResolveAsync(
        Solution solution,
        string handoffOrIdentifier,
        AnalysisSymbolIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(identity);

        var clean = InputNormalizer.NormalizeSymbolIdentifier(handoffOrIdentifier);
        var restored = HandoffHandleRegistry.Default.RestoreInternalHandoffForInput(clean);
        if (!restored.IsSuccess)
        {
            return Result<ISymbol?>.Failure(restored.Error!.Value);
        }

        var internalId = restored.Value!;
        if (!SymbolHandoffIdentifier.TryParse(internalId, out var parsed))
        {
            return Result<ISymbol?>.Failure(
                NavigationErrorCodes.InvalidHandoff,
                "The handoff identifier is malformed.");
        }

        var identityResult = await ValidateIdentityAsync(solution, identity, cancellationToken).ConfigureAwait(false);
        if (!identityResult.IsSuccess)
        {
            return Result<ISymbol?>.Failure(identityResult.Error!.Value);
        }
        var currentIdentity = identityResult.Value!;

        if (parsed.Origin != SymbolHandoffOrigin.Source
            || !SymbolHandoffToken.TryCreateTarget(solution.FilePath ?? string.Empty, out var currentTarget)
            || !string.Equals(parsed.TargetToken, currentTarget, StringComparison.Ordinal))
        {
            return Result<ISymbol?>.Failure(
                NavigationErrorCodes.TargetMismatch,
                "The handoff belongs to a different analysis target.");
        }

        if (!SymbolHandoffToken.TryCreateContent(currentIdentity.ContentHash, out var currentContent)
            || !string.Equals(parsed.ContentToken, currentContent, StringComparison.Ordinal))
        {
            return Result<ISymbol?>.Failure(
                NavigationErrorCodes.StaleSnapshot,
                "The handoff belongs to an older source snapshot.");
        }

        var projectMarkerSeparator = parsed.DocumentationCommentId.LastIndexOf("~p:", StringComparison.Ordinal);
        if (projectMarkerSeparator <= 0 || projectMarkerSeparator + 3 >= parsed.DocumentationCommentId.Length)
        {
            return Result<ISymbol?>.Failure(
                NavigationErrorCodes.InvalidHandoff,
                "The source handoff does not contain a project identity.");
        }

        var docId = parsed.DocumentationCommentId[..projectMarkerSeparator];
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string projectMarker;
            try
            {
                projectMarker = AnalysisSymbolIdentity.GetStableProjectMarker(project);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (!string.Equals(parsed.DocumentationCommentId[(projectMarkerSeparator + 3)..], projectMarker, StringComparison.Ordinal))
            {
                continue;
            }

            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            var symbol = DocumentationCommentId.GetFirstSymbolForDeclarationId(docId, compilation);
            if (symbol is null)
            {
                continue;
            }

            var expected = currentIdentity.FormatHandoff(symbol, solution);
            if (expected is null)
            {
                continue;
            }

            if (!string.Equals(expected, internalId, StringComparison.Ordinal))
            {
                return Result<ISymbol?>.Failure(
                    NavigationErrorCodes.StaleSnapshot,
                    "The handoff does not match the current source project context.");
            }

            return Result<ISymbol?>.Success(symbol);
        }

        return Result<ISymbol?>.Failure(
            NavigationErrorCodes.SymbolNotFound,
            "The handoff symbol could not be resolved in its source project.");
    }
}
