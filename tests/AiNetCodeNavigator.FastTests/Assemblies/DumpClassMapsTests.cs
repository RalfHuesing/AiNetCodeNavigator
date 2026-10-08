using AiNetCodeNavigator.AssemblyExport;
using AiNetCodeNavigator.Core.Assemblies;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class DumpClassMapsTests
{
    [Fact]
    public void Read_IndexesDeclaredNamespacesNestedGenericsPartialAndRecordClasses()
    {
        using var temp = TestTempDirectory.Create("dump-class-maps-");
        File.WriteAllText(temp.GetPath("misleading.cs"), """
            namespace @One.Two {
                namespace Three {
                    public partial class Outer<T> {
                        public class Inner<U, V> { }
                    }
                    public partial class Outer<X> { }
                    public record Data;
                    public record class More;
                    public record struct Value;
                    public interface I { }
                    public struct S { public class Nested { } }
                    public enum E { A }
                    public delegate void D();
                }
            }
            public class Global { }
            """);
        File.WriteAllText(temp.GetPath("part.cs"), "namespace One.Two.Three; public partial class Outer<Renamed> { }");
        File.WriteAllText(temp.GetPath("broken.cs"), "namespace Actual; public class Recoverable { void Broken(");
        File.WriteAllText(temp.GetPath("empty.cs"), "");
        var entries = DumpClassMaps.Read(temp.DirectoryPath, "Vendor/Library.dll", ["misleading.cs", "part.cs", "broken.cs", "empty.cs"], default);
        Assert.Equal(8, entries.Count);
        Assert.Contains(entries, entry => entry.Namespace == "" && entry.Symbol == "Global");
        Assert.Contains(entries, entry => entry.Namespace == "One.Two.Three" && entry.Symbol == "One.Two.Three.Outer`1+Inner`2");
        Assert.Equal(2, entries.Count(entry => entry.Symbol == "One.Two.Three.Outer`1"));
        Assert.Contains(entries, entry => entry.Symbol == "One.Two.Three.S+Nested");
        Assert.Contains(entries, entry => entry.Symbol == "Actual.Recoverable");
        Assert.All(entries, entry => Assert.StartsWith("Vendor/Library.dll/", entry.SourcePath, StringComparison.Ordinal));
    }

    [Fact]
    public void Read_RejectsEscapingMissingSourcesAndCancellation()
    {
        using var temp = TestTempDirectory.Create("dump-map-validation-");
        Assert.Throws<InvalidDataException>(() => DumpClassMaps.Read(temp.DirectoryPath, "child", ["../escape.cs"], default));
        Assert.Throws<FileNotFoundException>(() => DumpClassMaps.Read(temp.DirectoryPath, "child", ["missing.cs"], default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => DumpClassMaps.Read(temp.DirectoryPath, "child", ["missing.cs"], cancellation.Token));
    }

    [Fact]
    public async Task Runner_MapsOnlyPublishedClassesPreservesDuplicatesAndRemovesStaleEntries()
    {
        using var temp = TestTempDirectory.Create("dump-map-published-");
        var a = AssemblyTestHelper.EmitAssembly(temp, "A.Library", "public class A { }");
        var b = AssemblyTestHelper.EmitAssembly(temp, "B.Library", "public class B { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [a, b]));
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var failB = false;
        async Task<AssemblyProjectExportResult> Export(PlannedAssembly item, string stage, CancellationToken token)
        {
            if (failB && item.Identity.Name == "B.Library") throw new IOException("fixture failure");
            await File.WriteAllTextAsync(Path.Combine(stage, "types.csproj"), "<Project />", token);
            await File.WriteAllTextAsync(Path.Combine(stage, "types.cs"), "namespace Shared; public class C { }", token);
            return new(true, "types.csproj", ["types.cs"], item.ContentHash, "test", []);
        }
        Assert.Equal(0, await ExportRunner.RunAsync(plan, output, errors, export: Export));
        var first = await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "symbol-map.md"));
        Assert.Contains("- ``Shared.C`` -> ``A/A.Library.dll/types.cs``", first, StringComparison.Ordinal);
        Assert.Contains("- ``Shared.C`` -> ``B/B.Library.dll/types.cs``", first, StringComparison.Ordinal);
        Assert.Equal(0, await ExportRunner.RunAsync(plan, output, errors, export: Export));
        Assert.Equal(first, await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "symbol-map.md")));
        failB = true;
        Assert.Equal(1, await ExportRunner.RunAsync(plan, output, errors, export: Export));
        foreach (var filename in new[] { "namespace-map.md", "symbol-map.md" })
        {
            var map = await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, filename));
            Assert.Contains("A/A.Library.dll/types.cs", map, StringComparison.Ordinal);
            Assert.DoesNotContain("B/B.Library.dll/types.cs", map, StringComparison.Ordinal);
            Assert.DoesNotContain(temp.DirectoryPath, map, StringComparison.Ordinal);
        }
        Assert.Contains("## Shared\n", await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "namespace-map.md")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_MapWriteFailureLeavesRunningCatalogAndNoSuccessSummary()
    {
        using var temp = TestTempDirectory.Create("dump-map-write-failure-");
        var source = AssemblyTestHelper.EmitAssembly(temp, "Library", "public class C { }");
        var plan = ExportPlanner.Create(new(temp.GetPath("dump"), [source]));
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var exception = await Record.ExceptionAsync(() => ExportRunner.RunAsync(plan, output, errors, export: async (item, stage, token) =>
        {
            Directory.CreateDirectory(Path.Combine(plan.OutputDirectory, "namespace-map.md"));
            await File.WriteAllTextAsync(Path.Combine(stage, "types.csproj"), "<Project />", token);
            await File.WriteAllTextAsync(Path.Combine(stage, "types.cs"), "class C { }", token);
            return new(true, "types.csproj", ["types.cs"], item.ContentHash, "test", []);
        }));
        Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
        Assert.Contains("\"runState\":\"running\"", await File.ReadAllTextAsync(Path.Combine(plan.OutputDirectory, "assemblies.json")), StringComparison.Ordinal);
        Assert.DoesNotContain("RUN COMPLETE", output.ToString(), StringComparison.Ordinal);
    }
}
