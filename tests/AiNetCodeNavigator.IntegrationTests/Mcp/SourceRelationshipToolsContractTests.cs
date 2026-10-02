using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
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
public sealed class SourceRelationshipToolsContractTests
{
    [Fact]
    public async Task DependencyGraph_TraversesTargetInDocumentWindowAfterOneThousand()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-source-relationship-window-");
        var target = await CreateSourceSolutionAsync(fixture.DirectoryPath, fillerCount: 1000);
        var graph = await relationships.DependencyGraph(target, symbolIdentifier: "T:RelationshipProbe.LateRoot",
            direction: "outgoing", depth: 1, maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(graph, 65536, 4096);
        Assert.Contains("LateRoot", TextOf(graph), StringComparison.Ordinal);
        Assert.Contains("LaterDependency", TextOf(graph), StringComparison.Ordinal);
        Assert.Contains("isComplete\": true", TextOf(graph), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DependencyGraph_UsesHandoffProjectIdentityForEqualGenericTypeNames()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        var symbols = new SymbolTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-source-relationship-identity-");
        var target = await CreateDuplicateGenericSolutionAsync(fixture.DirectoryPath);
        var found = await symbols.FindSymbol(target, pattern: "Box", kind: "class", maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(found, 65536, 4096);
        var firstBoxLine = TextOf(found).Split('\n').Single(line => line.Contains("src/First/Box.cs", StringComparison.Ordinal));
        var firstHandoff = ReadHandoffFromLine(firstBoxLine);
        var graph = await relationships.DependencyGraph(target, symbolIdentifier: firstHandoff,
            direction: "outgoing", depth: 1, maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(graph, 65536, 4096);
        Assert.Contains("src/First/Box.cs", TextOf(graph), StringComparison.Ordinal);
        Assert.Contains("FirstDependency", TextOf(graph), StringComparison.Ordinal);
        Assert.DoesNotContain("src/Second/Box.cs", TextOf(graph), StringComparison.Ordinal);
        await AssertHandoffReachesBodyAsync(symbols, target, graph, "FirstDependency");
    }

    [Fact]
    public async Task SourceRelationshipHandlersReturnNavigableResultsErrorsAndBoundedProjections()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        var symbols = new SymbolTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-source-relationships-");
        var target = await CreateSourceSolutionAsync(fixture.DirectoryPath);
        const string runId = "M:RelationshipProbe.Entry.Run";
        const string targetId = "M:RelationshipProbe.Target.Read";
        const string baseId = "T:RelationshipProbe.Base";
        const string contractMethodId = "M:RelationshipProbe.IWorker.Work";
        const string genericId = "T:RelationshipProbe.GenericBox`1";
        var longCallId = "M:RelationshipProbe.Target.LongCaller" + new string('X', 500);

        var tree = await relationships.GetCallTree(target, runId, direction: "outgoing", depth: 1,
            maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(tree, 65536, 4096);
        Assert.Contains("Target.Read", TextOf(tree), StringComparison.Ordinal);
        await AssertHandoffReachesBodyAsync(symbols, target, tree, "public int Read");

        var references = await relationships.FindReferences(target, targetId,
            maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(references, 65536, 4096);
        Assert.Contains("Entry.Run", TextOf(references), StringComparison.Ordinal);
        Assert.Contains("column", TextOf(references), StringComparison.OrdinalIgnoreCase);
        await AssertHandoffReachesBodyAsync(symbols, target, references, "target.Read");

        var hierarchy = await relationships.GetTypeHierarchy(target, baseId,
            maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(hierarchy, 65536, 4096);
        Assert.Contains("Derived", TextOf(hierarchy), StringComparison.Ordinal);
        await AssertHandoffReachesBodyAsync(symbols, target, hierarchy, "Derived");

        var implementations = await relationships.FindImplementations(target, contractMethodId,
            maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(implementations, 65536, 4096);
        Assert.Contains("Derived.Work", TextOf(implementations), StringComparison.Ordinal);
        await AssertHandoffReachesBodyAsync(symbols, target, implementations, "public int Work");

        var dependencies = await relationships.DependencyGraph(target, symbolIdentifier: genericId,
            maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(dependencies, 65536, 4096);
        Assert.Contains("GenericBox", TextOf(dependencies), StringComparison.Ordinal);
        await AssertHandoffReachesBodyAsync(symbols, target, dependencies, "GenericBox");
        var fileDependencies = await relationships.DependencyGraph(target, filePath: "src/App/Relationships.cs",
            maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(fileDependencies, 65536, 4096);
        Assert.Contains("Entry", TextOf(fileDependencies), StringComparison.Ordinal);
        Assert.Contains("Derived", TextOf(fileDependencies), StringComparison.Ordinal);
        await AssertHandoffReachesBodyAsync(symbols, target, fileDependencies, "public static int Invoke");

        var origin = await relationships.ResolveTypeOrigin(target, typeName: "RelationshipProbe.Derived",
            maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(origin, 65536, 4096);
        Assert.Contains("assemblyOrigin", TextOf(origin), StringComparison.Ordinal);
        Assert.Contains("source", TextOf(origin), StringComparison.Ordinal);
        Assert.Contains("RelationshipProbe.App", TextOf(origin), StringComparison.Ordinal);
        var originByIdentifier = await relationships.ResolveTypeOrigin(target, symbolIdentifier: "T:RelationshipProbe.Derived",
            maxResponseBytes: 65536, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(originByIdentifier, 65536, 4096);
        Assert.Contains("RelationshipProbe.Derived", TextOf(originByIdentifier), StringComparison.Ordinal);

        var invalidDirection = await relationships.GetCallTree(target, "h:unknown", direction: "sideways",
            maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertErrorWithinBudget(invalidDirection, "INVALID_ARGUMENT", 16384, 2048);
        Assert.Contains("direction", TextOf(invalidDirection), StringComparison.Ordinal);
        var unknownReference = await relationships.FindReferences(target, "h:unknown",
            maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertErrorWithinBudget(unknownReference, "HANDOFF_UNKNOWN", 16384, 2048);
        var missingDependencySelection = await relationships.DependencyGraph(target,
            maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertErrorWithinBudget(missingDependencySelection, "INVALID_ARGUMENT", 16384, 2048);
        var conflictingDependencySelection = await relationships.DependencyGraph(target, filePath: "src/App/Relationships.cs",
            symbolIdentifier: genericId, maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertErrorWithinBudget(conflictingDependencySelection, "INVALID_ARGUMENT", 16384, 2048);
        var missingOriginSelection = await relationships.ResolveTypeOrigin(target,
            maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertErrorWithinBudget(missingOriginSelection, "INVALID_ARGUMENT", 16384, 2048);
        var conflictingOriginSelection = await relationships.ResolveTypeOrigin(target, symbolIdentifier: "T:RelationshipProbe.Derived",
            typeName: "RelationshipProbe.Derived", maxResponseBytes: 16384, maxResponseTokens: 2048);
        AssertErrorWithinBudget(conflictingOriginSelection, "INVALID_ARGUMENT", 16384, 2048);

        var longCallAscii = await relationships.GetCallTree(target, longCallId, direction: "outgoing", depth: 1, format: "ascii",
            maxResponseBytes: 65536, maxResponseTokens: 4096);
        await AssertProjectionMatchesAsync("get_call_tree ASCII", longCallAscii,
            (bytes, tokens, operation, continuation) => relationships.GetCallTree(target, longCallId, direction: "outgoing", depth: 1,
                format: "ascii", maxResponseBytes: bytes, maxResponseTokens: tokens, operationToken: operation, continuationToken: continuation),
            requireTokenRecovery: true);
        await AssertProjectionMatchesAsync("get_call_tree Mermaid",
            await relationships.GetCallTree(target, longCallId, direction: "outgoing", depth: 1, format: "mermaid",
                maxResponseBytes: 65536, maxResponseTokens: 4096),
            (bytes, tokens, operation, continuation) => relationships.GetCallTree(target, longCallId, direction: "outgoing", depth: 1,
                format: "mermaid", maxResponseBytes: bytes, maxResponseTokens: tokens, operationToken: operation, continuationToken: continuation),
            requireTokenRecovery: true);
        await AssertProjectionMatchesAsync("find_references", references,
            (bytes, tokens, operation, continuation) => relationships.FindReferences(target, targetId,
                maxResponseBytes: bytes, maxResponseTokens: tokens, operationToken: operation, continuationToken: continuation));
        await AssertProjectionMatchesAsync("get_type_hierarchy", hierarchy,
            (bytes, tokens, operation, continuation) => relationships.GetTypeHierarchy(target, baseId,
                maxResponseBytes: bytes, maxResponseTokens: tokens, operationToken: operation, continuationToken: continuation));
        await AssertProjectionMatchesAsync("find_implementations", implementations,
            (bytes, tokens, operation, continuation) => relationships.FindImplementations(target, contractMethodId,
                maxResponseBytes: bytes, maxResponseTokens: tokens, operationToken: operation, continuationToken: continuation));
        await AssertProjectionMatchesAsync("dependency_graph", dependencies,
            (bytes, tokens, operation, continuation) => relationships.DependencyGraph(target, symbolIdentifier: genericId,
                maxResponseBytes: bytes, maxResponseTokens: tokens,
                operationToken: operation, continuationToken: continuation));
        await AssertProjectionMatchesAsync("resolve_type_origin", origin,
            (bytes, tokens, operation, continuation) => relationships.ResolveTypeOrigin(target, typeName: "RelationshipProbe.Derived",
                maxResponseBytes: bytes, maxResponseTokens: tokens, operationToken: operation, continuationToken: continuation));
    }

    private static async Task AssertProjectionMatchesAsync(
        string label,
        CallToolResult broadResult,
        Func<int, int?, string?, string?, Task<CallToolResult>> invoke,
        bool requireTokenRecovery = false)
    {
        AssertSuccessWithinBudget(broadResult, 65536, 4096);
        var expected = BodyOf(TextOf(broadResult));
        var byteProjection = await ReadProjectionAsync(label + " byte", invoke, expected, 512, 4096);
        Assert.True(byteProjection.Pages >= 1, label);
        var tokenProjection = await ReadProjectionAsync(label + " token", invoke, expected, 65536, 120);
        if (requireTokenRecovery)
            Assert.True(tokenProjection.MinimumRecoveryObserved, $"{label} did not execute the advertised minimum-token recovery path.");
    }

    private static async Task<(int Pages, bool MinimumRecoveryObserved)> ReadProjectionAsync(
        string label,
        Func<int, int?, string?, string?, Task<CallToolResult>> invoke,
        string expected,
        int responseBytes,
        int responseTokens)
    {
        var output = new StringBuilder();
        var pages = 0;
        var minimumRecoveryObserved = false;
        var awaitingAdvertisedRetry = false;
        string? operation = null;
        string? continuation = null;
        var currentBytes = responseBytes;
        var currentTokens = responseTokens;
        var result = await invoke(currentBytes, currentTokens, operation, continuation);
        for (var request = 0; request < 200; request++)
        {
            var text = TextOf(result);
            AssertBudget(text, currentBytes, currentTokens);
            if (result.IsError == true && text.Contains("RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
            {
                Assert.False(awaitingAdvertisedRetry,
                    $"{label} returned another budget failure after retrying its advertised minimum pair: {text}");
                minimumRecoveryObserved = true;
                awaitingAdvertisedRetry = true;
                currentBytes = ReadBudget(text, "minimumResponseBytes");
                currentTokens = ReadBudget(text, "minimumResponseTokens");
                Assert.InRange(currentBytes, 512, 65536);
                Assert.True(currentTokens >= 1,
                    $"{label} advertised a recovery pair outside the public budget range: {text}");
                result = await invoke(currentBytes, currentTokens, operation, continuation);
                continue;
            }
            if (TryReadToken(text, "operationToken", out var pending))
            {
                operation = pending;
                await Task.Delay(50);
                result = await invoke(currentBytes, currentTokens, operation, continuation);
                continue;
            }
            if (text.Contains("operation=retry", StringComparison.Ordinal))
            {
                await Task.Delay(50);
                result = await invoke(currentBytes, currentTokens, operation, continuation);
                continue;
            }

            Assert.False(result.IsError ?? false, $"{label}: {text}");
            AssertBudget(text, currentBytes, currentTokens);
            output.Append(BodyOf(text));
            pages++;
            operation = null;
            awaitingAdvertisedRetry = false;
            if (!TryReadToken(text, "continuationToken", out continuation))
            {
                Assert.Equal(expected, output.ToString());
                return (pages, minimumRecoveryObserved);
            }
            currentBytes = responseBytes;
            currentTokens = responseTokens;
            result = await invoke(currentBytes, currentTokens, operation, continuation);
        }
        throw new Xunit.Sdk.XunitException($"{label} did not reach a final response page.");
    }

    private static async Task AssertHandoffReachesBodyAsync(SymbolTools symbols, string target, CallToolResult result, string expectedFragment)
    {
        var handoffs = FindHandoffs(BodyOf(TextOf(result)));
        Assert.NotEmpty(handoffs);
        var bodies = new List<string>();
        foreach (var handoff in handoffs)
        {
            var body = await symbols.GetSymbolBody(target, [handoff], maxResponseBytes: 16384, maxResponseTokens: 2048);
            if (body.IsError == true) { bodies.Add(TextOf(body)); continue; }
            AssertSuccessWithinBudget(body, 16384, 2048);
            var bodyText = TextOf(body);
            if (bodyText.Contains(expectedFragment, StringComparison.Ordinal)) return;
            bodies.Add(bodyText);
        }
        Assert.Fail($"No relationship handoff resolved to a body containing '{expectedFragment}'.\n{TextOf(result)}\nBodies:\n{string.Join("\n---\n", bodies)}");
    }

    private static string[] FindHandoffs(string text)
    {
        var handoffs = new List<string>();
        for (var position = 0; (position = text.IndexOf("h:", position, StringComparison.Ordinal)) >= 0;)
        {
            var start = position;
            position += 2;
            while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] is '_' or '-')) position++;
            if (position > start + 2) handoffs.Add(text[start..position]);
        }
        return handoffs.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string ReadHandoffFromLine(string line)
    {
        const string marker = "[handoff: ";
        var start = line.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, line);
        start += marker.Length;
        var end = line.IndexOf(']', start);
        Assert.True(end > start, line);
        return line[start..end];
    }

    private static async Task<string> CreateSourceSolutionAsync(string root, int fillerCount = 0)
    {
        var solution = Path.Combine(root, "Relationships.slnx");
        var projectDirectory = Path.Combine(root, "src", "App");
        Directory.CreateDirectory(projectDirectory);
        await File.WriteAllTextAsync(solution, "<Solution><Project Path=\"src/App/RelationshipProbe.App.csproj\" /></Solution>");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "RelationshipProbe.App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        var callerDeclarations = string.Join("\n", Enumerable.Range(0, 12).Select(index =>
            $"public static class Caller{index:D2} {{ public static int Invoke(Target target) => target.Read(); }}"));
        var source = $$"""
            namespace RelationshipProbe;
            public interface IWorker { int Work(); }
            public class Base { public virtual int Run() => 1; }
            public class Target : Base
            {
                public int Read() => 2;
                public void LongCaller{{new string('X', 500)}}() => _ = Read();
            }
            public sealed class Derived : Target, IWorker
            {
                public override int Run() => Read();
                public int Work() => Run();
            }
            public sealed class GenericBox<T> { public T Value = default!; }
            public sealed class GenericConsumer { public GenericBox<string> Box = new(); }
            public sealed class Entry
            {
                public int Run()
                {
                    var target = new Derived();
                    var first = target.Read();
                    var second = target.Read();
                    return first + second;
                }
            }
            {{callerDeclarations}}
            public static class Trigger
            {
                public static void Start() { new Entry().Run(); }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Relationships.cs"), source);
        foreach (var index in Enumerable.Range(0, fillerCount))
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, $"A{index:D4}.cs"), $"namespace Filler{index:D4}; public sealed class Filler{index:D4} {{ }}");
        if (fillerCount > 0)
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "ZLater.cs"),
                "namespace RelationshipProbe; public sealed class LaterDependency { } public sealed class LateRoot { public LaterDependency Value { get; set; } = new(); }");
        var nugetConfig = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(nugetConfig, "<configuration><packageSources><clear /></packageSources></configuration>");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("restore");
        start.ArgumentList.Add(solution);
        start.ArgumentList.Add("--configfile");
        start.ArgumentList.Add(nugetConfig);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start relationship fixture restore.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExitAsync();
        try { await exited.WaitAsync(TimeSpan.FromMinutes(2)); }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await exited;
            throw new TimeoutException($"Relationship fixture restore timed out.\n{await stderr}\n{await stdout}");
        }
        Assert.True(process.ExitCode == 0, $"Relationship fixture restore failed.\n{await stderr}\n{await stdout}");
        return solution;
    }

    private static async Task<string> CreateDuplicateGenericSolutionAsync(string root)
    {
        var solution = Path.Combine(root, "DuplicateGenericTypes.slnx");
        Directory.CreateDirectory(Path.Combine(root, "src", "First"));
        Directory.CreateDirectory(Path.Combine(root, "src", "Second"));
        await File.WriteAllTextAsync(solution,
            "<Solution><Project Path=\"src/First/First.csproj\" /><Project Path=\"src/Second/Second.csproj\" /></Solution>");
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>RelationshipProbe.App</AssemblyName><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>";
        await File.WriteAllTextAsync(Path.Combine(root, "src", "First", "First.csproj"), project);
        await File.WriteAllTextAsync(Path.Combine(root, "src", "Second", "Second.csproj"), project);
        await File.WriteAllTextAsync(Path.Combine(root, "src", "First", "Box.cs"),
            "namespace Duplicate; public sealed class Box<T> { public FirstDependency Value { get; set; } = new(); } public sealed class FirstDependency { }");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "Second", "Box.cs"),
            "namespace Duplicate; public sealed class Box<T> { public SecondDependency Value { get; set; } = new(); } public sealed class SecondDependency { }");
        var nugetConfig = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(nugetConfig, "<configuration><packageSources><clear /></packageSources></configuration>");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("restore");
        start.ArgumentList.Add(solution);
        start.ArgumentList.Add("--configfile");
        start.ArgumentList.Add(nugetConfig);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start duplicate-type fixture restore.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExitAsync();
        try { await exited.WaitAsync(TimeSpan.FromMinutes(2)); }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await exited;
            throw new TimeoutException($"Duplicate-type fixture restore timed out.\n{await stderr}\n{await stdout}");
        }
        Assert.True(process.ExitCode == 0, $"Duplicate-type fixture restore failed.\n{await stderr}\n{await stdout}");
        return solution;
    }
}
