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
using Microsoft.CodeAnalysis.FindSymbols;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Finds source-located static test candidates from direct references and bounded implementation/name expansion.
/// Candidate evidence does not prove test execution or coverage.
/// </summary>
public static class TestRecommendationBuilder
{
    public const int MaxImplementationExpansion = 64;
    public const int MaxCandidateFixtures = 256;
    public const int MaxReferenceLocations = 4096;
    public const int MaxExpandedHelpers = 200;

    private static readonly string[] TestAffixes =
    [
        "Tests", "Test", "Specs", "Spec", "IntegrationTests", "IntegrationTest", "UnitTests", "UnitTest", "FastTests"
    ];

    public static async Task<TestContextPayload> BuildAsync(
        ISymbol targetSymbol,
        Solution solution,
        CancellationToken ct = default,
        bool includeGenerated = false,
        SymbolScopeType scope = SymbolScopeType.All,
        int testHelperDepth = 1)
    {
        ArgumentNullException.ThrowIfNull(targetSymbol);
        ArgumentNullException.ThrowIfNull(solution);
        var formatter = await SourceReferenceFormattingContext.CreateFormatterAsync(solution, ct).ConfigureAwait(false);
        return await BuildCoreAsync(targetSymbol, solution, formatter, ct, includeGenerated, scope, testHelperDepth,
            MaxExpandedHelpers, MaxReferenceLocations).ConfigureAwait(false);
    }

    internal static Task<TestContextPayload> BuildAsync(
        ISymbol targetSymbol,
        Solution solution,
        SourceIdentityRequest identityRequest,
        CancellationToken ct = default,
        bool includeGenerated = false,
        SymbolScopeType scope = SymbolScopeType.All,
        int testHelperDepth = 1,
        int maxExpandedHelpers = MaxExpandedHelpers) =>
        BuildCoreAsync(targetSymbol, solution, symbol => identityRequest.FormatHandoff(symbol, solution), ct, includeGenerated, scope,
            testHelperDepth, maxExpandedHelpers, MaxReferenceLocations);

    // Request-local test seam: production always uses the public fixed bounds.
    internal static async Task<TestContextPayload> BuildWithLimitsAsync(ISymbol targetSymbol, Solution solution,
        int testHelperDepth, int maxExpandedHelpers, int maxReferenceLocations, CancellationToken ct = default)
    {
        var formatter = await SourceReferenceFormattingContext.CreateFormatterAsync(solution, ct).ConfigureAwait(false);
        return await BuildCoreAsync(targetSymbol, solution, formatter, ct, false, SymbolScopeType.All,
            testHelperDepth, maxExpandedHelpers, maxReferenceLocations).ConfigureAwait(false);
    }

    private static async Task<TestContextPayload> BuildCoreAsync(
        ISymbol targetSymbol,
        Solution solution,
        Func<ISymbol, string?> handoffFormatter,
        CancellationToken ct,
        bool includeGenerated,
        SymbolScopeType scope,
        int testHelperDepth,
        int maxExpandedHelpers,
        int maxReferenceLocations)
    {
        ArgumentNullException.ThrowIfNull(targetSymbol);
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentOutOfRangeException.ThrowIfNegative(testHelperDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(testHelperDepth, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExpandedHelpers, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxReferenceLocations, 1);

        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        var candidateBuilders = new Dictionary<ISymbol, CandidateBuilder>(SymbolEqualityComparer.Default);
        var methodsWithEvidence = new Dictionary<IMethodSymbol, List<TestCandidateEvidence>>(SymbolEqualityComparer.Default);
        var expandedImplementations = await FindBoundedImplementationsAsync(targetSymbol, solution, ct).ConfigureAwait(false);
        var implementationLimitReached = expandedImplementations.LimitReached;
        var candidateLimitReached = false;

        await AddNameCandidatesAsync(targetSymbol, "target-name-heuristic").ConfigureAwait(false);
        foreach (var implementation in expandedImplementations.Symbols)
        {
            await AddNameCandidatesAsync(implementation, "implementation-type-name-heuristic").ConfigureAwait(false);
            if (candidateLimitReached) break;
        }

        var referenceSymbols = new List<ISymbol> { targetSymbol };
        referenceSymbols.AddRange(expandedImplementations.Symbols);

        var referenceCount = 0;
        var referenceLimitReached = false;
        var helperLimitReached = false;
        var expandedHelperCount = 0;
        var referenceCache = new Dictionary<(ProjectId? Owner, string Symbol), IReadOnlyList<BoundUse>>();
        var inspectedLocations = new Dictionary<(DocumentId, int, int, string), BoundUse?>();
        var frontier = new List<HelperCandidate>();
        foreach (var referencedSymbol in referenceSymbols)
        {
            if (candidateLimitReached || referenceLimitReached) break;
            foreach (var use in await InspectReferencesAsync(referencedSymbol, endpoints: true).ConfigureAwait(false))
            {
                var evidenceType = ReferenceRepresentsSymbol(use.Definition, targetSymbol)
                    ? "direct-target-use" : "implementation-use";
                var evidence = CreateEvidence(evidenceType, use.Definition, use.Location, use.Document, solutionDir, handoffFormatter);
                if (TestDetector.IsTestMethod(use.Caller)) AddTestEvidence(use, evidence);
                else if (testHelperDepth > 0 && IsSourceHelper(use.Caller)
                    && !referenceSymbols.Any(endpoint => SameSymbol(use.Caller, endpoint)))
                    frontier.Add(new HelperCandidate(use.Caller, use.Document.Project.Id, evidence, [CreatePathStep(use)]));
                if (candidateLimitReached) break;
            }
        }

        var expandedHelpers = new HashSet<(ProjectId Owner, string Symbol)>();
        for (var depth = 1; depth <= testHelperDepth && frontier.Count > 0 && !candidateLimitReached && !referenceLimitReached; depth++)
        {
            var next = new List<HelperCandidate>();
            foreach (var helper in frontier.OrderBy(item => PathSortKey(item.Path), StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (expandedHelpers.Contains((helper.Owner, SymbolSortKey(helper.Symbol, solution)))) continue;
                if (expandedHelperCount >= maxExpandedHelpers)
                {
                    helperLimitReached = true;
                    break;
                }
                expandedHelpers.Add((helper.Owner, SymbolSortKey(helper.Symbol, solution)));
                expandedHelperCount++;
                foreach (var use in await InspectReferencesAsync(helper.Symbol, endpoints: false, helper.Owner).ConfigureAwait(false))
                {
                    // Exact source binding alone includes method groups. Only a proven call can link to a helper.
                    if (use.RelationshipKind is not (RelationshipEvidence.Call or RelationshipEvidence.StaticVirtualOrInterfaceTarget)) continue;
                    var path = new[] { CreatePathStep(use) }.Concat(helper.Path).ToArray();
                    if (TestDetector.IsTestMethod(use.Caller))
                        AddTestEvidence(use, helper.Endpoint with { EvidenceType = "indirect-helper-use", HelperPath = path });
                    else if (depth < testHelperDepth && IsSourceHelper(use.Caller)
                        && !referenceSymbols.Any(endpoint => SameSymbol(use.Caller, endpoint))
                        && !expandedHelpers.Contains((use.Document.Project.Id, SymbolSortKey(use.Caller, solution))))
                        next.Add(new HelperCandidate(use.Caller, use.Document.Project.Id, helper.Endpoint, path));
                    if (candidateLimitReached) break;
                }
                if (candidateLimitReached || referenceLimitReached) break;
            }
            if (helperLimitReached) break;
            frontier = next;
        }

        async Task<IReadOnlyList<BoundUse>> InspectReferencesAsync(ISymbol selectedSymbol, bool endpoints, ProjectId? owner = null)
        {
            var selectedKey = (owner, SymbolSortKey(selectedSymbol, solution));
            if (referenceCache.TryGetValue(selectedKey, out var cached)) return cached;
            var uses = new List<BoundUse>();
            var references = await SymbolFinder.FindReferencesAsync(selectedSymbol, solution, ct).ConfigureAwait(false);
            foreach (var reference in references.OrderBy(item => SymbolSortKey(item.Definition, solution), StringComparer.Ordinal))
            {
                if (!ReferenceRepresentsSymbol(reference.Definition, selectedSymbol)
                    && !(endpoints && expandedImplementations.Symbols.Any(symbol => ReferenceRepresentsSymbol(reference.Definition, symbol)))) continue;
                foreach (var location in reference.Locations.OrderBy(item => item.Document?.Project.FilePath, StringComparer.Ordinal)
                             .ThenBy(item => item.Document?.FilePath, StringComparer.Ordinal).ThenBy(item => item.Location.SourceSpan.Start))
                {
                    ct.ThrowIfCancellationRequested();
                    if (location.IsCandidateLocation || location.Document is not { } document) continue;
                    var key = (document.Id, location.Location.SourceSpan.Start, location.Location.SourceSpan.Length,
                        SymbolSortKey(reference.Definition, solution));
                    if (inspectedLocations.TryGetValue(key, out var reused))
                    {
                        if (reused is not null) uses.Add(reused);
                        continue;
                    }
                    if (referenceCount >= maxReferenceLocations)
                    {
                        referenceLimitReached = true;
                        break;
                    }
                    referenceCount++;
                    inspectedLocations.Add(key, null);
                    if (!includeGenerated && await GeneratedDocumentDetector.IsGeneratedDocumentAsync(document, ct).ConfigureAwait(false)) continue;
                    var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                    var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                    if (model is null || root is null) continue;
                    var node = root.FindNode(location.Location.SourceSpan, getInnermostNodeForTie: true);
                    if (!await IsExactReferenceBindingAsync(model, node, reference.Definition, selectedSymbol, solution, ct).ConfigureAwait(false)) continue;
                    if (model.GetEnclosingSymbol(location.Location.SourceSpan.Start) is not IMethodSymbol caller) continue;
                    var use = new BoundUse(caller, reference.Definition, location.Location, document, RelationshipEvidence.Classify(node, model));
                    inspectedLocations[key] = use;
                    uses.Add(use);
                }
                if (referenceLimitReached) break;
            }
            referenceCache.Add(selectedKey, uses);
            return uses;
        }

        void AddTestEvidence(BoundUse use, TestCandidateEvidence evidence)
        {
            var testMethod = use.Caller;
            var testClass = testMethod.ContainingType;
            if (testClass is null || (!TestDetector.IsTestClass(testClass) && !TestDetector.IsTestProject(use.Document.Project))) return;
            var isTestDocument = TestDetector.IsTestProject(use.Document.Project) || TestDetector.IsTestFile(use.Document.FilePath);
            if ((scope == SymbolScopeType.Tests && !isTestDocument) || (scope == SymbolScopeType.Production && isTestDocument)) return;
            if (!candidateBuilders.ContainsKey(testClass)) AddFixtureCandidate(testClass, evidence: null);
            if (!methodsWithEvidence.TryGetValue(testMethod, out var methodEvidence))
            {
                methodEvidence = [];
                methodsWithEvidence.Add(testMethod, methodEvidence);
            }
            if (evidence.HelperPath is not null)
            {
                var previous = methodEvidence.FirstOrDefault(item => item.HelperPath is not null);
                if (previous is not null && (previous.HelperPath!.Count < evidence.HelperPath.Count
                    || previous.HelperPath.Count == evidence.HelperPath.Count
                    && StringComparer.Ordinal.Compare(PathSortKey(previous.HelperPath), PathSortKey(evidence.HelperPath)) <= 0)) return;
                methodEvidence.RemoveAll(item => item.HelperPath is not null);
            }
            if (!methodEvidence.Contains(evidence)) methodEvidence.Add(evidence);
        }

        TestHelperPathStep CreatePathStep(BoundUse use)
        {
            var span = use.Location.GetLineSpan();
            return new TestHelperPathStep(use.Caller.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                use.Definition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), use.RelationshipKind,
                use.Document.Project.FilePath ?? string.Empty, PathNormalizer.ToRelative(solutionDir, span.Path),
                span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
                handoffFormatter(use.Caller), handoffFormatter(use.Definition))
            {
                DispatchLimitation = use.RelationshipKind == RelationshipEvidence.StaticVirtualOrInterfaceTarget
                    ? "Statically selected declaration; runtime dispatch is not proven." : null
            };
        }

        foreach (var pair in methodsWithEvidence)
        {
            if (!candidateBuilders.TryGetValue(pair.Key.ContainingType, out var candidate)) continue;
            candidate.AddMethodEvidence(pair.Key, pair.Value);
        }

        var allCandidates = candidateBuilders.Values
            .Select(builder => builder.Build(solution, solutionDir, handoffFormatter))
            .Where(static fixture => fixture is not null)
            .Select(static fixture => fixture!)
            .OrderBy(fixture => fixture.Methods.SelectMany(method => method.Evidence ?? []).Select(EvidenceRank).DefaultIfEmpty(2).Min())
            .ThenBy(fixture => fixture.ProjectIdentity, StringComparer.Ordinal)
            .ThenBy(fixture => PathNormalizer.NormalizeSeparators(fixture.FilePath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(fixture => fixture.Line)
            .ThenBy(fixture => fixture.ClassName, StringComparer.Ordinal)
            .ToList();
        return new TestContextPayload(
            TargetSymbol: targetSymbol.Name,
            TargetKind: targetSymbol.Kind.ToString().ToLowerInvariant(),
            TestFixtures: allCandidates,
            TotalTestFixtures: allCandidates.Count,
            TotalTestMethods: allCandidates.Sum(fixture => fixture.Methods.Count))
        {
            ExpandedImplementationCount = expandedImplementations.Symbols.Count,
            ImplementationExpansionLimitReached = implementationLimitReached,
            CandidateExpansionLimitReached = candidateLimitReached,
            ReferenceInspectionLimitReached = referenceLimitReached,
            TestHelperDepth = testHelperDepth,
            ExpandedHelperCount = expandedHelperCount,
            HelperExpansionLimitReached = helperLimitReached
        };

        async Task AddNameCandidatesAsync(ISymbol relatedSymbol, string evidenceType)
        {
            var relatedType = relatedSymbol as INamedTypeSymbol ?? relatedSymbol.ContainingType;
            var typeName = relatedType?.Name ?? relatedSymbol.Name;
            var names = BuildCandidateTestClassNames(typeName);
            var candidates = new List<(INamedTypeSymbol Symbol, TestCandidateEvidence Evidence)>();
            foreach (var project in solution.Projects.OrderBy(item => item.FilePath ?? item.Name, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                if (compilation is null) continue;
                foreach (var name in names.Order(StringComparer.OrdinalIgnoreCase).ThenBy(value => value, StringComparer.Ordinal))
                {
                    foreach (var testClass in compilation.GetSymbolsWithName(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase), SymbolFilter.Type)
                                 .OfType<INamedTypeSymbol>())
                    {
                        if (!TestDetector.IsTestClass(testClass) && !TestDetector.IsTestProject(project)) continue;
                        var isTestDocument = TestDetector.IsTestProject(project) ||
                            testClass.DeclaringSyntaxReferences.Select(reference => solution.GetDocument(reference.SyntaxTree))
                                .Any(document => document is not null && TestDetector.IsTestFile(document.FilePath));
                        if ((scope == SymbolScopeType.Tests && !isTestDocument) || (scope == SymbolScopeType.Production && isTestDocument)) continue;
                        var syntax = testClass.DeclaringSyntaxReferences.FirstOrDefault();
                        if (syntax is null) continue;
                        var document = solution.GetDocument(syntax.SyntaxTree);
                        if (document is null || (!includeGenerated && await GeneratedDocumentDetector.IsGeneratedDocumentAsync(document, ct).ConfigureAwait(false))) continue;
                        var evidence = CreateEvidence(evidenceType, relatedSymbol, (await syntax.GetSyntaxAsync(ct).ConfigureAwait(false)).GetLocation(), document,
                            solutionDir, handoffFormatter);
                        candidates.Add((testClass, evidence));
                    }
                }
            }
            foreach (var candidate in candidates
                         .OrderBy(item => ProjectIdentity(solution.GetDocument(item.Symbol.DeclaringSyntaxReferences[0].SyntaxTree)!.Project), StringComparer.Ordinal)
                         .ThenBy(item => item.Evidence.FilePath, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Evidence.Line)
                         .ThenBy(item => item.Evidence.Column)
                         .ThenBy(item => item.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
            {
                AddFixtureCandidate(candidate.Symbol, candidate.Evidence);
                if (candidateLimitReached) return;
            }
        }

        void AddFixtureCandidate(INamedTypeSymbol testClass, TestCandidateEvidence? evidence)
        {
            if (!candidateBuilders.TryGetValue(testClass, out var builder))
            {
                builder = new CandidateBuilder(testClass);
                candidateBuilders.Add(testClass, builder);
            }
            if (evidence is not null) builder.AddFixtureEvidence(evidence);
            if (candidateBuilders.Count > MaxCandidateFixtures) candidateLimitReached = true;
        }
    }

    private sealed record BoundUse(IMethodSymbol Caller, ISymbol Definition, Location Location, Document Document, string RelationshipKind);
    private sealed record HelperCandidate(IMethodSymbol Symbol, ProjectId Owner, TestCandidateEvidence Endpoint, IReadOnlyList<TestHelperPathStep> Path);

    private static bool IsSourceHelper(IMethodSymbol method) => method.DeclaringSyntaxReferences.Length > 0
        && method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.LocalFunction;

    private static string PathSortKey(IReadOnlyList<TestHelperPathStep> path) => string.Join("|", path.Select(step =>
        $"{step.ProjectPath}|{step.FilePath}|{step.Line:D8}|{step.Column:D8}|{step.Caller}|{step.Target}"));

    private static int EvidenceRank(TestCandidateEvidence evidence) => evidence.HelperPath is not null ? 1
        : evidence.EvidenceType.EndsWith("name-heuristic", StringComparison.Ordinal) ? 2 : 0;

    private static async Task<(IReadOnlyList<ISymbol> Symbols, bool LimitReached)> FindBoundedImplementationsAsync(
        ISymbol targetSymbol, Solution solution, CancellationToken ct)
    {
        IEnumerable<ISymbol> found = [];
        if (targetSymbol is INamedTypeSymbol namedType && (namedType.TypeKind == TypeKind.Interface || namedType.IsAbstract))
        {
            found = namedType.TypeKind == TypeKind.Interface
                ? await SymbolFinder.FindImplementationsAsync(namedType, solution, cancellationToken: ct).ConfigureAwait(false)
                : await SymbolFinder.FindDerivedClassesAsync(namedType, solution, transitive: true, cancellationToken: ct).ConfigureAwait(false);
        }
        else if (targetSymbol is IMethodSymbol method &&
                 (method.ContainingType?.TypeKind == TypeKind.Interface || method.IsAbstract))
        {
            found = method.ContainingType?.TypeKind == TypeKind.Interface
                ? await SymbolFinder.FindImplementationsAsync(method, solution, cancellationToken: ct).ConfigureAwait(false)
                : await SymbolFinder.FindOverridesAsync(method, solution, cancellationToken: ct).ConfigureAwait(false);
        }
        else if (targetSymbol is IPropertySymbol property &&
                 (property.ContainingType?.TypeKind == TypeKind.Interface || property.IsAbstract))
        {
            found = property.ContainingType?.TypeKind == TypeKind.Interface
                ? await SymbolFinder.FindImplementationsAsync(property, solution, cancellationToken: ct).ConfigureAwait(false)
                : await SymbolFinder.FindOverridesAsync(property, solution, cancellationToken: ct).ConfigureAwait(false);
        }
        else if (targetSymbol is IEventSymbol eventSymbol &&
                 (eventSymbol.ContainingType?.TypeKind == TypeKind.Interface || eventSymbol.IsAbstract))
        {
            found = eventSymbol.ContainingType?.TypeKind == TypeKind.Interface
                ? await SymbolFinder.FindImplementationsAsync(eventSymbol, solution, cancellationToken: ct).ConfigureAwait(false)
                : await SymbolFinder.FindOverridesAsync(eventSymbol, solution, cancellationToken: ct).ConfigureAwait(false);
        }

        var ordered = found.Distinct(SymbolEqualityComparer.Default)
            .OrderBy(symbol => SymbolSortKey(symbol, solution), StringComparer.Ordinal)
            .ToList();
        return (ordered.Take(MaxImplementationExpansion).ToList(), ordered.Count > MaxImplementationExpansion);
    }

    private static bool SameSymbol(ISymbol left, ISymbol right) =>
        SymbolEqualityComparer.Default.Equals(left, right) ||
        SymbolEqualityComparer.Default.Equals(left.OriginalDefinition, right.OriginalDefinition);

    private static bool ReferenceRepresentsSymbol(ISymbol referenceDefinition, ISymbol selectedSymbol) =>
        SameSymbol(referenceDefinition, selectedSymbol) ||
        referenceDefinition is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor &&
        selectedSymbol is INamedTypeSymbol type && SameSymbol(constructor.ContainingType, type);

    private static async Task<bool> IsExactReferenceBindingAsync(
        SemanticModel semanticModel, SyntaxNode referenceNode, ISymbol definition, ISymbol referencedSymbol,
        Solution solution, CancellationToken ct)
    {
        if (definition is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            if (referencedSymbol is INamedTypeSymbol type && SameSymbol(constructor.ContainingType, type))
            {
                var typeName = referenceNode.AncestorsAndSelf().OfType<TypeSyntax>()
                    .FirstOrDefault(node => node.Span.Contains(referenceNode.Span));
                if (typeName is null || semanticModel.GetTypeInfo(typeName, ct).Type is not { } boundType) return false;
                var sourceType = await SymbolFinder.FindSourceDefinitionAsync(boundType, solution, ct).ConfigureAwait(false);
                return sourceType is not null && SameSymbol(sourceType, type);
            }

            var creation = referenceNode.AncestorsAndSelf().FirstOrDefault(node =>
                node is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax);
            return creation is not null && await IsExactBindingAsync(semanticModel, creation, definition, solution, ct).ConfigureAwait(false);
        }

        var name = referenceNode.AncestorsAndSelf().OfType<SimpleNameSyntax>()
            .FirstOrDefault(node => node.Span.Contains(referenceNode.Span));
        if (name is null) return await IsExactBindingAsync(semanticModel, referenceNode, definition, solution, ct).ConfigureAwait(false);

        // Bind the name at the reference itself. Walking out to an enclosing invocation can
        // incorrectly turn a type or property receiver into evidence for the outer method call.
        if (name.Parent is MemberAccessExpressionSyntax memberAccess &&
            ReferenceEquals(memberAccess.Name, name) &&
            semanticModel.GetTypeInfo(memberAccess.Expression).Type?.TypeKind == TypeKind.Error)
            return false;
        if (name.Parent is MemberBindingExpressionSyntax memberBinding &&
            ReferenceEquals(memberBinding.Name, name) &&
            memberBinding.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
            semanticModel.GetTypeInfo(conditionalAccess.Expression).Type?.TypeKind == TypeKind.Error)
            return false;

        if (await IsExactBindingAsync(semanticModel, name, definition, solution, ct).ConfigureAwait(false)) return true;
        if (name.Parent is MemberAccessExpressionSyntax parentAccess && ReferenceEquals(parentAccess.Name, name))
            return await IsExactBindingAsync(semanticModel, parentAccess, definition, solution, ct).ConfigureAwait(false);
        if (name.Parent is MemberBindingExpressionSyntax parentBinding && ReferenceEquals(parentBinding.Name, name))
            return await IsExactBindingAsync(semanticModel, parentBinding, definition, solution, ct).ConfigureAwait(false);
        return false;
    }

    private static async Task<bool> IsExactBindingAsync(
        SemanticModel semanticModel, SyntaxNode node, ISymbol definition, Solution solution, CancellationToken ct)
    {
        var symbol = semanticModel.GetSymbolInfo(node, ct).Symbol;
        if (symbol is IAliasSymbol alias) symbol = alias.Target;
        if (symbol is null) return false;
        if (symbol is IMethodSymbol { ReducedFrom: { } reduced }) symbol = reduced.OriginalDefinition;
        if (SameSymbol(symbol, definition)) return true;
        var sourceDefinition = await SymbolFinder.FindSourceDefinitionAsync(symbol, solution, ct).ConfigureAwait(false);
        return sourceDefinition is not null && SameSymbol(sourceDefinition, definition);
    }

    private static string SymbolSortKey(ISymbol symbol, Solution solution)
    {
        var source = symbol.Locations.Where(location => location.IsInSource)
            .OrderBy(location => location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(location => location.SourceSpan.Start)
            .FirstOrDefault();
        var owner = source?.SourceTree is { } tree && solution.GetDocument(tree) is { } document
            ? ProjectIdentity(document.Project)
            : symbol.ContainingAssembly?.Identity.ToString() ?? string.Empty;
        var project = source?.SourceTree?.FilePath ?? string.Empty;
        var location = source?.GetLineSpan();
        return $"{owner}|{project}|{location?.StartLinePosition.Line ?? -1:D8}|{location?.StartLinePosition.Character ?? -1:D8}|{symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";
    }

    private static TestCandidateEvidence CreateEvidence(
        string evidenceType,
        ISymbol sourceSymbol,
        Location location,
        Document document,
        string solutionDir,
        Func<ISymbol, string?> handoffFormatter)
    {
        var lineSpan = location.GetLineSpan();
        var path = PathNormalizer.ToRelative(solutionDir, lineSpan.Path);
        var projectIdentity = ProjectIdentity(document.Project);
        var handoff = handoffFormatter(sourceSymbol);
        return new TestCandidateEvidence(
            evidenceType,
            sourceSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            path,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1,
            projectIdentity,
            handoff);
    }

    private static string ProjectIdentity(Project project) =>
        $"{PathNormalizer.NormalizeSeparators(project.FilePath ?? project.Name)}::{project.AssemblyName ?? project.Name}";

    private static HashSet<string> BuildCandidateTestClassNames(string typeName)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var affix in TestAffixes)
        {
            candidates.Add($"{typeName}{affix}");
            candidates.Add($"{affix}{typeName}");
        }
        return candidates;
    }

    private sealed class CandidateBuilder(INamedTypeSymbol symbol)
    {
        private readonly List<TestCandidateEvidence> _fixtureEvidence = [];
        private readonly Dictionary<IMethodSymbol, List<TestCandidateEvidence>> _methodEvidence = new(SymbolEqualityComparer.Default);

        internal void AddFixtureEvidence(TestCandidateEvidence evidence)
        {
            if (!_fixtureEvidence.Contains(evidence)) _fixtureEvidence.Add(evidence);
        }

        internal void AddMethodEvidence(IMethodSymbol method, IEnumerable<TestCandidateEvidence> evidence)
        {
            if (!_methodEvidence.TryGetValue(method, out var items))
            {
                items = [];
                _methodEvidence.Add(method, items);
            }
            foreach (var item in evidence)
                if (!items.Contains(item)) items.Add(item);
        }

        internal TestFixtureMatch? Build(Solution solution, string solutionDir, Func<ISymbol, string?> handoffFormatter)
        {
            var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef is null) return null;
            var document = solution.GetDocument(syntaxRef.SyntaxTree);
            if (document is null) return null;
            var loc = syntaxRef.GetSyntax().GetLocation();
            var lineSpan = loc.GetLineSpan();
            var filePath = PathNormalizer.ToRelative(solutionDir, lineSpan.Path);
            var methods = new List<TestMethodMatch>();
            var useAllAttributedMethods = _fixtureEvidence.Any(evidence => evidence.EvidenceType.EndsWith("name-heuristic", StringComparison.Ordinal));
            foreach (var method in symbol.GetMembers().OfType<IMethodSymbol>().Where(TestDetector.IsTestMethod))
            {
                if (!useAllAttributedMethods && !_methodEvidence.ContainsKey(method)) continue;
                var methodLocation = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation();
                var methodLine = methodLocation?.GetLineSpan().StartLinePosition.Line + 1 ?? 0;
                var methodHandoff = handoffFormatter(method);
                _methodEvidence.TryGetValue(method, out var evidence);
                methods.Add(new TestMethodMatch(method.Name, methodLine, methodHandoff,
                    evidence?.OrderBy(EvidenceRank).ThenBy(item => item.EvidenceType, StringComparer.Ordinal)
                        .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(item => item.Line).ThenBy(item => item.Column).ToArray() ?? Array.Empty<TestCandidateEvidence>())
                {
                    QualifiedName = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    FilePath = methodLocation is null ? null : PathNormalizer.ToRelative(solutionDir, methodLocation.GetLineSpan().Path),
                    Column = methodLocation?.GetLineSpan().StartLinePosition.Character + 1 ?? 0
                });
            }
            methods = methods.OrderBy(method => (method.Evidence ?? []).Select(EvidenceRank).DefaultIfEmpty(2).Min())
                .ThenBy(method => PathNormalizer.NormalizeSeparators(method.FilePath ?? string.Empty), StringComparer.OrdinalIgnoreCase)
                .ThenBy(method => method.Line)
                .ThenBy(method => method.Column)
                .ThenBy(method => method.MethodName, StringComparer.Ordinal).ToList();
            var framework = DetectFramework(symbol);
            var classHandoff = handoffFormatter(symbol);
            var evidenceItems = _fixtureEvidence
                .OrderBy(item => item.EvidenceType, StringComparer.Ordinal)
                .ThenBy(item => item.ProjectIdentity, StringComparer.Ordinal)
                .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Line)
                .ThenBy(item => item.Column)
                .ToArray();
            return new TestFixtureMatch(
                symbol.Name,
                filePath,
                lineSpan.StartLinePosition.Line + 1,
                framework,
                methods,
                classHandoff,
                document.Project.Name,
                evidenceItems,
                ProjectIdentity(document.Project))
            {
                SourceProjectId = document.Project.Id,
                ProjectPath = document.Project.FilePath,
                QualifiedName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
            };
        }
    }

    private static string DetectFramework(INamedTypeSymbol testClass)
    {
        foreach (var member in testClass.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (var attr in member.GetAttributes())
            {
                var name = attr.AttributeClass?.Name ?? "";
                if (name.Equals("Fact", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("FactAttribute", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Theory", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TheoryAttribute", StringComparison.OrdinalIgnoreCase)) return "xUnit";
                if (name.Equals("TestMethod", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TestMethodAttribute", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("DataTestMethod", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("DataTestMethodAttribute", StringComparison.OrdinalIgnoreCase)) return "MSTest";
                if (name.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TestAttribute", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TestCase", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TestCaseAttribute", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TestCaseSource", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TestCaseSourceAttribute", StringComparison.OrdinalIgnoreCase)) return "NUnit";
            }
        }
        return "Unknown";
    }
}
