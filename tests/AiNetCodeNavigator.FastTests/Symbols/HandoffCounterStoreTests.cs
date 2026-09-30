#nullable enable

using System.IO;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class HandoffCounterStoreTests
{
    [Fact]
    public void Next_FirstIssuance_StartsAtA()
    {
        using var temp = TestTempDirectory.Create("counter-start-");
        var filePath = temp.GetPath("counter.json");
        var store = new HandoffCounterStore(filePath, batchSize: 5);

        var first = store.Next();
        Assert.True(first.IsSuccess);
        Assert.Equal("a", first.Value);

        var second = store.Next();
        Assert.True(second.IsSuccess);
        Assert.Equal("b", second.Value);
    }

    [Fact]
    public void Next_AcrossRestarts_DoesNotReuseIssuedCounters()
    {
        using var temp = TestTempDirectory.Create("counter-restart-");
        var filePath = temp.GetPath("counter.json");

        string lastIssued;
        {
            var store1 = new HandoffCounterStore(filePath, batchSize: 3);
            Assert.Equal("a", store1.Next().Value);
            Assert.Equal("b", store1.Next().Value);
            lastIssued = "b";
        }

        // Second process instance opening same file
        {
            var store2 = new HandoffCounterStore(filePath, batchSize: 3);
            var next = store2.Next();
            Assert.True(next.IsSuccess);
            // The batch prefetch persisted "c" (batch size 3: a, b, c), so next must not be a or b
            Assert.NotEqual("a", next.Value);
            Assert.NotEqual(lastIssued, next.Value);
        }
    }

    [Fact]
    public void Next_WhenCounterDirectoryCannotBeCreated_ReturnsFailure()
    {
        using var temp = TestTempDirectory.Create("counter-unavailable-");
        var fileUsedAsDirectory = temp.GetPath("not-a-directory");
        File.WriteAllText(fileUsedAsDirectory, "blocker");
        var store = new HandoffCounterStore(
            Path.Combine(fileUsedAsDirectory, "counter.json"),
            lockTimeout: System.TimeSpan.FromMilliseconds(50));

        var result = store.Next();

        Assert.False(result.IsSuccess);
        Assert.Equal(AiNetCodeNavigator.Core.Workspace.NavigationErrorCodes.HandoffCounterUnavailable, result.Error!.Value.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not-json")]
    [InlineData("{\"formatVersion\":2,\"lastIssued\":\"a\"}")]
    [InlineData("{\"formatVersion\":1,\"lastIssued\":\"invalid!\"}")]
    public void Next_CorruptCounterState_ReturnsFailureWithoutResettingIt(string corruptState)
    {
        using var temp = TestTempDirectory.Create("counter-corrupt-");
        var filePath = temp.GetPath("counter.json");
        File.WriteAllText(filePath, corruptState);
        var store = new HandoffCounterStore(filePath, batchSize: 3);

        var result = store.Next();

        Assert.False(result.IsSuccess);
        Assert.Equal(AiNetCodeNavigator.Core.Workspace.NavigationErrorCodes.HandoffCounterUnavailable, result.Error!.Value.Code);
        Assert.Equal(corruptState, File.ReadAllText(filePath));
    }

    [Fact]
    public async Task Next_ConcurrentStoreInstances_ProduceUniqueCounters()
    {
        using var temp = TestTempDirectory.Create("counter-parallel-");
        var filePath = temp.GetPath("counter.json");
        var stores = new[]
        {
            new HandoffCounterStore(filePath, batchSize: 7),
            new HandoffCounterStore(filePath, batchSize: 7),
        };
        var counters = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(Enumerable.Range(0, 60), async (index, cancellationToken) =>
        {
            await Task.Yield();
            var result = stores[index % stores.Length].Next();
            Assert.True(result.IsSuccess);
            counters.Add(result.Value!);
        });

        Assert.Equal(60, counters.Count);
        Assert.Equal(60, counters.Distinct().Count());
    }
}
