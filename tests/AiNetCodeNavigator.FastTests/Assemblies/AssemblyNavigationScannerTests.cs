#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Fixtures;
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
