#nullable enable

using System.IO;
using System.Linq;
using AiNetCodeNavigator.Core.FileStructure;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.FileStructure;

[Trait("Category", "Unit")]
public sealed class GetFileTreeScannerTests
{
    [Fact]
    public void Scan_Directory_CollectsFilesAndIgnoresExcluded()
    {
        using var tempDir = TestTempDirectory.Create();
        var subDir = Path.Combine(tempDir.DirectoryPath, "src");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "Code.cs"), "public class Code {}");
        File.WriteAllText(Path.Combine(subDir, "Doc.txt"), "readme");

        var binDir = Path.Combine(tempDir.DirectoryPath, "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "Artifact.dll"), "binary");

        var request = new FileTreeScanRequest(
            RootDirectory: tempDir.DirectoryPath,
            View: "files");

        var result = GetFileTreeScanner.Scan(request);

        Assert.Equal(2, result.TotalFiles);
        Assert.Contains(result.Entries, e => e.Name == "Code.cs");
        Assert.Contains(result.Entries, e => e.Name == "Doc.txt");
        Assert.DoesNotContain(result.Entries, e => e.Name == "Artifact.dll");
    }

    [Fact]
    public void Scan_FileFilter_FiltersMatchingFiles()
    {
        using var tempDir = TestTempDirectory.Create();
        File.WriteAllText(Path.Combine(tempDir.DirectoryPath, "Test.cs"), "public class Test {}");
        File.WriteAllText(Path.Combine(tempDir.DirectoryPath, "Config.json"), "{}");

        var request = new FileTreeScanRequest(
            RootDirectory: tempDir.DirectoryPath,
            FileFilter: "*.cs");

        var result = GetFileTreeScanner.Scan(request);

        Assert.Single(result.Entries);
        Assert.Equal("Test.cs", result.Entries[0].Name);
    }

    [Fact]
    public void Scan_SummaryView_GeneratesSummaryByDirectory()
    {
        using var tempDir = TestTempDirectory.Create();
        var sub = Path.Combine(tempDir.DirectoryPath, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "File1.cs"), "1");
        File.WriteAllText(Path.Combine(sub, "File2.cs"), "2");

        var request = new FileTreeScanRequest(
            RootDirectory: tempDir.DirectoryPath,
            View: "summary");

        var result = GetFileTreeScanner.Scan(request);

        Assert.NotEmpty(result.Summaries);
        var subSummary = result.Summaries.FirstOrDefault(s => s.DirectoryPath.Contains("sub"));
        Assert.NotNull(subSummary);
        Assert.Equal(2, subSummary.FileCount);
        Assert.Contains("| Directory | Files | Total size |", result.FormattedText);
    }

    [Fact]
    public void Scan_TreeViewRendersNestedDirectoryAggregatesAndRootFiles()
    {
        using var tempDir = TestTempDirectory.Create();
        var src = Path.Combine(tempDir.DirectoryPath, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(tempDir.DirectoryPath, "README.md"), "readme");
        File.WriteAllText(Path.Combine(src, "Code.cs"), "class Code {}");

        var result = GetFileTreeScanner.Scan(new FileTreeScanRequest(tempDir.DirectoryPath));

        Assert.Contains("├── src/ 1 files", result.FormattedText);
        Assert.Contains("├── README.md", result.FormattedText);
        Assert.DoesNotContain("src/Code.cs", result.FormattedText);
    }

    [Fact]
    public void Scan_RelativeRootCannotEscapeAnalysisRoot()
    {
        using var tempDir = TestTempDirectory.Create();
        var root = Path.Combine(tempDir.DirectoryPath, "root");
        var sibling = Path.Combine(tempDir.DirectoryPath, "sibling");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "secret.cs"), "class Secret {}");

        var result = GetFileTreeScanner.Scan(new FileTreeScanRequest(root, RelativeRoot: "../sibling", View: "files"));

        Assert.Empty(result.Entries);
    }

    [Fact]
    public void Scan_SummaryRollsUpNestedMatchesAndLimitsReturnedDirectories()
    {
        using var tempDir = TestTempDirectory.Create();
        var deep = Path.Combine(tempDir.DirectoryPath, "src", "Project", "Nested");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "A.cs"), "class A {}");
        File.WriteAllText(Path.Combine(deep, "B.cs"), "class B {}");

        var result = GetFileTreeScanner.Scan(new FileTreeScanRequest(
            tempDir.DirectoryPath,
            View: "summary",
            MaxResults: 1,
            TreeDepth: null));

        Assert.Empty(result.Entries);
        Assert.Equal(2, result.TotalFiles);
        Assert.Single(result.Summaries);
        Assert.Equal(".", result.Summaries[0].DirectoryPath);
        Assert.Equal(2, result.Summaries[0].FileCount);
        Assert.True(result.IsTruncated);
        Assert.Contains("maxResults", result.TruncatedBy!);
        Assert.Equal("refine_scope", result.Next!.Kind);
    }

    [Fact]
    public void Scan_RespectsDepthAndReportsCancellationAndTruncation()
    {
        using var tempDir = TestTempDirectory.Create();
        var deep = Path.Combine(tempDir.DirectoryPath, "src", "Project");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(tempDir.DirectoryPath, "root.cs"), "class Root {}");
        File.WriteAllText(Path.Combine(deep, "deep.cs"), "class Deep {}");

        var depthLimited = GetFileTreeScanner.Scan(new FileTreeScanRequest(
            tempDir.DirectoryPath,
            View: "files",
            MaxDepth: 0));
        Assert.Single(depthLimited.Entries);
        Assert.True(depthLimited.IsTruncated);
        Assert.Contains("maxDepth", depthLimited.TruncatedBy!);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = GetFileTreeScanner.Scan(new FileTreeScanRequest(tempDir.DirectoryPath), cancellation.Token);
        Assert.False(cancelled.ScanCompleted);
        Assert.Contains("cancellation", cancelled.TruncatedBy!);
        Assert.Equal("refine_scope", cancelled.Next!.Kind);
    }

    [Fact]
    public void Scan_SortsBoundedResultsAndDoesNotModifySourceFiles()
    {
        using var tempDir = TestTempDirectory.Create();
        var first = Path.Combine(tempDir.DirectoryPath, "z.cs");
        var second = Path.Combine(tempDir.DirectoryPath, "a.cs");
        File.WriteAllText(first, "class Z {}\n");
        File.WriteAllText(second, "class A {}\n");
        var firstBefore = File.ReadAllBytes(first);
        var secondBefore = File.ReadAllBytes(second);

        var result = GetFileTreeScanner.Scan(new FileTreeScanRequest(
            tempDir.DirectoryPath,
            View: "files",
            MaxResults: 1));

        Assert.Equal("a.cs", Assert.Single(result.Entries).Name);
        Assert.True(result.IsTruncated);
        Assert.Contains("maxResults", result.TruncatedBy!);
        Assert.Equal(firstBefore, File.ReadAllBytes(first));
        Assert.Equal(secondBefore, File.ReadAllBytes(second));
    }

    [Fact]
    public void Scan_AppliesExtensionGlobAndRequestedExclusionFilters()
    {
        using var tempDir = TestTempDirectory.Create();
        var docs = Path.Combine(tempDir.DirectoryPath, "Docs");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "guide.md"), "guide");
        File.WriteAllText(Path.Combine(docs, "skip.md"), "excluded");
        File.WriteAllText(Path.Combine(docs, "data.json"), "{}");

        var result = GetFileTreeScanner.Scan(new FileTreeScanRequest(
            tempDir.DirectoryPath,
            View: "files",
            FileFilter: "Docs/**/*.md",
            IncludeExtensions: ["md"],
            ExcludePatterns: ["**/skip.md"]));

        var file = Assert.Single(result.Entries);
        Assert.Equal("Docs/guide.md", file.RelativePath);
        Assert.Equal(1, result.ExcludedFileCount);
        Assert.Equal(3, result.ScannedFileCount);
    }

    [Fact]
    public void Scan_RejectsInvalidBoundsAndRelativeGlobTraversal()
    {
        using var tempDir = TestTempDirectory.Create();

        var tooDeep = GetFileTreeScanner.Scan(new FileTreeScanRequest(tempDir.DirectoryPath, MaxDepth: 33));
        var tooMany = GetFileTreeScanner.Scan(new FileTreeScanRequest(tempDir.DirectoryPath, MaxResults: 2001));
        var escapingFilter = GetFileTreeScanner.Scan(new FileTreeScanRequest(tempDir.DirectoryPath, FileFilter: "../*.cs"));

        Assert.Contains("MaxDepth", tooDeep.Error);
        Assert.Contains("MaxResults", tooMany.Error);
        Assert.Contains("glob", escapingFilter.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scan_NonexistentRootReturnsRecoverableErrorState()
    {
        using var tempDir = TestTempDirectory.Create();
        var missing = Path.Combine(tempDir.DirectoryPath, "missing");

        var result = GetFileTreeScanner.Scan(new FileTreeScanRequest(tempDir.DirectoryPath, RelativeRoot: "missing"));

        Assert.False(result.ScanCompleted);
        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scan_CanSortBySizeAndOmitSizeMetadataWhileCountingLines()
    {
        using var tempDir = TestTempDirectory.Create();
        File.WriteAllText(Path.Combine(tempDir.DirectoryPath, "small.cs"), "a\n");
        File.WriteAllText(Path.Combine(tempDir.DirectoryPath, "large.cs"), "first\nsecond\nthird\n");

        var result = GetFileTreeScanner.Scan(new FileTreeScanRequest(
            tempDir.DirectoryPath,
            View: "files",
            SortBy: "size_desc",
            IncludeMetadata: false,
            IncludeLineCount: true));

        Assert.Equal("large.cs", result.Entries[0].Name);
        Assert.Null(result.Entries[0].Size);
        Assert.Equal(3, result.Entries[0].LineCount);
        Assert.True(result.TotalBytes > 0);
    }
}
