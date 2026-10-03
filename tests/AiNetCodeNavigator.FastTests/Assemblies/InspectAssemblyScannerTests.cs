#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Fixtures;
using Microsoft.CodeAnalysis;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Assemblies;

// @covers InspectAssemblyScanner
// @covers AssemblyPaging
// @covers AssemblyAnalysisSession
[Trait("Category", "Component")]
public sealed class InspectAssemblyScannerTests
{
    [Fact]
    public async Task InspectAssembly_ReturnsPublicApiWithOverloadsGenericsAndAttributes()
    {
        using var temp = TestTempDirectory.Create("assembly-inspect-api-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "ApiProbe", """
            using System;
            namespace Probe.Api;
            [Obsolete]
            public sealed class PublicApi
            {
                public string Name { get; set; } = "";
                public event EventHandler? Changed;
                public int Convert(string value) => value.Length;
                public int Convert(int value) => value;
                public T Echo<T>(T value) where T : class => value;
                private void Hidden() { }
            }
            """);

        var result = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            Namespace: "Probe.Api",
            TypeName: "PublicApi",
            PublicOnly: true,
            MaxResults: 100));

        Assert.True(result.IsSuccess);
        var payload = result.Value!;
        Assert.Equal("complete", payload.Completeness);
        var apiType = Assert.Single(payload.Types);
        Assert.True(apiType.Handoff);
        Assert.NotNull(apiType.Id);
        Assert.StartsWith("h:", apiType.HandoffId, StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(assemblyPath), apiType.OwnerTargetPath);
        Assert.DoesNotContain("i:", apiType.HandoffId, StringComparison.Ordinal);
        var exposedHandle = apiType.HandoffId!;
        var restored = AiNetCodeNavigator.Core.Symbols.HandoffHandleRegistry.Default.RestoreInternalHandoffForInput(exposedHandle);
        Assert.True(restored.IsSuccess);
        Assert.Equal(apiType.Id, restored.Value);
        Assert.True(AiNetCodeNavigator.Core.Symbols.SymbolHandoffIdentifier.TryParse(restored.Value!, out var parsedHandoff));
        Assert.Equal(AiNetCodeNavigator.Core.Symbols.SymbolHandoffOrigin.Assembly, parsedHandoff.Origin);
        Assert.True(AiNetCodeNavigator.Core.Symbols.SymbolHandoffToken.TryCreateTarget(assemblyPath, out var expectedTarget));
        Assert.Equal(expectedTarget, parsedHandoff.TargetToken);
        Assert.Contains(apiType.Members, member => member.Name == "Name");
        Assert.Contains(apiType.Members, member => member.Name == "Changed");
        Assert.Contains(apiType.Members, member => member.Signature.Contains("Convert(string value)", StringComparison.Ordinal));
        Assert.Contains(apiType.Members, member => member.Signature.Contains("Convert(int value)", StringComparison.Ordinal));
        Assert.Contains(apiType.Members, member => member.Signature.Contains("Echo<T>(T value)", StringComparison.Ordinal));
        Assert.DoesNotContain(apiType.Members, member => member.Name is "get_Name" or "set_Name" or "add_Changed" or "Hidden");
    }

    [Fact]
    public async Task StableId_InvalidAssemblyIdentityDoesNotExposeRawSymbolIdAsHandoff()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var compilation = await fixture.Solution.Projects.First().GetCompilationAsync();
        Assert.NotNull(compilation);
        var symbol = compilation.GetTypeByMetadataName("SampleNamespace.Greeter");
        Assert.NotNull(symbol);

        var stableId = typeof(InspectAssemblyScanner).GetMethod(
            "StableId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(stableId);
        var id = (string?)stableId!.Invoke(null, [symbol, AnalysisSymbolIdentity.ForAssembly(string.Empty, "invalid")]);

        Assert.Null(id);

        var toTypeDto = typeof(InspectAssemblyScanner).GetMethod(
            "ToTypeDto",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(toTypeDto);
        var dto = Assert.IsType<AssemblyTypeDto>(toTypeDto!.Invoke(null,
        [
            symbol,
            new InspectAssemblyRequest(@"C:\invalid.dll", PublicOnly: false),
            AnalysisSymbolIdentity.ForAssembly(string.Empty, "invalid"),
            @"C:\invalid.dll",
        ]));
        Assert.False(dto.Handoff);
        Assert.Null(dto.Id);
        Assert.Empty(dto.AllowedFollowUpTools!);
    }

    [Fact]
    public async Task InspectAssembly_UsesResultLimitAndIgnoresUnrelatedInvalidDlls()
    {
        using var temp = TestTempDirectory.Create("assembly-inspect-limit-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "LimitedProbe", """
            namespace Probe;
            public sealed class First { }
            public sealed class Second { }
            """);
        File.WriteAllBytes(temp.GetPath("unrelated.dll"), [0, 1, 2, 3]);

        var result = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            PublicOnly: true,
            MaxResults: 1));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Types);
        Assert.Equal(2, result.Value.TotalTypes);
        Assert.True(result.Value.Truncated);
        Assert.StartsWith("v1.1.", result.Value.ResultCursor, StringComparison.Ordinal);
        Assert.Equal("complete", result.Value.Completeness);
    }

    [Fact]
    public async Task InspectAssembly_CursorReturnsTheNextStablePage()
    {
        using var temp = TestTempDirectory.Create("assembly-inspect-paging-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "PagingProbe", """
            namespace Probe;
            public sealed class Alpha { }
            public sealed class Beta { }
            public sealed class Gamma { }
            """);

        var first = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            PublicOnly: true,
            MaxResults: 1));

        Assert.True(first.IsSuccess);
        var firstToken = first.Value!.ResultCursor;

        var second = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            PublicOnly: true,
            MaxResults: 1,
            Cursor: firstToken));

        Assert.True(second.IsSuccess);
        Assert.Equal("Alpha", first.Value.Types.Single().Name);
        Assert.Equal("Beta", second.Value!.Types.Single().Name);
        Assert.Equal(3, second.Value.TotalTypes);
        Assert.StartsWith("v1.2.", second.Value.ResultCursor, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectAssembly_RejectsUnboundOrQueryMismatchedContinuation()
    {
        using var temp = TestTempDirectory.Create("assembly-inspect-unbound-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "UnboundProbe", """
            namespace Probe;
            public sealed class First { }
            public sealed class Second { }
            """);

        var first = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            PublicOnly: true,
            MaxResults: 1));
        Assert.True(first.IsSuccess);
        var firstToken = first.Value!.ResultCursor;

        var unbound = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            PublicOnly: true,
            MaxResults: 1,
            Cursor: "1"));
        Assert.False(unbound.IsSuccess);
        Assert.Equal(NavigationErrorCodes.InvalidArgument, unbound.Error!.Value.Code);

        var changedQuery = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            TypeName: "ChangedProbe",
            PublicOnly: true,
            MaxResults: 1,
            Cursor: firstToken));
        Assert.False(changedQuery.IsSuccess);
        Assert.Equal(NavigationErrorCodes.StaleSnapshot, changedQuery.Error!.Value.Code);
    }

    [Fact]
    public async Task InspectAssembly_CursorExpiresWhenTransitiveReferenceSnapshotChanges()
    {
        using var temp = TestTempDirectory.Create("assembly-inspect-reference-cursor-");
        var leaf = AssemblyTestHelper.EmitAssembly(temp, "CursorLeaf", "namespace Probe.Leaf; public sealed class Leaf { public int Version => 1; }");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "CursorDependency", "namespace Probe.Reference; public sealed class Dependency { public Probe.Leaf.Leaf? Value; }", leaf);
        var target = AssemblyTestHelper.EmitAssembly(temp, "CursorTarget", "public sealed class Alpha { public Probe.Reference.Dependency? Value; } public sealed class Beta { }", dependency);

        var first = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(target, MaxResults: 1));
        Assert.True(first.IsSuccess, first.Error?.ToString());
        Assert.NotNull(first.Value!.ResultCursor);
        var originalGeneration = first.Value.Generation;

        using var replacementTemp = TestTempDirectory.Create("assembly-inspect-reference-cursor-replacement-");
        var replacement = AssemblyTestHelper.EmitAssembly(replacementTemp, "CursorLeaf", "namespace Probe.Leaf; public sealed class Leaf { public int Version => 2; public int Added => 3; }");
        File.Copy(replacement, leaf, overwrite: true);

        var stale = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            target,
            MaxResults: 1,
            Cursor: first.Value.ResultCursor));

        Assert.False(stale.IsSuccess);
        Assert.Equal(NavigationErrorCodes.StaleSnapshot, stale.Error!.Value.Code);
        Assert.True(stale.Error.Value.Message.Contains("resultCursor", StringComparison.Ordinal));
        Assert.True(originalGeneration > 0);
    }

    [Fact]
    public async Task InspectAssembly_RejectsRelativeAndMissingPathsWithoutRuntimeLoading()
    {
        var relative = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            "relative.dll",
            PublicOnly: true,
            MaxResults: 100));
        Assert.False(relative.IsSuccess);
        Assert.Equal(NavigationErrorCodes.InvalidArgument, relative.Error!.Value.Code);

        using var temp = TestTempDirectory.Create("assembly-inspect-missing-");
        var missing = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            Path.Combine(temp.DirectoryPath, "missing.dll"),
            PublicOnly: true,
            MaxResults: 100));
        Assert.False(missing.IsSuccess);
        Assert.Equal(NavigationErrorCodes.InvalidArgument, missing.Error!.Value.Code);
        Assert.Contains("not found", missing.Error!.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InspectAssembly_AcceptsManagedExeWithoutExecutingIt()
    {
        using var temp = TestTempDirectory.Create("assembly-inspect-exe-");
        var assemblyPath = AssemblyTestHelper.EmitExecutable(temp, "ManagedExeProbe", """
            namespace Probe;
            public static class Program
            {
                public static void Main() { }
                public static string Describe() => "managed-exe";
            }
            """);

        var result = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            TypeName: "Program",
            PublicOnly: true,
            MaxResults: 100));

        Assert.True(result.IsSuccess);
        Assert.Equal("ManagedExeProbe", result.Value!.Identity!.Name);
        Assert.NotNull(result.Value.DecompiledSourceRoot);
        Assert.Contains("Describe", result.Value.Types.SelectMany(type => type.Members).Select(member => member.Name));
        Assert.Equal("complete", result.Value.Completeness);
    }

    [Fact]
    public async Task InspectAssembly_NativePeFailsWithTypedInvalidAssemblyDiagnostic()
    {
        var nativeAssemblyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "kernel32.dll");
        Assert.True(File.Exists(nativeAssemblyPath), $"Native PE fixture is missing: {nativeAssemblyPath}");

        var result = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            nativeAssemblyPath,
            PublicOnly: true,
            MaxResults: 100));

        Assert.False(result.IsSuccess);
        Assert.Equal(NavigationErrorCodes.InvalidAssembly, result.Error!.Value.Code);
        Assert.Contains("not a valid managed .NET assembly", result.Error!.Value.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("managed .NET .dll or .exe containing IL", result.Error!.Value.Hint ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(nativeAssemblyPath), result.Error!.Value.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetDirectoryName(nativeAssemblyPath)!, result.Error!.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InspectAssembly_CompactsNamespacesWhenOverThreshold()
    {
        using var temp = TestTempDirectory.Create("assembly-inspect-ns-");
        var code = string.Join(Environment.NewLine, Enumerable.Range(1, 12).Select(i =>
            $"namespace Ns{i:D2} {{ public sealed class Type{i} {{ }} }}"));

        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "NamespacesProbe", code);

        var result = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            PublicOnly: true,
            MaxResults: 100));

        Assert.True(result.IsSuccess);
        Assert.Equal(12, result.Value!.TotalNamespaces);
        Assert.Equal(11, result.Value.Namespaces.Count);
        Assert.Contains("Top 10 Namespaces and", result.Value.Namespaces[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectAssembly_ReturnsAllKnownMembersWithoutDroppingThem()
    {
        using var temp = TestTempDirectory.Create("assembly-inspect-maxmembers-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "MembersProbe", """
            namespace Probe;
            public sealed class ManyMembers
            {
                public void M1() { }
                public void M2() { }
                public void M3() { }
                public void M4() { }
                public void M5() { }
            }
            """);

        var result = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            TypeName: "ManyMembers",
            PublicOnly: true,
            MaxResults: 100));

        Assert.True(result.IsSuccess);
        var type = Assert.Single(result.Value!.Types);
        Assert.Equal(5, type.Members.Count);
    }

    [Fact]
    public async Task InspectAssembly_ResultCursorPagesTypesThenEveryReferenceExactlyOnce()
    {
        using var temp = TestTempDirectory.Create("assembly-inspect-reference-pages-");
        const int dependencyCount = 36;
        var dependencies = Enumerable.Range(0, dependencyCount)
            .Select(index => AssemblyTestHelper.EmitAssembly(
                temp,
                $"ReferencePagesDependency{index:D2}",
                $"namespace Probe.Reference; public sealed class Dependency{index:D2} {{ }}"))
            .ToArray();
        var fields = string.Join(Environment.NewLine, Enumerable.Range(0, dependencyCount)
            .Select(index => $"public Probe.Reference.Dependency{index:D2}? Dependency{index:D2};"));
        var target = AssemblyTestHelper.EmitAssembly(
            temp,
            "ReferencePagesProbe",
            $"namespace Probe; public sealed class Target {{ {fields} }}",
            dependencies);
        const int pageSize = 7;
        var first = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            target, MaxResults: pageSize, IncludeReferences: true));
        Assert.True(first.IsSuccess, first.Error?.ToString());
        Assert.Single(first.Value!.Types);
        Assert.Equal(pageSize - 1, first.Value.References.Count);
        var broad = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            target, MaxResults: 1_000, IncludeReferences: true));
        Assert.True(broad.IsSuccess, broad.Error?.ToString());
        Assert.Null(broad.Value!.ResultCursor);

        var allTypes = first.Value.Types.Select(type => type.Name).ToList();
        var allReferences = first.Value.References.ToList();
        var cursor = first.Value.ResultCursor;
        while (cursor is not null)
        {
            var page = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
                target, MaxResults: pageSize, IncludeReferences: true, Cursor: cursor));
            Assert.True(page.IsSuccess, page.Error?.ToString());
            allTypes.AddRange(page.Value!.Types.Select(type => type.Name));
            allReferences.AddRange(page.Value.References);
            cursor = page.Value.ResultCursor;
        }

        Assert.Equal(first.Value.TotalTypes, allTypes.Count);
        var expectedReferences = Enumerable.Range(0, dependencyCount)
            .Select(index => $"ReferencePagesDependency{index:D2}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.True(first.Value.ReferenceSummary!.TotalReferenceCount > 32);
        Assert.All(expectedReferences, expected => Assert.Contains(allReferences, reference => reference.Name == expected));
        Assert.Equal(first.Value.ReferenceSummary.TotalReferenceCount, allReferences.Count);
        Assert.Equal(
            broad.Value.References.Select(reference => (reference.Depth, reference.Name, reference.ResolvedPath)),
            allReferences.Select(reference => (reference.Depth, reference.Name, reference.ResolvedPath)));
    }

    [Fact]
    public async Task InspectAssembly_FiltersByMemberNameAndExactTypeName()
    {
        using var temp = TestTempDirectory.Create("assembly-inspect-filters-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "FiltersProbe", """
            namespace Probe;
            public sealed class TargetType
            {
                public void MethodA() { }
                public void MethodB() { }
            }
            public sealed class TargetTypeExtended
            {
                public void Other() { }
            }
            """);

        // Exact type match
        var exactTypeResult = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            TypeName: "TargetType",
            ExactTypeName: true,
            PublicOnly: true,
            MaxResults: 100));

        Assert.True(exactTypeResult.IsSuccess);
        Assert.Single(exactTypeResult.Value!.Types);
        Assert.Equal("TargetType", exactTypeResult.Value!.Types[0].Name);

        // Member name filter
        var memberFilterResult = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            TypeName: "TargetType",
            MemberName: "MethodA",
            PublicOnly: true,
            MaxResults: 100));

        Assert.True(memberFilterResult.IsSuccess);
        var members = memberFilterResult.Value!.Types[0].Members;
        Assert.Single(members);
        Assert.Equal("MethodA", members[0].Name);
    }

    [Fact]
    public async Task InspectAssembly_IncludeReferencesFlag_ControlsDetails()
    {
        using var temp = TestTempDirectory.Create("assembly-inspect-refs-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "RefsProbe", """
            namespace Probe;
            public sealed class AnyType { }
            """);

        // Reference details remain excluded by default, including with a type filter.
        var withoutRefs = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            TypeName: "AnyType"));
        Assert.True(withoutRefs.IsSuccess);
        Assert.False(withoutRefs.Value!.ReferenceDetailsIncluded);
        Assert.Empty(withoutRefs.Value.References);

        // When IncludeReferences is explicitly true
        var withRefs = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            TypeName: "AnyType",
            IncludeReferences: true));
        Assert.True(withRefs.IsSuccess);
        Assert.True(withRefs.Value!.ReferenceDetailsIncluded);
        Assert.NotEmpty(withRefs.Value!.References);
    }
}
