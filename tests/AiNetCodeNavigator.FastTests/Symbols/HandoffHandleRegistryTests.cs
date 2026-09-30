#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class HandoffHandleRegistryTests
{
    private sealed class FailingCounterStore : IHandoffCounterStore
    {
        public Result<string> Next() =>
            Result<string>.Failure(
                NavigationErrorCodes.HandoffCounterUnavailable,
                "Counter-Speicher absichtlich nicht verfügbar.");
    }

    [Fact]
    public void GetOrCreateOpaqueHandleForOutput_ReturnsConsistentHandlesAndBijection()
    {
        using var temp = TestTempDirectory.Create("registry-");
        var store = new HandoffCounterStore(temp.GetPath("counter.json"));
        var registry = new HandoffHandleRegistry(store);

        const string internalId1 = "i:0:targetA:contentA:M:Namespace.Class.MethodA";
        const string internalId2 = "i:0:targetB:contentB:M:Namespace.Class.MethodB";

        var handle1 = registry.GetOrCreateOpaqueHandleForOutput(internalId1);
        var handle1Again = registry.GetOrCreateOpaqueHandleForOutput(internalId1);
        var handle2 = registry.GetOrCreateOpaqueHandleForOutput(internalId2);

        Assert.True(handle1.IsSuccess);
        Assert.Equal("h:a", handle1.Value);
        Assert.Equal("h:a", handle1Again.Value);

        Assert.True(handle2.IsSuccess);
        Assert.Equal("h:b", handle2.Value);

        // Verify roundtrip
        var restored1 = registry.RestoreInternalHandoffForInput(handle1.Value!);
        var restored2 = registry.RestoreInternalHandoffForInput(handle2.Value!);

        Assert.True(restored1.IsSuccess);
        Assert.Equal(internalId1, restored1.Value);

        Assert.True(restored2.IsSuccess);
        Assert.Equal(internalId2, restored2.Value);
    }

    [Fact]
    public void GetOrCreateOpaqueHandleForOutput_RejectsEmptyInternalId()
    {
        using var temp = TestTempDirectory.Create("registry-");
        var store = new HandoffCounterStore(temp.GetPath("counter.json"));
        var registry = new HandoffHandleRegistry(store);

        var result = registry.GetOrCreateOpaqueHandleForOutput(string.Empty);
        Assert.False(result.IsSuccess);
        Assert.Equal(NavigationErrorCodes.InvalidArgument, result.Error!.Value.Code);
    }

    [Fact]
    public void GetOrCreateOpaqueHandleForOutput_PropagatesCounterStoreFailure()
    {
        var registry = new HandoffHandleRegistry(new FailingCounterStore());

        var result = registry.GetOrCreateOpaqueHandleForOutput("internal-id");

        Assert.False(result.IsSuccess);
        Assert.Equal(NavigationErrorCodes.HandoffCounterUnavailable, result.Error!.Value.Code);
    }

    [Fact]
    public void RestoreInternalHandoffForInput_PassesSemanticInputUnchanged()
    {
        using var temp = TestTempDirectory.Create("registry-");
        var store = new HandoffCounterStore(temp.GetPath("counter.json"));
        var registry = new HandoffHandleRegistry(store);

        var docComment = "M:MyNamespace.MyClass.MyMethod(System.String)";
        Assert.Equal(docComment, registry.RestoreInternalHandoffForInput(docComment).Value);

        var fileLocation = "src/MyClass.cs:12:5";
        Assert.Equal(fileLocation, registry.RestoreInternalHandoffForInput(fileLocation).Value);

        var windowsPath = @"C:\src\MyClass.cs:12:5";
        Assert.Equal(windowsPath, registry.RestoreInternalHandoffForInput(windowsPath).Value);
    }

    [Fact]
    public void RestoreInternalHandoffForInput_UnknownHandle_ReturnsError()
    {
        using var temp = TestTempDirectory.Create("registry-");
        var store = new HandoffCounterStore(temp.GetPath("counter.json"));
        var registry = new HandoffHandleRegistry(store);

        var result = registry.RestoreInternalHandoffForInput("h:unknown99");
        Assert.False(result.IsSuccess);
        Assert.Equal(NavigationErrorCodes.HandoffUnknown, result.Error!.Value.Code);
    }

    [Fact]
    public void ConcurrentRequests_AreThreadSafeAndDeduplicated()
    {
        using var temp = TestTempDirectory.Create("registry-concurrent-");
        var store = new HandoffCounterStore(temp.GetPath("counter.json"), batchSize: 50);
        var registry = new HandoffHandleRegistry(store);

        var internalIds = Enumerable.Range(0, 20)
            .Select(i => $"i:0:target:content:M:Class.Method{i}")
            .ToArray();

        const int requestsPerId = 5;
        var results = new (string InternalId, string Handle)[internalIds.Length * requestsPerId];
        Parallel.For(0, results.Length, index =>
        {
            var id = internalIds[index % internalIds.Length];
            var handleResult = registry.GetOrCreateOpaqueHandleForOutput(id);
            Assert.True(handleResult.IsSuccess);
            results[index] = (id, handleResult.Value!);
        });

        var handlesById = results.GroupBy(result => result.InternalId).ToArray();
        Assert.Equal(internalIds.Length, handlesById.Length);
        Assert.All(handlesById, group => Assert.Single(group.Select(result => result.Handle).Distinct()));
        Assert.Equal(internalIds.Length, handlesById.Select(group => group.First().Handle).Distinct().Count());
        Assert.Equal(internalIds.Length, registry.Count);

        foreach (var result in handlesById.Select(group => group.First()))
        {
            var restored = registry.RestoreInternalHandoffForInput(result.Handle);
            Assert.True(restored.IsSuccess);
            Assert.Equal(result.InternalId, restored.Value);
        }
    }

    [Fact]
    public void ConcurrentOutputHandles_AreRestorableImmediately()
    {
        using var temp = TestTempDirectory.Create("registry-immediate-restore-");
        var store = new HandoffCounterStore(temp.GetPath("counter.json"), batchSize: 128);
        var registry = new HandoffHandleRegistry(store);
        const int uniqueKeys = 128;
        const int requestsPerKey = 128;

        Parallel.For(0, uniqueKeys * requestsPerKey, index =>
        {
            var key = $"i:0:target:content:M:Class.Method{index % uniqueKeys}";
            var output = registry.GetOrCreateOpaqueHandleForOutput(key);
            Assert.True(output.IsSuccess);

            var restored = registry.RestoreInternalHandoffForInput(output.Value!);
            Assert.True(restored.IsSuccess);
            Assert.Equal(key, restored.Value);
        });

        Assert.Equal(uniqueKeys, registry.Count);
    }
}
