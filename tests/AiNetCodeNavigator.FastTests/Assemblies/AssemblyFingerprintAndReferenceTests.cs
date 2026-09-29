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
