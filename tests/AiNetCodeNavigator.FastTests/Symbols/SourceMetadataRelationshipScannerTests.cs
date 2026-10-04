#nullable enable

using System;
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
public sealed class SourceMetadataRelationshipScannerTests
{
    internal const string Api = """
        namespace External;
        public interface IContract { void Run(int value); void Run(string value); string Name { get; } event System.Action Changed; }
        public abstract class Base { public abstract void Stop(); public virtual string Label => "base"; public virtual event System.Action Changed { add { } remove { } } public void Concrete() { } }
        """;
    internal const string Bridge = """
        public class Worker : External.IContract { public void Run(int value) { } public void Run(string value) { } public string Name => "worker"; public event System.Action Changed { add { } remove { } } }
        public class OverrideWorker : External.Base { public override void Stop() { } public override string Label => "worker"; public override event System.Action Changed { add { } remove { } } }
        """;

    [Theory]
    [InlineData("External.IContract", true, "Worker", "DerivedWorker")]
    [InlineData("External.IContract", false, "Worker", "DerivedWorker")]
    [InlineData("M:External.IContract.Run(System.Int32)", false, "Run", null)]
    [InlineData("P:External.IContract.Name", false, "Name", null)]
    [InlineData("E:External.IContract.Changed", false, "Changed", null)]
    [InlineData("M:External.Base.Stop", false, "Stop", "Stop")]
    [InlineData("P:External.Base.Label", false, "Label", null)]
    [InlineData("E:External.Base.Changed", false, "Changed", null)]
    public async Task SelectedImageFindsActualMappingsAndTransitiveSourceRelations(string identifier, bool hierarchy,
        string firstName, string? secondName)
    {
        using var first = TestTempDirectory.Create("metadata-relationships-first-");
        using var second = TestTempDirectory.Create("metadata-relationships-second-");
        var firstPath = AssemblyTestHelper.EmitAssembly(first, "SharedContracts", Api);
        var secondPath = AssemblyTestHelper.EmitAssembly(second, "SharedContracts", Api + " public class ImageTwo { }");
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            new ProjectSpec("Bridge", [("Bridge.cs", Bridge)], AdditionalReferences: [MetadataReference.CreateFromFile(firstPath)]),
            new ProjectSpec("App", [("App.cs", "public class DerivedWorker : Worker { } public class DerivedOverride : OverrideWorker { public override void Stop() { } }")], ProjectReferences: ["Bridge"], AdditionalReferences: [MetadataReference.CreateFromFile(firstPath)]),
            new ProjectSpec("OtherImage", [("Other.cs", Bridge.Replace("Worker", "OtherWorker", StringComparison.Ordinal))], AdditionalReferences: [MetadataReference.CreateFromFile(secondPath)]));
        var snapshot = Capture(workspace.Solution);
        var appCompilation = await snapshot.Solution.Projects.Single(project => project.Name == "App").GetCompilationAsync();
        Assert.DoesNotContain(appCompilation!.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var selected = await SourceMetadataContractResolver.ResolveAsync(snapshot, identifier, firstPath);
        Assert.True(selected.IsSuccess, selected.Error?.Message);
        var result = await SourceMetadataRelationshipScanner.CollectAsync(snapshot, selected.Selected!, hierarchy, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(secondName is null ? 1 : 2, result.Value.Length);
        Assert.Contains(result.Value, symbol => symbol.Name == firstName);
        if (secondName is not null) Assert.Contains(result.Value, symbol => symbol.Name == secondName);
        Assert.DoesNotContain(result.Value, symbol => symbol.ContainingAssembly.Name == "OtherImage");
        if (identifier.StartsWith("M:External.IContract", StringComparison.Ordinal))
            Assert.All(result.Value, symbol => Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<IMethodSymbol>(symbol).Parameters.Single().Type.SpecialType));
    }

    [Fact]
    public async Task TransitiveSourceBaseWithoutOwnMetadataReferenceRetainsOriginalImageProof()
    {
        using var directory = TestTempDirectory.Create("metadata-relationships-transitive-");
        var path = AssemblyTestHelper.EmitAssembly(directory, "TransitiveContracts", Api);
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            new ProjectSpec("Bridge", [("Bridge.cs", Bridge)], AdditionalReferences: [MetadataReference.CreateFromFile(path)]),
            new ProjectSpec("App", [("App.cs", "public class DerivedWorker : Worker { }")], ProjectReferences: ["Bridge"]));
        var snapshot = Capture(workspace.Solution);
        var compilation = await snapshot.Solution.Projects.Single(project => project.Name == "App").GetCompilationAsync();
        Assert.DoesNotContain(compilation!.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var selected = await SourceMetadataContractResolver.ResolveAsync(snapshot, "External.IContract", path);
        var result = await SourceMetadataRelationshipScanner.CollectAsync(snapshot, selected.Selected!, false, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value.Length);
        Assert.Contains(result.Value, symbol => symbol.Name == "DerivedWorker");
    }

    [Fact]
    public async Task SealedConcreteMetadataOverrideIsNotAnImplementationSeed()
    {
        using var directory = TestTempDirectory.Create("metadata-relationships-sealed-");
        var path = AssemblyTestHelper.EmitAssembly(directory, "SealedContracts", Api + " public class SealedMember : Base { public sealed override void Stop() { } }");
        using var workspace = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("App", [("App.cs", "public class App { }")],
            AdditionalReferences: [MetadataReference.CreateFromFile(path)]));
        var snapshot = Capture(workspace.Solution);
        var selected = await SourceMetadataContractResolver.ResolveAsync(snapshot, "M:External.SealedMember.Stop", path);
        Assert.True(selected.IsSuccess, selected.Error?.Message);
        var result = await SourceMetadataRelationshipScanner.CollectAsync(snapshot, selected.Selected!, false, default);
        Assert.False(result.IsSuccess);
        Assert.Equal(NavigationErrorCodes.InvalidArgument, result.Error!.Value.Code);
    }

    [Fact]
    public async Task ClosedGenericInterfaceUsesConstructedPropertyForSemanticMapping()
    {
        using var directory = TestTempDirectory.Create("metadata-relationships-generic-");
        var path = AssemblyTestHelper.EmitAssembly(directory, "GenericContracts", "namespace External; public interface IValue<T> { T Value { get; } }");
        using var workspace = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("App",
            [("Value.cs", "public class IntValue : External.IValue<int> { public int Value => 42; }")],
            AdditionalReferences: [MetadataReference.CreateFromFile(path)]));
        var snapshot = Capture(workspace.Solution);
        var selected = await SourceMetadataContractResolver.ResolveAsync(snapshot, "P:External.IValue`1.Value", path);
        Assert.True(selected.IsSuccess, selected.Error?.Message);
        var result = await SourceMetadataRelationshipScanner.CollectAsync(snapshot, selected.Selected!, false, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        var implementation = Assert.IsAssignableFrom<IPropertySymbol>(Assert.Single(result.Value));
        Assert.Equal(SpecialType.System_Int32, implementation.Type.SpecialType);
        Assert.Equal("IntValue", implementation.ContainingType.Name);
    }

    [Fact]
    public async Task DualClosedGenericInterfacesReturnBothExplicitProperties()
    {
        using var directory = TestTempDirectory.Create("metadata-relationships-dual-generic-");
        var path = AssemblyTestHelper.EmitAssembly(directory, "DualContracts", "namespace External; public interface IValue<T> { T Value { get; } }");
        using var workspace = TestWorkspaceBuilder.CreateSolution(@"C:\VirtualRepo\DualGeneric.slnx", new ProjectSpec("App",
            [("Value.cs", "public class DualValue : External.IValue<int>, External.IValue<string> { int External.IValue<int>.Value => 42; string External.IValue<string>.Value => \"both\"; }")],
            AdditionalReferences: [MetadataReference.CreateFromFile(path)]));
        var snapshot = Capture(workspace.Solution);
        var selected = await SourceMetadataContractResolver.ResolveAsync(snapshot, "P:External.IValue`1.Value", path);
        Assert.True(selected.IsSuccess, selected.Error?.Message);
        var result = await SourceMetadataRelationshipScanner.CollectAsync(snapshot, selected.Selected!, false, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value.Length);
        Assert.Equal(new[] { SpecialType.System_Int32, SpecialType.System_String },
            result.Value.OfType<IPropertySymbol>().Select(property => property.Type.SpecialType).OrderBy(value => value));
        var projected = await FindReferencesResolver.ProjectImplementationsAsync(selected.Selected!.Occurrences[0].Symbol,
            snapshot.Solution, result.Value, 10, default, SymbolScopeType.All, false, null);
        Assert.Equal(2, projected.TotalCount);
        Assert.Equal(2, projected.Implementations.Select(entry => entry.HandoffId).Distinct().Count());
        Assert.All(projected.Implementations, entry => Assert.StartsWith("src:", entry.HandoffId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task VirtualInterfaceMappingPreservesExistingSemanticSearch()
    {
        using var directory = TestTempDirectory.Create("metadata-relationships-interface-override-");
        var path = AssemblyTestHelper.EmitAssembly(directory, "OverrideContracts", Api);
        var source = Bridge.Replace("public void Run(int", "public virtual void Run(int", StringComparison.Ordinal);
        using var workspace = TestWorkspaceBuilder.CreateSolution(
            new ProjectSpec("Bridge", [("Worker.cs", source)], AdditionalReferences: [MetadataReference.CreateFromFile(path)]),
            new ProjectSpec("App", [("Derived.cs", "public class DerivedWorker : Worker { public override void Run(int value) { } }")],
                ProjectReferences: ["Bridge"], AdditionalReferences: [MetadataReference.CreateFromFile(path)]));
        var snapshot = Capture(workspace.Solution);
        var selected = await SourceMetadataContractResolver.ResolveAsync(snapshot, "M:External.IContract.Run(System.Int32)", path);
        var existing = await FindReferencesResolver.FindImplementationsAsync(selected.Selected!.Occurrences[0].Symbol, snapshot.Solution);
        Assert.Equal(1, existing.TotalCount);
        var result = await SourceMetadataRelationshipScanner.CollectAsync(snapshot, selected.Selected, false, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(existing.TotalCount, result.Value.Length);
        Assert.Equal("Worker", Assert.Single(result.Value).ContainingType.Name);
    }

    [Fact]
    public async Task TransitiveRetargetingUsesActualOwnerPathEvenForIdenticalBytesAndSourceAssemblyNames()
    {
        using var first = TestTempDirectory.Create("metadata-relationships-copy-first-");
        using var second = TestTempDirectory.Create("metadata-relationships-copy-second-");
        var firstPath = AssemblyTestHelper.EmitAssembly(first, "CopiedContracts", Api);
        var secondPath = second.GetPath("CopiedContracts.dll");
        System.IO.File.Copy(firstPath, secondPath);
        using var workspace = TestWorkspaceBuilder.CreateSolution(@"C:\VirtualRepo\Retargeted.slnx",
            new ProjectSpec("FirstBridge", [("Worker.cs", Bridge)], AdditionalReferences: [MetadataReference.CreateFromFile(firstPath)], AssemblyName: "SameSource"),
            new ProjectSpec("SecondBridge", [("Worker.cs", Bridge)], AdditionalReferences: [MetadataReference.CreateFromFile(secondPath)], AssemblyName: "SameSource"),
            new ProjectSpec("App", [("Derived.cs", "public class DerivedWorker : Worker { }")], ProjectReferences: ["FirstBridge"], AdditionalReferences: [MetadataReference.CreateFromFile(secondPath)]));
        var snapshot = Capture(workspace.Solution);
        foreach (var (path, count) in new[] { (firstPath, 1), (secondPath, 2) })
        {
            var selected = await SourceMetadataContractResolver.ResolveAsync(snapshot, "External.IContract", path);
            var result = await SourceMetadataRelationshipScanner.CollectAsync(snapshot, selected.Selected!, false, default);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(count, result.Value.Length);
            var formatter = await SourceReferenceFormattingContext.CreateFormatterAsync(snapshot.Solution, default);
            var references = result.Value.Select(formatter).ToArray();
            Assert.All(references, reference => Assert.StartsWith("src:", reference));
            Assert.Equal(count, references.Distinct().Count());
            Assert.Equal(path == secondPath, result.Value.Any(symbol => symbol.Name == "DerivedWorker"));
        }
    }

    [Fact]
    public async Task UnboundImageCannotProveImplementationsInsideItsCompilation()
    {
        using var first = TestTempDirectory.Create("metadata-relationships-unbound-first-");
        using var second = TestTempDirectory.Create("metadata-relationships-unbound-second-");
        var firstPath = AssemblyTestHelper.EmitAssembly(first, "SharedContracts", Api);
        var secondPath = AssemblyTestHelper.EmitAssembly(second, "SharedContracts", Api + " public class ImageTwo { }");
        using var workspace = TestWorkspaceBuilder.CreateSolution(new ProjectSpec("App", [("App.cs", Bridge)],
            AdditionalReferences: [MetadataReference.CreateFromFile(firstPath), MetadataReference.CreateFromFile(secondPath)]));
        var snapshot = Capture(workspace.Solution);
        var ambiguous = await SourceMetadataContractResolver.ResolveAsync(snapshot, "External.IContract");
        var unbound = ambiguous.Candidates.Single(candidate => !candidate.Occurrences[0].IsBoundInProject);
        var result = await SourceMetadataRelationshipScanner.CollectAsync(snapshot, unbound, false, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Empty(result.Value);
        var bound = ambiguous.Candidates.Single(candidate => candidate.Occurrences[0].IsBoundInProject);
        var actual = await SourceMetadataRelationshipScanner.CollectAsync(snapshot, bound, false, default);
        Assert.True(actual.IsSuccess, actual.Error?.Message);
        Assert.Equal("Worker", Assert.Single(actual.Value).Name);
    }

    private static SourceIdentityValidatedSnapshot Capture(Solution solution)
    {
        var captured = MetadataReferenceImageCapture.Capture(solution, previousInputs: null);
        return new(captured.Solution, captured.Inputs);
    }
}
