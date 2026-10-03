#nullable enable

using System.IO;
using AiNetCodeNavigator.Core.Symbols;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class StableTargetPathTests
{
    [Fact]
    public void TargetDigestUsesNormalizedAbsolutePath()
    {
        var canonicalPath = Path.Combine(Path.GetTempPath(), "StableTargetPath", "App.dll");
        var equivalentPath = Path.Combine(Path.GetTempPath(), "StableTargetPath", "..", "StableTargetPath", "App.dll");

        Assert.True(StableTargetPath.TryNormalizeTargetPath(canonicalPath, out var canonical));
        Assert.True(StableTargetPath.TryNormalizeTargetPath(equivalentPath, out var equivalent));
        Assert.Equal(canonical, equivalent);
        Assert.True(StableTargetPath.TryCreateTarget(canonicalPath, out var canonicalDigest));
        Assert.True(StableTargetPath.TryCreateTarget(equivalentPath, out var equivalentDigest));
        Assert.Equal(canonicalDigest, equivalentDigest);
        Assert.False(StableTargetPath.TryNormalizeTargetPath("relative/App.dll", out _));
        Assert.False(StableTargetPath.TryCreateTarget("relative/App.dll", out _));
    }
}
