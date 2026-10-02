#nullable enable

using System.IO;
using AiNetCodeNavigator.Core.Common;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Common;

[Trait("Category", "Unit")]
public sealed class PathNormalizerTests
{
    [Fact]
    public void ToRelative_UsesFileNameForSiblingDirectorySharingRootPrefix()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator", "repo");
        var siblingFile = Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator", "repo-other", "File.cs");

        var result = PathNormalizer.ToRelative(outputRoot, siblingFile);

        Assert.Equal("File.cs", result);
    }

    [Fact]
    public void ToRelative_PreservesRelativePathForFileInsideRoot()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator", "repo");
        var rootWithTrailingSeparator = outputRoot + Path.DirectorySeparatorChar;
        var nestedFile = Path.Combine(outputRoot, "src", "File.cs");

        var result = PathNormalizer.ToRelative(rootWithTrailingSeparator, nestedFile);

        Assert.Equal("src/File.cs", result);
    }
}
