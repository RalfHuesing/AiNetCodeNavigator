#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>Request-owned exact source owner contexts used by synchronous result projection.</summary>
internal sealed class SourceReferenceFormattingContext
{
    private sealed record Owner(Project Project, Compilation Compilation, IReadOnlySet<SyntaxTree> Trees);

    private readonly Solution solution;
    private readonly Owner[] owners;

    private SourceReferenceFormattingContext(Solution solution, Owner[] owners)
    {
        this.solution = solution;
        this.owners = owners;
    }

    internal bool IsForSolution(Solution candidate) => ReferenceEquals(solution, candidate);

    internal static async Task<SourceReferenceFormattingContext> CreateAsync(
        Solution solution,
        CancellationToken cancellationToken)
    {
        var owners = new List<Owner>();
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (project.Language != LanguageNames.CSharp || project.ParseOptions is not Microsoft.CodeAnalysis.CSharp.CSharpParseOptions
                || project.CompilationOptions is not Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions)
            {
                continue;
            }

            Compilation? compilation;
            try
            {
                compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
            {
                continue;
            }
            if (compilation is null) continue;

            var trees = await ExactSourceSymbolResolver.GetOwnerSyntaxTreesAsync(project, cancellationToken).ConfigureAwait(false);
            owners.Add(new Owner(project, compilation, trees));
        }

        return new SourceReferenceFormattingContext(solution, owners.ToArray());
    }

    internal string? Format(ISymbol? symbol)
    {
        if (symbol is null) return null;

        var declaration = symbol is IMethodSymbol { ReducedFrom: { } reduced }
            ? reduced.OriginalDefinition
            : symbol.OriginalDefinition;
        var trees = declaration.DeclaringSyntaxReferences.Select(reference => reference.SyntaxTree).ToHashSet();
        var matchingOwners = owners.Where(owner =>
                SymbolEqualityComparer.Default.Equals(declaration.ContainingAssembly, owner.Compilation.Assembly)
                && trees.Overlaps(owner.Trees))
            .ToArray();
        if (matchingOwners.Length != 1) return null;

        var owner = matchingOwners[0];
        var result = ExactSourceSymbolResolver.CreateReference(solution, owner.Project, owner.Compilation, owner.Trees, declaration);
        return result.IsSuccess ? StableSymbolReferenceCodec.Format(result.Value!) : null;
    }
}
