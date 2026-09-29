#nullable enable

using System.Collections.Concurrent;
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
                NavigationErrorCodes.HandoffUnknown,
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

        var handles = new ConcurrentBag<string>();

        Parallel.For(0, 100, _ =>
        {
            var id = internalIds[Random.Shared.Next(internalIds.Length)];
            var handleResult = registry.GetOrCreateOpaqueHandleForOutput(id);
            if (handleResult.IsSuccess)
            {
                handles.Add(handleResult.Value!);
            }
        });

        // Exactly 20 distinct handles issued for 20 distinct IDs
        Assert.Equal(20, handles.Distinct().Count());
        Assert.Equal(20, registry.Count);
    }
}
