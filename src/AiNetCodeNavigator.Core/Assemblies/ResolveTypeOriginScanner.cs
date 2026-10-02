#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Assemblies;

public static class ResolveTypeOriginScanner
{
    public const int MaxCandidates = 100;

    public static async Task<Result<ResolveTypeOriginPayload>> ResolveAsync(
        ResolveTypeOriginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TypeName))
        {
            return Result<ResolveTypeOriginPayload>.Failure(NavigationErrorCodes.InvalidArgument, "typeName must not be empty.");
        }

        var opened = await AssemblyNavigationSessionScope.OpenAsync(request.AssemblyPath, cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) return Result<ResolveTypeOriginPayload>.Failure(opened.Error);
        await using var scope = opened.Value!;
        var context = scope.Context;
        var typeName = NormalizeTypeName(request.TypeName);
        var matches = new List<ITypeSymbol>();
        AddMatches(context.Assembly, typeName, matches);
        if (request.IncludeReferences)
        {
            foreach (var assembly in context.Compilation.SourceModule.ReferencedAssemblySymbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddMatches(assembly, typeName, matches);
                if (matches.Count > MaxCandidates) break;
            }
        }

        var unique = matches
            .GroupBy(type => $"{type.ContainingAssembly?.Identity}|{type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}", StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(MaxCandidates)
            .ToList();
        if (unique.Count == 0)
        {
            return Result<ResolveTypeOriginPayload>.Failure(
                NavigationErrorCodes.SymbolNotFound,
                $"Type '{request.TypeName.Trim()}' was not found in the assembly or its resolved references.",
                request.IncludeReferences ? "Check the fully qualified type name and confirm the assembly reference is available." : "Enable reference lookup or check the type name.");
        }

        var pathMatches = unique
            .Select(type => (Type: type, Paths: ResolveAssemblyPaths(type.ContainingAssembly!, context)))
            .ToArray();
        var paths = pathMatches.SelectMany(match => match.Paths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unprovenReferencePath = pathMatches.Any(match =>
            !SymbolEqualityComparer.Default.Equals(match.Type.ContainingAssembly, context.Assembly)
            && match.Paths.Length != 1);
        if (unique.Count > 1 || unprovenReferencePath)
        {
            return Result<ResolveTypeOriginPayload>.Success(new ResolveTypeOriginPayload(
                request.TypeName.Trim(), "ambiguous", null, null, null, null, true,
                paths.Take(MaxCandidates).ToArray(), context.Diagnostics));
        }

        var resolvedType = unique[0];
        var isLocal = SymbolEqualityComparer.Default.Equals(resolvedType.ContainingAssembly, context.Assembly);
        var assemblyPath = pathMatches[0].Paths.SingleOrDefault();
        var package = TryGetNuGetPackage(assemblyPath);
        var framework = !isLocal && assemblyPath is not null && IsFrameworkPath(assemblyPath);
        return Result<ResolveTypeOriginPayload>.Success(new ResolveTypeOriginPayload(
            request.TypeName.Trim(),
            isLocal ? "local" : framework ? "framework" : "reference",
            resolvedType.ContainingAssembly?.Identity.Name,
            assemblyPath,
            package.Id,
            package.Version,
            false,
            paths,
            context.Diagnostics));
    }

    private static void AddMatches(IAssemblySymbol assembly, string name, ICollection<ITypeSymbol> matches)
    {
        var fullyQualified = NormalizeMetadataName(name.TrimStart('.'));
        var exact = assembly.GetTypeByMetadataName(fullyQualified);
        if (exact is not null)
        {
            matches.Add(exact);
            return;
        }

        if (!fullyQualified.Contains('+')
            && TryAddNestedMatches(assembly, fullyQualified, matches)) return;
        if (fullyQualified.Contains('+')) return;
        foreach (var type in AssemblyAnalysisSymbolTraversal.GetAllTypes(assembly.GlobalNamespace))
        {
            if (matches.Count >= MaxCandidates) return;
            var display = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            var metadataDisplay = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
            var metadataName = type.MetadataName;
            if (string.Equals(type.Name, fullyQualified, StringComparison.Ordinal)
                || string.Equals(display, fullyQualified, StringComparison.Ordinal)
                || string.Equals(metadataDisplay, fullyQualified, StringComparison.Ordinal)
                || string.Equals(metadataName, fullyQualified, StringComparison.Ordinal))
            {
                matches.Add(type);
            }
        }
    }

    internal static bool TryAddNestedMatches(IAssemblySymbol assembly, string name, ICollection<ITypeSymbol> matches)
    {
        var found = false;
        for (var separator = name.LastIndexOf('.'); separator > 0; separator = name.LastIndexOf('.', separator - 1))
        {
            var metadataName = name[..separator] + "+" + name[(separator + 1)..].Replace('.', '+');
            var nested = assembly.GetTypeByMetadataName(NormalizeMetadataName(metadataName));
            if (nested is null) continue;
            matches.Add(nested);
            found = true;
        }
        return found;
    }

    private static string NormalizeTypeName(string value) => value.Trim()
        .Replace("global::", string.Empty, StringComparison.Ordinal)
        .Replace('/', '+');

    internal static string NormalizeMetadataName(string value)
    {
        if (!value.Contains('<')) return value;
        var normalized = new System.Text.StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '<')
            {
                normalized.Append(value[index]);
                continue;
            }

            var depth = 1;
            var arity = 1;
            var closing = index + 1;
            for (; closing < value.Length && depth > 0; closing++)
            {
                if (value[closing] == '<') depth++;
                else if (value[closing] == '>') depth--;
                else if (value[closing] == ',' && depth == 1) arity++;
            }

            if (depth != 0)
            {
                normalized.Append(value.AsSpan(index));
                break;
            }

            normalized.Append('`').Append(arity);
            index = closing - 1;
        }
        return normalized.ToString();
    }

    internal static string? ResolveAssemblyPath(IAssemblySymbol assembly, AssemblyContext context)
    {
        var paths = ResolveAssemblyPaths(assembly, context);
        return paths.Length == 1 ? paths[0] : null;
    }

    private static string[] ResolveAssemblyPaths(IAssemblySymbol assembly, AssemblyContext context)
    {
        if (SymbolEqualityComparer.Default.Equals(assembly, context.Assembly)) return [context.Origin.CanonicalPath];
        return context.Compilation.References
            .OfType<PortableExecutableReference>()
            .Where(reference => reference.FilePath is not null)
            .Where(reference => context.Compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol referencedAssembly
                && referencedAssembly.Identity.Equals(assembly.Identity))
            .Select(reference => reference.FilePath!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsFrameworkPath(string path) =>
        path.Contains("/shared/Microsoft.NETCore.App/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("\\shared\\Microsoft.NETCore.App\\", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/reference assemblies/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("\\reference assemblies\\", StringComparison.OrdinalIgnoreCase);

    private static (string? Id, string? Version) TryGetNuGetPackage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, null);
        var segments = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 2 < segments.Length; index++)
        {
            if (index > 0
                && string.Equals(segments[index], "packages", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[index - 1], ".nuget", StringComparison.OrdinalIgnoreCase))
            {
                return (segments[index + 1], segments[index + 2]);
            }
        }
        return (null, null);
    }
}
