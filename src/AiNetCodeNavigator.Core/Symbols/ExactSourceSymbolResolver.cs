#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>Creates and resolves source references only through exact loaded project owners.</summary>
public static class ExactSourceSymbolResolver
{
    public static async Task<Result<StableSymbolReference.Source>> CreateReferenceAsync(
        Solution solution,
        Project project,
        ISymbol symbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(symbol);

        project = solution.GetProject(project.Id) ?? project;

        var compilationResult = await GetCompilationAsync(project, cancellationToken).ConfigureAwait(false);
        if (!compilationResult.IsSuccess)
        {
            return Result<StableSymbolReference.Source>.Failure(compilationResult.Error!);
        }
        var compilation = compilationResult.Value!;

        var ownerTrees = await GetOwnerSyntaxTreesAsync(project, cancellationToken).ConfigureAwait(false);
        return CreateReference(solution, project, compilation, ownerTrees, symbol);
    }

    /// <summary>Creates a source reference from a request-owned current compilation context.</summary>
    internal static Result<StableSymbolReference.Source> CreateReference(
        Solution solution,
        Project project,
        Compilation compilation,
        IReadOnlySet<SyntaxTree> ownerTrees,
        ISymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(ownerTrees);
        ArgumentNullException.ThrowIfNull(symbol);

        var currentProject = solution.GetProject(project.Id);
        var loaded = currentProject is not null;
        project = currentProject ?? project;
        var hasCoordinate = SourceReferenceOwner.TryCreateProjectCoordinate(solution.FilePath, project.FilePath, out var projectPath, out var ownerReason);
        if (!loaded || !hasCoordinate)
        {
            var detail = ownerReason.Length == 0 ? "The project is not loaded in the selected solution." : ownerReason;
            return Result<StableSymbolReference.Source>.Failure(
                NavigationErrorCodes.UnsupportedIdentifier,
                $"A stable source reference cannot be created: {detail}",
                "Select or load the intended project in the current solution and retry. If its path cannot be represented as a stable project coordinate, use a raw source file location.");
        }

        var owners = FindProjectsAtPath(solution, project.FilePath!);
        if (owners.Count != 1)
        {
            return Result<StableSymbolReference.Source>.Failure(
                NavigationErrorCodes.AmbiguousSymbol,
                BuildAmbiguityMessage(owners),
                "Load one intended project context in the selected solution and retry.");
        }

        var declaration = Normalize(symbol);
        if (declaration.IsImplicitlyDeclared || declaration is IMethodSymbol { MethodKind: MethodKind.LocalFunction })
        {
            return Result<StableSymbolReference.Source>.Failure(
                NavigationErrorCodes.UnsupportedIdentifier,
                "Implicit declarations and local functions do not have supported stable declaration references.",
                "Use the declaration's raw source location because Roslyn does not provide a stable reference for it.");
        }

        if (!SymbolEqualityComparer.Default.Equals(declaration.ContainingAssembly, compilation.Assembly)
            || !declaration.DeclaringSyntaxReferences.Any(reference => ownerTrees.Contains(reference.SyntaxTree)))
        {
            return Result<StableSymbolReference.Source>.Failure(
                NavigationErrorCodes.TargetMismatch,
                "The symbol is not declared by the selected loaded source project.",
                "Select the intended loaded source project that owns this declaration in the current solution, then retry.");
        }

        var declarationId = DocumentationCommentId.CreateDeclarationId(declaration);
        if (!StableSymbolReferenceCodec.IsCanonicalDeclarationId(declarationId ?? string.Empty))
        {
            return Result<StableSymbolReference.Source>.Failure(
                NavigationErrorCodes.UnsupportedIdentifier,
                "Roslyn did not produce a supported declaration ID for this source symbol.",
                "Use the declaration's raw source location because it has no stable declaration ID.");
        }

        var reference = new StableSymbolReference.Source(projectPath, declarationId!);
        if (!StableSymbolReferenceCodec.TryFormatCanonical(reference, out _, out var codecError))
        {
            return Result<StableSymbolReference.Source>.Failure(
                NavigationErrorCodes.UnsupportedIdentifier,
                codecError?.Message ?? "The source declaration could not be encoded as a canonical reference.",
                "Rediscover the declaration and use the canonical reference returned by the tool.");
        }

        var resolved = ResolveInOwner(compilation, ownerTrees, declarationId!);
        if (!resolved.IsSuccess)
        {
            return Result<StableSymbolReference.Source>.Failure(resolved.Error!);
        }
        if (resolved.Value is null || !SymbolEqualityComparer.Default.Equals(declaration, resolved.Value))
        {
            return Result<StableSymbolReference.Source>.Failure(
                NavigationErrorCodes.UnsupportedIdentifier,
                "The source declaration ID does not round-trip to the exact selected Roslyn symbol.",
                "Use the symbol's raw source location and select a declaration that has a unique stable ID.");
        }

        return Result<StableSymbolReference.Source>.Success(reference);
    }

    public static async Task<Result<ISymbol>> ResolveAsync(
        Solution solution,
        StableSymbolReference.Source reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(reference);
        if (!StableSymbolReferenceCodec.TryFormatCanonical(reference, out _, out var codecError))
        {
            return Result<ISymbol>.Failure(codecError ?? new ResultError(
                NavigationErrorCodes.InvalidSymbolReference,
                "The typed source reference is malformed or noncanonical."));
        }

        if (!SourceReferenceOwner.TryResolveLoadedProjectPath(solution.FilePath, reference.ProjectPath, out var expectedPath, out var ownerReason))
        {
            return Result<ISymbol>.Failure(
                NavigationErrorCodes.UnsupportedIdentifier,
                $"The source owner path could not be established: {ownerReason}",
                "Correct the nearest .git owner metadata or select a solution with a stable project coordinate; use a raw source file location when no stable coordinate can be formed.");
        }

        var projects = solution.Projects.Where(project => SamePath(project.FilePath, expectedPath)).ToArray();
        if (projects.Length == 0)
        {
            return Result<ISymbol>.Failure(
                NavigationErrorCodes.TargetMismatch,
                "The canonical source owner path does not identify a project loaded in the selected solution.",
                "Open the intended solution containing a project at this reference's canonical owner coordinate, then rediscover the declaration there; do not choose a project by name alone.");
        }

        if (projects.Length > 1)
        {
            return Result<ISymbol>.Failure(
                NavigationErrorCodes.AmbiguousSymbol,
                BuildAmbiguityMessage(projects),
                "Load one intended project context in the selected solution and retry.");
        }

        var owner = projects[0];
        var compilationResult = await GetCompilationAsync(owner, cancellationToken).ConfigureAwait(false);
        if (!compilationResult.IsSuccess)
        {
            return Result<ISymbol>.Failure(compilationResult.Error!);
        }
        var compilation = compilationResult.Value!;

        var result = await ResolveInOwnerAsync(solution, owner, compilation, reference.DeclarationId, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess) return result;
        return Result<ISymbol>.Failure(result.Error!);
    }

    private static async Task<Result<ISymbol>> ResolveInOwnerAsync(
        Solution solution,
        Project project,
        Compilation compilation,
        string declarationId,
        CancellationToken cancellationToken)
    {
        var ownerTrees = await GetOwnerSyntaxTreesAsync(project, cancellationToken).ConfigureAwait(false);
        return ResolveInOwner(compilation, ownerTrees, declarationId);
    }

    private static Result<ISymbol> ResolveInOwner(
        Compilation compilation,
        IReadOnlySet<SyntaxTree> ownerTrees,
        string declarationId)
    {
        var symbols = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, compilation)
            .Select(Normalize)
            .Where(symbol => SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly)
                && symbol.DeclaringSyntaxReferences.Any(reference => ownerTrees.Contains(reference.SyntaxTree))
                && string.Equals(DocumentationCommentId.CreateDeclarationId(symbol), declarationId, StringComparison.Ordinal))
            .Distinct(SymbolEqualityComparer.Default)
            .ToArray();

        return symbols.Length switch
        {
            1 => Result<ISymbol>.Success(symbols[0]),
            0 => Result<ISymbol>.Failure(
                NavigationErrorCodes.SymbolNotFound,
                "The exact declaration ID does not resolve in the selected source project.",
                "Rediscover the current declaration in this project and use its returned reference; verify the owner's path and declaration signature."),
            _ => Result<ISymbol>.Failure(
                NavigationErrorCodes.AmbiguousSymbol,
                "The declaration ID resolves to multiple declarations in the selected source project.",
                "A stable reference requires a unique exact source owner and declaration ID. If uniqueness cannot be established, navigate independently using the raw source location."),
        };
    }

    internal static async Task<HashSet<SyntaxTree>> GetOwnerSyntaxTreesAsync(Project project, CancellationToken cancellationToken)
    {
        var ownerTrees = new HashSet<SyntaxTree>();
        foreach (var document in project.Documents)
        {
            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (tree is not null) ownerTrees.Add(tree);
        }

        foreach (var document in await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false))
        {
            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (tree is not null) ownerTrees.Add(tree);
        }
        return ownerTrees;
    }

    internal static async Task<Dictionary<SyntaxTree, SourceGeneratedDocument>> GetSourceGeneratedDocumentOwnersAsync(
        Solution solution,
        CancellationToken cancellationToken)
    {
        var owners = new Dictionary<SyntaxTree, SourceGeneratedDocument>();
        foreach (var project in solution.Projects)
        {
            if (project.Language != LanguageNames.CSharp) continue;
            foreach (var document in await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false))
            {
                var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
                if (tree is not null) owners.TryAdd(tree, document);
            }
        }

        return owners;
    }

    private static ISymbol Normalize(ISymbol symbol) => symbol is IMethodSymbol { ReducedFrom: { } reduced }
        ? reduced.OriginalDefinition
        : symbol.OriginalDefinition;

    private static async Task<Result<Compilation>> GetCompilationAsync(Project project, CancellationToken cancellationToken)
    {
        if (project.Language != LanguageNames.CSharp || project.ParseOptions is not CSharpParseOptions
            || project.CompilationOptions is not CSharpCompilationOptions)
        {
            return Result<Compilation>.Failure(
                NavigationErrorCodes.WorkspaceDiagnostic,
                $"Project '{project.Name}' does not have a supported C# compilation context.",
                "Correct or load the project's C# language and compilation configuration, then retry. Use a raw source location if this workspace cannot provide a C# compilation context.");
        }

        try
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            return compilation is null
                ? Result<Compilation>.Failure(NavigationErrorCodes.WorkspaceDiagnostic,
                    $"The current compilation for project '{project.Name}' is unavailable.",
                    "Correct or reload the project's compilation configuration, then retry. Use a raw source location if this workspace cannot provide a C# compilation context.")
                : Result<Compilation>.Success(compilation);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return Result<Compilation>.Failure(
                NavigationErrorCodes.WorkspaceDiagnostic,
                $"The current compilation for project '{project.Name}' is unavailable: {exception.Message}",
                "Correct or reload the project's compilation configuration, then retry. Use a raw source location if this workspace cannot provide a C# compilation context.");
        }
    }

    private static IReadOnlyList<Project> FindProjectsAtPath(Solution solution, string projectPath) =>
        solution.Projects.Where(project => SamePath(project.FilePath, projectPath)).ToArray();

    private static bool SamePath(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) return false;
        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(System.IO.Path.GetFullPath(left), System.IO.Path.GetFullPath(right), comparison);
        }
        catch (Exception exception) when (exception is ArgumentException or System.IO.IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static string BuildAmbiguityMessage(IEnumerable<Project> projects)
    {
        var contexts = projects.Select(project => new
            {
                Path = CanonicalLoadedPath(project.FilePath),
                project.Name,
                Framework = GetBuildProperty(project, "TargetFramework"),
                Configuration = GetBuildProperty(project, "Configuration"),
                Platform = GetBuildProperty(project, "Platform"),
            })
            .OrderBy(context => context.Path, StringComparer.Ordinal)
            .ThenBy(context => context.Name, StringComparer.Ordinal)
            .ThenBy(context => context.Framework, StringComparer.Ordinal)
            .ThenBy(context => context.Configuration, StringComparer.Ordinal)
            .ThenBy(context => context.Platform, StringComparer.Ordinal)
            .ToArray();

        var lines = new List<string> { "The source owner path has multiple loaded project contexts:" };
        foreach (var context in contexts.GroupBy(value => (value.Path, value.Name, value.Framework, value.Configuration, value.Platform)))
        {
            var item = context.Key;
            var multiplicity = context.Count();
            lines.Add($"- {item.Path} | project={item.Name} | TargetFramework={item.Framework} | Configuration={item.Configuration} | Platform={item.Platform}" + (multiplicity > 1 ? $" | multiplicity={multiplicity}" : string.Empty));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string GetBuildProperty(Project project, string property)
    {
        var key = "build_property." + property;
        return project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value) ? value : "unknown";
    }

    private static string CanonicalLoadedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "unknown";
        try
        {
            var canonical = System.IO.Path.GetFullPath(path);
            return OperatingSystem.IsWindows() ? canonical.ToUpperInvariant() : canonical;
        }
        catch (Exception exception) when (exception is ArgumentException or System.IO.IOException or NotSupportedException) { return "unknown"; }
    }
}
