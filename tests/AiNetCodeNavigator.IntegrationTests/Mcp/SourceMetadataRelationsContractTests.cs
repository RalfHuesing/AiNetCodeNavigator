using System;
using System.Linq;
using System.Text.Json;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class SourceMetadataRelationsContractTests
{
    private static string JsonBody(string text)
    {
        var start = text.IndexOf('{');
        Assert.True(start >= 0, text);
        return text[start..];
    }

    private static ProjectSpec Materialize(TestTempDirectory fixture, ProjectSpec project) => project with
    {
        VirtualProjectDirectory = project.Name,
        Documents = project.Documents.Select(document =>
            (fixture.CreateFile(project.Name + "/" + document.FileName, document.Content), document.Content)).ToArray(),
    };

    private const string Api = "namespace External; public interface IContract { void Run(int value); void Run(string value); string Name { get; } event System.Action Changed; } public class Concrete { public void Run() { } }";
    private const string Worker = "public class Worker : External.IContract { public void Run(int value) { } public void Run(string value) { } public string Name => \"worker\"; public event System.Action Changed { add { } remove { } } }";

    [Fact]
    public async Task MetadataOwnersPreserveSourcePagingScopeAndSeparateExactBodyFollowups()
    {
        using var first = TestTempDirectory.Create("metadata-public-first-");
        using var second = TestTempDirectory.Create("metadata-public-second-");
        var firstPath = AssemblyTestHelper.EmitAssembly(first, "SameContracts", Api);
        var secondPath = AssemblyTestHelper.EmitAssembly(second, "SameContracts", Api + " public class OtherImage { }");
        var target = first.CreateFile("Source.slnx", "<Solution />");
        ProjectSpec[] projects =
        [
            new ProjectSpec("Bridge", [("Worker.cs", Worker), ("Generated.g.cs", Worker.Replace("Worker", "GeneratedWorker", StringComparison.Ordinal))], AdditionalReferences: [MetadataReference.CreateFromFile(firstPath)]),
            new ProjectSpec("App", [("App.cs", "public class DerivedWorker : Worker { }")], ProjectReferences: ["Bridge"]),
            new ProjectSpec("Repeat", [("Repeat.cs", Worker.Replace("Worker", "RepeatWorker", StringComparison.Ordinal))], AdditionalReferences: [MetadataReference.CreateFromFile(firstPath)]),
            new ProjectSpec("Contract.Tests", [("Tests.cs", Worker.Replace("Worker", "TestWorker", StringComparison.Ordinal))], AdditionalReferences: [MetadataReference.CreateFromFile(firstPath)]),
            new ProjectSpec("Other", [("Other.cs", Worker.Replace("Worker", "OtherWorker", StringComparison.Ordinal))], AdditionalReferences: [MetadataReference.CreateFromFile(secondPath)]),
        ];
        await using var host = InMemorySourceTestHost.Create(target, projects.Select(project => Materialize(first, project)));
        var relations = new RelationshipTools(host.Runtime);
        var ambiguity = await relations.GetTypeRelations(target, "External.IContract", "implementations", maxResponseBytes: 65536);
        Assert.True(ambiguity.IsError);
        Assert.Contains("AMBIGUOUS_SYMBOL", TextOf(ambiguity), StringComparison.Ordinal);
        Assert.Contains("assemblyIdentity", TextOf(ambiguity), StringComparison.Ordinal);
        Assert.Contains("T:External.IContract", TextOf(ambiguity), StringComparison.Ordinal);
        Assert.Contains(JsonSerializer.Serialize(firstPath), TextOf(ambiguity), StringComparison.Ordinal);
        Assert.Contains(JsonSerializer.Serialize(secondPath), TextOf(ambiguity), StringComparison.Ordinal);

        string? cursor = null;
        var refs = new List<string>();
        string? rootRef = null;
        do
        {
            var response = await relations.GetTypeRelations(target, "External.IContract", "implementations", metadataOwnerPath: firstPath,
                maxResults: 1, maxResponseBytes: 65536, maxResponseTokens: 8192, resultCursor: cursor);
            AssertSuccessWithinBudget(response, 65536, 8192);
            using var page = JsonDocument.Parse(JsonBody(TextOf(response)));
            Assert.Equal(4, page.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(target, page.RootElement.GetProperty("implementationOwnerTargetPath").GetString());
            var root = page.RootElement.GetProperty("metadataRoot");
            Assert.Equal(firstPath, root.GetProperty("ownerTargetPath").GetString());
            Assert.True(root.GetProperty("stableReferenceAvailable").GetBoolean());
            rootRef = root.GetProperty("handoffId").GetString();
            var item = Assert.Single(page.RootElement.GetProperty("implementations").EnumerateArray());
            Assert.DoesNotContain("Other", item.GetProperty("signature").GetString(), StringComparison.Ordinal);
            refs.Add(item.GetProperty("handoffId").GetString()!);
            cursor = page.RootElement.TryGetProperty("resultCursor", out var next) ? next.GetString() : null;
            if (refs.Count == 1)
            {
                var changed = await relations.GetTypeRelations(target, "External.IContract", "implementations", metadataOwnerPath: secondPath,
                    maxResults: 1, resultCursor: cursor);
                Assert.Contains("RESULT_CURSOR_ARGUMENT_MISMATCH", TextOf(changed), StringComparison.Ordinal);
            }
        } while (cursor is not null);
        Assert.Equal(4, refs.Distinct().Count());
        Assert.All(refs, reference => Assert.StartsWith("src:", reference, StringComparison.Ordinal));
        Assert.StartsWith("asm:SameContracts|", rootRef, StringComparison.Ordinal);
        var symbols = new SymbolTools(host.Runtime);
        AssertSuccessWithinBudget(await symbols.GetSymbolBody(target, [refs[0]], maxResponseBytes: 65536, maxResponseTokens: 8192), 65536, 8192);
        var rootBody = await symbols.GetSymbolBody(firstPath, [rootRef!], maxResponseBytes: 65536, maxResponseTokens: 8192);
        AssertSuccessWithinBudget(rootBody, 65536, 8192);
        Assert.Contains("IContract", TextOf(rootBody), StringComparison.Ordinal);

        foreach (var (scope, generated, count) in new[] { ("production", false, 3), ("tests", false, 1), ("all", true, 5) })
        {
            var response = await relations.GetTypeRelations(target, "External.IContract", "hierarchy", metadataOwnerPath: firstPath,
                scopeType: scope, includeGenerated: generated, maxResponseBytes: 65536);
            Assert.False(response.IsError, TextOf(response));
            using var page = JsonDocument.Parse(JsonBody(TextOf(response)));
            Assert.Equal(count, page.RootElement.GetProperty("totalSubtypes").GetInt32());
        }
        foreach (var id in new[] { "M:External.IContract.Run(System.Int32)", "P:External.IContract.Name", "E:External.IContract.Changed" })
        {
            var response = await relations.GetTypeRelations(target, id, "implementations", metadataOwnerPath: firstPath, maxResponseBytes: 65536);
            Assert.False(response.IsError, TextOf(response));
            using var page = JsonDocument.Parse(JsonBody(TextOf(response)));
            Assert.Equal(3, page.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(id, page.RootElement.GetProperty("metadataRoot").GetProperty("declarationId").GetString());
        }
        foreach (var id in new[] { "M:External.IContract.Run(System.Int32)", "P:External.IContract.Name", "E:External.IContract.Changed" })
            Assert.Contains("INVALID_ARGUMENT", TextOf(await relations.GetTypeRelations(target, id, "hierarchy", metadataOwnerPath: firstPath)), StringComparison.Ordinal);
        Assert.Contains("INVALID_ARGUMENT", TextOf(await relations.GetTypeRelations(target, "M:External.Concrete.Run", "implementations", metadataOwnerPath: firstPath)), StringComparison.Ordinal);
        Assert.Contains("SYMBOL_NOT_FOUND", TextOf(await relations.GetTypeRelations(target, "M:External.IContract.Run(System.Boolean)", "implementations", metadataOwnerPath: firstPath)), StringComparison.Ordinal);
        Assert.Contains("$.metadataOwnerPath", TextOf(await relations.GetTypeRelations(target, refs[0], "implementations", metadataOwnerPath: firstPath)), StringComparison.Ordinal);
        Assert.Contains("$.metadataOwnerPath", TextOf(await relations.GetTypeRelations(firstPath, "External.IContract", "hierarchy", metadataOwnerPath: firstPath)), StringComparison.Ordinal);
        Assert.Contains("$.metadataOwnerPath", TextOf(await relations.GetTypeRelations(target, "External.IContract", "hierarchy", metadataOwnerPath: "relative.dll")), StringComparison.Ordinal);
        Assert.Contains("$.metadataOwnerPath", TextOf(await relations.GetTypeRelations(target, "External.IContract", "hierarchy", metadataOwnerPath: first.GetPath("unknown.dll"))), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BclFallbackPreservesSourceAmbiguityAndStableReferenceFailures()
    {
        using var fixture = TestTempDirectory.Create("metadata-public-source-first-");
        var target = fixture.CreateFile("Source.slnx", "<Solution />");
        ProjectSpec[] projects =
        [
            new ProjectSpec("One", [("One.cs", "namespace Clash; public interface IContract { } public sealed class Resource : System.IDisposable { public void Dispose() { } }")]),
            new ProjectSpec("Two", [("Two.cs", "namespace Clash; public interface IContract { }")]),
        ];
        await using var host = InMemorySourceTestHost.Create(target, projects.Select(project => Materialize(fixture, project)));
        var relations = new RelationshipTools(host.Runtime);
        var bcl = await relations.GetTypeRelations(target, "M:System.IDisposable.Dispose", "implementations", maxResponseBytes: 65536);
        Assert.False(bcl.IsError, TextOf(bcl));
        using var page = JsonDocument.Parse(JsonBody(TextOf(bcl)));
        Assert.Equal("Dispose", Assert.Single(page.RootElement.GetProperty("implementations").EnumerateArray()).GetProperty("symbolName").GetString());
        Assert.Contains("AMBIGUOUS_SYMBOL", TextOf(await relations.GetTypeRelations(target, "T:Clash.IContract", "hierarchy")), StringComparison.Ordinal);
        Assert.Contains("INVALID_SYMBOL_REFERENCE", TextOf(await relations.GetTypeRelations(target, "src:bad", "hierarchy")), StringComparison.Ordinal);
        Assert.Contains("TARGET_MISMATCH", TextOf(await relations.GetTypeRelations(target, "asm:Other|T:Clash.IContract", "hierarchy")), StringComparison.Ordinal);
        var found = await new SymbolTools(host.Runtime).FindSymbol(target, pattern: "Clash.Resource", kind: "class");
        var sourceReference = Assert.Single(ReadStableReferences(TextOf(found)));
        Assert.Contains("SYMBOL_NOT_FOUND", TextOf(await relations.GetTypeRelations(target,
            sourceReference.Replace("T:Clash.Resource", "T:Clash.Missing", StringComparison.Ordinal), "hierarchy")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CapturedInMemoryMetadataHasSourceImplementersButNoInventedDllHandoff()
    {
        using var fixture = TestTempDirectory.Create("metadata-public-memory-");
        var dll = AssemblyTestHelper.EmitAssembly(fixture, "MemoryContracts", Api);
        var reference = CapturedMetadataReference.CreateFromImage([.. await System.IO.File.ReadAllBytesAsync(dll)], filePath: dll);
        var target = fixture.CreateFile("Source.slnx", "<Solution />");
        await using var host = InMemorySourceTestHost.Create(target,
            [Materialize(fixture, new ProjectSpec("App", [("App.cs", Worker)], AdditionalReferences: [reference]))]);
        var relations = new RelationshipTools(host.Runtime);
        var response = await relations.GetTypeRelations(target, "External.IContract", "implementations", maxResponseBytes: 65536);
        Assert.False(response.IsError, TextOf(response));
        using var page = JsonDocument.Parse(JsonBody(TextOf(response)));
        Assert.Equal(1, page.RootElement.GetProperty("totalCount").GetInt32());
        var root = page.RootElement.GetProperty("metadataRoot");
        Assert.False(root.GetProperty("stableReferenceAvailable").GetBoolean());
        Assert.False(root.TryGetProperty("handoffId", out var handoff) && handoff.ValueKind == JsonValueKind.String);
        Assert.False(root.TryGetProperty("ownerTargetPath", out var owner) && owner.ValueKind == JsonValueKind.String);
        Assert.Contains("$.metadataOwnerPath", TextOf(await relations.GetTypeRelations(target, "External.IContract", "implementations", metadataOwnerPath: dll)), StringComparison.Ordinal);
    }
}
