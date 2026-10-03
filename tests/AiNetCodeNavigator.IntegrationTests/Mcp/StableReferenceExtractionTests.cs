#nullable enable

using System;
using System.Linq;
using AiNetCodeNavigator.Core.CallTree;
using AiNetCodeNavigator.Core.Skeletons;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Builders;
using Xunit;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class StableReferenceExtractionTests
{
    [Fact]
    public void JsonAndRenderedCallTreeReferencesRemainExactAndIgnoreNullOrMalformedValues()
    {
        const string declarationId = "M:Probe.Worker.Run``1(System.Int32,System.String)~System.Int32";
        var reference = StableSymbolReferenceCodec.Format(new StableSymbolReference.Assembly("Probe", declarationId));
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            handoffId = reference,
            fromHandoffId = reference,
            toHandoffId = (string?)null,
            nested = new { enclosingSymbolHandoffId = reference },
            malformed = "asm:Probe|M:broken%41",
        });

        Assert.Equal(new[] { reference }, IntegrationMcpAssertions.ReadStableReferences(json));
        Assert.True(StableSymbolReferenceCodec.TryParse(reference, out var parsed, out var error), error?.Message);
        Assert.Equal(reference, StableSymbolReferenceCodec.Format(parsed!));

        var graph = new CallGraphPayload("N1",
            [new CallGraphNode("N1", declarationId, "Worker.Run", "", "method", reference,
                @"C:\owner with spaces\Probe.dll")],
            Array.Empty<CallGraphEdge>());
        var mermaid = CallTreeMermaidRenderer.RenderMermaid(graph);

        Assert.Contains($"handoffId: {reference}; targetPath: C:\\owner with spaces\\Probe.dll", mermaid, StringComparison.Ordinal);
        Assert.Equal(new[] { reference }, IntegrationMcpAssertions.ReadStableReferences(mermaid));

        var renderedText = CallGraphTextRenderer.RenderAscii(graph);
        Assert.Contains(reference, IntegrationMcpAssertions.ReadStableReferences(renderedText));
    }

    [Fact]
    public async Task ClassStructureMarkdownReferenceRoundTripsWithEscapedCellDelimiterAndMethodPunctuation()
    {
        const string source = "namespace ReferenceProbe; public sealed class Worker { public int Run(int left, string right) => left; }";
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\ReferenceExtraction.slnx",
            new ProjectSpec("ReferenceProbe", [("Worker.cs", source)]));

        var payload = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(fixture.Solution, "ReferenceProbe.Worker"));
        Assert.NotNull(payload);
        var run = Assert.Single(payload.Members, member => member.Name == "Run");
        var reference = Assert.IsType<string>(run.HandoffId);
        Assert.Contains("(", reference, StringComparison.Ordinal);
        Assert.Contains(",", reference, StringComparison.Ordinal);
        Assert.Contains(")", reference, StringComparison.Ordinal);

        var markdown = ClassStructureScanner.RenderMarkdown(payload);
        Assert.Contains(reference.Replace("|", "\\|", StringComparison.Ordinal), markdown, StringComparison.Ordinal);
        var extracted = IntegrationMcpAssertions.ReadStableReferences(markdown);
        Assert.Contains(reference, extracted);
        Assert.All(extracted, value =>
        {
            Assert.True(StableSymbolReferenceCodec.TryParse(value, out var parsed, out var error), error?.Message);
            Assert.Equal(value, StableSymbolReferenceCodec.Format(parsed!));
        });
    }

    [Fact]
    public async Task SkeletonMarkdownCrlfLinesPreserveEveryGenericAndMethodReference()
    {
        const string source = "namespace ReferenceProbe; public sealed class Worker<T> "
            + "{ public TResult Convert<TResult>(T value, int count) => default!; "
            + "public int Read() => 1; }";
        using var fixture = TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\SkeletonReferenceExtraction.slnx",
            new ProjectSpec("ReferenceProbe", [("Worker.cs", source)]));
        var document = fixture.Solution.Projects.Single(project => project.Name == "ReferenceProbe")
            .Documents.Single(document => document.Name == "Worker.cs");

        var solutionPath = fixture.Solution.FilePath;
        Assert.False(string.IsNullOrWhiteSpace(solutionPath), "The renderer fixture must have a concrete solution path.");
        var markdown = await FileSkeletonBuilder.BuildMarkdownForDocumentAsync(document,
            solutionPath ?? throw new Xunit.Sdk.XunitException("The renderer fixture lost its solution path."));
        var crlfMarkdown = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);
        Assert.Contains("\r\n", crlfMarkdown, StringComparison.Ordinal);

        var references = IntegrationMcpAssertions.ReadStableReferences(crlfMarkdown);
        Assert.True(references.Length >= 3, $"Expected type and multiple member references. Skeleton:\n{crlfMarkdown}");
        Assert.Equal(references.Length, references.Distinct(StringComparer.Ordinal).Count());
        var decoded = references.Select(reference =>
        {
            Assert.True(StableSymbolReferenceCodec.TryParse(reference, out var parsed, out var error), error?.Message);
            Assert.Equal(reference, StableSymbolReferenceCodec.Format(parsed!));
            return Assert.IsType<StableSymbolReference.Source>(parsed);
        }).ToArray();
        var convert = Assert.Single(decoded, reference => reference.DeclarationId.Contains("Convert``1", StringComparison.Ordinal));
        Assert.Contains("(`0,System.Int32)", convert.DeclarationId, StringComparison.Ordinal);
        Assert.Contains("~``0", convert.DeclarationId, StringComparison.Ordinal);
        Assert.Contains(decoded, reference => reference.DeclarationId == "M:ReferenceProbe.Worker`1.Read~System.Int32");
    }
}
