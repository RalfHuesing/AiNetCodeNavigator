using System.IO;
using System.Text.Json;
using System.Threading;
using AiNetCodeNavigator.Core.Dependencies;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class SourceDependencyGraphCacheContractTests
{
    [Fact]
    public async Task DependencyGraph_WarmProjectionsReuseFactsAndFreshSnapshotCollectsAgain()
    {
        var collectedDocumentCount = 0;
        await using var cache = new DependencyGraphCache(observer: new DependencyGraphCollectionObserver(
            DocumentCollected: _ => Interlocked.Increment(ref collectedDocumentCount)));
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(
            host.Services.GetRequiredService<IHostApplicationLifetime>(), dependencyGraphCache: cache);
        var relationships = new RelationshipTools(runtime);
        var symbols = new SymbolTools(runtime);
        using var fixture = TestTempDirectory.Create("ainet-source-dependency-graph-cache-");
        var (target, sourceFile) = await CreateSolutionAsync(fixture.DirectoryPath);

        var cold = await relationships.DependencyGraph(target, symbolIdentifier: "T:CacheProbe.Root",
            direction: "outgoing", depth: 1, maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(cold, 65536, 4096);
        Assert.Equal(1, Volatile.Read(ref collectedDocumentCount));
        var coldEdge = ReadEdge(cold, "Root", "Dependency");
        var rootHandoff = RequiredHandoff(coldEdge, "fromHandoffId");
        var dependencyHandoff = RequiredHandoff(coldEdge, "toHandoffId");

        var changedProjection = await relationships.DependencyGraph(target, symbolIdentifier: "T:CacheProbe.Root",
            direction: "both", depth: 3, maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(changedProjection, 65536, 4096);
        var warmEdge = ReadEdge(changedProjection, "Root", "Dependency");
        Assert.Equal(rootHandoff, RequiredHandoff(warmEdge, "fromHandoffId"));
        Assert.Equal(dependencyHandoff, RequiredHandoff(warmEdge, "toHandoffId"));
        var followedWarmEndpoints = await symbols.GetSymbolBody(target, [rootHandoff, dependencyHandoff],
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(followedWarmEndpoints, 32768, 4096);
        Assert.Contains("Resolution status: resolved", TextOf(followedWarmEndpoints), StringComparison.Ordinal);
        Assert.Contains("class Root", TextOf(followedWarmEndpoints), StringComparison.Ordinal);
        Assert.Contains("class Dependency", TextOf(followedWarmEndpoints), StringComparison.Ordinal);
        var changedRoot = await relationships.DependencyGraph(target, symbolIdentifier: "T:CacheProbe.Caller",
            direction: "outgoing", depth: 2, maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(changedRoot, 65536, 4096);
        _ = ReadEdge(changedRoot, "Caller", "Root");
        Assert.Equal(2, Volatile.Read(ref collectedDocumentCount));

        var testsScope = await relationships.DependencyGraph(target, symbolIdentifier: "T:CacheProbe.Root",
            direction: "incoming", scopeType: "tests", maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(testsScope, 65536, 4096);
        var filteredEndpointEdge = ReadEdge(testsScope, "TestCaller", "Root");
        var filteredEndpointHandoff = RequiredHandoff(filteredEndpointEdge, "toHandoffId");
        var followedFilteredEndpoint = await symbols.GetSymbolBody(target, [filteredEndpointHandoff],
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(followedFilteredEndpoint, 32768, 4096);
        Assert.Contains("Resolution status: resolved", TextOf(followedFilteredEndpoint), StringComparison.Ordinal);
        Assert.Contains("class Root", TextOf(followedFilteredEndpoint), StringComparison.Ordinal);
        Assert.Equal(3, Volatile.Read(ref collectedDocumentCount));

        var generated = await relationships.DependencyGraph(target, symbolIdentifier: "T:CacheProbe.Root",
            direction: "incoming", includeGenerated: true, maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(generated, 65536, 4096);
        Assert.Equal(6, Volatile.Read(ref collectedDocumentCount));

        var originalTimestamp = File.GetLastWriteTimeUtc(sourceFile);
        await File.AppendAllTextAsync(sourceFile, "\npublic sealed class FreshDependency { }");
        File.SetLastWriteTimeUtc(sourceFile, originalTimestamp);
        var refreshed = await relationships.DependencyGraph(target, symbolIdentifier: "T:CacheProbe.Root",
            direction: "outgoing", depth: 1, maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(refreshed, 65536, 4096);
        Assert.Equal(7, Volatile.Read(ref collectedDocumentCount));
        using var payload = JsonDocument.Parse(JsonBody(TextOf(refreshed)));
        Assert.True(payload.RootElement.GetProperty("isComplete").GetBoolean(), TextOf(refreshed));
    }

    private static string JsonBody(string text)
    {
        var start = text.IndexOf('{');
        Assert.True(start >= 0, text);
        return text[start..];
    }

    private static JsonElement ReadEdge(CallToolResult result, string from, string to)
    {
        using var payload = JsonDocument.Parse(JsonBody(TextOf(result)));
        return payload.RootElement.GetProperty("typeDependencies").EnumerateArray()
            .Single(edge => edge.GetProperty("fromTypeName").GetString() == from
                && edge.GetProperty("toTypeName").GetString() == to).Clone();
    }

    private static string RequiredHandoff(JsonElement edge, string property) =>
        edge.GetProperty(property).GetString() is { Length: > 0 } value
            ? value
            : throw new Xunit.Sdk.XunitException($"Expected visible edge to contain {property}: {edge}");

    private static async Task<(string Solution, string SourceFile)> CreateSolutionAsync(string root)
    {
        var solution = Path.Combine(root, "DependencyGraphCache.slnx");
        var projectDirectory = Path.Combine(root, "src", "App");
        var sourceFile = Path.Combine(projectDirectory, "Relationships.cs");
        Directory.CreateDirectory(projectDirectory);
        await File.WriteAllTextAsync(solution, "<Solution><Project Path=\"src/App/DependencyGraphCache.csproj\" /></Solution>");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "DependencyGraphCache.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>disable</ImplicitUsings><Nullable>enable</Nullable><GenerateAssemblyInfo>false</GenerateAssemblyInfo><GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute></PropertyGroup></Project>");
        await File.WriteAllTextAsync(sourceFile,
            "namespace CacheProbe; public sealed class Root { public Dependency Value { get; set; } = new(); } public sealed class Dependency { } public sealed class Caller { public Root Value { get; set; } = new(); }");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "CacheProbe.Tests.cs"),
            "namespace CacheProbe; public sealed class TestCaller { public Root Value { get; set; } = new(); }");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Generated.g.cs"),
            "// <auto-generated/>\nnamespace CacheProbe; public sealed class GeneratedCaller { public Root Value { get; set; } = new(); }");
        var nugetConfig = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(nugetConfig, "<configuration><packageSources><clear /></packageSources></configuration>");
        await FixtureRestore.RunAsync(solution, root, nugetConfig, "Dependency-graph cache fixture restore");
        return (solution, sourceFile);
    }
}
