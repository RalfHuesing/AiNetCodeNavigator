using AiNetCodeNavigator.Cli;

namespace AiNetCodeNavigator.FastTests.Mcp;

[Trait("Category", "Component")]
[Collection(HostRuntimeIsolationCollection.Name)]
public sealed class CommandLineOptionsTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    [InlineData("--doc", "topics")]
    [InlineData("--doc", "overview")]
    [InlineData("--doc", "setup")]
    [InlineData("--doc", "tools")]
    public async Task InformationalInvocation_UsesOnlyStderrWithoutStartingHost(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var started = false;
        int exitCode;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            exitCode = await CommandLineOptions.InvokeAsync(args, (_, _, _) =>
            {
                started = true;
                return Task.FromResult(0);
            });
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Equal(0, exitCode);
        Assert.False(started);
        Assert.Empty(stdout.ToString());
        Assert.NotEmpty(stderr.ToString());
        if (args[0] == "--help")
        {
            Assert.Contains("Read-only C#", stderr.ToString());
            Assert.Contains("--doc topics", stderr.ToString());
            Assert.Contains("stderr", stderr.ToString());
        }
    }

    [Theory]
    [InlineData("overview", "# AiNetCodeNavigator")]
    [InlineData("setup", "# Setup")]
    [InlineData("tools", "# MCP Tools")]
    public async Task Documentation_ContainsCanonicalContentAndOptionalCaptureHint(string topic, string heading)
    {
        using var stderr = new StringWriter();
        var exitCode = await CommandLineOptions.InvokeAsync(["--doc", topic], (_, _, _) => throw new InvalidOperationException("Host started"),
            errorOutput: stderr);
        var text = stderr.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains(heading, text);
        Assert.Contains("Consider saving stderr to a file", text);
        Assert.Contains($"--doc {topic} 2>", text);
        Assert.True(text.IndexOf("Consider saving stderr", StringComparison.Ordinal) < text.IndexOf(heading, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("--doc", "missing")]
    [InlineData("--doc")]
    [InlineData("--config")]
    [InlineData("--doc", "--config")]
    [InlineData("--doc", "tools", "--config")]
    [InlineData("--doc", "tools", "--config", "missing.json")]
    [InlineData("--unknown")]
    public async Task InvalidInvocation_ReportsErrorWithoutStartingHost(params string[] args)
    {
        using var stderr = new StringWriter();
        var exitCode = await CommandLineOptions.InvokeAsync(args, (_, _, _) => throw new InvalidOperationException("Host started"),
            errorOutput: stderr);
        Assert.NotEqual(0, exitCode);
        Assert.NotEmpty(stderr.ToString());
        if (args.Contains("missing"))
            Assert.Contains("--doc topics", stderr.ToString());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("hostsettings.json", false)]
    public async Task ServerInvocation_PreservesConfigurationAndFailure(string? path, bool defaultPath)
    {
        using var stderr = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        var called = false;
        var args = path is null ? Array.Empty<string>() : ["--config", path];
        var exitCode = await CommandLineOptions.InvokeAsync(args, (configuredPath, isDefault, token) =>
        {
            called = true;
            Assert.Equal(path, configuredPath);
            Assert.Equal(defaultPath, isDefault);
            Assert.True(token.CanBeCanceled);
            return Task.FromResult(7);
        }, cancellation.Token, stderr);
        Assert.True(called);
        Assert.Equal(7, exitCode);
        Assert.Empty(stderr.ToString());
    }
}
