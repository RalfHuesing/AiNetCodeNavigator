namespace AiNetCodeNavigator.FastTests.CallTree;

public class CallTreeTests
{
    [Fact]
    public void CallTree_NamespaceShell_Initializes()
    {
        Assert.NotNull(typeof(AiNetCodeNavigator.Core.CallTree.CallTreeBuilder));
    }
}
