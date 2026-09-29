namespace AiNetCodeNavigator.FastTests.Symbols;

public class SymbolNavigationTests
{
    [Fact]
    public void SymbolNavigation_NamespaceShell_Initializes()
    {
        Assert.NotNull(typeof(AiNetCodeNavigator.Core.Symbols.SymbolNavigationService));
    }
}
