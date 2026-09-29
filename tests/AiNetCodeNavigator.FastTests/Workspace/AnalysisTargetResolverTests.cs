#nullable enable

using System;
using System.IO;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Workspace;

[Trait("Category", "Unit")]
public sealed class AnalysisTargetResolverTests
{
    [Fact]
    public void Resolve_TargetPathOnly_InfersSolutionAndCanonicalizesExistingFile()
    {
        using var tempDir = TestTempDirectory.Create("analysis-target-solution-");
        var solutionPath = Path.Combine(tempDir.DirectoryPath, "workspace", "sub", "..", "sample.slnx");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(solutionPath))!);
        File.WriteAllText(Path.GetFullPath(solutionPath), string.Empty);

        var result = AnalysisTargetResolver.Resolve(new AnalysisTargetRequest(solutionPath));

        Assert.Null(result.Error);
        Assert.NotNull(result.Target);
        Assert.Equal(AnalysisTargetType.Project, result.Target!.TargetType);
        Assert.Equal(Path.GetFullPath(solutionPath), result.Target.CanonicalPath);
        Assert.Equal(Path.GetDirectoryName(result.Target.CanonicalPath), result.Target.AnalysisRoot);
        Assert.NotEmpty(result.Target.Fingerprint);
    }

    [Theory]
    [InlineData("sample.txt")]
    [InlineData("sample.bin")]
    public void Resolve_TargetPathOnly_RejectsUnsupportedExtension(string fileName)
    {
        using var tempDir = TestTempDirectory.Create("analysis-target-extension-");
        var path = Path.Combine(tempDir.DirectoryPath, fileName);
        File.WriteAllText(path, string.Empty);

        var result = AnalysisTargetResolver.Resolve(new AnalysisTargetRequest(path));

        Assert.Null(result.Target);
        Assert.NotNull(result.Error);
        Assert.Equal(NavigationErrorCodes.InvalidArgument, result.Error!.Code);
        Assert.Contains("INVALID_ARGUMENT", result.Error.FormattedMessage, StringComparison.Ordinal);
        Assert.Contains("Endung", result.Error.FormattedMessage, StringComparison.Ordinal);
        Assert.Contains("fieldPath: $.targetPath", result.Error.FormattedMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sample.sln", "Project")]
    [InlineData("sample.slnx", "Project")]
    [InlineData("sample.dll", "Assembly")]
    [InlineData("sample.exe", "Assembly")]
    public void Resolve_TargetPathOnly_AcceptsSupportedFileKinds(string fileName, string expectedTypeName)
    {
        using var tempDir = TestTempDirectory.Create("analysis-target-kinds-");
        var path = Path.Combine(tempDir.DirectoryPath, "folder with spaces", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);

        var result = AnalysisTargetResolver.Resolve(new AnalysisTargetRequest(path));

        Assert.Null(result.Error);
        Assert.NotNull(result.Target);
        var target = result.Target!;
        var expectedType = Enum.Parse<AnalysisTargetType>(expectedTypeName);
        Assert.Equal(expectedType, target.TargetType);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(path)), target.AnalysisRoot);
        Assert.Equal(
            expectedType == AnalysisTargetType.Project ? AnalysisTargetOrigin.Source : AnalysisTargetOrigin.Decompiled,
            target.Origin);
    }

    [Fact]
    public void Resolve_TargetPathOnly_RejectsRelativeMissingAndDirectoryPaths()
    {
        using var tempDir = TestTempDirectory.Create("analysis-target-path-");
        var directory = Directory.CreateDirectory(Path.Combine(tempDir.DirectoryPath, "sample.slnx")).FullName;
        var missing = Path.Combine(tempDir.DirectoryPath, "missing.slnx");

        foreach (var path in new[] { "relative.slnx", missing, directory })
        {
            var result = AnalysisTargetResolver.Resolve(new AnalysisTargetRequest(path));
            Assert.Null(result.Target);
            Assert.NotNull(result.Error);
            Assert.Equal(NavigationErrorCodes.InvalidArgument, result.Error!.Code);
            Assert.Contains("INVALID_ARGUMENT", result.Error.FormattedMessage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ResolveOptional_WithoutTargetKeepsNull()
    {
        var result = AnalysisTargetResolver.ResolveOptional(new AnalysisTargetRequest(null));

        Assert.Null(result.Target);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("*.dll")]
    [InlineData("C:\\fixtures\\*.dll")]
    [InlineData("C:\\fixtures\\???.sln")]
    public void Resolve_TargetPathOnly_RejectsWildcardsExplicitly(string wildcardPath)
    {
        var result = AnalysisTargetResolver.Resolve(new AnalysisTargetRequest(wildcardPath));
        Assert.Null(result.Target);
        Assert.NotNull(result.Error);
        Assert.Equal(NavigationErrorCodes.InvalidArgument, result.Error!.Code);
        Assert.Contains("INVALID_ARGUMENT", result.Error.FormattedMessage, StringComparison.Ordinal);
        Assert.Contains("Wildcards oder Suchmasken", result.Error.FormattedMessage, StringComparison.Ordinal);
        Assert.Contains("keine Globs", result.Error.FormattedMessage, StringComparison.Ordinal);
        Assert.Contains("Status: operation=error, completeness=not_applicable", result.Error.FullMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveRequiredSourceTarget_ValidSolution_ReturnsTarget()
    {
        using var tempDir = TestTempDirectory.Create("analysis-target-req-");
        var path = tempDir.CreateFile("my.sln", "");

        var target = AnalysisTargetResolver.ResolveRequiredSourceTarget(path);
        Assert.Equal(AnalysisTargetType.Project, target.TargetType);
        Assert.Equal(path, target.CanonicalPath);
    }

    [Fact]
    public void ResolveRequiredSourceTarget_AssemblyOrMissing_ThrowsInvalidOperationException()
    {
        using var tempDir = TestTempDirectory.Create("analysis-target-req-err-");
        var dllPath = tempDir.CreateFile("my.dll", "");

        Assert.Throws<InvalidOperationException>(() => AnalysisTargetResolver.ResolveRequiredSourceTarget(dllPath));
        Assert.Throws<InvalidOperationException>(() => AnalysisTargetResolver.ResolveRequiredSourceTarget("missing.sln"));
    }
}
