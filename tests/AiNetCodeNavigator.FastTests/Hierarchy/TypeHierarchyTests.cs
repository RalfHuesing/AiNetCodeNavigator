namespace AiNetCodeNavigator.FastTests.Hierarchy;

public class TypeHierarchyTests
{
    [Fact]
    public void TypeHierarchy_NamespaceShell_Initializes()
    {
        Assert.NotNull(typeof(AiNetCodeNavigator.Core.Hierarchy.TypeHierarchyService));
    }
}
