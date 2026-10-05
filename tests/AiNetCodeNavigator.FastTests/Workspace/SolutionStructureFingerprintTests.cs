#nullable enable

using System.Collections.Generic;
using System.IO;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Builders;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Workspace;

[Trait("Category", "Unit")]
public sealed class SolutionStructureFingerprintTests
{
    [Fact]
    public void SourceMembershipIgnoresBuildTreesButTracksOrdinaryAndExternalGlobFiles()
    {
        using var fixture = TestTempDirectory.Create("structure-source-membership-");
        var solutionPath = fixture.CreateFile("Sample.slnx", "<Solution />");
        fixture.CreateFile("App/App.csproj", "<Project />");
        var source = fixture.CreateFile("App/Source.cs", "class Source { }");
        var external = fixture.CreateFile("External/Initial.cs", "class Initial { }");
        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("App", [(source, "class Source { }")], VirtualProjectDirectory: "App")).Build();
        var inputs = new SolutionStructureInputs([], [], [Path.GetDirectoryName(external)!], [], [],
            new Dictionary<string, ConfiguredTargetFrameworks>());
        var initial = SolutionStructureFingerprint.Create(workspace.Solution, solutionPath, inputs);
        foreach (var directory in new[] { "App/bin", "App/OBJ", "App/Nested/.git", "External/bin" })
            fixture.CreateFile(directory + "/Nested/Excluded.cs", "class Excluded { }");
        Assert.Equal(initial, SolutionStructureFingerprint.Create(workspace.Solution, solutionPath, inputs));
        fixture.CreateFile("App/Nested/Added.cs", "class Added { }");
        var withSource = SolutionStructureFingerprint.Create(workspace.Solution, solutionPath, inputs);
        Assert.NotEqual(initial, withSource);
        fixture.CreateFile("External/Added.cs", "class AddedExternal { }");
        Assert.NotEqual(withSource, SolutionStructureFingerprint.Create(workspace.Solution, solutionPath, inputs));
    }

    [Fact]
    public void ExplicitBuildTreeDocumentsAndWildcardImportsRemainTracked()
    {
        MSBuildSolutionLoader.EnsureMSBuildRegistered();
        using var fixture = TestTempDirectory.Create("structure-explicit-inputs-");
        var solutionPath = fixture.CreateFile("Sample.slnx", "<Solution />");
        fixture.CreateFile("App/App.csproj", "<Project />");
        var source = fixture.CreateFile("App/bin/Explicit.cs", "class Explicit { }");
        using var workspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(solutionPath)
            .WithProject(new ProjectSpec("App", [(source, "class Explicit { }")], VirtualProjectDirectory: "App")).Build();
        var pattern = fixture.GetPath("App/obj/*.targets");
        var inputs = new SolutionStructureInputs([], [], [], [pattern], [], new Dictionary<string, ConfiguredTargetFrameworks>());
        var initial = SolutionStructureFingerprint.Create(workspace.Solution, solutionPath, inputs);
        var import = fixture.CreateFile("App/obj/Custom.targets", "<Project />");
        var withImport = SolutionStructureFingerprint.Create(workspace.Solution, solutionPath, inputs);
        Assert.NotEqual(initial, withImport);
        var timestamp = File.GetLastWriteTimeUtc(import);
        File.WriteAllText(import, "<Project/>");
        File.SetLastWriteTimeUtc(import, timestamp);
        var changedImport = SolutionStructureFingerprint.Create(workspace.Solution, solutionPath, inputs);
        Assert.NotEqual(withImport, changedImport);
        File.Delete(source);
        Assert.NotEqual(changedImport, SolutionStructureFingerprint.Create(workspace.Solution, solutionPath, inputs));
    }
}
