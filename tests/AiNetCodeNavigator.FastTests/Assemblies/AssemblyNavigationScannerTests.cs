#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Assemblies;

// @covers AssemblyContextScanner
// @covers AssemblySearchScanner
// @covers FindAssemblyExtensionsScanner
// @covers ResolveTypeOriginScanner
[Trait("Category", "Component")]
public sealed class AssemblyNavigationScannerTests
{
    [Fact]
    public async Task Context_ReturnsBoundedTypeAndReferenceSummary()
    {
        using var temp = TestTempDirectory.Create("assembly-context-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "ContextProbe", """
            namespace Probe.Context;
            public sealed class First { }
            public sealed class Second { }
            """);

        var result = await AssemblyContextScanner.GetAsync(new AssemblyContextRequest(path, MaxResults: 1));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalTypes);
        Assert.Single(result.Value.Types);
        Assert.True(result.Value.Truncated);
        Assert.Equal(1, result.Value.ShownCount);
        Assert.Equal(0, result.Value.References.Count);
        Assert.True(result.Value.TotalReferenceCount > 0);
    }

    [Fact]
    public async Task Search_SupportsTextExternalCallsAndDataAccessWithResultBounds()
    {
        using var temp = TestTempDirectory.Create("assembly-search-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "SearchProbe", """
            namespace Probe.Search;
            public sealed class Searchable
            {
                public string NeedleValue = "needle";
                public const string NetworkMarker = "HttpClient";
                public void Run() { NeedleValue.Trim(); }
                public void ReadOne() { ExecuteReader(); }
                public void ReadTwo() { ExecuteReader(); }
                private void ExecuteReader() { }
            }
            """);

        var text = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(path, "NeedleValue", SearchKind: "text"));
        var external = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(path, SearchKind: "external_calls"));
        var data = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(path, SearchKind: "data_access", MaxResults: 1));
        var declaration = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(path, "ExecuteReader", SearchKind: "data_access", DeclarationOnly: true));
        var invalidRegex = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(path, "(", UseRegex: true));

        Assert.True(text.IsSuccess);
        Assert.Contains(text.Value!.Results, hit => hit.Text.Contains("NeedleValue", StringComparison.Ordinal));
        Assert.True(external.IsSuccess);
        Assert.Contains(external.Value!.Results, hit => hit.Text.Contains("HttpClient", StringComparison.Ordinal));
        Assert.True(data.IsSuccess);
        Assert.Single(data.Value!.Results);
        Assert.True(data.Value.Truncated);
        Assert.True(data.Value.TotalCount > data.Value.Results.Count);
        Assert.True(declaration.IsSuccess);
        Assert.Single(declaration.Value!.Results);
        Assert.Contains("ExecuteReader", declaration.Value.Results[0].Text, StringComparison.Ordinal);
        Assert.False(invalidRegex.IsSuccess);
        Assert.Equal(AiNetCodeNavigator.Core.Workspace.NavigationErrorCodes.InvalidArgument, invalidRegex.Error!.Value.Code);
    }

    [Fact]
    public async Task Search_AutoDetectsRegexAndAppliesFileAndDeclarationKindFilters()
    {
        using var temp = TestTempDirectory.Create("assembly-search-filters-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "SearchFilterProbe", """
            namespace Probe.Search;
            public sealed class Searchable
            {
                public string NeedleValue = "needle";
                public const string WildcardMarker = "Searchable[";
                public void Run() { NeedleValue.Trim(); }
            }
            """);

        var result = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(
            path,
            Query: "Searchable|Missing",
            FileFilter: "^.*$",
            MaxResults: 50,
            Kind: "type"));

        Assert.True(result.IsSuccess, result.Error?.ToString());
        var hit = Assert.Single(result.Value!.Results);
        Assert.Contains("Searchable", hit.Text, StringComparison.Ordinal);
        Assert.Equal("Searchable", hit.Symbol);

        var methodHeader = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(
            path, Query: "public", Kind: "method", MaxResults: 50));
        Assert.True(methodHeader.IsSuccess, methodHeader.Error?.ToString());
        var methodHit = Assert.Single(methodHeader.Value!.Results);
        Assert.Equal("Run", methodHit.Symbol);

        var wildcard = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(path, Query: "Searchable[*"));
        Assert.True(wildcard.IsSuccess, wildcard.Error?.ToString());
        Assert.Contains(wildcard.Value!.Results, match => match.Text.Contains("Searchable[", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_CursorExpiresWhenTransitiveReferenceSnapshotChanges()
    {
        using var temp = TestTempDirectory.Create("assembly-search-reference-cursor-");
        var leaf = AssemblyTestHelper.EmitAssembly(temp, "SearchCursorLeaf", "namespace Probe.Leaf; public sealed class Leaf { public int Version => 1; }");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "SearchCursorDependency", "namespace Probe.Reference; public sealed class Dependency { public Probe.Leaf.Leaf? Value; }", leaf);
        var target = AssemblyTestHelper.EmitAssembly(temp, "SearchCursorTarget", """
            public sealed class Alpha { public Probe.Reference.Dependency? Value; }
            public sealed class Beta { }
            """, dependency);

        var first = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(
            target, Query: "Alpha|Beta", UseRegex: true, Kind: "type", MaxResults: 1));
        Assert.True(first.IsSuccess, first.Error?.ToString());
        Assert.Single(first.Value!.Results);
        Assert.NotNull(first.Value.ContinuationToken);

        using var replacementTemp = TestTempDirectory.Create("assembly-search-reference-cursor-replacement-");
        var replacement = AssemblyTestHelper.EmitAssembly(replacementTemp, "SearchCursorLeaf", "namespace Probe.Leaf; public sealed class Leaf { public int Version => 2; public int Added => 3; }");
        File.Copy(replacement, leaf, overwrite: true);

        var stale = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(
            target, Query: "Alpha|Beta", UseRegex: true, Kind: "type", MaxResults: 1,
            Cursor: first.Value.ContinuationToken));

        Assert.False(stale.IsSuccess);
        Assert.Equal(AiNetCodeNavigator.Core.Workspace.NavigationErrorCodes.InvalidArgument, stale.Error!.Value.Code);
        Assert.Contains("continuationToken", stale.Error.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_DoesNotAllocateHandlesForResultsBeyondCurrentPage()
    {
        using var temp = TestTempDirectory.Create("assembly-search-page-handles-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "PagedHandleProbe", """
            namespace Probe.Search;
            public sealed class Alpha { }
            public sealed class Beta { }
            public sealed class Gamma { }
            """);
        var isolatedRegistry = new HandoffHandleRegistry();

        var first = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(
            path, Query: "Alpha|Beta|Gamma", UseRegex: true, Kind: "type", MaxResults: 1), isolatedRegistry);

        Assert.True(first.IsSuccess, first.Error?.ToString());
        var hit = Assert.Single(first.Value!.Results);
        Assert.Equal("Alpha", hit.Symbol);
        Assert.StartsWith("h:", hit.HandoffId, StringComparison.Ordinal);
        Assert.Equal(1, isolatedRegistry.Count);
        Assert.NotNull(first.Value.ContinuationToken);
    }

    [Fact]
    public async Task Search_DeclarationOnlyIncludesFieldsEventFieldsAndEnumMembers()
    {
        using var temp = TestTempDirectory.Create("assembly-search-declarations-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "DeclarationProbe", """
            namespace Probe.Declarations;
            public sealed class Searchable
            {
                public int TargetField, NeighborField;
                public event System.Action? TargetEvent, NeighborEvent;
                public void UseMembers() { _ = TargetField; _ = "TargetField"; /* TargetField */ }
            }
            public enum TargetEnum { FirstMember, TargetMember }
            """);

        var field = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(path, "TargetField", DeclarationOnly: true));
        var secondField = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(path, "NeighborField", DeclarationOnly: true));
        var eventField = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(path, "TargetEvent", DeclarationOnly: true));
        var enumMember = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(path, "TargetMember", DeclarationOnly: true));

        Assert.True(field.IsSuccess, field.Error?.ToString());
        Assert.Single(field.Value!.Results);
        Assert.Contains(field.Value.Results, hit => hit.Text.Contains("TargetField", StringComparison.Ordinal));
        Assert.True(secondField.IsSuccess, secondField.Error?.ToString());
        Assert.Contains(secondField.Value!.Results, hit => hit.Text.Contains("NeighborField", StringComparison.Ordinal));
        Assert.Equal("NeighborField", Assert.Single(secondField.Value.Results).Symbol);
        Assert.True(eventField.IsSuccess, eventField.Error?.ToString());
        Assert.Contains(eventField.Value!.Results, hit => hit.Text.Contains("TargetEvent", StringComparison.Ordinal));
        var secondEvent = await AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(path, "NeighborEvent", DeclarationOnly: true));
        Assert.True(secondEvent.IsSuccess, secondEvent.Error?.ToString());
        Assert.Equal("NeighborEvent", Assert.Single(secondEvent.Value!.Results).Symbol);
        Assert.True(enumMember.IsSuccess, enumMember.Error?.ToString());
        Assert.Contains(enumMember.Value!.Results, hit => hit.Text.Contains("TargetMember", StringComparison.Ordinal));
        Assert.Equal("TargetMember", Assert.Single(enumMember.Value!.Results).Symbol);
    }

    [Fact]
    public void Search_DeclarationSymbolSelectsMatchingSecondFieldDeclarator()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            public sealed class Probe
            {
                public int A, B;
            }
            """);
        var sourceText = tree.GetText();
        var root = tree.GetRoot();
        var match = Assert.Single(AssemblySearchScanner.FindTextLines(sourceText, root, new Regex("B", RegexOptions.NonBacktracking), declarationOnly: true));
        Assert.Equal("B", sourceText.ToString(match.DeclarationNameSpan!.Value));

        var symbol = AssemblySearchScanner.GetContainingSymbolName(root, sourceText, sourceText.Lines[match.LineNumber], match.DeclarationNameSpan);

        Assert.Equal("B", symbol);
    }

    [Fact]
    public void Search_DeclarationSymbolSelectsMatchingEnumMember()
    {
        var tree = CSharpSyntaxTree.ParseText("public enum ProbeEnum { FirstMember, BMember }");
        var sourceText = tree.GetText();
        var root = tree.GetRoot();
        var match = Assert.Single(AssemblySearchScanner.FindTextLines(sourceText, root, new Regex("BMember", RegexOptions.NonBacktracking), declarationOnly: true));
        Assert.Equal("BMember", sourceText.ToString(match.DeclarationNameSpan!.Value));

        var symbol = AssemblySearchScanner.GetContainingSymbolName(root, sourceText, sourceText.Lines[match.LineNumber], match.DeclarationNameSpan);

        Assert.Equal("BMember", symbol);
    }

    [Fact]
    public async Task Extensions_FindsMatchingReceiverAndHonorsLimit()
    {
        using var temp = TestTempDirectory.Create("assembly-extensions-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "ExtensionProbe", """
            namespace Probe.Extensions;
            public static class StringExtensions
            {
                public static string Mark(this string value) => value;
                public static int Size(this string value) => value.Length;
            }
            """);

        var result = await FindAssemblyExtensionsScanner.FindAsync(new FindAssemblyExtensionsRequest(
            path, ReceiverType: "string", MaxResults: 1));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Extensions);
        Assert.True(result.Value.TotalCount >= 2);
        Assert.True(result.Value.Truncated);
    }

    [Fact]
    public async Task TypeOrigin_ResolvesLocalAndReferencedAssemblyAndReportsMissingTypes()
    {
        using var temp = TestTempDirectory.Create("assembly-origin-");
        var generatedExternalPath = AssemblyTestHelper.EmitAssembly(temp, "ExternalOrigin", """
            namespace Probe.External;
            public sealed class ReferencedType { }
            """);
        var generatedPath = AssemblyTestHelper.EmitAssembly(temp, "OriginProbe", """
            namespace Probe.Local { public sealed class LocalType { public Probe.External.ReferencedType? Other; } }
            namespace Probe.One { public sealed class SharedType { } }
            namespace Probe.Two { public sealed class SharedType { } }
            """, generatedExternalPath);
        var packageDirectory = temp.CreateSubdirectory(Path.Combine(".nuget", "packages", "Fake.Package", "1.2.3", "lib", "net10.0"));
        var externalPath = Path.Combine(packageDirectory, "ExternalOrigin.dll");
        var path = Path.Combine(packageDirectory, "OriginProbe.dll");
        File.Move(generatedExternalPath, externalPath);
        File.Move(generatedPath, path);

        var local = await ResolveTypeOriginScanner.ResolveAsync(new ResolveTypeOriginRequest(path, "Probe.Local.LocalType"));
        var external = await ResolveTypeOriginScanner.ResolveAsync(new ResolveTypeOriginRequest(path, "Probe.External.ReferencedType"));
        var noReferences = await ResolveTypeOriginScanner.ResolveAsync(new ResolveTypeOriginRequest(path, "Probe.External.ReferencedType", IncludeReferences: false));
        var framework = await ResolveTypeOriginScanner.ResolveAsync(new ResolveTypeOriginRequest(path, "System.String"));
        var ambiguous = await ResolveTypeOriginScanner.ResolveAsync(new ResolveTypeOriginRequest(path, "SharedType"));
        var missing = await ResolveTypeOriginScanner.ResolveAsync(new ResolveTypeOriginRequest(path, "Probe.Missing.Type"));

        Assert.True(local.IsSuccess);
        Assert.Equal("local", local.Value!.OriginKind);
        Assert.True(external.IsSuccess, external.Error?.ToString());
        Assert.Equal("reference", external.Value!.OriginKind);
        Assert.Equal(Path.GetFullPath(externalPath), Path.GetFullPath(external.Value.AssemblyPath!));
        Assert.Equal("Fake.Package", external.Value.PackageId);
        Assert.Equal("1.2.3", external.Value.PackageVersion);
        Assert.False(noReferences.IsSuccess);
        Assert.True(framework.IsSuccess);
        Assert.Equal("framework", framework.Value!.OriginKind);
        Assert.True(ambiguous.IsSuccess);
        Assert.True(ambiguous.Value!.IsAmbiguous);
        Assert.False(missing.IsSuccess);
    }

    [Fact]
    public async Task TypeOrigin_ResolvesNestedTypeUsingCSharpQualifiedName()
    {
        using var temp = TestTempDirectory.Create("assembly-origin-nested-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "NestedOriginProbe", """
            namespace Probe;
            public class Outer { public class Inner { } }
            public class GenericOuter<T> { public class GenericInner<U> { } }
            """);

        var result = await ResolveTypeOriginScanner.ResolveAsync(new ResolveTypeOriginRequest(path, "Probe.Outer.Inner"));
        var generic = await ResolveTypeOriginScanner.ResolveAsync(new ResolveTypeOriginRequest(path, "Probe.GenericOuter<int>.GenericInner<string>"));

        Assert.True(result.IsSuccess, result.Error?.ToString());
        Assert.Equal("local", result.Value!.OriginKind);
        Assert.Equal(Path.GetFullPath(path), Path.GetFullPath(result.Value.AssemblyPath!));
        Assert.True(generic.IsSuccess, generic.Error?.ToString());
        Assert.Equal("local", generic.Value!.OriginKind);
    }

    [Fact]
    public void TypeOrigin_UsesFullAssemblyIdentityForSameNamedPackageVersions()
    {
        using var temp = TestTempDirectory.Create("assembly-origin-identity-");
        var packageV1 = temp.CreateSubdirectory(Path.Combine(".nuget", "packages", "shared.package", "1.0.0", "lib", "net10.0"));
        var packageV2 = temp.CreateSubdirectory(Path.Combine(".nuget", "packages", "shared.package", "2.0.0", "lib", "net10.0"));
        using var olderTemp = TestTempDirectory.Create("assembly-origin-v1-");
        using var newerTemp = TestTempDirectory.Create("assembly-origin-v2-");
        var oldGenerated = AssemblyTestHelper.EmitAssembly(olderTemp, "SharedDependency", """
            [assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
            namespace Probe.Versions; public sealed class VersionOneType { }
            """);
        var newGenerated = AssemblyTestHelper.EmitAssembly(newerTemp, "SharedDependency", """
            [assembly: System.Reflection.AssemblyVersion("2.0.0.0")]
            namespace Probe.Versions; public sealed class VersionTwoType { }
            """);
        var olderPath = Path.Combine(packageV1, "SharedDependency.dll");
        var newerPath = Path.Combine(packageV2, "SharedDependency.dll");
        File.Move(oldGenerated, olderPath);
        File.Move(newGenerated, newerPath);
        var oldMetadataReference = MetadataReference.CreateFromFile(olderPath);
        var newMetadataReference = MetadataReference.CreateFromFile(newerPath);
        var compilation = CSharpCompilation.Create("IdentityProbe", references: [oldMetadataReference, newMetadataReference]);
        var symbol = Assert.IsAssignableFrom<IAssemblySymbol>(compilation.GetAssemblyOrModuleSymbol(newMetadataReference));
        var references = new[]
        {
            new AssemblyReferenceDto("SharedDependency", "1.0.0.0", "neutral", true, olderPath),
            new AssemblyReferenceDto("SharedDependency", "2.0.0.0", "neutral", true, newerPath),
        };
        var context = new AssemblyContext(
            compilation.Assembly,
            null,
            references,
            [],
            compilation,
            new AssemblyOrigin("target", temp.GetPath("target.dll"), "", ""),
            0,
            AssemblySessionStatus.Complete);

        var resolvedPath = ResolveTypeOriginScanner.ResolveAssemblyPath(symbol, context);

        Assert.Equal(newerPath, resolvedPath);
    }

    [Fact]
    public async Task Scanners_RejectMissingAndNativeTargetsAsStructuredErrors()
    {
        using var temp = TestTempDirectory.Create("assembly-navigation-errors-");
        var missing = Path.Combine(temp.DirectoryPath, "missing.dll");
        var native = temp.GetPath("native.dll");
        File.WriteAllBytes(native, [0, 1, 2, 3, 4]);

        var missingResult = await AssemblyContextScanner.GetAsync(new AssemblyContextRequest(missing));
        var nativeResult = await AssemblyContextScanner.GetAsync(new AssemblyContextRequest(native));

        Assert.False(missingResult.IsSuccess);
        Assert.Equal(AiNetCodeNavigator.Core.Workspace.NavigationErrorCodes.InvalidArgument, missingResult.Error!.Value.Code);
        Assert.False(nativeResult.IsSuccess);
        Assert.Equal(AiNetCodeNavigator.Core.Workspace.NavigationErrorCodes.InvalidAssembly, nativeResult.Error!.Value.Code);
    }

    [Fact]
    public async Task Scanners_KeepTargetReadOnlyAndAnalyzeConcurrentRequests()
    {
        using var temp = TestTempDirectory.Create("assembly-navigation-concurrent-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "ConcurrentProbe", """
            namespace Probe.Concurrent;
            public sealed class Target { public void Read() { } }
            public static class Extensions { public static int Measure(this string value) => value.Length; }
            """);
        var originalBytes = await File.ReadAllBytesAsync(path);

        var context = AssemblyContextScanner.GetAsync(new AssemblyContextRequest(path));
        var search = AssemblySearchScanner.SearchAsync(new AssemblySearchRequest(path, "Read", SearchKind: "text"));
        var extensions = FindAssemblyExtensionsScanner.FindAsync(new FindAssemblyExtensionsRequest(path, "string"));
        var origin = ResolveTypeOriginScanner.ResolveAsync(new ResolveTypeOriginRequest(path, "Probe.Concurrent.Target"));
        await Task.WhenAll(context, search, extensions, origin);

        Assert.True((await context).IsSuccess);
        var searchResult = await search;
        Assert.True(searchResult.IsSuccess, searchResult.Error?.ToString());
        Assert.True((await extensions).IsSuccess);
        Assert.True((await origin).IsSuccess);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Context_PreservesResolvedAndMissingReferenceDiagnostics()
    {
        using var temp = TestTempDirectory.Create("assembly-navigation-missing-reference-");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "MissingDependency", """
            namespace Probe.Dependency;
            public sealed class DependencyType { }
            """);
        var path = AssemblyTestHelper.EmitAssembly(temp, "MissingReferenceProbe", """
            namespace Probe.Consumer;
            public sealed class Consumer { public Probe.Dependency.DependencyType? Value; }
            """, dependency);
        File.Delete(dependency);

        var result = await AssemblyContextScanner.GetAsync(new AssemblyContextRequest(path, IncludeReferences: true));

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!.References, reference => !reference.Resolved);
        Assert.NotEmpty(result.Value.Diagnostics);
    }

    [Fact]
    public async Task FindSymbol_RejectsOwnerWhoseClosureExceedsRootSnapshotBoundary()
    {
        using var temp = TestTempDirectory.Create("assembly-find-batch-reference-boundary-");
        var root = AssemblyTestHelper.EmitAssembly(temp, "DepthNode00", "namespace Depth; public sealed class Node00 { }");
        for (var index = 1; index <= AssemblyReferenceResolver.MaxReferenceDepth + 2; index++)
        {
            root = AssemblyTestHelper.EmitAssembly(temp, $"DepthNode{index:D2}",
                $"namespace Depth; public sealed class Node{index:D2} {{ public Node{index - 1:D2}? Value; }}", root);
        }

        var opened = await AssemblyNavigationSessionScope.OpenAsync(root, default);
        Assert.True(opened.IsSuccess, opened.Error?.ToString());
        await using var pinnedRoot = opened.Value!;
        Assert.Contains(pinnedRoot.Context.References, reference =>
            reference.SourceAssemblyPath is not null
            && Path.GetFileNameWithoutExtension(reference.SourceAssemblyPath) == "DepthNode02"
            && reference.ResolutionState == "depth_limit");

        var result = await AssemblyFindSymbolScanner.FindAsync(root, "Depth", maxResults: 100,
            includeReferences: true, pinnedRootScope: pinnedRoot);

        Assert.Null(result.Error);
        Assert.True(result.IsTruncated);
        Assert.Contains("unresolvedReferences", result.TruncatedBy);
        Assert.DoesNotContain(result.Entries, entry =>
            entry.OwnerTargetPath is not null
            && Path.GetFileNameWithoutExtension(entry.OwnerTargetPath) is "DepthNode00" or "DepthNode01");
    }

    [Fact]
    public async Task FindSymbol_RejectsOwnerWithChangedTransitiveDependency()
    {
        using var temp = TestTempDirectory.Create("assembly-find-transitive-owner-snapshot-");
        using var replacementTemp = TestTempDirectory.Create("assembly-find-transitive-owner-replacement-");
        var leaf = AssemblyTestHelper.EmitAssembly(temp, "BatchLeaf", "namespace Batch; public sealed class Leaf { public int Old => 1; }");
        var middle = AssemblyTestHelper.EmitAssembly(temp, "BatchMiddle",
            "namespace Batch; public sealed class Middle { public Leaf? Value; }", leaf);
        var root = AssemblyTestHelper.EmitAssembly(temp, "BatchRoot",
            "namespace Batch; public sealed class Root { public Middle? Value; }", middle);
        var replacement = AssemblyTestHelper.EmitAssembly(replacementTemp, "BatchLeaf",
            "namespace Batch; public sealed class Leaf { public int New => 2; }");

        var opened = await AssemblyNavigationSessionScope.OpenAsync(root, default);
        Assert.True(opened.IsSuccess, opened.Error?.ToString());
        await using var pinnedRoot = opened.Value!;
        File.Copy(replacement, leaf, overwrite: true);

        var result = await AssemblyFindSymbolScanner.FindAsync(root, "Batch", maxResults: 100,
            includeReferences: true, pinnedRootScope: pinnedRoot);

        Assert.NotNull(result.Error);
        Assert.Equal("STALE_SNAPSHOT", result.Error?.Code);
        Assert.Contains("transitive reference snapshot", result.Error?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreCanceledNavigationRequestPropagatesCancellation()
    {
        using var temp = TestTempDirectory.Create("assembly-navigation-canceled-");
        var path = AssemblyTestHelper.EmitAssembly(temp, "CanceledProbe", "public sealed class Target { }");
        using var cancellation = new System.Threading.CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AssemblyContextScanner.GetAsync(new AssemblyContextRequest(path), cancellation.Token));
    }
}
