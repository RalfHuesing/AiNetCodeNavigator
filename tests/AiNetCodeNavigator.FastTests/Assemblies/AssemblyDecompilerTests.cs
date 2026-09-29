namespace AiNetCodeNavigator.FastTests.Assemblies;

public class AssemblyDecompilerTests
{
    [Fact]
    public void AssemblyDecompiler_NamespaceShell_Initializes()
    {
        Assert.NotNull(typeof(AiNetCodeNavigator.Core.Assemblies.AssemblyDecompilerService));
    }
}
