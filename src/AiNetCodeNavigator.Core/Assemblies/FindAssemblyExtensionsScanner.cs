#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
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
        return order != 0 ? order : StringComparer.Ordinal.Compare(left.AssemblyName, right.AssemblyName);
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
        var assemblies = new List<IAssemblySymbol> { context.Assembly };
        if (request.IncludeReferences)
        {
            assemblies.AddRange(context.Compilation.SourceModule.ReferencedAssemblySymbols
                .Where(assembly => !SymbolEqualityComparer.Default.Equals(assembly, context.Assembly))
                .Take(128));
        }

        var extensions = new SortedSet<AssemblyExtensionDto>(ExtensionComparer);
        var totalCount = 0;
        foreach (var assembly in assemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var type in AssemblyAnalysisSymbolTraversal.GetAllTypes(assembly.GlobalNamespace))
            {
                foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(method => method.IsExtensionMethod && method.Parameters.Length > 0))
                {
                    var receiver = method.Parameters[0].Type;
                    var receiverName = receiver.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                    var namespaceName = type.ContainingNamespace.ToDisplayString();
                    if (!MatchesReceiver(receiverName, request.ReceiverType)
                        || !Matches(method.Name, request.ExtensionName)
                        || !Matches(namespaceName, request.Namespace)) continue;
                    totalCount++;
                    extensions.Add(new AssemblyExtensionDto(
                        namespaceName,
                        type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        method.Name,
                        method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        receiverName,
                        method.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        assembly.Identity.Name));
                    if (extensions.Count > MaxResults) extensions.Remove(extensions.Max!);
                }
            }
        }

        var limit = InspectAssemblyScanner.NormalizeLimit(request.MaxResults, DefaultMaxResults, MaxResults);
        return Result<FindAssemblyExtensionsPayload>.Success(new FindAssemblyExtensionsPayload(
            context.Origin.CanonicalPath,
            extensions.Take(limit).ToArray(),
            totalCount,
            totalCount > limit,
            context.Diagnostics));
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
