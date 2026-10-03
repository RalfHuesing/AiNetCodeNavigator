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

    private static readonly string[] TestAffixes =
    [
        "Tests", "Test", "Specs", "Spec", "IntegrationTests", "IntegrationTest", "UnitTests", "UnitTest", "FastTests"
    ];

    public static Task<TestContextPayload> BuildAsync(
        ISymbol targetSymbol,
        Solution solution,
        CancellationToken ct = default,
        bool includeGenerated = false,
        SymbolScopeType scope = SymbolScopeType.All) =>
        BuildAsync(targetSymbol, solution, identity: null, ct, includeGenerated, scope);

    /// <summary>Build candidates from an already resolved symbol and the identity of its existing source snapshot.</summary>
    public static async Task<TestContextPayload> BuildAsync(
        ISymbol targetSymbol,
        Solution solution,
        AnalysisSymbolIdentity? identity,
        CancellationToken ct = default,
        bool includeGenerated = false,
        SymbolScopeType scope = SymbolScopeType.All)
    {
        ArgumentNullException.ThrowIfNull(targetSymbol);
        ArgumentNullException.ThrowIfNull(solution);

        var handoffIdentity = identity ?? await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
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
        foreach (var referencedSymbol in referenceSymbols)
        {
            if (candidateLimitReached) break;
            ct.ThrowIfCancellationRequested();
            var references = await SymbolFinder.FindReferencesAsync(referencedSymbol, solution, ct).ConfigureAwait(false);
            foreach (var reference in references.OrderBy(item => SymbolSortKey(item.Definition, solution), StringComparer.Ordinal))
            {
                var evidenceType = ReferenceRepresentsSymbol(reference.Definition, targetSymbol)
                    ? "direct-target-use"
                    : expandedImplementations.Symbols.Any(symbol => ReferenceRepresentsSymbol(reference.Definition, symbol))
                        ? "implementation-use"
                        : null;
                if (evidenceType is null) continue;
                foreach (var location in reference.Locations
                             .OrderBy(item => item.Document?.Project.FilePath, StringComparer.Ordinal)
                             .ThenBy(item => item.Document?.FilePath, StringComparer.Ordinal)
                             .ThenBy(item => item.Location.SourceSpan.Start))
                {
                    ct.ThrowIfCancellationRequested();
                    if (location.IsCandidateLocation) continue;
                    if (referenceCount >= MaxReferenceLocations)
                    {
                        referenceLimitReached = true;
                        break;
                    }
                    referenceCount++;
                    if (location.Document is not { } document) continue;
                    if (!includeGenerated && await GeneratedDocumentDetector.IsGeneratedDocumentAsync(document, ct).ConfigureAwait(false)) continue;
                    var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                    var syntaxRoot = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                    if (semanticModel is null || syntaxRoot is null) continue;
                    var referenceNode = syntaxRoot.FindNode(location.Location.SourceSpan, getInnermostNodeForTie: true);
                    if (!IsExactReferenceBinding(semanticModel, referenceNode, reference.Definition, referencedSymbol)) continue;
                    if (semanticModel?.GetEnclosingSymbol(location.Location.SourceSpan.Start) is not IMethodSymbol testMethod ||
                        !TestDetector.IsTestMethod(testMethod)) continue;

                    var testClass = testMethod.ContainingType;
                    if (testClass is null || (!TestDetector.IsTestClass(testClass) && !TestDetector.IsTestProject(document.Project))) continue;
                    var isTestDocument = TestDetector.IsTestProject(document.Project) || TestDetector.IsTestFile(document.FilePath);
                    if ((scope == SymbolScopeType.Tests && !isTestDocument) || (scope == SymbolScopeType.Production && isTestDocument)) continue;
                    if (!candidateBuilders.ContainsKey(testClass)) AddFixtureCandidate(testClass, evidence: null);

                    if (!methodsWithEvidence.TryGetValue(testMethod, out var methodEvidence))
                    {
                        methodEvidence = [];
                        methodsWithEvidence.Add(testMethod, methodEvidence);
                    }
                    var evidence = CreateEvidence(
                        evidenceType,
                        reference.Definition,
                        location.Location,
                        document,
                        solutionDir,
                        handoffIdentity,
                        solution);
                    if (!methodEvidence.Contains(evidence)) methodEvidence.Add(evidence);
                    if (candidateLimitReached) break;
                }
                if (referenceLimitReached || candidateLimitReached) break;
            }
            if (referenceLimitReached || candidateLimitReached) break;
        }

        foreach (var pair in methodsWithEvidence)
        {
            if (!candidateBuilders.TryGetValue(pair.Key.ContainingType, out var candidate)) continue;
            candidate.AddMethodEvidence(pair.Key, pair.Value);
        }

        var allCandidates = candidateBuilders.Values
            .Select(builder => builder.Build(solution, solutionDir, handoffIdentity))
            .Where(static fixture => fixture is not null)
            .Select(static fixture => fixture!)
            .OrderBy(fixture => fixture.ProjectIdentity, StringComparer.Ordinal)
            .ThenBy(fixture => PathNormalizer.NormalizeSeparators(fixture.FilePath), StringComparer.OrdinalIgnoreCase)
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
            ReturnedTestFixtures = allCandidates.Count
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
                            solutionDir, handoffIdentity, solution);
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

    private static bool IsExactReferenceBinding(
        SemanticModel semanticModel, SyntaxNode referenceNode, ISymbol definition, ISymbol referencedSymbol)
    {
        if (definition is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            if (referencedSymbol is INamedTypeSymbol type && SameSymbol(constructor.ContainingType, type))
            {
                var typeName = referenceNode.AncestorsAndSelf().OfType<TypeSyntax>()
                    .FirstOrDefault(node => node.Span.Contains(referenceNode.Span));
                return typeName is not null && semanticModel.GetTypeInfo(typeName).Type is { } boundType && SameSymbol(boundType, type);
            }

            var creation = referenceNode.AncestorsAndSelf().FirstOrDefault(node =>
                node is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax);
            return creation is not null && IsExactBinding(semanticModel, creation, definition);
        }

        var name = referenceNode.AncestorsAndSelf().OfType<SimpleNameSyntax>()
            .FirstOrDefault(node => node.Span.Contains(referenceNode.Span));
        if (name is null) return IsExactBinding(semanticModel, referenceNode, definition);

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

        if (IsExactBinding(semanticModel, name, definition)) return true;
        if (name.Parent is MemberAccessExpressionSyntax parentAccess && ReferenceEquals(parentAccess.Name, name))
            return IsExactBinding(semanticModel, parentAccess, definition);
        if (name.Parent is MemberBindingExpressionSyntax parentBinding && ReferenceEquals(parentBinding.Name, name))
            return IsExactBinding(semanticModel, parentBinding, definition);
        return false;
    }

    private static bool IsExactBinding(SemanticModel semanticModel, SyntaxNode node, ISymbol definition)
    {
        var symbol = semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is IAliasSymbol alias) symbol = alias.Target;
        return symbol is not null && SameSymbol(symbol, definition);
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
        AnalysisSymbolIdentity? identity,
        Solution solution)
    {
        var lineSpan = location.GetLineSpan();
        var path = PathNormalizer.ToRelative(solutionDir, lineSpan.Path);
        var projectIdentity = ProjectIdentity(document.Project);
        var handoff = identity is null ? null : SourceHandoffFormatter.Format(sourceSymbol, solution, identity);
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

        internal TestFixtureMatch? Build(Solution solution, string solutionDir, AnalysisSymbolIdentity? identity)
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
                var methodHandoff = SourceHandoffFormatter.Format(method, solution, identity);
                _methodEvidence.TryGetValue(method, out var evidence);
                methods.Add(new TestMethodMatch(method.Name, methodLine, methodHandoff,
                    evidence?.OrderBy(item => item.EvidenceType, StringComparer.Ordinal)
                        .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(item => item.Line).ThenBy(item => item.Column).ToArray() ?? Array.Empty<TestCandidateEvidence>()));
            }
            methods = methods.OrderBy(method => method.Line).ThenBy(method => method.MethodName, StringComparer.Ordinal).ToList();
            var framework = DetectFramework(symbol);
            var classHandoff = SourceHandoffFormatter.Format(symbol, solution, identity);
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
                SourceProjectId = document.Project.Id
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
