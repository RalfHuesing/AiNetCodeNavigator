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
        hasTree = manager.TryGetTree(filePath, time1, out retrieved);
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
    public void CompilationCacheManager_Invalidation_RemovesEntries()
    {
        var manager = new CompilationCacheManager();
        var filePath = @"C:\Test\File1.cs";
        var projPath = @"C:\Test\Proj1.csproj";
        var now = DateTime.UtcNow;

        var tree = CSharpSyntaxTree.ParseText("class A {}");
        var compilation = CSharpCompilation.Create("TestComp").AddSyntaxTrees(tree);

        manager.StoreTree(filePath, now, "h", tree);
        manager.StoreCompilation(projPath, now, compilation);

        Assert.Equal(1, manager.GetStatistics().CachedTreesCount);
        Assert.Equal(1, manager.GetStatistics().CachedCompilationsCount);

        // Invalidate file
        Assert.True(manager.InvalidateFile(filePath));
        Assert.False(manager.TryGetTree(filePath, now, out _));

        // Invalidate project
        Assert.True(manager.InvalidateProject(projPath));
        Assert.False(manager.TryGetCompilation(projPath, now, out _));

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
            manager.StoreTree(path, time, $"hash_{i}", tree);
            manager.TryGetTree(path, time, out _);
        });

        var stats = manager.GetStatistics();
        Assert.True(stats.CachedTreesCount <= 10);
        Assert.True(stats.Hits + stats.Misses >= 100);
    }
}
