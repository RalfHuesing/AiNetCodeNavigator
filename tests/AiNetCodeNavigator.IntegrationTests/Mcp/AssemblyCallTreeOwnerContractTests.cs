#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
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
public sealed class AssemblyCallTreeOwnerContractTests
{
    [Fact]
    public async Task IncomingClosureMapsSameNamedCallersToTheirOwnersBeforeMerging()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);
        var symbols = new SymbolTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-assembly-calltree-owner-contract-");
        var leafPath = AssemblyTestHelper.EmitAssembly(fixture, "CallTreeLeaf",
            "namespace CallTreeLeaf; public sealed class Leaf { public int Run() => 7; }");
        var bridgePath = AssemblyTestHelper.EmitAssembly(fixture, "CallTreeBridge",
            "namespace CallTreeBridge; public sealed class Bridge { public int Run() => new CallTreeLeaf.Leaf().Run(); }", leafPath);
        var rootPath = AssemblyTestHelper.EmitAssembly(fixture, "CallTreeRoot",
            "namespace CallTreeRoot; public sealed class Root { public int Run() => new CallTreeBridge.Bridge().Run(); }", bridgePath);

        const string leafDeclarationId = "M:CallTreeLeaf.Leaf.Run";
        var graphResult = await PollAsync(operation => relationships.GetCallTree(rootPath, leafDeclarationId,
            direction: "incoming", depth: 3, topN: 10, includeReferences: true, includeDiagnostics: true,
            maxResponseBytes: 65536, maxResponseTokens: 12000, operationToken: operation));
        var graphText = TextOf(graphResult);
        Assert.False(TryReadToken(graphText, "continuationToken", out _),
            $"The focused three-owner graph should fit one response and expose domain completeness directly: {graphText}");
        Assert.Contains("CallTreeBridge", graphText, StringComparison.Ordinal);
        Assert.Contains("CallTreeRoot", graphText, StringComparison.Ordinal);
        Assert.Contains("analysisCompleteness=complete", graphText, StringComparison.Ordinal);
        Assert.DoesNotContain("omissions=none", graphText, StringComparison.Ordinal);

        var leafReference = ReadOwnerReference(graphText, leafPath, "M:CallTreeLeaf.Leaf.Run~System.Int32");
        var bridgeReference = ReadOwnerReference(graphText, bridgePath, "M:CallTreeBridge.Bridge.Run~System.Int32");
        var rootReference = ReadOwnerReference(graphText, rootPath, "M:CallTreeRoot.Root.Run~System.Int32");
        Assert.Equal(3, new[] { leafReference, bridgeReference, rootReference }.Distinct(StringComparer.Ordinal).Count());

        await AssertReferenceOpensOwnedBodyAsync(symbols, leafPath, leafReference, "7");
        await AssertReferenceOpensOwnedBodyAsync(symbols, bridgePath, bridgeReference, "new ", ".Run()");
        await AssertReferenceOpensOwnedBodyAsync(symbols, rootPath, rootReference, "new ", ".Run()");
    }

    private static string ReadOwnerReference(string text, string ownerPath, string expectedDeclarationId)
    {
        var ownerLines = text.Split('\n').Where(line =>
            line.Contains("targetPath: " + ownerPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.True(ownerLines.Length == 1,
            $"Expected one rendered handoff for owner '{ownerPath}', found {ownerLines.Length}. Full call tree:\n{text}");
        var references = ReadStableReferences(ownerLines[0]);
        Assert.True(references.Length == 1,
            $"Expected one stable reference for owner '{ownerPath}', found {references.Length}. Full call tree:\n{text}");
        var reference = references[0];
        Assert.True(StableSymbolReferenceCodec.TryParse(reference, out var parsed, out var error), error?.Message);
        var assemblyReference = Assert.IsType<StableSymbolReference.Assembly>(parsed);
        Assert.Equal(Path.GetFileNameWithoutExtension(ownerPath), assemblyReference.SimpleName);
        Assert.Equal(expectedDeclarationId, assemblyReference.Id);
        return reference;
    }

    private static async Task AssertReferenceOpensOwnedBodyAsync(
        SymbolTools symbols,
        string ownerPath,
        string reference,
        params string[] expectedBodyFragments)
    {
        var body = await PollAsync(operation => symbols.GetSymbolBody(ownerPath, [reference],
            maxResponseBytes: 16384, maxResponseTokens: 2048, operationToken: operation));
        var text = TextOf(body);
        Assert.Contains("Resolution status: resolved (availability: available)", text, StringComparison.Ordinal);
        foreach (var fragment in expectedBodyFragments)
            Assert.Contains(fragment, text, StringComparison.Ordinal);
    }

    private static async Task<CallToolResult> PollAsync(Func<string?, Task<CallToolResult>> invoke)
    {
        string? operation = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var result = await invoke(operation);
            var text = TextOf(result);
            if (!text.Contains("operation=running", StringComparison.Ordinal))
            {
                Assert.False(result.IsError ?? false, text);
                return result;
            }
            operation = text.Split('\n').FirstOrDefault(line => line.StartsWith("operationToken=", StringComparison.Ordinal))?
                ["operationToken=".Length..];
            Assert.False(string.IsNullOrWhiteSpace(operation), text);
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException("The assembly call-tree owner contract did not complete after operation polling.");
    }
}
