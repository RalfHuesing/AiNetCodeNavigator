#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
// @covers InspectAssemblyFormatter
// @covers AssemblyPaging
// @covers AssemblyAnalysisSession
[Trait("Category", "Component")]
public sealed class InspectAssemblyScannerTests
{
    private static readonly Regex ContinuationTokenPattern = new(
        "continuationToken: `(?<token>[^`]+)`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static string ExtractContinuationToken(string text)
    {
        var match = ContinuationTokenPattern.Match(text);
        Assert.True(match.Success, text);
        return match.Groups["token"].Value;
    }

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
        var text = payload.FormattedText;

        Assert.Contains("Completeness: `complete`", text, StringComparison.Ordinal);
        Assert.Contains("Source: Decompilation", text, StringComparison.Ordinal);
        Assert.Contains("`Probe.Api.PublicApi`; handoffId: `h:", text, StringComparison.Ordinal);
        var apiType = Assert.Single(payload.Types);
        Assert.True(apiType.Handoff);
        Assert.NotNull(apiType.Id);
        var exposedHandle = Regex.Match(
            text,
            @"`Probe\.Api\.PublicApi`; handoffId: `(?<id>h:[A-Za-z0-9_-]+)`",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)).Groups["id"].Value;
        var restored = AiNetCodeNavigator.Core.Symbols.HandoffHandleRegistry.Default.RestoreInternalHandoffForInput(exposedHandle);
        Assert.True(restored.IsSuccess);
        Assert.Equal(apiType.Id, restored.Value);
        Assert.True(AiNetCodeNavigator.Core.Symbols.SymbolHandoffIdentifier.TryParse(restored.Value!, out var parsedHandoff));
        Assert.Equal(AiNetCodeNavigator.Core.Symbols.SymbolHandoffOrigin.Assembly, parsedHandoff.Origin);
        Assert.True(AiNetCodeNavigator.Core.Symbols.SymbolHandoffToken.TryCreateTarget(assemblyPath, out var expectedTarget));
        Assert.Equal(expectedTarget, parsedHandoff.TargetToken);
        Assert.Contains("property: `Probe.Api.PublicApi.Name`", text, StringComparison.Ordinal);
        Assert.Contains("event: `Probe.Api.PublicApi.Changed`", text, StringComparison.Ordinal);
        Assert.Contains("Convert(string value)", text, StringComparison.Ordinal);
        Assert.Contains("Convert(int value)", text, StringComparison.Ordinal);
        Assert.Contains("Echo<T>(T value)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("get_Name", text, StringComparison.Ordinal);
        Assert.DoesNotContain("set_Name", text, StringComparison.Ordinal);
        Assert.DoesNotContain("add_Changed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden", text, StringComparison.Ordinal);
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
            50,
            AnalysisSymbolIdentity.ForAssembly(string.Empty, "invalid"),
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
        var text = result.Value!.FormattedText;
        Assert.Contains("Public API types: 1 of 2 (truncated: maxResults)", text, StringComparison.Ordinal);
        Assert.StartsWith("v1.1.", ExtractContinuationToken(text), StringComparison.Ordinal);
        Assert.Contains("Completeness: `complete`", text, StringComparison.Ordinal);
        Assert.DoesNotContain("unrelated.dll", text, StringComparison.OrdinalIgnoreCase);
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
        var firstText = first.Value!.FormattedText;
        var firstToken = ExtractContinuationToken(firstText);

        var second = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            PublicOnly: true,
            MaxResults: 1,
            Cursor: firstToken));

        Assert.True(second.IsSuccess);
        var secondText = second.Value!.FormattedText;

        Assert.Contains("`Probe.Alpha`", firstText, StringComparison.Ordinal);
        Assert.DoesNotContain("`Probe.Alpha`", secondText, StringComparison.Ordinal);
        Assert.Contains("`Probe.Beta`", secondText, StringComparison.Ordinal);
        Assert.Contains("Public API types: 1 of 3", secondText, StringComparison.Ordinal);
        Assert.StartsWith("v1.2.", ExtractContinuationToken(secondText), StringComparison.Ordinal);
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
        var firstToken = first.Value!.ContinuationToken;

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
        Assert.Equal(NavigationErrorCodes.InvalidArgument, changedQuery.Error!.Value.Code);
    }

    [Fact]
    public async Task InspectAssembly_CursorExpiresWhenTransitiveReferenceSnapshotChanges()
    {
        using var temp = TestTempDirectory.Create("assembly-inspect-reference-cursor-");
        var leaf = AssemblyTestHelper.EmitAssembly(temp, "CursorLeaf", "namespace Probe.Leaf; public sealed class Leaf { public int Version => 1; }");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "CursorDependency", "namespace Probe.Reference; public sealed class Dependency { public Probe.Leaf.Leaf? Value; }", leaf);
        var target = AssemblyTestHelper.EmitAssembly(temp, "CursorTarget", "public sealed class Alpha { public Probe.Reference.Dependency? Value; } public sealed class Beta { }", dependency);

        var first = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(target, MaxResults: 1, MaxMembers: 20));
        Assert.True(first.IsSuccess, first.Error?.ToString());
        Assert.NotNull(first.Value!.ContinuationToken);
        var originalGeneration = first.Value.Generation;

        using var replacementTemp = TestTempDirectory.Create("assembly-inspect-reference-cursor-replacement-");
        var replacement = AssemblyTestHelper.EmitAssembly(replacementTemp, "CursorLeaf", "namespace Probe.Leaf; public sealed class Leaf { public int Version => 2; public int Added => 3; }");
        File.Copy(replacement, leaf, overwrite: true);

        var stale = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            target,
            MaxResults: 1,
            MaxMembers: 20,
            Cursor: first.Value.ContinuationToken));

        Assert.False(stale.IsSuccess);
        Assert.Equal(NavigationErrorCodes.InvalidArgument, stale.Error!.Value.Code);
        Assert.True(stale.Error.Value.Message.Contains("continuationToken", StringComparison.Ordinal));
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
        var text = result.Value!.FormattedText;
        Assert.Contains("Assembly: `ManagedExeProbe`", text, StringComparison.Ordinal);
        Assert.Contains("decompileRoot:", text, StringComparison.Ordinal);
        Assert.DoesNotContain(assemblyPath, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Describe()", text, StringComparison.Ordinal);
        Assert.Contains("Completeness: `complete`", text, StringComparison.Ordinal);
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
        var text = result.Value!.FormattedText;
        Assert.Contains("Public Namespaces: 12", text, StringComparison.Ordinal);
        Assert.Contains("Top 10 Namespaces and 2 more", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectAssembly_MaxMembersLimitingTruncatesOutput()
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
            MaxResults: 100,
            MaxMembers: 2));

        Assert.True(result.IsSuccess);
        var text = result.Value!.FormattedText;
        Assert.Contains("Members 2 of 5 shown (truncated: maxMembers)", text, StringComparison.Ordinal);
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

        // When TypeName is specified, IncludeReferences defaults to false
        var withoutRefs = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            TypeName: "AnyType"));
        Assert.True(withoutRefs.IsSuccess);
        Assert.Contains("- Reference details not requested", withoutRefs.Value!.FormattedText, StringComparison.Ordinal);

        // When IncludeReferences is explicitly true
        var withRefs = await InspectAssemblyScanner.InspectAsync(new InspectAssemblyRequest(
            assemblyPath,
            TypeName: "AnyType",
            IncludeReferences: true));
        Assert.True(withRefs.IsSuccess);
        Assert.DoesNotContain("- Reference details not requested", withRefs.Value!.FormattedText, StringComparison.Ordinal);
        Assert.NotEmpty(withRefs.Value!.References);
    }
}
