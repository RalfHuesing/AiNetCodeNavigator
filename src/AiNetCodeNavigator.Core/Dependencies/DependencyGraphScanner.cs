#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiNetCodeNavigator.Core.Dependencies;

/// <summary>
/// Analysiert Projekt- und Namespace-Abhängigkeiten sowie typbasierte Datei-Beziehungen.
/// </summary>
public static class DependencyGraphScanner
{
    public static async Task<DependencyGraphPayload> ScanSolutionAsync(
        Solution solution,
        CancellationToken ct = default)
    {
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;

        // 1. Project Dependencies
        var projectDeps = new List<ProjectDependency>();
        foreach (var project in solution.Projects)
        {
            foreach (var pref in project.ProjectReferences)
            {
                var targetProj = solution.GetProject(pref.ProjectId);
                if (targetProj != null)
                {
                    projectDeps.Add(new ProjectDependency(project.Name, targetProj.Name));
                }
            }
        }

        // 2. Namespace & File Dependencies
        var nsMap = new Dictionary<(string From, string To), HashSet<string>>();
        var fileMap = new Dictionary<(string From, string To), HashSet<string>>();

        // Collect all types declared in this solution to exclude BCL noise
        var solutionTypes = new Dictionary<string, (string Namespace, string FilePath)>(StringComparer.Ordinal);
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var doc in project.Documents)
            {
                var syntaxTree = await doc.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
                if (syntaxTree is null) continue;
                var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
                if (semanticModel is null) continue;

                var root = await syntaxTree.GetRootAsync(ct).ConfigureAwait(false);
                var relPath = PathNormalizer.ToRelative(solutionDir, doc.FilePath ?? doc.Name);

                foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(typeDecl, ct);
                    if (symbol is INamedTypeSymbol nts)
                    {
                        var ns = nts.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                        solutionTypes[nts.ToDisplayString()] = (ns, relPath);
                    }
                }
            }
        }

        // Now scan type usages in documents
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var doc in project.Documents)
            {
                ct.ThrowIfCancellationRequested();
                var syntaxTree = await doc.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
                if (syntaxTree is null) continue;
                var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
                if (semanticModel is null) continue;

                var root = await syntaxTree.GetRootAsync(ct).ConfigureAwait(false);
                var currentFilePath = PathNormalizer.ToRelative(solutionDir, doc.FilePath ?? doc.Name);

                foreach (var node in root.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    var typeInfo = semanticModel.GetTypeInfo(node, ct);
                    var typeSymbol = typeInfo.Type as INamedTypeSymbol;
                    if (typeSymbol is null) continue;

                    var displayString = typeSymbol.ToDisplayString();
                    if (!solutionTypes.TryGetValue(displayString, out var targetInfo))
                    {
                        continue;
                    }

                    var enclosingSymbol = semanticModel.GetEnclosingSymbol(node.SpanStart);
                    var currentNs = enclosingSymbol?.ContainingNamespace?.ToDisplayString() ?? string.Empty;

                    // Namespace edge
                    if (!string.IsNullOrEmpty(currentNs) && !string.IsNullOrEmpty(targetInfo.Namespace) && currentNs != targetInfo.Namespace)
                    {
                        var nsKey = (currentNs, targetInfo.Namespace);
                        if (!nsMap.TryGetValue(nsKey, out var types))
                        {
                            types = new HashSet<string>(StringComparer.Ordinal);
                            nsMap[nsKey] = types;
                        }
                        types.Add(typeSymbol.Name);
                    }

                    // File edge
                    if (!string.IsNullOrEmpty(currentFilePath) && !string.IsNullOrEmpty(targetInfo.FilePath) && currentFilePath != targetInfo.FilePath)
                    {
                        var fileKey = (currentFilePath, targetInfo.FilePath);
                        if (!fileMap.TryGetValue(fileKey, out var fileTypes))
                        {
                            fileTypes = new HashSet<string>(StringComparer.Ordinal);
                            fileMap[fileKey] = fileTypes;
                        }
                        fileTypes.Add(typeSymbol.Name);
                    }
                }
            }
        }

        var namespaceDeps = nsMap
            .OrderBy(kv => kv.Key.From, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.To, StringComparer.Ordinal)
            .Select(kv => new NamespaceDependency(kv.Key.From, kv.Key.To, kv.Value.OrderBy(t => t, StringComparer.Ordinal).ToList()))
            .ToList();

        var fileDeps = fileMap
            .OrderBy(kv => kv.Key.From, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.To, StringComparer.Ordinal)
            .Select(kv => new FileDependency(kv.Key.From, kv.Key.To, kv.Value.OrderBy(t => t, StringComparer.Ordinal).ToList()))
            .ToList();

        return new DependencyGraphPayload(
            ProjectDependencies: projectDeps,
            NamespaceDependencies: namespaceDeps,
            FileDependencies: fileDeps);
    }
}
