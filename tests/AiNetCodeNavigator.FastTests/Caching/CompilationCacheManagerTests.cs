#nullable enable

namespace AiNetCodeNavigator.FastTests.Caching;

using System;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Caching;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

public class CompilationCacheManagerTests
{
    [Fact]
    public void CompilationCacheManager_TreeCache_StoresAndRetrieves()
    {
        var manager = new CompilationCacheManager();
        var filePath = @"C:\Test\Sample.cs";
        var time1 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var tree = CSharpSyntaxTree.ParseText("public class Demo {}");

        // Miss on empty
        var hasTree = manager.TryGetTree(filePath, time1, out var retrieved);
        Assert.False(hasTree);
        Assert.Null(retrieved);

        // Store
        manager.StoreTree(filePath, time1, "hash1", tree);

        // Hit with same time
        hasTree = manager.TryGetTree(filePath, time1, out retrieved, "hash1");
        Assert.True(hasTree);
        Assert.Same(tree, retrieved);

        // Miss with newer time
        var time2 = time1.AddMinutes(5);
        hasTree = manager.TryGetTree(filePath, time2, out retrieved);
        Assert.False(hasTree);
        Assert.Null(retrieved);

        // Check stats
        var stats = manager.GetStatistics();
        Assert.Equal(1, stats.Hits);
        Assert.Equal(2, stats.Misses);
        Assert.Equal(1, stats.CachedTreesCount);
    }

    [Fact]
    public void CompilationCacheManager_TreeCache_UsesContentHashWhenProvided()
    {
        var manager = new CompilationCacheManager();
        var filePath = @"C:\Test\SameTimestamp.cs";
        var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var tree = CSharpSyntaxTree.ParseText("public class Original {}");
        manager.StoreTree(filePath, timestamp, "content-v1", tree);

        Assert.False(manager.TryGetTree(filePath, timestamp, out var retrievedWithoutHash));
        Assert.Null(retrievedWithoutHash);
        Assert.False(manager.TryGetTree(filePath, timestamp, out var retrieved, "content-v2"));
        Assert.Null(retrieved);
        Assert.True(manager.TryGetTree(filePath, timestamp, out retrieved, "content-v1"));
        Assert.Same(tree, retrieved);
    }

    [Fact]
    public void CompilationCacheManager_CompilationCache_RequiresCompleteFingerprint()
    {
        var manager = new CompilationCacheManager();
        var projectPath = @"C:\Test\Project.csproj";
        var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var compilation = CSharpCompilation.Create("Project");
        var fingerprint = CreateFingerprint();
        manager.StoreCompilation(projectPath, timestamp, fingerprint, compilation);

        Assert.True(manager.TryGetCompilation(projectPath, timestamp, fingerprint, out var retrieved));
        Assert.Same(compilation, retrieved);

        // Keep the maximum source timestamp unchanged: edits to any source or compilation input must still miss.
        var changedInputs = new[]
        {
            fingerprint with { SourceTreesHash = "sources-v2" },
            fingerprint with { ParseOptionsHash = "parse-v2" },
            fingerprint with { CompilationOptionsHash = "compilation-v2" },
            fingerprint with { ProjectReferencesHash = "project-refs-v2" },
            fingerprint with { MetadataReferencesHash = "metadata-refs-v2" }
        };

        foreach (var changedFingerprint in changedInputs)
        {
            Assert.False(manager.TryGetCompilation(projectPath, timestamp, changedFingerprint, out retrieved));
            Assert.Null(retrieved);
        }
    }

    [Fact]
    public void CompilationCacheManager_CacheKeys_AreCaseInsensitive()
    {
        var manager = new CompilationCacheManager();
        var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var tree = CSharpSyntaxTree.ParseText("class A {}");
        var compilation = CSharpCompilation.Create("Project");
        manager.StoreTree(@"C:\Test\File.cs", timestamp, null, tree);
        manager.StoreCompilation(@"C:\Test\Project.csproj", timestamp, CreateFingerprint(), compilation);

        Assert.True(manager.TryGetTree(@"c:\test\FILE.cs", timestamp, out var retrievedTree));
        Assert.Same(tree, retrievedTree);
        Assert.True(manager.TryGetCompilation(@"c:\test\PROJECT.CSPROJ", timestamp, CreateFingerprint(), out var retrievedCompilation));
        Assert.Same(compilation, retrievedCompilation);
    }

    [Fact]
    public void CompilationCacheManager_RejectsNonUtcTimestamps()
    {
        var manager = new CompilationCacheManager();
        var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
        var tree = CSharpSyntaxTree.ParseText("class A {}");
        var compilation = CSharpCompilation.Create("Project");

        Assert.Throws<ArgumentException>(() => manager.TryGetTree("File.cs", timestamp, out _));
        Assert.Throws<ArgumentException>(() => manager.StoreTree("File.cs", timestamp, null, tree));
        var fingerprint = CreateFingerprint();
        Assert.Throws<ArgumentException>(() => manager.TryGetCompilation("Project.csproj", timestamp, fingerprint, out _));
        Assert.Throws<ArgumentException>(() => manager.StoreCompilation("Project.csproj", timestamp, fingerprint, compilation));
    }

    [Fact]
    public void CompilationCacheManager_RejectsIncompleteCompilationFingerprints()
    {
        var manager = new CompilationCacheManager();
        var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var compilation = CSharpCompilation.Create("Project");
        var fingerprint = CreateFingerprint();

        Assert.Throws<ArgumentNullException>(() => manager.StoreCompilation("Project.csproj", timestamp, null!, compilation));
        Assert.Throws<ArgumentNullException>(() => manager.TryGetCompilation("Project.csproj", timestamp, null!, out _));

        var incompleteFingerprints = new[]
        {
            fingerprint with { SourceTreesHash = " " },
            fingerprint with { ParseOptionsHash = " " },
            fingerprint with { CompilationOptionsHash = " " },
            fingerprint with { ProjectReferencesHash = " " },
            fingerprint with { MetadataReferencesHash = " " }
        };

        foreach (var incompleteFingerprint in incompleteFingerprints)
        {
            Assert.Throws<ArgumentException>(() => manager.StoreCompilation(
                "Project.csproj", timestamp, incompleteFingerprint, compilation));
            Assert.Throws<ArgumentException>(() => manager.TryGetCompilation(
                "Project.csproj", timestamp, incompleteFingerprint, out _));
        }
    }

    [Fact]
    public void CompilationCacheManager_Invalidation_RemovesEntries()
    {
        var manager = new CompilationCacheManager();
        var filePath = @"C:\Test\File1.cs";
        var projPath = @"C:\Test\Proj1.csproj";
        var now = DateTime.UtcNow;

        var tree = CSharpSyntaxTree.ParseText("class A {}");
        var compilation = CSharpCompilation.Create("TestComp").AddSyntaxTrees(tree);

        manager.StoreTree(filePath, now, "h", tree);
        manager.StoreCompilation(projPath, now, CreateFingerprint(), compilation);

        Assert.Equal(1, manager.GetStatistics().CachedTreesCount);
        Assert.Equal(1, manager.GetStatistics().CachedCompilationsCount);

        // Invalidate file
        Assert.True(manager.InvalidateFile(filePath.ToLowerInvariant()));
        Assert.False(manager.TryGetTree(filePath, now, out _));
        Assert.False(manager.InvalidateFile(filePath));

        // File and project entries have independent invalidation scopes.
        manager.StoreTree(filePath, now, "h", tree);
        Assert.True(manager.InvalidateProject(projPath));
        Assert.True(manager.TryGetTree(filePath, now, out _, "h"));
        Assert.False(manager.InvalidateProject(projPath));

        // Invalidate project
        Assert.False(manager.TryGetCompilation(projPath, now, CreateFingerprint(), out _));

        // Clear all
        manager.Clear();
        var stats = manager.GetStatistics();
        Assert.Equal(0, stats.CachedTreesCount);
        Assert.Equal(0, stats.CachedCompilationsCount);
        Assert.Equal(0, stats.Hits);
        Assert.Equal(0, stats.Misses);
    }

    [Fact]
    public void CompilationCacheManager_ThreadSafety_ConcurrentWritesAndReads()
    {
        var manager = new CompilationCacheManager();
        var time = DateTime.UtcNow;

        Parallel.For(0, 100, i =>
        {
            var path = $@"C:\Test\File_{i % 10}.cs";
            var tree = CSharpSyntaxTree.ParseText($"class Class_{i} {{}}");
            var hash = $"hash_{i}";
            manager.StoreTree(path, time, hash, tree);
            manager.TryGetTree(path, time, out _, hash);
        });

        var stats = manager.GetStatistics();
        Assert.True(stats.CachedTreesCount <= 10);
        Assert.True(stats.Hits + stats.Misses >= 100);
    }

    private static CompilationInputFingerprint CreateFingerprint() => new(
        "sources-v1",
        "parse-v1",
        "compilation-v1",
        "project-refs-v1",
        "metadata-refs-v1");
}
