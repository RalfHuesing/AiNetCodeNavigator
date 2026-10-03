#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Assemblies;

public static class FindAssemblyExtensionsScanner
{
    public const int DefaultMaxResults = 100;
    public const int MaxResults = 1000;
    private static readonly IComparer<AssemblyExtensionDto> ExtensionComparer = Comparer<AssemblyExtensionDto>.Create((left, right) =>
    {
        var order = StringComparer.Ordinal.Compare(left.Namespace, right.Namespace);
        if (order != 0) return order;
        order = StringComparer.Ordinal.Compare(left.ContainingType, right.ContainingType);
        if (order != 0) return order;
        order = StringComparer.Ordinal.Compare(left.Name, right.Name);
        if (order != 0) return order;
        order = StringComparer.Ordinal.Compare(left.Signature, right.Signature);
        if (order != 0) return order;
        order = StringComparer.Ordinal.Compare(left.AssemblyName, right.AssemblyName);
        return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(left.OwnerTargetPath, right.OwnerTargetPath);
    });

    public static async Task<Result<FindAssemblyExtensionsPayload>> FindAsync(
        FindAssemblyExtensionsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var opened = await AssemblyNavigationSessionScope.OpenAsync(request.AssemblyPath, cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) return Result<FindAssemblyExtensionsPayload>.Failure(opened.Error);
        await using var scope = opened.Value!;
        var context = scope.Context;
        var extensions = new SortedSet<AssemblyExtensionDto>(ExtensionComparer);
        var totalCount = 0;
        var diagnostics = context.Diagnostics.ToList();
        var incompleteRelationships = context.References.Any(reference => !reference.Resolved
            || reference.ResolutionState is "depth_limit" or "invalid")
            || context.Status is AssemblySessionStatus.Partial or AssemblySessionStatus.Degraded;
        ScanOwner(context);
        if (request.IncludeReferences)
        {
            var owners = context.References
                .Where(reference => reference.Resolved && !string.IsNullOrWhiteSpace(reference.ResolvedPath))
                .Select(reference => Path.GetFullPath(reference.ResolvedPath!))
                .Where(path => !string.Equals(path, context.Origin.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (owners.Length > 128) incompleteRelationships = true;
            foreach (var ownerPath in owners.Take(128))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var openedOwner = await AssemblyNavigationSessionScope.OpenAsync(ownerPath, cancellationToken).ConfigureAwait(false);
                if (!openedOwner.IsSuccess)
                {
                    incompleteRelationships = true;
                    diagnostics.Add($"incompleteRelationships: referenced owner could not be opened ({openedOwner.Error!.Value.Code}).");
                    continue;
                }

                await using var ownerScope = openedOwner.Value!;
                var validation = AssemblyReferenceSnapshotValidator.ValidateOwner(context, ownerPath, ownerScope.Context, out var staleError);
                if (validation != AssemblyReferenceSnapshotValidator.OwnerValidationStatus.Matches)
                {
                    incompleteRelationships = true;
                    diagnostics.Add(validation == AssemblyReferenceSnapshotValidator.OwnerValidationStatus.Stale
                        ? $"incompleteRelationships: referenced owner snapshot changed ({Path.GetFileName(ownerPath)})."
                        : $"incompleteRelationships: referenced owner closure was incomplete ({Path.GetFileName(ownerPath)}).");
                    continue;
                }
                ScanOwner(ownerScope.Context);
            }
        }

        var limit = InspectAssemblyScanner.NormalizeLimit(request.MaxResults, DefaultMaxResults, MaxResults);
        var identity = AnalysisSymbolIdentity.ForAssembly(context.Origin.CanonicalPath, context.Origin.ContentHash,
            context.Generation, context.ReferenceSnapshotHash);
        var binding = AssemblyPaging.CreateExtensionsBinding(context.Origin.CanonicalPath, context.Origin.ContentHash,
            context.ReferenceSnapshotHash, request with { MaxResults = limit });
        var cursorStatus = AssemblyPaging.ReadBoundOffset(request.Cursor, binding, out var offset);
        if (cursorStatus != AssemblyPaging.BoundCursorStatus.Valid)
            return Result<FindAssemblyExtensionsPayload>.Failure(
                cursorStatus == AssemblyPaging.BoundCursorStatus.StaleBinding
                    ? NavigationErrorCodes.StaleSnapshot
                    : NavigationErrorCodes.InvalidArgument,
                "resultCursor is not bound to the target, reference snapshot, and extension query.",
                "Repeat the same extension query against the same assembly snapshot using its latest resultCursor.");
        var ordered = extensions.ToArray();
        if (request.Cursor is not null && offset >= ordered.Length)
            return Result<FindAssemblyExtensionsPayload>.Failure(NavigationErrorCodes.StaleSnapshot,
                "resultCursor is beyond the extension results.", "Use a resultCursor from a nonfinal page.");
        var page = ordered.Skip(offset).Take(limit).ToArray();
        var hasMore = offset + page.Length < ordered.Length;
        var resultCursor = hasMore ? AssemblyPaging.CreateToken(offset + page.Length, binding) : null;
        var truncatedBy = new List<string>();
        if (hasMore) truncatedBy.Add("maxResults");
        if (incompleteRelationships) truncatedBy.Add("incompleteRelationships");
        return Result<FindAssemblyExtensionsPayload>.Success(new FindAssemblyExtensionsPayload(
            context.Origin.CanonicalPath,
            page,
            totalCount,
            truncatedBy.Count > 0,
            diagnostics.Distinct(StringComparer.Ordinal).ToArray(),
            new NavigationAnalysisMetadata(NavigationAnalysisMetadata.CreateSnapshotId("assembly", identity.ContentHash),
                $"extensions(receiver={request.ReceiverType?.Trim() ?? "*"}, name={request.ExtensionName?.Trim() ?? "*"}, namespace={request.Namespace?.Trim() ?? "*"}, references={request.IncludeReferences}, page={limit})",
                truncatedBy.Where(reason => reason != "maxResults").ToArray(), incompleteRelationships ? "partial" : "complete", hasMore),
            resultCursor));

        void ScanOwner(AssemblyContext owner)
        {
            var identity = AnalysisSymbolIdentity.ForAssembly(owner.Origin.CanonicalPath, owner.Origin.ContentHash,
                owner.Generation, owner.ReferenceSnapshotHash);
            foreach (var type in AssemblyAnalysisSymbolTraversal.GetAllTypes(owner.Assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(method => method.IsExtensionMethod && method.Parameters.Length > 0))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var receiver = method.Parameters[0].Type;
                    var receiverName = receiver.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                    var namespaceName = type.ContainingNamespace.ToDisplayString();
                    if (!MatchesReceiver(receiverName, request.ReceiverType)
                        || !Matches(method.Name, request.ExtensionName)
                        || !Matches(namespaceName, request.Namespace)) continue;
                    var stableId = identity.FormatHandoff(method);
                    if (stableId is null)
                    {
                        incompleteRelationships = true;
                        diagnostics.Add($"incompleteRelationships: extension declaration has no stable owner handoff ({method.Name}).");
                        continue;
                    }
                    var added = extensions.Add(new AssemblyExtensionDto(
                        namespaceName,
                        type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        method.Name,
                        method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        receiverName,
                        method.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        owner.Identity?.Name ?? Path.GetFileNameWithoutExtension(owner.Origin.CanonicalPath),
                        stableId,
                        HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(stableId),
                        owner.Origin.CanonicalPath));
                    if (added) totalCount++;
                }
            }
        }
    }

    private static bool Matches(string value, string? filter) =>
        string.IsNullOrWhiteSpace(filter) || value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesReceiver(string receiver, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var normalizedReceiver = NormalizeType(receiver);
        var normalizedFilter = NormalizeType(filter.Trim());
        return string.Equals(normalizedReceiver, normalizedFilter, StringComparison.OrdinalIgnoreCase)
            || normalizedReceiver.EndsWith("." + normalizedFilter, StringComparison.OrdinalIgnoreCase)
            || normalizedFilter.EndsWith("." + normalizedReceiver, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeType(string value) => value switch
    {
        "string" => "System.String",
        "object" => "System.Object",
        "bool" => "System.Boolean",
        "byte" => "System.Byte",
        "char" => "System.Char",
        "decimal" => "System.Decimal",
        "double" => "System.Double",
        "float" => "System.Single",
        "int" => "System.Int32",
        "long" => "System.Int64",
        "short" => "System.Int16",
        _ => value.Replace("global::", string.Empty, StringComparison.Ordinal),
    };
}
