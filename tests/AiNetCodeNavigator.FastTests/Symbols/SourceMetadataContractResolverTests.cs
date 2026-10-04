#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Component")]
public sealed class SourceMetadataContractResolverTests
{
    private const string Contracts = """
        namespace External;
        public interface IContract {
            void Run(int value);
            void Run(string value);
            string Name { get; }
            event System.Action Changed;
        }
        public class Outer<T> { public interface INested { void Invoke(T value); } }
        """;

    [Theory]
    [InlineData("External.IContract", "T:External.IContract")]
    [InlineData("T:External.IContract", "T:External.IContract")]
    [InlineData("M:External.IContract.Run(System.Int32)", "M:External.IContract.Run(System.Int32)")]
    [InlineData("M:External.IContract.Run(System.String)", "M:External.IContract.Run(System.String)")]
    [InlineData("P:External.IContract.Name", "P:External.IContract.Name")]
    [InlineData("E:External.IContract.Changed", "E:External.IContract.Changed")]
    [InlineData("M:External.Outer`1.INested.Invoke(`0)", "M:External.Outer`1.INested.Invoke(`0)")]
    public async Task ExactDeclarationsRetainEveryRepeatedProvenOccurrence(string identifier, string expectedId)
    {
        using var directory = TestTempDirectory.Create("metadata-contract-repeat-");
        var path = AssemblyTestHelper.EmitAssembly(directory, "Contracts", Contracts);
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            new ProjectSpec("One", [("One.cs", "public class One { }")], AdditionalReferences: [MetadataReference.CreateFromFile(path)]),
            new ProjectSpec("Two", [("Two.cs", "public class Two { }")], AdditionalReferences: [MetadataReference.CreateFromFile(path)]),
            new ProjectSpec("Transitive", [("Three.cs", "public class Three : One { }")], ProjectReferences: ["One"]));
        var snapshot = Capture(workspace.Solution);
        var result = await SourceMetadataContractResolver.ResolveAsync(snapshot, identifier);
        Assert.True(result.IsSuccess, result.Error?.Message);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(expectedId, candidate.DeclarationId);
        Assert.Equal(path, candidate.OwnerPath);
        Assert.Equal(2, candidate.Occurrences.Length);
        Assert.Equal(2, candidate.Occurrences.Select(item => item.ProjectId).Distinct().Count());
        foreach (var occurrence in candidate.Occurrences)
        {
            Assert.Same(occurrence.Assembly, occurrence.Symbol.ContainingAssembly);
            Assert.Same(occurrence.Reference, snapshot.Solution.GetProject(occurrence.ProjectId)!.MetadataReferences.ElementAt(occurrence.ReferenceOrdinal));
            Assert.Same(occurrence.Assembly, occurrence.ProjectCompilation.GetAssemblyOrModuleSymbol(occurrence.Reference));
            Assert.True(occurrence.IsBoundInProject);
            Assert.Same(occurrence.ProjectCompilation, occurrence.OwnerCompilation);
            Assert.Equal(expectedId, DocumentationCommentId.CreateDeclarationId(occurrence.Symbol));
            Assert.NotEmpty(occurrence.Images);
        }
        Assert.Equal(3, snapshot.Solution.ProjectIds.Count); // Selection has not reduced the source search solution.
        var wrongOverload = await SourceMetadataContractResolver.ResolveAsync(snapshot, "M:External.IContract.Run(System.Boolean)");
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, wrongOverload.Error!.Value.Code);
    }

    [Fact]
    public async Task EqualAssemblyIdentitiesWithDifferentImagesRequireProvenOwnerSelection()
    {
        using var first = TestTempDirectory.Create("metadata-contract-first-");
        using var second = TestTempDirectory.Create("metadata-contract-second-");
        var firstPath = AssemblyTestHelper.EmitAssembly(first, "SameContracts", Contracts);
        var secondPath = AssemblyTestHelper.EmitAssembly(second, "SameContracts", Contracts + " public class DifferentImage { }");
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            new ProjectSpec("One", [("One.cs", "public class One { }")], AdditionalReferences: [MetadataReference.CreateFromFile(firstPath)]),
            new ProjectSpec("Two", [("Two.cs", "public class Two { }")], AdditionalReferences: [MetadataReference.CreateFromFile(secondPath)]));
        var snapshot = Capture(workspace.Solution);
        var result = await SourceMetadataContractResolver.ResolveAsync(snapshot, "M:External.IContract.Run(System.Int32)");
        Assert.Equal(NavigationErrorCodes.AmbiguousSymbol, result.Error!.Value.Code);
        Assert.Equal(2, result.Candidates.Length);
        Assert.Single(result.Candidates.Select(item => item.AssemblyIdentity).Distinct());
        Assert.Equal(2, result.Candidates.Select(item => item.Occurrences[0].Images[0].Sha256).Distinct().Count());
        foreach (var path in new[] { firstPath, secondPath })
        {
            var selected = await SourceMetadataContractResolver.ResolveAsync(snapshot, "M:External.IContract.Run(System.Int32)", path);
            Assert.True(selected.IsSuccess, selected.Error?.Message);
            Assert.Equal(path, selected.Selected!.OwnerPath);
            Assert.Single(selected.Selected.Occurrences);
            Assert.Equal(2, snapshot.Solution.ProjectIds.Count);
        }
        var unknown = await SourceMetadataContractResolver.ResolveAsync(snapshot, "External.IContract", Path.Combine(first.DirectoryPath, "unknown.dll"));
        Assert.Equal(NavigationErrorCodes.InvalidArgument, unknown.Error!.Value.Code);
        Assert.Equal("metadataOwnerPath", unknown.ArgumentName);
        Assert.False(File.Exists(Path.Combine(first.DirectoryPath, "unknown.dll")));
    }

    [Fact]
    public async Task IdenticalBytesAtDifferentPathsRemainSeparateOwners()
    {
        using var first = TestTempDirectory.Create("metadata-contract-copy-first-");
        using var second = TestTempDirectory.Create("metadata-contract-copy-second-");
        var firstPath = AssemblyTestHelper.EmitAssembly(first, "CopiedContracts", Contracts);
        var secondPath = Path.Combine(second.DirectoryPath, "CopiedContracts.dll");
        File.Copy(firstPath, secondPath);
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            new ProjectSpec("One", [("One.cs", "public class One { }")], AdditionalReferences: [MetadataReference.CreateFromFile(firstPath)]),
            new ProjectSpec("Two", [("Two.cs", "public class Two { }")], AdditionalReferences: [MetadataReference.CreateFromFile(secondPath)]));
        var snapshot = Capture(workspace.Solution);
        var result = await SourceMetadataContractResolver.ResolveAsync(snapshot, "External.IContract");
        Assert.False(result.IsSuccess);
        Assert.Equal(NavigationErrorCodes.AmbiguousSymbol, result.Error!.Value.Code);
        Assert.Equal(2, result.Candidates.Length);
        Assert.Single(result.Candidates.Select(item => item.Occurrences[0].Images[0].Sha256).Distinct());
    }

    [Fact]
    public async Task CompetingReferencesInsideOneCompilationDoNotSelectTheFirstImage()
    {
        using var first = TestTempDirectory.Create("metadata-contract-one-project-first-");
        using var second = TestTempDirectory.Create("metadata-contract-one-project-second-");
        var firstPath = AssemblyTestHelper.EmitAssembly(first, "SameContracts", Contracts);
        var secondPath = AssemblyTestHelper.EmitAssembly(second, "SameContracts", Contracts + " public class DifferentImage { }");
        using var workspace = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("App", [("App.cs", "public class App { }")],
            AdditionalReferences: [MetadataReference.CreateFromFile(firstPath), MetadataReference.CreateFromFile(secondPath)]));
        var snapshot = Capture(workspace.Solution);
        var result = await SourceMetadataContractResolver.ResolveAsync(snapshot, "External.IContract");
        Assert.False(result.IsSuccess);
        Assert.Equal(NavigationErrorCodes.AmbiguousSymbol, result.Error!.Value.Code);
        Assert.Equal(2, result.Candidates.Length);
        var unbound = Assert.Single(result.Candidates.SelectMany(item => item.Occurrences).Where(item => !item.IsBoundInProject));
        Assert.NotNull(unbound.OwnerPath);
        var selected = await SourceMetadataContractResolver.ResolveAsync(snapshot, "M:External.IContract.Run(System.Int32)", unbound.OwnerPath);
        Assert.True(selected.IsSuccess, selected.Error?.Message);
        var occurrence = Assert.Single(selected.Selected!.Occurrences);
        Assert.False(occurrence.IsBoundInProject);
        Assert.Same(occurrence.Assembly, occurrence.OwnerCompilation.GetAssemblyOrModuleSymbol(occurrence.Reference));
    }

    [Fact]
    public async Task InMemoryImageCannotInventOwnerFromClaimedFilePath()
    {
        using var directory = TestTempDirectory.Create("metadata-contract-memory-");
        var path = AssemblyTestHelper.EmitAssembly(directory, "MemoryContracts", Contracts);
        var reference = CapturedMetadataReference.CreateFromImage([.. await File.ReadAllBytesAsync(path)], filePath: path);
        using var workspace = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("App", [("App.cs", "public class App { }")], AdditionalReferences: [reference]));
        var snapshot = Capture(workspace.Solution);
        var result = await SourceMetadataContractResolver.ResolveAsync(snapshot, "External.IContract");
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Null(result.Selected!.OwnerPath);
        Assert.StartsWith("in-memory:", Assert.Single(Assert.Single(result.Selected.Occurrences).Images).CanonicalImageKey);
        var guessed = await SourceMetadataContractResolver.ResolveAsync(snapshot, "External.IContract", path);
        Assert.Equal(NavigationErrorCodes.InvalidArgument, guessed.Error!.Value.Code);
        Assert.Equal("metadataOwnerPath", guessed.ArgumentName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative.dll")]
    public async Task InvalidOwnerSelectorsAreArgumentErrors(string selector)
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution("public class App { }");
        var result = await SourceMetadataContractResolver.ResolveAsync(Capture(workspace.Solution), "System.IDisposable", selector);
        Assert.Equal(NavigationErrorCodes.InvalidArgument, result.Error!.Value.Code);
        Assert.Equal("metadataOwnerPath", result.ArgumentName);
    }

    [Fact]
    public async Task BclMemberResolutionIsExactAndSourceDeclarationsAreExcluded()
    {
        using var workspace = TestWorkspaceBuilder.CreateSolution("namespace Local; public interface IContract { void Run(); }");
        var snapshot = Capture(workspace.Solution);
        var result = await SourceMetadataContractResolver.ResolveAsync(snapshot, "M:System.IDisposable.Dispose");
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.IsAssignableFrom<IMethodSymbol>(result.Selected!.Occurrences[0].Symbol);
        Assert.Equal("M:System.IDisposable.Dispose", result.Selected.DeclarationId);
        var source = await SourceMetadataContractResolver.ResolveAsync(snapshot, "T:Local.IContract");
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, source.Error!.Value.Code);
    }

    private static SourceIdentityValidatedSnapshot Capture(Solution solution)
    {
        var captured = MetadataReferenceImageCapture.Capture(solution, previousInputs: null);
        return new(captured.Solution, captured.Inputs);
    }
}
