using AiNetCodeNavigator.AssemblyExport;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class ExportCommandLineTests
{
    [Theory]
    [InlineData()]
    [InlineData("output")]
    public void TryParse_RequiresOutputAndSource(params string[] args)
    {
        Assert.False(ExportCommandLine.TryParse(args, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_PreservesMultipleLiteralPatterns()
    {
        Assert.True(ExportCommandLine.TryParse(["dump", @"C:\VendorA\*.dll", @"C:\VendorB\Product?.dll"], out var parsed, out var error));
        Assert.Null(error);
        Assert.Equal("dump", parsed!.OutputDirectory);
        Assert.Equal([@"C:\VendorA\*.dll", @"C:\VendorB\Product?.dll"], parsed.Sources);
    }
}
