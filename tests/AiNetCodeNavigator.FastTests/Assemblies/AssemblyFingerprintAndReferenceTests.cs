#nullable enable

using System;
using System.IO;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.TestKit;

namespace AiNetCodeNavigator.FastTests.Assemblies;

// @covers AssemblyFingerprintCalculator
// @covers AssemblyReferenceResolver
[Trait("Category", "Component")]
public sealed class AssemblyFingerprintAndReferenceTests
{
    [Fact]
    public void Create_ValidDll_ReturnsFingerprint()
    {
        using var tempDir = TestTempDirectory.Create("assembly-fingerprint-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(
            tempDir,
            "FingerprintProbe",
            "namespace Probe; public sealed class Value { }");

        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);

        Assert.Equal(assemblyPath, fingerprint.CanonicalPath, StringComparer.OrdinalIgnoreCase);
        Assert.True(fingerprint.Length > 0);
        Assert.NotEqual(default, fingerprint.MtimeUtc);
        Assert.NotEmpty(fingerprint.Sha256);
    }

    [Fact]
    public void TryCreate_MissingFile_ReturnsFalseWithDiagnostic()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");

        var result = AssemblyFingerprintCalculator.TryCreate(missing, out var fingerprint, out var diagnostic);

        Assert.False(result);
        Assert.Null(fingerprint);
        Assert.NotNull(diagnostic);
        Assert.Equal(AssemblyDiagnosticSeverity.Error, diagnostic!.Severity);
    }

    [Fact]
    public void TryCreate_EmptyPath_ReturnsFalseWithDiagnostic()
    {
        var result = AssemblyFingerprintCalculator.TryCreate(string.Empty, out var fingerprint, out var diagnostic);

        Assert.False(result);
        Assert.Null(fingerprint);
        Assert.NotNull(diagnostic);
        Assert.Equal(AssemblyDiagnosticSeverity.Error, diagnostic!.Severity);
    }

    [Fact]
    public void Resolve_ValidDll_ReturnsIdentityAndReferences()
    {
        using var tempDir = TestTempDirectory.Create("assembly-reference-resolver-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(
            tempDir,
            "ReferenceProbe",
            "namespace Probe; public sealed class Ref { }");

        var resolver = new AssemblyReferenceResolver();
        var result = resolver.Resolve(assemblyPath);

        Assert.NotNull(result.Identity);
        Assert.Equal("ReferenceProbe", result.Identity!.Name, StringComparer.Ordinal);
        Assert.NotEmpty(result.References);
    }

    [Fact]
    public void Resolve_TraversesRootToBToCReferenceClosure()
    {
        using var tempDir = TestTempDirectory.Create("assembly-reference-closure-");
        var leaf = AssemblyTestHelper.EmitAssembly(tempDir, "ClosureLeaf", "namespace Closure.Leaf; public sealed class C { }");
        var middle = AssemblyTestHelper.EmitAssembly(tempDir, "ClosureMiddle", "namespace Closure.Middle; public sealed class B { public Closure.Leaf.C? Value; }", leaf);
        var root = AssemblyTestHelper.EmitAssembly(tempDir, "ClosureRoot", "namespace Closure.Root; public sealed class A { public Closure.Middle.B? Value; }", middle);

        var resolution = new AssemblyReferenceResolver().Resolve(root);

        var referenceC = Assert.Single(resolution.References.Where(reference => reference.Name == "ClosureLeaf"));
        Assert.True(referenceC.Resolved);
        Assert.Equal(Path.GetFullPath(leaf), Path.GetFullPath(referenceC.ResolvedPath!));
        Assert.Equal(2, referenceC.Depth);
    }

    [Fact]
    public void Resolve_DoesNotAcceptSameNamedAssemblyWithDifferentVersion()
    {
        using var versionOne = TestTempDirectory.Create("assembly-reference-version-one-");
        using var versionTwo = TestTempDirectory.Create("assembly-reference-version-two-");
        using var consumerDirectory = TestTempDirectory.Create("assembly-reference-version-consumer-");
        var expected = AssemblyTestHelper.EmitAssembly(versionOne, "VersionedDependency", """
            [assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
            namespace Versioned; public sealed class Api { }
            """);
        var unexpected = AssemblyTestHelper.EmitAssembly(versionTwo, "VersionedDependency", """
            [assembly: System.Reflection.AssemblyVersion("2.0.0.0")]
            namespace Versioned; public sealed class Api { }
            """);
        var root = AssemblyTestHelper.EmitAssembly(consumerDirectory, "VersionedConsumer",
            "public sealed class Consumer { public Versioned.Api? Value; }", expected);
        File.Copy(unexpected, consumerDirectory.GetPath("VersionedDependency.dll"));

        var result = new AssemblyReferenceResolver().Resolve(root);

        var dependency = Assert.Single(result.References.Where(reference => reference.Name == "VersionedDependency"));
        Assert.False(dependency.Resolved);
        Assert.Equal("version_mismatch", dependency.ResolutionState);
        Assert.Null(dependency.ResolvedPath);
    }

    [Fact]
    public void IdentityMatches_RejectsDifferentPublicKeyToken()
    {
        var expected = new AssemblyReferenceDto("SignedDependency", "1.2.3.4", "neutral", true)
        {
            PublicKeyToken = "0011223344556677",
        };
        var actual = new AssemblyIdentityDto("SignedDependency", "1.2.3.4", "neutral", "8899AABBCCDDEEFF");

        Assert.False(AssemblyReferenceResolver.IdentityMatches(expected, actual));
    }

    [Fact]
    public void Resolve_ReportsReferenceDepthBoundary()
    {
        using var temp = TestTempDirectory.Create("assembly-reference-depth-boundary-");
        var dependency = AssemblyTestHelper.EmitAssembly(temp, "DepthNode00", "namespace Depth; public sealed class Node00 { }");
        for (var index = 1; index <= AssemblyReferenceResolver.MaxReferenceDepth + 2; index++)
        {
            dependency = AssemblyTestHelper.EmitAssembly(temp, $"DepthNode{index:D2}",
                $"namespace Depth; public sealed class Node{index:D2} {{ public Node{index - 1:D2}? Value; }}", dependency);
        }

        var result = new AssemblyReferenceResolver().Resolve(dependency);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == AssemblyReferenceResolver.BoundaryDiagnosticCode);
        Assert.Contains(result.References, reference => reference.ResolutionState == "depth_limit");
    }

    [Fact]
    public void Resolve_MissingFile_ReturnsErrorDiagnostic()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");
        var resolver = new AssemblyReferenceResolver();

        var result = resolver.Resolve(missing);

        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Diagnostics, d => d.Severity == AssemblyDiagnosticSeverity.Error);
    }

    [Fact]
    public void CreateCacheKey_SameInputs_ReturnsSameStableValue()
    {
        using var tempDir = TestTempDirectory.Create("assembly-cache-key-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(
            tempDir,
            "CacheKeyProbe",
            "namespace Probe; public sealed class Value { }");

        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        var options = AssemblyDecompilationOptions.Default;

        var key1 = AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options);
        var key2 = AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options);

        Assert.Equal(key1.StableValue, key2.StableValue, StringComparer.Ordinal);
    }
}
