using AiNetCodeNavigator.AssemblyExport;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class ExportCommandLineTests
{
    [Theory]
    [InlineData()]
    [InlineData("output")]
    [InlineData("--output", "dump")]
    [InlineData("--source", "input")]
    [InlineData("--output", "dump", "--source")]
    [InlineData("--output", "dump", "--source", "input", "--unknown")]
    [InlineData("--output", "dump", "--output", "other", "--source", "input")]
    [InlineData("--output", "dump", "--source", "input", "--dependencies", "other")]
    [InlineData("--output", "dump", "--source", "input", "--dependencies", "all", "--dependencies", "none")]
    [InlineData("--output", "dump", "--source", "input", "--include", "folder/*.dll")]
    [InlineData("--output", "dump", "--source", "input", "--exclude", "C:\\folder\\*.dll")]
    [InlineData("--output", "dump", "--source", "input", "--exclude", "bad|name.dll")]
    [InlineData("--output", "dump", "--source", "input", "--exclude", "DevExpress**.dll")]
    [InlineData("--output", "dump", "--source", "input", "--include", "bad\u0001.dll")]
    [InlineData("--output", "dump", "--source", "input", "--exclude", "")]
    [InlineData("--output", "dump", "--source", "input", "extra")]
    [InlineData("--output", "dump", "--source", " ")]
    public void TryParse_RejectsInvalidArguments(params string[] args)
    {
        Assert.False(ExportCommandLine.TryParse(args, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_PreservesNamedOptionsRegardlessOfOrder()
    {
        Assert.True(ExportCommandLine.TryParse([
            "--exclude", "DevExpress*.dll", "--source", @"C:\VendorA", "--include", "Product?.dll",
            "--output", "dump", "--source", @"C:\VendorB\Product.exe", "--include", "Own*.dll",
            "--exclude", "Other*.dll", "--dependencies", "none", "--dry-run"], out var parsed, out var error));
        Assert.Null(error);
        Assert.Equal("dump", parsed!.OutputDirectory);
        Assert.Equal([@"C:\VendorA", @"C:\VendorB\Product.exe"], parsed.Sources);
        Assert.Equal(["Product?.dll", "Own*.dll"], parsed.Includes);
        Assert.Equal(["DevExpress*.dll", "Other*.dll"], parsed.Excludes);
        Assert.Equal(ExportDependencyMode.None, parsed.Dependencies);
        Assert.True(parsed.DryRun);
    }

    [Fact]
    public void TryParse_DefaultsToAllDependenciesAndNoFilters()
    {
        Assert.True(ExportCommandLine.TryParse(["--output", "dump", "--source", "input"], out var parsed, out _));
        Assert.Equal(ExportDependencyMode.All, parsed!.Dependencies);
        Assert.Empty(parsed.Includes);
        Assert.Empty(parsed.Excludes);
        Assert.False(parsed.DryRun);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task InvokeAsync_HelpReturnsSuccessWithoutRunningExport(string help)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var called = false;
        var exitCode = await ExportCommandLine.InvokeAsync([help], _ => { called = true; return Task.FromResult(0); }, output, errors);
        Assert.Equal(0, exitCode);
        Assert.False(called);
        Assert.Contains("--output", output.ToString());
        Assert.Contains("--exclude", output.ToString());
        Assert.Contains("--dependencies", output.ToString());
        Assert.Contains("--doc", output.ToString());
        Assert.Contains("saving it to a file", output.ToString());
        Assert.Empty(errors.ToString());
    }

    [Fact]
    public void TryParse_HelpDoesNotProduceExportArguments()
    {
        Assert.False(ExportCommandLine.TryParse(["--help"], out var parsed, out var error));
        Assert.Null(parsed);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("topics", "Embedded documentation topics:")]
    [InlineData("guide", "# Assembly export CLI")]
    public async Task InvokeAsync_DocumentationIsAvailableWithoutExportArguments(string topic, string expected)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var called = false;
        var exitCode = await ExportCommandLine.InvokeAsync(["--doc", topic],
            _ => { called = true; return Task.FromResult(0); }, output, errors);
        Assert.Equal(0, exitCode);
        Assert.False(called);
        Assert.Contains(expected, output.ToString());
        Assert.Empty(errors.ToString());
        Assert.Contains("saving it to a file", output.ToString());
        if (topic == "guide") Assert.Contains("The output directory is **disposable**", output.ToString());
    }

    [Theory]
    [InlineData("--doc", "guide")]
    [InlineData("--doc", "topics", "--output", "dump", "--source", "input")]
    public void TryParse_DocumentationDoesNotProduceExportArguments(params string[] args)
    {
        Assert.False(ExportCommandLine.TryParse(args, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("--doc")]
    [InlineData("--doc", "unknown")]
    [InlineData("--doc", "")]
    [InlineData("--doc", "guide", "--bogus")]
    [InlineData("--doc", "guide", "--source")]
    [InlineData("--doc", "guide", "--dependencies", "invalid")]
    [InlineData("--doc", "guide", "--output", "a", "--output", "b")]
    public async Task InvokeAsync_InvalidDocumentationReturnsUsageErrorWithoutExport(params string[] args)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var called = false;
        var exitCode = await ExportCommandLine.InvokeAsync(args,
            _ => { called = true; return Task.FromResult(0); }, output, errors);
        Assert.Equal(2, exitCode);
        Assert.False(called);
        Assert.NotEmpty(errors.ToString());
    }

    [Fact]
    public async Task InvokeAsync_DocumentationRejectsExportArgumentsWithoutRunningExport()
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var called = false;
        var exitCode = await ExportCommandLine.InvokeAsync(["--output", "dump", "--source", "input", "--doc", "guide"],
            _ => { called = true; return Task.FromResult(0); }, output, errors);
        Assert.Equal(2, exitCode);
        Assert.False(called);
        Assert.Empty(output.ToString());
        Assert.Contains("--doc cannot be combined with export options", errors.ToString());
    }

    [Fact]
    public async Task InvokeAsync_InvalidArgumentsReturnUsageErrorWithoutRunningExport()
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var called = false;
        var exitCode = await ExportCommandLine.InvokeAsync([], _ => { called = true; return Task.FromResult(0); }, output, errors);
        Assert.Equal(2, exitCode);
        Assert.False(called);
        Assert.NotEmpty(errors.ToString());
    }

    [Fact]
    public async Task InvokeAsync_PreservesExportFailureExitCode()
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var exitCode = await ExportCommandLine.InvokeAsync(["--output", "dump", "--source", "input"],
            _ => Task.FromResult(1), output, errors);
        Assert.Equal(1, exitCode);
    }
}
