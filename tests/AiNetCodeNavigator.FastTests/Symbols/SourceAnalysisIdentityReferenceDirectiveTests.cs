#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class SourceAnalysisIdentityReferenceDirectiveTests
{
    [Fact]
    public async Task ActiveReferenceDirectiveFailsClosedButInactiveAndLexicalTextDoesNotMatch()
    {
        using var fixture = TestTempDirectory.Create("source-analysis-identity-reference-directive-");
        using var workspace = TestWorkspaceBuilder.Create()
            .WithVirtualSolutionPath(fixture.GetPath("ReferenceDirective.slnx"))
            .WithProject(new ProjectSpec("ReferenceDirective", [("Main.csx", "public sealed class Probe { }")],
                VirtualProjectDirectory: "src/ReferenceDirective"))
            .Build();

        var original = workspace.Solution;
        var project = Assert.Single(original.Projects);
        var document = Assert.Single(project.Documents);
        var scriptOptions = Assert.IsType<CSharpParseOptions>(project.ParseOptions).WithKind(SourceCodeKind.Script);

        var active = original.WithProjectParseOptions(project.Id, scriptOptions)
            .WithDocumentText(document.Id, SourceText.From("#r \"external.dll\"\npublic sealed class Probe { }"));
        var activeResult = await ComputeWithBuilderProofAsync(original, active);
        Assert.False(activeResult.IsSuccess);
        Assert.Equal(NavigationErrorCodes.WorkspaceDiagnostic, activeResult.Error!.Value.Code);
        Assert.Contains("active #r directive", activeResult.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resolved reference inputs", activeResult.Error.Value.Message, StringComparison.OrdinalIgnoreCase);

        var inert = original.WithProjectParseOptions(project.Id, scriptOptions)
            .WithDocumentText(document.Id, SourceText.From("#if NEVER_DEFINED\n#r \"disabled.dll\"\n#endif\n// #r \"comment.dll\"\nvar text = \"#r \\\"literal.dll\\\"\";"));
        var inertResult = await ComputeWithBuilderProofAsync(original, inert);
        Assert.True(inertResult.IsSuccess, inertResult.Error?.Message);
    }

    private static async Task<Result<SourceIdentityFingerprintData>> ComputeWithBuilderProofAsync(
        Solution original,
        Solution updated)
    {
        var proof = WorkspaceInputProvenance.FindTestWorkspaceBuilderOutput(original);
        Assert.NotNull(proof);
        var carriedProof = proof!.CarryKnownTextChanges(original, updated);
        Assert.NotNull(carriedProof);
        var captured = MetadataReferenceImageCapture.Capture(updated, previousInputs: null,
            cancellationToken: CancellationToken.None, provenance: carriedProof);
        return await SourceAnalysisIdentityEncoder.ComputeAsync(
            new SourceIdentityValidatedSnapshot(captured.Solution, captured.Inputs), CancellationToken.None);
    }
}
