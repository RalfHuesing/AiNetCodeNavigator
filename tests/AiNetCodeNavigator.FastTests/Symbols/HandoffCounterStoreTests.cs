#nullable enable

using System.IO;
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
}
