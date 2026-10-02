using System.Text;
using System.Reflection;
using AiNetCodeNavigator.Configuration;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.Tools;
using AiNetCodeNavigator.Mcp.Tools.Assemblies;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using Serilog.Core;
using Serilog.Events;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class AssemblyToolsContractTests
{
    [Fact]
    public async Task AssemblyClassStructureUsesDeclarationOrderBeforeCapAndContextAndMemberHandlesStayNavigable()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var configuration = new NavigatorHostConfiguration(
            Path.Combine(Path.GetTempPath(), "ainet-assembly-structure-contract-" + Guid.NewGuid().ToString("N") + ".json"),
            isDefaultPath: true,
            new LoggingLevelSwitch(LogEventLevel.Warning));
        Assert.True((await configuration.LoadStartupAsync(CancellationToken.None)).Succeeded);
        await using var runtime = new NavigatorHostRuntime(configuration, host.Services.GetRequiredService<IHostApplicationLifetime>());
        var structureTools = new StructureTools(runtime);
        var assemblyTools = new AssemblyTools(runtime);
        var symbolTools = new SymbolTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-structure-contract-");
        const string source = """
            namespace StructureOrderProbe;
            public sealed class OrderProbe
            {
                public int Zulu()
                {
                    var first = 1;
                    var second = first + 1;
                    var third = second + 1;
                    var fourth = third + 1;
                    var fifth = fourth + 1;
                    var sixth = fifth + 1;
                    var seventh = sixth + 1;
                    var eighth = seventh + 1;
                    return eighth;
                }

                public int Alpha() => 42;
            }
            """;
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "StructureOrderProbe", source);

        var limited = await structureTools.GetClassStructure(assemblyPath, "StructureOrderProbe.OrderProbe", maxMembers: 1);
        AssertSuccessWithinBudget(limited, 16 * 1024, 4096);
        Assert.Contains("Zulu()", TextOf(limited), StringComparison.Ordinal);
        Assert.DoesNotContain("Alpha()", TextOf(limited), StringComparison.Ordinal);
        var zuluHandoff = ReadHandoff(TextOf(limited), "Zulu");
        var zuluBody = await symbolTools.GetSymbolBody(assemblyPath, [zuluHandoff]);
        AssertSuccessWithinBudget(zuluBody, 16 * 1024, 4096);
        Assert.Contains("Zulu", TextOf(zuluBody), StringComparison.Ordinal);

        var complete = await structureTools.GetClassStructure(assemblyPath, "StructureOrderProbe.OrderProbe", maxMembers: 50);
        AssertSuccessWithinBudget(complete, 16 * 1024, 4096);
        AssertDeclarationOrder(TextOf(complete));

        var sourceRoot = Path.Combine(fixture.DirectoryPath, "source");
        Directory.CreateDirectory(sourceRoot);
        var sourceSolution = Path.Combine(sourceRoot, "OrderProbe.slnx");
        var sourceProject = Path.Combine(sourceRoot, "OrderProbe.csproj");
        await File.WriteAllTextAsync(sourceSolution, "<Solution><Project Path=\"OrderProbe.csproj\" /></Solution>");
        await File.WriteAllTextAsync(sourceProject,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "OrderProbe.cs"), source);
        var sourceStructure = await structureTools.GetClassStructure(sourceSolution, "StructureOrderProbe.OrderProbe", maxMembers: 50,
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(sourceStructure, 32768, 4096);
        AssertDeclarationOrder(TextOf(sourceStructure));
        Assert.Equal(ReadMemberNames(TextOf(sourceStructure)), ReadMemberNames(TextOf(complete)));
        var sourceNamespaceTree = await structureTools.GetNamespaceTree(sourceSolution,
            namespacePrefix: "StructureOrderProbe", maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(sourceNamespaceTree, 32768, 4096);
        var sourceNamespaceHandle = ReadAnyHandoff(TextOf(sourceNamespaceTree));
        var sourceNamespaceBody = await symbolTools.GetSymbolBody(sourceSolution, [sourceNamespaceHandle], maxResponseBytes: 32768,
            maxResponseTokens: 4096);
        AssertSuccessWithinBudget(sourceNamespaceBody, 32768, 4096);
        Assert.Contains("OrderProbe", TextOf(sourceNamespaceBody), StringComparison.Ordinal);

        var context = await assemblyTools.GetAssemblyContext(assemblyPath, "StructureOrderProbe.OrderProbe",
            includeClassStructure: true, maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(context, 65536, 4096);
        var contextText = TextOf(context);
        var classStructure = contextText[(contextText.IndexOf("## Class Structure", StringComparison.Ordinal))..];
        AssertDeclarationOrder(classStructure);
        var contextHandoff = ReadHandoff(classStructure, "Zulu");
        var contextBody = await symbolTools.GetSymbolBody(assemblyPath, [contextHandoff]);
        AssertSuccessWithinBudget(contextBody, 16 * 1024, 4096);
        Assert.Contains("Zulu", TextOf(contextBody), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssemblyNavigationHandlersReturnOwnerResultsAcrossAllSeventeenRoutes()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var configuration = new NavigatorHostConfiguration(
            Path.Combine(Path.GetTempPath(), "ainet-assembly-routes-" + Guid.NewGuid().ToString("N") + ".json"),
            isDefaultPath: true,
            new LoggingLevelSwitch(LogEventLevel.Warning));
        Assert.True((await configuration.LoadStartupAsync(CancellationToken.None)).Succeeded);
        await using var runtime = new NavigatorHostRuntime(configuration, host.Services.GetRequiredService<IHostApplicationLifetime>());
        var symbols = new SymbolTools(runtime);
        var structure = new StructureTools(runtime);
        var relationships = new RelationshipTools(runtime);
        var assemblies = new AssemblyTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-routes-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "AssemblyRouteProbe", """
            namespace AssemblyRouteProbe;
            public interface IReadable { int Read(); string Label { get; } }
            public class BaseProbe { public virtual int Value() => 1; public virtual string Label => "base"; }
            public sealed class Probe : BaseProbe, IReadable
            {
                public override int Value() => 2;
                public override string Label => "probe";
                public int Read() => Value();
                public int Entry() => Read();
            }
            public static class ProbeExtensions
            {
                public static int Twice(this Probe value) => value.Read() * 2;
                public static int Thrice(this Probe value) => value.Read() * 3;
            }
            """);

        var foundEntry = await symbols.FindSymbol(assemblyPath, pattern: "Entry", kind: "method", maxResponseBytes: 32768);
        AssertOwnerResult(foundEntry, "Entry");
        var entryHandle = ReadAnyHandoff(TextOf(foundEntry));
        var foundType = await symbols.FindSymbol(assemblyPath, pattern: "AssemblyRouteProbe.Probe", kind: "class", maxResponseBytes: 32768);
        AssertOwnerResult(foundType, "Probe");
        var typeHandle = ReadHandoff(TextOf(foundType), "class Probe");
        var foundInterface = await symbols.FindSymbol(assemblyPath, pattern: "IReadable", kind: "interface", maxResponseBytes: 32768);
        AssertOwnerResult(foundInterface, "IReadable");
        var interfaceHandle = ReadAnyHandoff(TextOf(foundInterface));
        var foundRead = await symbols.FindSymbol(assemblyPath, pattern: "Probe.Read", kind: "method", maxResponseBytes: 32768);
        AssertOwnerResult(foundRead, "Probe.Read");
        var readHandle = ReadHandoff(TextOf(foundRead), "Probe.Read");

        AssertOwnerResult(await symbols.GetSymbolBody(assemblyPath, [entryHandle], maxResponseBytes: 32768), "Entry");
        var tree = await structure.GetFileTree(assemblyPath, view: "files", maxResults: 20, maxResponseBytes: 32768);
        AssertOwnerResult(tree, "AssemblyRouteProbe");
        var filePath = TextOf(tree).Split('\n')
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line[2..].Trim())
            .First(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
        var skeleton = await structure.GetFileSkeleton(assemblyPath, [filePath], maxResponseBytes: 32768);
        AssertOwnerResult(skeleton, "Probe");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, skeleton);
        AssertOwnerResult(await structure.GetClassStructure(assemblyPath, typeHandle, maxResponseBytes: 32768), "Entry");
        var namespaceTree = await structure.GetNamespaceTree(assemblyPath, namespacePrefix: "AssemblyRouteProbe", maxResponseBytes: 32768);
        AssertOwnerResult(namespaceTree, "Probe");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, namespaceTree);

        var callTree = await relationships.GetCallTree(assemblyPath, entryHandle, direction: "outgoing", maxResponseBytes: 32768);
        AssertOwnerResult(callTree, "Entry");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, callTree);
        var mermaidCallTree = await relationships.GetCallTree(assemblyPath, entryHandle, direction: "outgoing",
            format: "mermaid", maxResponseBytes: 32768);
        AssertOwnerResult(mermaidCallTree, "flowchart TD");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, mermaidCallTree);
        AssertOwnerResult(await relationships.FindReferences(assemblyPath, "M:AssemblyRouteProbe.Probe.Read", maxResponseBytes: 32768), "Entry");
        var hierarchy = await relationships.GetTypeHierarchy(assemblyPath, typeHandle, maxResponseBytes: 32768);
        AssertOwnerResult(hierarchy, "Probe");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, hierarchy);
        var implementations = await relationships.FindImplementations(assemblyPath, interfaceHandle, maxResponseBytes: 32768);
        AssertOwnerResult(implementations, "Probe");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, implementations);
        var methodOverrides = await relationships.FindImplementations(assemblyPath, "M:AssemblyRouteProbe.BaseProbe.Value", maxResponseBytes: 32768);
        AssertOwnerResult(methodOverrides, "Value");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, methodOverrides);
        var propertyOverrides = await relationships.FindImplementations(assemblyPath, "P:AssemblyRouteProbe.BaseProbe.Label", maxResponseBytes: 32768);
        AssertOwnerResult(propertyOverrides, "Label");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, propertyOverrides);
        var impact = await relationships.GetImpact(assemblyPath, "M:AssemblyRouteProbe.Probe.Read", maxResponseBytes: 32768);
        AssertOwnerResult(impact, "Entry");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, impact);
        var impactOpaque = await relationships.GetImpact(assemblyPath, readHandle, maxResponseBytes: 32768);
        var impactQualified = await relationships.GetImpact(assemblyPath, "AssemblyRouteProbe.Probe.Read", includeReferences: false, maxResponseBytes: 32768);
        var impactPosition = await relationships.GetImpact(assemblyPath, "Probe.cs:" + ReadPosition(TextOf(foundRead)), maxResponseBytes: 32768);
        Assert.Equal(TextOf(impact), TextOf(impactOpaque));
        Assert.Equal(TextOf(impact), TextOf(impactQualified));
        Assert.Equal(TextOf(impact), TextOf(impactPosition));
        var dependency = await relationships.DependencyGraph(assemblyPath, symbolIdentifier: typeHandle, maxResponseBytes: 32768);
        AssertOwnerResult(dependency, "Probe");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, dependency);
        AssertOwnerResult(await relationships.ResolveTypeOrigin(assemblyPath, typeName: "AssemblyRouteProbe.Probe", maxResponseBytes: 32768), "AssemblyRouteProbe");

        AssertOwnerResult(await assemblies.GetAssemblyContext(assemblyPath, "AssemblyRouteProbe.Probe",
            maxResponseBytes: 65536, maxResponseTokens: 4096), "AssemblyRouteProbe");
        var inspectDefault = await assemblies.InspectAssembly(assemblyPath, typeName: "Probe", maxResponseBytes: 32768);
        var inspectZero = await assemblies.InspectAssembly(assemblyPath, typeName: "Probe", maxResults: 0, maxMembers: 0, maxResponseBytes: 32768);
        AssertOwnerResult(inspectDefault, "Probe");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, inspectDefault);
        Assert.Equal(TextOf(inspectDefault), TextOf(inspectZero));
        var searchDefault = await assemblies.SearchAssembly(assemblyPath, pattern: "Entry", declarationOnly: true,
            kind: "method", maxResponseBytes: 32768);
        var searchZero = await assemblies.SearchAssembly(assemblyPath, pattern: "Entry", declarationOnly: true,
            kind: "method", maxResults: 0, maxFiles: 0, maxResponseBytes: 32768);
        AssertOwnerResult(searchDefault, "Entry");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, searchDefault);
        Assert.Equal(TextOf(searchDefault), TextOf(searchZero));
        var extensionDefault = await assemblies.FindAssemblyExtensions(assemblyPath, receiverType: "", maxResponseBytes: 32768);
        var extensionZero = await assemblies.FindAssemblyExtensions(assemblyPath, receiverType: "", maxResults: 0, maxResponseBytes: 32768);
        AssertOwnerResult(extensionDefault, "Twice");
        Assert.Contains("Thrice", TextOf(extensionDefault), StringComparison.Ordinal);
        Assert.Equal(TextOf(extensionDefault), TextOf(extensionZero));
        var filteredExtensions = await assemblies.FindAssemblyExtensions(assemblyPath, receiverType: "AssemblyRouteProbe.Probe",
            extensionName: "Twice", @namespace: "AssemblyRouteProbe", maxResults: 1, maxResponseBytes: 32768);
        AssertOwnerResult(filteredExtensions, "Twice");
        Assert.DoesNotContain("Thrice", TextOf(filteredExtensions), StringComparison.Ordinal);
        var cappedExtensions = await assemblies.FindAssemblyExtensions(assemblyPath, receiverType: "AssemblyRouteProbe.Probe",
            maxResults: 1, maxResponseBytes: 32768);
        AssertOwnerPage(TextOf(cappedExtensions));
        Assert.Contains("truncated", TextOf(cappedExtensions), StringComparison.Ordinal);
        var contextDefault = await assemblies.GetAssemblyContext(assemblyPath, maxResponseBytes: 32768);
        var contextZero = await assemblies.GetAssemblyContext(assemblyPath, maxResults: 0, maxResponseBytes: 32768);
        AssertOwnerResult(contextDefault, "AssemblyRouteProbe");
        Assert.Equal(TextOf(contextDefault), TextOf(contextZero));

        var badContext = await assemblies.GetAssemblyContext(assemblyPath, "h:zzzz", detailLevel: "unsupported", maxResponseBytes: 16384);
        AssertError(badContext, "INVALID_ARGUMENT");
        AssertError(await assemblies.GetAssemblyContext(assemblyPath, "h:zzzz", includeBody: true, maxResponseBytes: 16384), "HANDOFF_UNKNOWN");
        AssertError(await symbols.GetSymbolBody(assemblyPath, ["h:zzzz"], maxResponseBytes: 16384), "HANDOFF_UNKNOWN");
        AssertError(await structure.GetClassStructure(assemblyPath, "h:zzzz", maxResponseBytes: 16384), "HANDOFF_UNKNOWN");
        AssertError(await relationships.GetTypeHierarchy(assemblyPath, "h:zzzz", maxResponseBytes: 16384), "HANDOFF_UNKNOWN");
        AssertError(await relationships.FindImplementations(assemblyPath, "h:zzzz", maxResponseBytes: 16384), "HANDOFF_UNKNOWN");
        AssertError(await assemblies.InspectAssembly(assemblyPath, detailLevel: "unsupported", maxResponseBytes: 16384), "INVALID_ARGUMENT");
        AssertError(await assemblies.SearchAssembly(assemblyPath, searchKind: "unsupported", pattern: "x", maxResponseBytes: 16384), "INVALID_ARGUMENT");
        AssertError(await assemblies.FindAssemblyExtensions(assemblyPath, detailLevel: "unsupported", maxResponseBytes: 16384), "INVALID_ARGUMENT");

        Func<int, int?, Task<CallToolResult>>[] budgetProjections =
        [
            (bytes, tokens) => symbols.FindSymbol(assemblyPath, pattern: "Entry", kind: "method", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => symbols.GetSymbolBody(assemblyPath, [entryHandle], maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => structure.GetFileSkeleton(assemblyPath, [filePath], maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => structure.GetClassStructure(assemblyPath, typeHandle, maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => structure.GetFileTree(assemblyPath, view: "files", maxResults: 20, maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => structure.GetNamespaceTree(assemblyPath, namespacePrefix: "AssemblyRouteProbe", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.GetCallTree(assemblyPath, entryHandle, direction: "outgoing", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.FindReferences(assemblyPath, "M:AssemblyRouteProbe.Probe.Read", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.GetTypeHierarchy(assemblyPath, typeHandle, maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.FindImplementations(assemblyPath, interfaceHandle, maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.GetImpact(assemblyPath, "M:AssemblyRouteProbe.Probe.Read", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.DependencyGraph(assemblyPath, symbolIdentifier: typeHandle, maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => relationships.ResolveTypeOrigin(assemblyPath, typeName: "AssemblyRouteProbe.Probe", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => assemblies.GetAssemblyContext(assemblyPath, "AssemblyRouteProbe.Probe", includeBody: true, maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => assemblies.InspectAssembly(assemblyPath, maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => assemblies.SearchAssembly(assemblyPath, pattern: "Probe", maxResponseBytes: bytes, maxResponseTokens: tokens),
            (bytes, tokens) => assemblies.FindAssemblyExtensions(assemblyPath, receiverType: "", maxResponseBytes: bytes, maxResponseTokens: tokens),
        ];
        foreach (var projection in budgetProjections)
        {
            await AssertRecoverableProjectionAsync(projection, 512, 4096);
            await AssertRecoverableProjectionAsync(projection, 65536, 512);
        }
    }

    [Fact]
    public async Task AssemblyLockedTargetReturnsTypedErrorAndRecoversAfterUnlock()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var configuration = new NavigatorHostConfiguration(
            Path.Combine(Path.GetTempPath(), "ainet-assembly-lock-" + Guid.NewGuid().ToString("N") + ".json"),
            isDefaultPath: true,
            new LoggingLevelSwitch(LogEventLevel.Warning));
        Assert.True((await configuration.LoadStartupAsync(CancellationToken.None)).Succeeded);
        await using var runtime = new NavigatorHostRuntime(configuration, host.Services.GetRequiredService<IHostApplicationLifetime>());
        var assemblies = new AssemblyTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-lock-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "LockedAssemblyProbe",
            "namespace LockedAssemblyProbe; public sealed class Probe { public int Read() => 1; }");
        CallToolResult locked;
        await using (new FileStream(assemblyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            locked = await assemblies.InspectAssembly(assemblyPath, typeName: "Probe", maxResponseBytes: 24576);
        }

        AssertError(locked, "TARGET_UNREADABLE");
        var recovered = await assemblies.InspectAssembly(assemblyPath, typeName: "Probe", maxResponseBytes: 24576);
        AssertOwnerResult(recovered, "Probe");
    }

    [Fact]
    public async Task AssemblyZeroByteDefaultsMatchPublishedBudgetsForLargeResults()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var configuration = new NavigatorHostConfiguration(
            Path.Combine(Path.GetTempPath(), "ainet-assembly-zero-bytes-" + Guid.NewGuid().ToString("N") + ".json"),
            isDefaultPath: true,
            new LoggingLevelSwitch(LogEventLevel.Warning));
        Assert.True((await configuration.LoadStartupAsync(CancellationToken.None)).Succeeded);
        await using var runtime = new NavigatorHostRuntime(configuration, host.Services.GetRequiredService<IHostApplicationLifetime>());
        var assemblies = new AssemblyTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-zero-bytes-");
        var suffix = new string('T', 120);
        var source = new StringBuilder("namespace ZeroByteProbe;\n");
        for (var index = 0; index < 400; index++)
        {
            source.Append("public sealed class Type").Append(index.ToString("D3")).Append('_').Append(suffix).AppendLine(" {");
            source.Append("public string NeedleBudget").Append(index.ToString("D3"))
                .Append("() => \"").Append('x', 600).AppendLine("\";");
            source.AppendLine("}");
        }
        source.AppendLine("public static class Extensions {");
        for (var index = 0; index < 100; index++)
        {
            source.Append("public static int ExtensionBudget").Append(index.ToString("D3")).Append('_').Append(suffix)
                .Append("(this Type000_").Append(suffix).Append(" receiver) => ").Append(index).AppendLine(";");
        }
        source.AppendLine("}");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "ZeroByteProbe", source.ToString());

        var inspectDefault = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.InspectAssembly(
            assemblyPath, maxResults: 42, maxMembers: 1, maxResponseTokens: tokens, continuationToken: continuation), 24576, 16000);
        var inspectZero = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.InspectAssembly(
            assemblyPath, maxResults: 42, maxMembers: 1, maxResponseBytes: 0, maxResponseTokens: tokens, continuationToken: continuation), 24576, 16000);
        Assert.Contains("Type000", inspectDefault.Text, StringComparison.Ordinal);
        Assert.Equal(BodyOf(inspectDefault.FirstPage), BodyOf(inspectZero.FirstPage));
        Assert.Equal(inspectDefault.Text, inspectZero.Text);
        Assert.InRange(Encoding.UTF8.GetByteCount(inspectDefault.FirstPage), 16 * 1024 + 1, 24 * 1024);

        var searchDefault = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.SearchAssembly(
            assemblyPath, pattern: "NeedleBudget", declarationOnly: true, kind: "method", maxResults: 30,
            maxResponseTokens: tokens, continuationToken: continuation), 24576, 16000);
        var searchZero = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.SearchAssembly(
            assemblyPath, pattern: "NeedleBudget", declarationOnly: true, kind: "method", maxResults: 30,
            maxResponseBytes: 0, maxResponseTokens: tokens, continuationToken: continuation), 24576, 16000);
        Assert.Contains("NeedleBudget", searchDefault.Text, StringComparison.Ordinal);
        Assert.Equal(BodyOf(searchDefault.FirstPage), BodyOf(searchZero.FirstPage));
        Assert.Equal(searchDefault.Text, searchZero.Text);
        Assert.InRange(Encoding.UTF8.GetByteCount(searchDefault.FirstPage), 16 * 1024 + 1, 24 * 1024);

        var extensionsDefault = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.FindAssemblyExtensions(
            assemblyPath, receiverType: "", maxResults: 50, maxResponseTokens: tokens, continuationToken: continuation), 16384, 16000);
        var extensionsZero = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.FindAssemblyExtensions(
            assemblyPath, receiverType: "", maxResults: 50, maxResponseBytes: 0, maxResponseTokens: tokens,
            continuationToken: continuation), 16384, 16000);
        Assert.Contains("ExtensionBudget000", extensionsDefault.Text, StringComparison.Ordinal);
        Assert.Equal(BodyOf(extensionsDefault.FirstPage), BodyOf(extensionsZero.FirstPage));
        Assert.Equal(extensionsDefault.Text, extensionsZero.Text);
        Assert.InRange(Encoding.UTF8.GetByteCount(extensionsDefault.FirstPage), 8 * 1024 + 1, 16 * 1024);

        var contextStandardDefault = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.GetAssemblyContext(
            assemblyPath, maxResults: 200, maxResponseTokens: tokens, continuationToken: continuation), 32768, 16000);
        var contextStandardZero = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.GetAssemblyContext(
            assemblyPath, maxResults: 200, maxResponseBytes: 0, maxResponseTokens: tokens, continuationToken: continuation), 32768, 16000);
        Assert.Contains("Type000", contextStandardDefault.Text, StringComparison.Ordinal);
        Assert.Equal(BodyOf(contextStandardDefault.FirstPage), BodyOf(contextStandardZero.FirstPage));
        Assert.Equal(contextStandardDefault.Text, contextStandardZero.Text);
        Assert.InRange(Encoding.UTF8.GetByteCount(contextStandardDefault.FirstPage), 16 * 1024 + 1, 32 * 1024);

        var contextFullDefault = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.GetAssemblyContext(
            assemblyPath, maxResults: 400, detailLevel: "full", maxResponseTokens: tokens, continuationToken: continuation), 65536, 32000);
        var contextFullZero = await ReadOuterPagesAsync((_, tokens, continuation) => assemblies.GetAssemblyContext(
            assemblyPath, maxResults: 400, detailLevel: "full", maxResponseBytes: 0, maxResponseTokens: tokens,
            continuationToken: continuation), 65536, 32000);
        Assert.Contains("Type000", contextFullDefault.Text, StringComparison.Ordinal);
        Assert.Equal(BodyOf(contextFullDefault.FirstPage), BodyOf(contextFullZero.FirstPage));
        Assert.Equal(contextFullDefault.Text, contextFullZero.Text);
        Assert.InRange(Encoding.UTF8.GetByteCount(contextFullDefault.FirstPage), 32 * 1024 + 1, 65_536);
    }

    [Fact]
    public async Task AssemblyContextReturnsBodyOwnerStaleSnapshotAfterSuccessfulSymbolResolution()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var configuration = new NavigatorHostConfiguration(
            Path.Combine(Path.GetTempPath(), "ainet-assembly-context-section-error-" + Guid.NewGuid().ToString("N") + ".json"),
            isDefaultPath: true,
            new LoggingLevelSwitch(LogEventLevel.Warning));
        Assert.True((await configuration.LoadStartupAsync(CancellationToken.None)).Succeeded);
        await using var runtime = new NavigatorHostRuntime(configuration, host.Services.GetRequiredService<IHostApplicationLifetime>());
        var assemblies = new AssemblyTools(runtime);
        var symbols = new SymbolTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-context-section-error-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "ContextSectionProbe", """
            namespace ContextSectionProbe;
            public sealed class Probe { public int Read() => 1; }
            """);
        using var replacementFixture = TestTempDirectory.Create("ainet-assembly-context-section-replacement-");
        var replacementPath = AssemblyTestHelper.EmitAssembly(replacementFixture, "ContextSectionProbe", """
            namespace ContextSectionProbe;
            public sealed class Probe { public int Read() => 2; public int Added() => 3; }
            """);
        var originalIdentity = AssemblyName.GetAssemblyName(assemblyPath);
        var replacementIdentity = AssemblyName.GetAssemblyName(replacementPath);
        Assert.Equal(originalIdentity.Name, replacementIdentity.Name);
        Assert.Equal(originalIdentity.Version, replacementIdentity.Version);
        var produced = await symbols.FindSymbol(assemblyPath, pattern: "Read", kind: "method", maxResponseBytes: 32768);
        AssertOwnerResult(produced, "Read");
        var handle = ReadAnyHandoff(TextOf(produced));
        var replacedAfterResolution = false;
        assemblies.BeforeAssemblyContextBodyLoadForTesting = () =>
        {
            File.Copy(replacementPath, assemblyPath, overwrite: true);
            replacedAfterResolution = true;
        };

        var context = await assemblies.GetAssemblyContext(assemblyPath, handle, includeBody: true,
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        var text = TextOf(context);
        Assert.True(replacedAfterResolution, "The fixture must change after get_assembly_context resolved the handoff and before the body owner reload.");
        Assert.True(context.IsError ?? false, text);
        Assert.Contains("STALE_SNAPSHOT", text, StringComparison.Ordinal);
        Assert.Contains("Run inspect_assembly again", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Status: operation=ok", text, StringComparison.Ordinal);
        Assert.InRange(Encoding.UTF8.GetByteCount(text), 0, 32768);
        Assert.InRange(McpResponseFormatter.CountTokens(text), 0, 4096);

        var added = await symbols.FindSymbol(assemblyPath, pattern: "Added", kind: "method", maxResponseBytes: 32768);
        AssertOwnerResult(added, "Added");
        await FollowAssemblyHandoffAsync(symbols, assemblyPath, added);
        var changedRead = await symbols.FindSymbol(assemblyPath, pattern: "Probe.Read", kind: "method", maxResponseBytes: 32768);
        AssertOwnerResult(changedRead, "Probe.Read");
        var changedReadBody = await symbols.GetSymbolBody(assemblyPath, [ReadAnyHandoff(TextOf(changedRead))], maxResponseBytes: 32768);
        AssertOwnerResult(changedReadBody, "return 2");
    }

    [Fact]
    public async Task AssemblyInspectAndSearchHandlersKeepDomainCursorsSeparateFromOuterPages()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var configuration = new NavigatorHostConfiguration(
            Path.Combine(Path.GetTempPath(), "ainet-assembly-domain-pages-" + Guid.NewGuid().ToString("N") + ".json"),
            isDefaultPath: true,
            new LoggingLevelSwitch(LogEventLevel.Warning));
        Assert.True((await configuration.LoadStartupAsync(CancellationToken.None)).Succeeded);
        await using var runtime = new NavigatorHostRuntime(configuration, host.Services.GetRequiredService<IHostApplicationLifetime>());
        var assemblies = new AssemblyTools(runtime);
        var symbols = new SymbolTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-domain-pages-");
        var source = "namespace DomainPageProbe;\n" + string.Join("\n", Enumerable.Range(0, 8).Select(index =>
            $"public sealed class Probe{index} {{ {string.Join(" ", Enumerable.Range(0, 24).Select(member => $"public int Needle{index}_{member}() => {index + member};"))} }}"));
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "DomainPageProbe", source);
        var foreignPath = AssemblyTestHelper.EmitAssembly(fixture, "ForeignDomainPageProbe", "public sealed class ForeignProbe { }");

        var inspectFirst = await assemblies.InspectAssembly(assemblyPath, maxResults: 1, maxMembers: 20,
            includeReferences: false, maxResponseBytes: 65536, maxResponseTokens: 16000);
        AssertOwnerResultWithinBudget(inspectFirst, "Probe0", 65536, 16000);
        var inspectCursor = ReadDomainCursor(TextOf(inspectFirst));
        Assert.StartsWith("v1.", inspectCursor, StringComparison.Ordinal);
        var inspectNext = await assemblies.InspectAssembly(assemblyPath, maxResults: 1, maxMembers: 20,
            includeReferences: false, continuationToken: inspectCursor, maxResponseBytes: 65536, maxResponseTokens: 16000);
        AssertOwnerResultWithinBudget(inspectNext, "Probe1", 65536, 16000);
        var inspectReplay = await assemblies.InspectAssembly(assemblyPath, maxResults: 1, maxMembers: 20,
            includeReferences: false, continuationToken: inspectCursor, maxResponseBytes: 65536, maxResponseTokens: 16000);
        Assert.Equal(TextOf(inspectNext), TextOf(inspectReplay));
        AssertError(await assemblies.InspectAssembly(assemblyPath, typeName: "Changed", maxResults: 1, maxMembers: 20,
            includeReferences: false, continuationToken: inspectCursor, maxResponseBytes: 16384), "INVALID_ARGUMENT");
        AssertError(await assemblies.InspectAssembly(foreignPath, maxResults: 1, maxMembers: 20,
            includeReferences: false, continuationToken: inspectCursor, maxResponseBytes: 16384), "INVALID_ARGUMENT");
        var inspectNames = new List<string>();
        string? fullInspectCursor = null;
        var inspectDomains = 0;
        do
        {
            var inspectDomainCursor = fullInspectCursor;
            var broadPage = await ReadOuterPagesAsync((bytes, tokens, continuation) => assemblies.InspectAssembly(
                assemblyPath, maxResults: 1, maxMembers: 100, includeReferences: false,
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation), 65536, 16000,
                inspectDomainCursor);
            var smallPage = await ReadOuterPagesAsync((bytes, tokens, continuation) => assemblies.InspectAssembly(
                assemblyPath, maxResults: 1, maxMembers: 100, includeReferences: false,
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation), 4096, 1600,
                inspectDomainCursor);
            Assert.Equal(broadPage.Text, smallPage.Text);
            Assert.True(smallPage.Pages > 1, "This inspect domain page must itself require outer response pages.");
            using var pageJson = System.Text.Json.JsonDocument.Parse(broadPage.Text);
            inspectNames.Add(pageJson.RootElement.GetProperty("types")[0].GetProperty("name").GetString()!);
            fullInspectCursor = ReadOptionalDomainCursor(broadPage.Text);
            inspectDomains++;
        } while (fullInspectCursor is not null && inspectDomains < 20);
        Assert.Equal(8, inspectDomains);
        Assert.Equal(Enumerable.Range(0, 8).Select(index => $"Probe{index}"), inspectNames);

        var searchFirst = await assemblies.SearchAssembly(assemblyPath, pattern: "Needle", declarationOnly: true,
            kind: "method", maxResults: 100, maxFiles: 0, maxResponseBytes: 65536, maxResponseTokens: 50000);
        AssertOwnerResultWithinBudget(searchFirst, "Needle0", 65536, 50000);
        var searchCursor = ReadDomainCursor(TextOf(searchFirst));
        Assert.StartsWith("v1.", searchCursor, StringComparison.Ordinal);
        var searchNext = await assemblies.SearchAssembly(assemblyPath, pattern: "Needle", declarationOnly: true,
            kind: "method", maxResults: 100, maxFiles: 0, continuationToken: searchCursor,
            maxResponseBytes: 65536, maxResponseTokens: 50000);
        AssertOwnerPage(TextOf(searchNext));
        Assert.Contains("Probe", TextOf(searchNext), StringComparison.Ordinal);
        Assert.Equal(TextOf(searchNext), TextOf(await assemblies.SearchAssembly(assemblyPath, pattern: "Needle",
            declarationOnly: true, kind: "method", maxResults: 100, maxFiles: 0, continuationToken: searchCursor,
            maxResponseBytes: 65536, maxResponseTokens: 50000)));
        AssertError(await assemblies.SearchAssembly(assemblyPath, pattern: "Needle", declarationOnly: true,
            kind: "type", maxResults: 1, maxFiles: 0, continuationToken: searchCursor, maxResponseBytes: 16384), "INVALID_ARGUMENT");
        AssertError(await assemblies.SearchAssembly(foreignPath, pattern: "Needle", declarationOnly: true,
            kind: "method", maxResults: 1, maxFiles: 0, continuationToken: searchCursor, maxResponseBytes: 16384), "INVALID_ARGUMENT");
        var searchNames = new List<string>();
        string? fullSearchCursor = null;
        var searchDomains = 0;
        do
        {
            var searchDomainCursor = fullSearchCursor;
            var broadPage = await ReadOuterPagesAsync((bytes, tokens, continuation) => assemblies.SearchAssembly(
                assemblyPath, pattern: "Needle", declarationOnly: true, kind: "method", maxResults: 100, maxFiles: 0,
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation), 65536, 16000,
                searchDomainCursor);
            var smallPage = await ReadOuterPagesAsync((bytes, tokens, continuation) => assemblies.SearchAssembly(
                assemblyPath, pattern: "Needle", declarationOnly: true, kind: "method", maxResults: 100, maxFiles: 0,
                maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation), 4096, 1600,
                searchDomainCursor);
            Assert.Equal(broadPage.Text, smallPage.Text);
            Assert.True(smallPage.Pages > 1, "This search domain page must itself require outer response pages.");
            using var pageJson = System.Text.Json.JsonDocument.Parse(broadPage.Text);
            foreach (var hit in pageJson.RootElement.GetProperty("results").EnumerateArray())
                searchNames.Add(hit.GetProperty("symbol").GetString()!);
            fullSearchCursor = ReadOptionalDomainCursor(broadPage.Text);
            searchDomains++;
        } while (fullSearchCursor is not null && searchDomains < 20);
        Assert.Equal(2, searchDomains);
        Assert.Equal(192, searchNames.Count);
        Assert.Equal(192, searchNames.Distinct(StringComparer.Ordinal).Count());

        var filesLimited = await assemblies.SearchAssembly(assemblyPath, pattern: "Needle", declarationOnly: true,
            kind: "method", maxResults: 100, maxFiles: 1, maxResponseBytes: 65536, maxResponseTokens: 4096);
        var filesLimitedText = TextOf(filesLimited);
        AssertOwnerPage(filesLimitedText);
        Assert.Contains("maxFiles", filesLimitedText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"continuationToken\": \"v1.", filesLimitedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Needle7", filesLimitedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssemblyReferenceClosureHandsOffAcrossRootBridgeAndLeafOwners()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var configuration = new NavigatorHostConfiguration(
            Path.Combine(Path.GetTempPath(), "ainet-assembly-root-bridge-leaf-" + Guid.NewGuid().ToString("N") + ".json"),
            isDefaultPath: true,
            new LoggingLevelSwitch(LogEventLevel.Warning));
        Assert.True((await configuration.LoadStartupAsync(CancellationToken.None)).Succeeded);
        await using var runtime = new NavigatorHostRuntime(configuration, host.Services.GetRequiredService<IHostApplicationLifetime>());
        var symbols = new SymbolTools(runtime);
        var relationships = new RelationshipTools(runtime);
        var assemblies = new AssemblyTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-root-bridge-leaf-");
        var longLiteral = new string('x', 1200);
        var leafBodySource = $"namespace ClosureLeaf; public sealed class Leaf {{ public int Read() {{ var longText = \"{longLiteral}\"; var value = 0; "
            + string.Join(" ", Enumerable.Range(0, 80).Select(index => $"value += {index};")) + " return value + longText.Length; } }";
        var leaf = AssemblyTestHelper.EmitAssembly(fixture, "ClosureLeaf", leafBodySource);
        var bridge = AssemblyTestHelper.EmitAssembly(fixture, "ClosureBridge",
            "namespace ClosureBridge; public sealed class Bridge { public int Forward() => new ClosureLeaf.Leaf().Read(); }", leaf);
        var root = AssemblyTestHelper.EmitAssembly(fixture, "ClosureRoot",
            "namespace ClosureRoot; public sealed class Entry { public int Run() => new ClosureBridge.Bridge().Forward(); }", bridge);

        var leafFromRoot = await PollAssemblyOwnerAsync(operation => symbols.FindSymbol(root, pattern: "ClosureLeaf.Leaf.Read", kind: "method", includeReferences: true,
            maxResults: 10, maxResponseBytes: 65536, maxResponseTokens: 4096, operationToken: operation));
        AssertOwnerResult(leafFromRoot, "Read");
        var leafHandle = ReadHandoff(TextOf(leafFromRoot), "Read");
        var leafBody = await symbols.GetSymbolBody(leaf, [leafHandle], maxResponseBytes: 32768, maxResponseTokens: 4096);
        Assert.False(leafBody.IsError ?? false, TextOf(leafBody) + "\nProducer:\n" + TextOf(leafFromRoot) + "\nHandle: " + leafHandle);
        Assert.Contains("Read", TextOf(leafBody), StringComparison.Ordinal);

        var incoming = await PollAssemblyOwnerAsync(operation => relationships.GetCallTree(root, leafHandle, direction: "incoming", depth: 3,
            includeReferences: true, maxResponseBytes: 65536, maxResponseTokens: 4096, operationToken: operation));
        AssertOwnerPage(TextOf(incoming));
        var incomingText = TextOf(incoming);
        Assert.Contains("ClosureBridge", incomingText, StringComparison.Ordinal);
        Assert.Contains("ClosureRoot", incomingText, StringComparison.Ordinal);
        Assert.Contains(leaf, incomingText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(bridge, incomingText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(root, incomingText, StringComparison.OrdinalIgnoreCase);
        var bridgeHandle = ReadCallTreeHandoff(incomingText, "Forward", bridge);
        var bridgeBody = await symbols.GetSymbolBody(bridge, [bridgeHandle], maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertOwnerResult(bridgeBody, "Forward");
        var rootHandle = ReadCallTreeHandoff(incomingText, "Run", root);
        var rootBody = await symbols.GetSymbolBody(root, [rootHandle], maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertOwnerResult(rootBody, "Run");

        var references = await PollAssemblyOwnerAsync(operation => relationships.FindReferences(root, leafHandle, includeReferences: true, depth: 3,
            maxResponseBytes: 65536, maxResponseTokens: 4096, operationToken: operation));
        AssertOwnerPage(TextOf(references));
        var referencesText = TextOf(references);
        Assert.Contains("ClosureBridge", referencesText, StringComparison.Ordinal);
        Assert.Contains("ClosureRoot", referencesText, StringComparison.Ordinal);
        Assert.Contains("Forward", referencesText, StringComparison.Ordinal);

        var context = await PollAssemblyOwnerAsync(operation => assemblies.GetAssemblyContext(root, leafHandle, includeReferences: true,
            includeCallers: true, includeImpact: true, includeBody: true, maxCallers: 20, depth: 3, topN: 20,
            maxResponseBytes: 65536, maxResponseTokens: 12000, operationToken: operation));
        AssertOwnerResultWithinBudget(context, "## Body", 65536, 12000);
        var contextText = TextOf(context);
        Assert.Contains("## Callers", contextText, StringComparison.Ordinal);
        Assert.Contains("## Impact", contextText, StringComparison.Ordinal);
        Assert.Contains("ClosureBridge", contextText, StringComparison.Ordinal);
        Assert.Contains("ClosureRoot", contextText, StringComparison.Ordinal);
        var broadContext = await PollAssemblyOwnerAsync(operation => assemblies.GetAssemblyContext(root, leafHandle,
            includeReferences: true, includeCallers: true, includeImpact: true, includeBody: true, includeClassStructure: true,
            maxCallers: 20, depth: 3, topN: 20, maxResponseBytes: 65536, maxResponseTokens: 16000,
            operationToken: operation));
        Task<CallToolResult> InvokeMixedContext(int bytes, int? tokens, string? continuation = null) =>
            assemblies.GetAssemblyContext(root, leafHandle, includeReferences: true, includeCallers: true,
                includeImpact: true, includeBody: true, includeClassStructure: true, maxCallers: 20, depth: 3,
                topN: 20, maxResponseBytes: bytes, maxResponseTokens: tokens, continuationToken: continuation);
        var contextRecovery = await ReadOuterPagesWithBudgetRecoveryAsync(InvokeMixedContext);
        var reconstructedContext = contextRecovery.Text;
        Assert.Equal(BodyOf(TextOf(broadContext)), reconstructedContext);
        Assert.True(contextRecovery.Pages > 1, "The mixed context must continue through multiple outer pages.");
        Assert.True(contextRecovery.SawExactBudgetRecovery, "A mixed-context continuation must offer a concrete budget recovery and accept its exact minimum pair.");
        Assert.Equal(BodyOf(TextOf(broadContext)), reconstructedContext);
        foreach (var requiredSection in new[] { "## Body", "## Class Structure", "## Callers", "## Impact" })
            Assert.Contains(requiredSection, reconstructedContext, StringComparison.Ordinal);
    }

    private static void AssertDeclarationOrder(string text)
    {
        var zulu = text.IndexOf("Zulu()", StringComparison.Ordinal);
        var alpha = text.IndexOf("Alpha()", StringComparison.Ordinal);
        Assert.True(zulu >= 0 && alpha > zulu, text);
    }

    private static string[] ReadMemberNames(string text) => text.Split('\n')
        .Where(line => line.StartsWith("- Method ", StringComparison.Ordinal))
        .Select(line => line[(line.IndexOf("int ", StringComparison.Ordinal) + 4)..line.IndexOf('(', StringComparison.Ordinal)])
        .ToArray();

    private static string ReadHandoff(string text, string memberName)
    {
        var line = text.Split('\n').First(line => line.Contains(memberName, StringComparison.Ordinal));
        var start = line.IndexOf("[handoff: ", StringComparison.Ordinal);
        var end = line.IndexOf(']', start + 10);
        Assert.True(start >= 0 && end > start, line);
        return line[(start + 10)..end];
    }

    private static string ReadCallTreeHandoff(string text, string name, string expectedOwnerPath)
    {
        var line = text.Split('\n').FirstOrDefault(value => value.Contains(name, StringComparison.Ordinal)
            && value.Contains("h:", StringComparison.Ordinal));
        Assert.True(line is not null, text);
        Assert.Contains("targetPath: " + expectedOwnerPath, line, StringComparison.OrdinalIgnoreCase);
        var marker = "`h:";
        var start = line.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, line);
        start += 1;
        var end = line.IndexOf('`', start + 2);
        Assert.True(end > start, line);
        return line[start..end];
    }

    private static string ReadAnyHandoff(string text)
    {
        for (var start = text.IndexOf("h:", StringComparison.Ordinal); start >= 0;
             start = text.IndexOf("h:", start + 2, StringComparison.Ordinal))
        {
            var end = start + 2;
            while (end < text.Length && char.IsAsciiLetterOrDigit(text[end])) end++;
            if (end > start + 2) return text[start..end];
        }
        Assert.Fail("The handler did not emit an opaque assembly handoff.\n" + text);
        return string.Empty;
    }

    private static int ReadPosition(string text)
    {
        var start = text.IndexOf("Probe.cs:", StringComparison.Ordinal);
        Assert.True(start >= 0, text);
        start += "Probe.cs:".Length;
        var end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
        Assert.True(int.TryParse(text[start..end], out var line), text);
        return line;
    }

    private static async Task FollowAssemblyHandoffAsync(SymbolTools symbols, string targetPath, CallToolResult producer)
    {
        var handle = ReadAnyHandoff(TextOf(producer));
        var body = await symbols.GetSymbolBody(targetPath, [handle], maxResponseBytes: 32768, maxResponseTokens: 4096);
        var text = TextOf(body);
        Assert.False(body.IsError ?? false, text);
        Assert.DoesNotContain("HANDOFF_UNKNOWN", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TARGET_MISMATCH", text, StringComparison.Ordinal);
        Assert.Contains("Status: operation=ok", text, StringComparison.Ordinal);
    }

    private static void AssertOwnerResult(CallToolResult result, string expectedText)
        => AssertOwnerResultWithinBudget(result, expectedText, 65536, 4096);

    private static void AssertOwnerResultWithinBudget(CallToolResult result, string expectedText, int bytes, int tokens)
    {
        var text = TextOf(result);
        Assert.False(result.IsError ?? false, text);
        Assert.DoesNotContain("operation=running", text, StringComparison.Ordinal);
        Assert.DoesNotContain("operation=retry", text, StringComparison.Ordinal);
        Assert.Contains(expectedText, text, StringComparison.Ordinal);
        Assert.InRange(Encoding.UTF8.GetByteCount(text), 0, bytes);
        Assert.InRange(McpResponseFormatter.CountTokens(text), 0, tokens);
    }

    private static void AssertError(CallToolResult result, string code)
    {
        var text = TextOf(result);
        Assert.True(result.IsError ?? false, text);
        Assert.Contains(code, text, StringComparison.Ordinal);
    }

    private static async Task AssertRecoverableProjectionAsync(
        Func<int, int?, Task<CallToolResult>> invoke,
        int requestedBytes,
        int requestedTokens)
    {
        var first = await invoke(requestedBytes, requestedTokens);
        var firstText = TextOf(first);
        Assert.InRange(Encoding.UTF8.GetByteCount(firstText), 0, requestedBytes);
        Assert.InRange(McpResponseFormatter.CountTokens(firstText), 0, requestedTokens);
        if (!firstText.Contains("RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
        {
            Assert.False(first.IsError ?? false, firstText);
            AssertOwnerPage(firstText);
            return;
        }

        var minBytes = ReadBudget(firstText, "minimumResponseBytes");
        var minTokens = ReadBudget(firstText, "minimumResponseTokens");
        var recovered = await invoke(minBytes, minTokens);
        var recoveredText = TextOf(recovered);
        Assert.False(recovered.IsError ?? false, recoveredText);
        Assert.DoesNotContain("RESPONSE_BUDGET_TOO_SMALL", recoveredText, StringComparison.Ordinal);
        Assert.InRange(Encoding.UTF8.GetByteCount(recoveredText), 0, minBytes);
        Assert.InRange(McpResponseFormatter.CountTokens(recoveredText), 0, minTokens);
        AssertOwnerPage(recoveredText);

        var repeated = await invoke(minBytes, minTokens);
        var repeatedText = TextOf(repeated);
        Assert.Equal(recoveredText, repeatedText);
        Assert.InRange(Encoding.UTF8.GetByteCount(repeatedText), 0, minBytes);
        Assert.InRange(McpResponseFormatter.CountTokens(repeatedText), 0, minTokens);
    }

    private static void AssertOwnerPage(string text)
    {
        Assert.Contains("Status: operation=ok", text, StringComparison.Ordinal);
        Assert.DoesNotContain("operation=running", text, StringComparison.Ordinal);
        Assert.DoesNotContain("operation=retry", text, StringComparison.Ordinal);
        Assert.DoesNotContain("operation=loading", text, StringComparison.Ordinal);
    }

    private static int ReadBudget(string text, string key)
    {
        var marker = key + ": ";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, text);
        start += marker.Length;
        var end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
        Assert.True(int.TryParse(text[start..end], out var value), text);
        return value;
    }

    private static void AssertSuccessWithinBudget(CallToolResult result, int bytes, int tokens)
    {
        Assert.False(result.IsError ?? false, TextOf(result));
        var text = TextOf(result);
        Assert.InRange(Encoding.UTF8.GetByteCount(text), 0, bytes);
        Assert.InRange(McpResponseFormatter.CountTokens(text), 0, tokens);
    }

    private static string ReadDomainCursor(string text)
    {
        return ReadOptionalDomainCursor(text) ?? throw new Xunit.Sdk.XunitException("The domain page omitted its continuationToken.");
    }

    private static string? ReadOptionalDomainCursor(string text)
    {
        var body = BodyOf(text);
        var jsonStart = body.IndexOf('{');
        Assert.True(jsonStart >= 0, body);
        using var document = System.Text.Json.JsonDocument.Parse(body[jsonStart..]);
        return document.RootElement.TryGetProperty("continuationToken", out var cursor) ? cursor.GetString() : null;
    }

    private static async Task<(string Text, int Pages, string FirstPage)> ReadOuterPagesAsync(
        Func<int, int?, string?, Task<CallToolResult>> invoke,
        int bytes,
        int tokens,
        string? initialContinuation = null)
    {
        var accumulated = new StringBuilder();
        string? continuation = initialContinuation;
        string? firstPage = null;
        for (var pageNumber = 0; pageNumber < 100; pageNumber++)
        {
            var result = await invoke(bytes, tokens, continuation);
            var text = TextOf(result);
            firstPage ??= text;
            Assert.False(result.IsError ?? false, text);
            Assert.InRange(Encoding.UTF8.GetByteCount(text), 0, bytes);
            Assert.InRange(McpResponseFormatter.CountTokens(text), 0, tokens);
            Assert.DoesNotContain("operation=running", text, StringComparison.Ordinal);
            Assert.DoesNotContain("operation=retry", text, StringComparison.Ordinal);
            accumulated.Append(BodyOf(text));
            continuation = ReadOuterContinuation(text);
            if (continuation is null) return (accumulated.ToString(), pageNumber + 1, firstPage);
            Assert.NotEmpty(continuation);
            Assert.All(continuation, character => Assert.True(char.IsAsciiDigit(character)));
        }
        throw new Xunit.Sdk.XunitException("The outer response did not reach its final page.");
    }

    private static async Task<(string Text, int Pages, bool SawExactBudgetRecovery)> ReadOuterPagesWithBudgetRecoveryAsync(
        Func<int, int?, string?, Task<CallToolResult>> invoke)
    {
        var text = new StringBuilder();
        var bytes = 512;
        int? tokens = 89;
        string? continuation = null;
        var sawExactBudgetRecovery = false;
        for (var pageNumber = 0; pageNumber < 100; pageNumber++)
        {
            var result = await invoke(bytes, tokens, continuation);
            var page = TextOf(result);
            Assert.InRange(Encoding.UTF8.GetByteCount(page), 0, bytes);
            Assert.InRange(McpResponseFormatter.CountTokens(page), 0, tokens.Value);
            if (page.Contains("RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
            {
                Assert.True(result.IsError ?? false, page);
                var minBytes = ReadBudget(page, "minimumResponseBytes");
                var minTokens = ReadBudget(page, "minimumResponseTokens");
                Assert.InRange(minBytes, 512, 65536);
                Assert.True(minTokens > 0);
                var recovered = await invoke(minBytes, minTokens, continuation);
                var recoveredText = TextOf(recovered);
                Assert.False(recovered.IsError ?? false, recoveredText);
                Assert.DoesNotContain("RESPONSE_BUDGET_TOO_SMALL", recoveredText, StringComparison.Ordinal);
                Assert.InRange(Encoding.UTF8.GetByteCount(recoveredText), 0, minBytes);
                Assert.InRange(McpResponseFormatter.CountTokens(recoveredText), 0, minTokens);
                Assert.Equal(recoveredText, TextOf(await invoke(minBytes, minTokens, continuation)));
                result = recovered;
                page = recoveredText;
                bytes = minBytes;
                tokens = minTokens;
                sawExactBudgetRecovery = true;
            }
            else
            {
                Assert.False(result.IsError ?? false, page);
            }

            Assert.Contains("Status: operation=ok", page, StringComparison.Ordinal);
            Assert.DoesNotContain("operation=running", page, StringComparison.Ordinal);
            Assert.DoesNotContain("operation=retry", page, StringComparison.Ordinal);
            text.Append(BodyOf(page));
            continuation = ReadOuterContinuation(page);
            if (continuation is null) return (text.ToString(), pageNumber + 1, sawExactBudgetRecovery);
            Assert.NotEmpty(continuation);
            Assert.All(continuation, character => Assert.True(char.IsAsciiDigit(character)));
        }
        throw new Xunit.Sdk.XunitException("The recovered mixed context did not reach its final outer page.");
    }

    private static string BodyOf(string text)
    {
        var lines = text.Split('\n');
        var firstContentLine = lines.Length > 0 && lines[0].StartsWith("Status:", StringComparison.Ordinal) ? 1 : 0;
        if (firstContentLine < lines.Length && lines[firstContentLine].StartsWith("nextAction: ", StringComparison.Ordinal))
            firstContentLine++;
        if (firstContentLine < lines.Length && lines[firstContentLine].StartsWith("continuationToken=", StringComparison.Ordinal))
            firstContentLine++;
        return string.Join("\n", lines.Skip(firstContentLine));
    }

    private static string? ReadOuterContinuation(string text)
    {
        var line = text.Split('\n').FirstOrDefault(value => value.StartsWith("continuationToken=", StringComparison.Ordinal));
        return line?[("continuationToken=".Length)..];
    }

    private static async Task<CallToolResult> PollAssemblyOwnerAsync(Func<string?, Task<CallToolResult>> invoke)
    {
        string? operation = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var result = await invoke(operation);
            var text = TextOf(result);
            if (!text.Contains("operation=running", StringComparison.Ordinal)) return result;
            operation = text.Split('\n').FirstOrDefault(line => line.StartsWith("operationToken=", StringComparison.Ordinal))?
                ["operationToken=".Length..];
            Assert.False(string.IsNullOrWhiteSpace(operation), text);
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException("The direct assembly owner did not complete after polling its operation token.");
    }

    private static string TextOf(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
}
