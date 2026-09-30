#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;

namespace AiNetCodeNavigator.Core.Assemblies;

public static class AssemblyContextScanner
{
    public const int DefaultMaxResults = 100;
    public const int MaxResults = 1000;

    public static async Task<Result<AssemblyContextPayload>> GetAsync(
        AssemblyContextRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var opened = await AssemblyNavigationSessionScope.OpenAsync(request.AssemblyPath, cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) return Result<AssemblyContextPayload>.Failure(opened.Error);
        await using var scope = opened.Value!;
        var context = scope.Context;
        var limit = InspectAssemblyScanner.NormalizeLimit(request.MaxResults, DefaultMaxResults, MaxResults);
        var typeNames = new List<string>(limit);
        var namespaceSet = new HashSet<string>(StringComparer.Ordinal);
        var totalTypes = 0;
        foreach (var type in AssemblyAnalysisSymbolTraversal.GetAllTypes(context.Assembly.GlobalNamespace))
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalTypes++;
            var namespaceName = type.ContainingNamespace.ToDisplayString();
            if (namespaceName.Length > 0) namespaceSet.Add(namespaceName);
            if (typeNames.Count < limit) typeNames.Add(type.ToDisplayString());
        }
        var namespaces = namespaceSet
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
        return Result<AssemblyContextPayload>.Success(new AssemblyContextPayload(
            context.Origin.CanonicalPath,
            context.Identity,
            context.Origin,
            context.Status.ToWireValue(),
            totalTypes,
            namespaceSet.Count,
            namespaces,
            typeNames,
            request.IncludeReferences ? context.References.Take(128).ToArray() : Array.Empty<AssemblyReferenceDto>(),
            context.Diagnostics,
            typeNames.Count,
            totalTypes > limit || namespaceSet.Count > limit || request.IncludeReferences && context.References.Count > 128,
            context.References.Count,
            request.IncludeReferences && context.References.Count > 128));
    }
}
