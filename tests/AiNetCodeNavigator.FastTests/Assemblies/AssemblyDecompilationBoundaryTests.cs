#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.TestKit;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.FastTests.Assemblies;

// @covers AssemblyDecompilationAdapter
// @covers AssemblyDecompilationCache
// @covers AssemblyRoslynWorkspaceFactory
[Trait("Category", "Component")]
public sealed class AssemblyDecompilationBoundaryTests
{
    [Fact]
    public async Task DecompileAsync_LeavesManagedAssemblyBytesAndTimestampUnchanged()
    {
        using var temp = TestTempDirectory.Create("assembly-decompile-read-only-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(
            temp,
            "ReadOnlyProbe",
            "namespace Probe; public sealed class Value { public int Number => 42; }");
        var originalBytes = await File.ReadAllBytesAsync(assemblyPath);
        var originalTimestamp = File.GetLastWriteTimeUtc(assemblyPath);
        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        var options = AssemblyDecompilationOptions.Default;
        var request = new DecompilationRequest(
            assemblyPath,
            fingerprint,
            AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options),
            options,
            CancellationToken.None);

        var result = await new AssemblyDecompilationAdapter().DecompileAsync(
            request,
            new AssemblyReferenceResolver().Resolve(assemblyPath));

        Assert.True(result.IsComplete);
        Assert.NotEmpty(result.Documents);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(assemblyPath));
        Assert.Equal(originalTimestamp, File.GetLastWriteTimeUtc(assemblyPath));
    }

    [Fact]
    public async Task DecompileAsync_InvalidManagedImageReturnsDiagnostic()
    {
        using var temp = TestTempDirectory.Create("assembly-decompile-invalid-");
        var assemblyPath = temp.GetPath("invalid.dll");
        await File.WriteAllBytesAsync(assemblyPath, [0x4d, 0x5a, 0x00, 0x01]);
        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        var options = AssemblyDecompilationOptions.Default;
        var request = new DecompilationRequest(
            assemblyPath,
            fingerprint,
            AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options),
            options,
            CancellationToken.None);

        var result = await new AssemblyDecompilationAdapter().DecompileAsync(
            request,
            new AssemblyReferenceResolution(null, [], [], []));

        Assert.False(result.IsComplete);
        Assert.Empty(result.Documents);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == AssemblyDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task DecompileAsync_NativePeReturnsManagedAssemblyDiagnostic()
    {
        if (!OperatingSystem.IsWindows()) return;

        var assemblyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "kernel32.dll");
        Assert.True(File.Exists(assemblyPath));
        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        var options = AssemblyDecompilationOptions.Default;
        var request = new DecompilationRequest(
            assemblyPath,
            fingerprint,
            AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options),
            options,
            CancellationToken.None);

        var result = await new AssemblyDecompilationAdapter().DecompileAsync(
            request,
            new AssemblyReferenceResolution(null, [], [], []));

        Assert.False(result.IsComplete);
        Assert.Empty(result.Documents);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == AssemblyDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("managed .NET .dll or .exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DecompileAsync_CallerCancellationIsPropagated()
    {
        using var temp = TestTempDirectory.Create("assembly-decompile-cancelled-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "CancelledProbe", "namespace Probe; public sealed class Value { }");
        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var options = AssemblyDecompilationOptions.Default;
        var request = new DecompilationRequest(
            assemblyPath,
            fingerprint,
            AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options),
            options,
            cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AssemblyDecompilationAdapter().DecompileAsync(
                request,
                new AssemblyReferenceResolver().Resolve(assemblyPath)));
    }

    [Fact]
    public async Task DecompileAsync_TimeoutDoesNotReadPartialStagingOutput()
    {
        using var temp = TestTempDirectory.Create("assembly-decompile-timeout-partial-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "TimeoutPartialProbe", "namespace Probe; public sealed class Value { }");
        var stagingDirectory = temp.GetPath("staging");
        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        var options = AssemblyDecompilationOptions.Default with { Timeout = TimeSpan.FromMilliseconds(1) };
        var request = new DecompilationRequest(
            assemblyPath,
            fingerprint,
            AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options),
            options,
            CancellationToken.None,
            stagingDirectory);
        var adapter = new AssemblyDecompilationAdapter(async (decompilationRequest, _) =>
        {
            Directory.CreateDirectory(decompilationRequest.StagingDirectory!);
            await File.WriteAllTextAsync(Path.Combine(decompilationRequest.StagingDirectory!, "partial.csproj"), "<Project />");
            await File.WriteAllTextAsync(
                Path.Combine(decompilationRequest.StagingDirectory!, "partial.cs"),
                "namespace Probe; public sealed class Partial { /*" + new string('x', 8 * 1024 * 1024) + "*/ }");
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, decompilationRequest.CancellationToken);
            return new DecompilationResult([], [], true);
        });

        var result = await adapter.DecompileAsync(request, new AssemblyReferenceResolver().Resolve(assemblyPath));

        Assert.False(result.IsComplete);
        Assert.Empty(result.Documents);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("configured timeout", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(stagingDirectory, "partial.cs")));
    }

    [Fact]
    public async Task TryRead_RejectsGenerationWhenRequestFingerprintDiffersFromPublishedFingerprint()
    {
        using var temp = TestTempDirectory.Create("assembly-cache-stale-fingerprint-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "FingerprintCacheProbe", "namespace Probe; public sealed class Value { }");
        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        var options = AssemblyDecompilationOptions.Default;
        var key = AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options);
        var references = new AssemblyReferenceResolver().Resolve(assemblyPath);
        var request = new AssemblyCachePublishRequest(
            fingerprint,
            key,
            options,
            references,
            new DecompilationResult(
                [new DecompiledDocument("Value.cs", "Probe.Value", "namespace Probe; public sealed class Value { }")],
                [],
                true),
            AssemblySessionStatus.Complete);
        var cache = new AssemblyDecompilationCache(temp.GetPath("cache"));
        Assert.True((await cache.PublishAsync(request)).Succeeded);

        var changedFingerprint = fingerprint with { Sha256 = new string('A', 64) };
        var canRead = cache.TryRead(
            new AssemblyCacheReadRequest(key, changedFingerprint, references),
            out var generation,
            out var diagnostic);

        Assert.False(canRead);
        Assert.Null(generation);
        Assert.NotNull(diagnostic);
    }

    [Fact]
    public async Task TryRead_RejectsCachedSourceChangedAfterPublication()
    {
        using var temp = TestTempDirectory.Create("assembly-cache-edited-source-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "EditedCacheProbe", "namespace Probe; public sealed class Value { }");
        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        var options = AssemblyDecompilationOptions.Default;
        var key = AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options);
        var references = new AssemblyReferenceResolver().Resolve(assemblyPath);
        var source = "namespace Probe; public sealed class Value { }";
        var request = new AssemblyCachePublishRequest(
            fingerprint,
            key,
            options,
            references,
            new DecompilationResult([new DecompiledDocument("Value.cs", "Probe.Value", source)], [], true),
            AssemblySessionStatus.Complete);
        var cache = new AssemblyDecompilationCache(temp.GetPath("cache"));
        var published = await cache.PublishAsync(request);
        Assert.True(published.Succeeded);

        var cachedSourcePath = Directory.EnumerateFiles(published.EntryDirectory!, "*.cs", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(cachedSourcePath, source.Replace("Value", "Falue", StringComparison.Ordinal), Encoding.UTF8);

        var canRead = cache.TryRead(
            new AssemblyCacheReadRequest(key, fingerprint, references),
            out var generation,
            out var diagnostic);

        Assert.False(canRead);
        Assert.Null(generation);
        Assert.NotNull(diagnostic);
    }

    [Fact]
    public async Task PublishAsync_ConcurrentIdenticalRequestsReturnExistingGeneration()
    {
        using var temp = TestTempDirectory.Create("assembly-cache-concurrent-publish-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "ConcurrentCacheProbe", "namespace Probe; public sealed class Value { }");
        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        var options = AssemblyDecompilationOptions.Default;
        var key = AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options);
        var references = new AssemblyReferenceResolver().Resolve(assemblyPath);
        var request = new AssemblyCachePublishRequest(
            fingerprint,
            key,
            options,
            references,
            new DecompilationResult(
                [new DecompiledDocument("Value.cs", "Probe.Value", "namespace Probe; public sealed class Value { }")],
                [],
                true),
            AssemblySessionStatus.Complete);
        var cache = new AssemblyDecompilationCache(temp.GetPath("cache"));
        using var barrier = new Barrier(8);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await cache.PublishAsync(request);
        })));

        Assert.All(results, result =>
        {
            Assert.True(result.Succeeded);
            Assert.NotNull(result.EntryDirectory);
            Assert.True(Directory.Exists(result.EntryDirectory));
        });
        Assert.True(cache.TryRead(
            new AssemblyCacheReadRequest(key, fingerprint, references),
            out var generation,
            out var diagnostic));
        Assert.NotNull(generation);
        Assert.Null(diagnostic);
        Assert.Empty(Directory.EnumerateDirectories(cache.RootPath, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CreateAsync_RejectsEmptyDocumentsAndCancelledRequests()
    {
        using var temp = TestTempDirectory.Create("assembly-roslyn-empty-snapshot-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(temp, "EmptySnapshotProbe", "namespace Probe; public sealed class Value { }");
        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        var request = new AssemblyWorkspaceRequest(
            assemblyPath,
            fingerprint,
            [],
            [],
            AssemblySessionStatus.Complete);
        var factory = new AssemblyRoslynWorkspaceFactory();

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(
            request,
            "EmptySnapshotProbe",
            fingerprint.Sha256,
            CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.CreateAsync(
            request with { Documents = [new DecompiledDocument("Value.cs", "Probe.Value", "namespace Probe; public sealed class Value { }")] },
            "EmptySnapshotProbe",
            fingerprint.Sha256,
            cancellation.Token));
    }

    [Fact]
    public async Task CreateAsync_ExcludesTargetAssemblyFromMetadataReferences()
    {
        using var temp = TestTempDirectory.Create("assembly-roslyn-target-reference-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(
            temp,
            "TargetReferenceProbe",
            "namespace Probe; public sealed class Value { public int Number => 42; }");
        var fingerprint = AssemblyFingerprintCalculator.Create(assemblyPath);
        var options = AssemblyDecompilationOptions.Default;
        var key = AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options);
        var references = new AssemblyReferenceResolver().Resolve(assemblyPath);
        var decompilation = await new AssemblyDecompilationAdapter().DecompileAsync(
            new DecompilationRequest(assemblyPath, fingerprint, key, options, CancellationToken.None),
            references);
        var request = new AssemblyWorkspaceRequest(
            assemblyPath,
            fingerprint,
            decompilation.Documents,
            references.MetadataReferences,
            AssemblySessionStatus.Complete,
            decompilation.ProjectFilePath);

        using var snapshot = await new AssemblyRoslynWorkspaceFactory().CreateAsync(
            request,
            "TargetReferenceProbe",
            fingerprint.Sha256,
            CancellationToken.None);

        Assert.NotNull(snapshot.Compilation.GetTypeByMetadataName("Probe.Value"));
        Assert.DoesNotContain(snapshot.Compilation.References.OfType<PortableExecutableReference>(), reference =>
            string.Equals(reference.FilePath, assemblyPath, StringComparison.OrdinalIgnoreCase));
        Assert.All(snapshot.Documents, document => Assert.True(snapshot.Origins.ContainsKey(document.Id)));
    }
}
