#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.TestKit;

namespace AiNetCodeNavigator.FastTests.Assemblies;

// @covers AssemblyDecompilationCache
// @covers AssemblyDecompilationAdapter
// @covers AssemblyRoslynWorkspaceFactory
[Trait("Category", "Component")]
public sealed class AssemblyDecompilationCacheTests
{
    [Fact]
    public async Task PublishAsync_ThenTryRead_ReturnsMatchingGeneration()
    {
        using var tempDir = TestTempDirectory.Create("assembly-cache-roundtrip-");
        var cacheRoot = tempDir.GetPath("cache");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(
            tempDir,
            "CacheRoundTrip",
            "namespace Probe; public sealed class Value { public int X => 1; }");

        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        var options = AssemblyDecompilationOptions.Default;
        var cacheKey = AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options);
        var resolver = new AssemblyReferenceResolver();
        var references = resolver.Resolve(assemblyPath);

        var adapter = new AssemblyDecompilationAdapter();
        var request = new DecompilationRequest(
            assemblyPath,
            fingerprint,
            cacheKey,
            options,
            System.Threading.CancellationToken.None);
        var decompilation = await adapter.DecompileAsync(request, references);

        Assert.NotEmpty(decompilation.Documents);

        var cache = new AssemblyDecompilationCache(cacheRoot);
        var publishRequest = new AssemblyCachePublishRequest(
            fingerprint,
            cacheKey,
            options,
            references,
            decompilation,
            AssemblySessionStatus.Complete);
        var published = await cache.PublishAsync(publishRequest);

        Assert.True(published.Succeeded, "Cache publish must succeed.");
        Assert.NotNull(published.EntryDirectory);

        var readRequest = new AssemblyCacheReadRequest(cacheKey, fingerprint, references);
        var hit = cache.TryRead(readRequest, out var cached, out var diagnostic);

        Assert.True(hit, "Cache hit expected after publish.");
        Assert.Null(diagnostic);
        Assert.NotNull(cached);
        Assert.NotEmpty(cached!.Documents);
    }

    [Fact]
    public async Task PublishAsync_TwiceWithSameKey_ReturnsSameGeneration()
    {
        using var tempDir = TestTempDirectory.Create("assembly-cache-idempotent-");
        var cacheRoot = tempDir.GetPath("cache");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(
            tempDir,
            "IdempotentPublish",
            "namespace Probe; public sealed class Idempotent { }");

        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        var options = AssemblyDecompilationOptions.Default;
        var cacheKey = AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options);
        var resolver = new AssemblyReferenceResolver();
        var references = resolver.Resolve(assemblyPath);
        var adapter = new AssemblyDecompilationAdapter();
        var decompRequest = new DecompilationRequest(assemblyPath, fingerprint, cacheKey, options, System.Threading.CancellationToken.None);
        var decompilation = await adapter.DecompileAsync(decompRequest, references);

        var cache = new AssemblyDecompilationCache(cacheRoot);
        var publishRequest = new AssemblyCachePublishRequest(fingerprint, cacheKey, options, references, decompilation, AssemblySessionStatus.Complete);
        var first = await cache.PublishAsync(publishRequest);
        var second = await cache.PublishAsync(publishRequest);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(
            Path.GetFullPath(first.EntryDirectory!),
            Path.GetFullPath(second.EntryDirectory!),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_WithoutPublish_ReturnsCacheMiss()
    {
        using var tempDir = TestTempDirectory.Create("assembly-cache-miss-");
        var cacheRoot = tempDir.GetPath("cache");

        var fingerprint = new AssemblyFingerprint(
            tempDir.GetPath("missing.dll"),
            1234,
            DateTime.UtcNow,
            "AABB" + new string('0', 56));
        var options = AssemblyDecompilationOptions.Default;
        var cacheKey = AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options);
        var references = new AssemblyReferenceResolution(null, [], [], []);

        var cache = new AssemblyDecompilationCache(cacheRoot);
        var readRequest = new AssemblyCacheReadRequest(cacheKey, fingerprint, references);
        var hit = cache.TryRead(readRequest, out var cached, out var diagnostic);

        Assert.False(hit);
        Assert.Null(cached);
        Assert.Null(diagnostic);
    }

    [Fact]
    public async Task CreateAsync_WithDecompiledDocuments_ProducesRoslynSnapshot()
    {
        using var tempDir = TestTempDirectory.Create("assembly-roslyn-snapshot-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(
            tempDir,
            "RoslynSnapshotProbe",
            "namespace Probe; public sealed class Probe { public string Name => nameof(Probe); }");

        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        var options = AssemblyDecompilationOptions.Default;
        var cacheKey = AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options);
        var resolver = new AssemblyReferenceResolver();
        var references = resolver.Resolve(assemblyPath);
        var adapter = new AssemblyDecompilationAdapter();
        var decompRequest = new DecompilationRequest(assemblyPath, fingerprint, cacheKey, options, System.Threading.CancellationToken.None);
        var decompilation = await adapter.DecompileAsync(decompRequest, references);

        Assert.NotEmpty(decompilation.Documents);

        var factory = new AssemblyRoslynWorkspaceFactory();
        var workspaceRequest = new AssemblyWorkspaceRequest(
            assemblyPath,
            fingerprint,
            decompilation.Documents,
            references.MetadataReferences,
            AssemblySessionStatus.Complete,
            decompilation.ProjectFilePath);
        using var snapshot = await factory.CreateAsync(
            workspaceRequest,
            "RoslynSnapshotProbe",
            fingerprint.Sha256,
            System.Threading.CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.Compilation);
        Assert.NotEmpty(snapshot.Documents);
        Assert.All(snapshot.Origins, pair => Assert.Equal("decompiled", pair.Value.OriginKind));
    }
}
