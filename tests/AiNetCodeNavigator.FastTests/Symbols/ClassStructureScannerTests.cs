#nullable enable

using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class ClassStructureScannerTests
{
    [Fact]
    public async Task ScanAsync_ExtractsAllMembersAndVisibilities()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new ClassStructureScanRequest(fixture.Solution, "Greeter");

        var payload = await ClassStructureScanner.ScanAsync(request);

        Assert.NotNull(payload);
        Assert.Equal("SampleNamespace.Greeter", payload.TypeName);
        Assert.Equal("Class", payload.Kind);
        Assert.True(payload.TotalLines > 0);
        Assert.Single(payload.Files);

        // Members
        Assert.Contains(payload.Members, m => m.Name == "Prefix" && m.Kind == "Property" && m.Visibility == "public");
        Assert.Contains(payload.Members, m => m.Name == "Greet" && m.Kind == "Method" && m.Visibility == "public");
        Assert.Contains(payload.Members, m => m.Name == "GreetLoud" && m.Kind == "Method" && m.Visibility == "public");
    }

    [Fact]
    public async Task ScanAsync_FilterByKind_FiltersMembers()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new ClassStructureScanRequest(fixture.Solution, "Greeter", KindFilter: "Method");

        var payload = await ClassStructureScanner.ScanAsync(request);

        Assert.NotNull(payload);
        Assert.All(payload.Members, m => Assert.Equal("Method", m.Kind));
        Assert.Equal(2, payload.Members.Count); // Greet, GreetLoud
    }

    [Fact]
    public async Task ScanAsync_SortByName_OrdersAlphabetically()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new ClassStructureScanRequest(fixture.Solution, "Greeter", SortBy: "name");

        var payload = await ClassStructureScanner.ScanAsync(request);

        Assert.NotNull(payload);
        var names = payload.Members.Select(m => m.Name).ToList();
        var sorted = names.OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, names);
    }

    [Fact]
    public async Task ScanAsync_MaxMembersReportsTruncationAndCounts()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var payload = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(fixture.Solution, "Greeter", MaxMembers: 1));

        Assert.NotNull(payload);
        Assert.True(payload.Truncated);
        Assert.Equal(1, payload.ShownMemberCount);
        Assert.Single(payload.Members);
        Assert.True(payload.TotalMemberCount > payload.ShownMemberCount);
        Assert.Contains("maxMembers", payload.TruncatedBy);
    }

    [Fact]
    public async Task RenderMarkdown_FormatsMarkdownTable()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var request = new ClassStructureScanRequest(fixture.Solution, "Greeter");

        var payload = await ClassStructureScanner.ScanAsync(request);
        Assert.NotNull(payload);

        var markdown = ClassStructureScanner.RenderMarkdown(payload);

        Assert.Contains("# Typ: SampleNamespace.Greeter", markdown);
        Assert.Contains("| Kind | Name | Visibility | Lines | Signature | Handoff |", markdown);
        Assert.Contains("| Method | Greet | public |", markdown);
    }

    [Fact]
    public async Task RenderMarkdown_MultiFilePartialTypeShowsDeclaringFileForSameLineMembers()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var first = project.AddDocument("Partial.First.cs", "namespace StructureTests; public partial class Shared { public void First() { } }");
        var second = first.Project.AddDocument("Partial.Second.cs", "namespace StructureTests; public partial class Shared { public void Second() { } }");

        var payload = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(second.Project.Solution, "StructureTests.Shared"));
        Assert.NotNull(payload);

        var markdown = ClassStructureScanner.RenderMarkdown(payload);

        Assert.Contains("| Kind | Name | Visibility | File | Lines | Signature | Handoff |", markdown);
        Assert.Contains("| Method | First | public | Partial.First.cs | 1-1 (1) |", markdown);
        Assert.Contains("| Method | Second | public | Partial.Second.cs | 1-1 (1) |", markdown);
    }

    [Fact]
    public async Task RenderMarkdown_EscapesPipesInOperatorAndConstantSignatures()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var document = project.AddDocument("PipedSignatures.cs", """
            namespace StructureTests;
            public sealed class PipeValue
            {
                public const string Delimited = "left|right";
                public static PipeValue operator |(PipeValue left, PipeValue right) => left;
            }
            """);

        var payload = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(document.Project.Solution, "StructureTests.PipeValue"));
        Assert.NotNull(payload);

        var markdown = ClassStructureScanner.RenderMarkdown(payload);
        var operatorRow = markdown.Split('\n').Single(row => row.Contains("operator", System.StringComparison.Ordinal));
        var constantRow = markdown.Split('\n').Single(row => row.Contains("Delimited", System.StringComparison.Ordinal));
        var operatorEntry = Assert.Single(payload.Members, member => member.Signature.Contains("operator |", System.StringComparison.Ordinal));

        Assert.Equal(7, CountUnescapedPipes(operatorRow));
        Assert.Equal(7, CountUnescapedPipes(constantRow));
        Assert.Contains("operator \\|", operatorRow);
        Assert.Contains($"`{operatorEntry.HandoffId}`", operatorRow);
        Assert.Contains("left\\|right", constantRow);
    }

    [Fact]
    public void RenderMarkdown_NormalizesLineBreaksInDynamicCells()
    {
        var payload = new ClassStructurePayload(
            "StructureTests.LineBreaks",
            "Class",
            new[] { "LineBreaks.cs" },
            1,
            1,
            1,
            false,
            new[]
            {
                new ClassStructureMemberEntry(
                    "Method", "Name\r\nBreak", "public", 1, 1, 1,
                    "void Name()\r\n{ }", "LineBreaks.cs", null)
            },
            System.Array.Empty<string>());

        var markdown = ClassStructureScanner.RenderMarkdown(payload);
        var memberRow = markdown.Split('\n').Single(row => row.StartsWith("| Method | Name", System.StringComparison.Ordinal));

        Assert.DoesNotContain('\r', memberRow);
        Assert.Contains("Name Break", memberRow);
        Assert.Contains("void Name() { }", memberRow);
        Assert.Equal(7, CountUnescapedPipes(memberRow));
    }

    private static int CountUnescapedPipes(string value)
    {
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '|' && (i == 0 || value[i - 1] != '\\'))
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public async Task ScanAsync_IncludesRecordPrimaryConstructorParametersAndSpecificRecordKinds()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var document = project.AddDocument("StructuredTypes.cs", """
            namespace StructureTests;
            public record Person(string Name, int Age)
            {
                public string DisplayName => Name;
            }
            public record struct Coordinate(double X, double Y);
            public interface IContract
            {
                string Name { get; }
                void Execute();
            }
            """);
        var solution = document.Project.Solution;

        var person = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(solution, "StructureTests.Person"));
        var coordinate = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(solution, "StructureTests.Coordinate"));
        var contract = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(solution, "StructureTests.IContract"));

        Assert.NotNull(person);
        Assert.Equal("Record Class", person.Kind);
        Assert.Contains(person.Members, member => member.Kind == "PrimaryCtor-Param" && member.Name == "Name" && member.Visibility == "public");
        Assert.Contains(person.Members, member => member.Kind == "PrimaryCtor-Param" && member.Name == "Age");
        Assert.Contains(person.Members, member => member.Kind == "Constructor" && member.Signature.Contains("Person(", System.StringComparison.Ordinal));
        Assert.Contains(person.Members, member => member.Kind == "Property" && member.Name == "DisplayName");
        var constructorView = await ClassStructureScanner.ScanAsync(
            new ClassStructureScanRequest(solution, "StructureTests.Person", KindFilter: "Constructor"));
        Assert.NotNull(constructorView);
        Assert.Contains(constructorView.Members, member => member.Kind == "PrimaryCtor-Param" && member.Name == "Name");
        Assert.All(constructorView.Members, member => Assert.Contains(member.Kind, new[] { "Constructor", "PrimaryCtor-Param" }));

        Assert.NotNull(coordinate);
        Assert.Equal("Record Struct", coordinate.Kind);
        Assert.Contains(coordinate.Members, member => member.Kind == "PrimaryCtor-Param" && member.Name == "X");
        Assert.Contains(coordinate.Members, member => member.Kind == "PrimaryCtor-Param" && member.Name == "Y");

        Assert.NotNull(contract);
        Assert.Equal("Interface", contract.Kind);
        Assert.Contains(contract.Members, member => member.Kind == "Property" && member.Name == "Name" && member.Visibility == "public");
        Assert.Contains(contract.Members, member => member.Kind == "Method" && member.Name == "Execute" && member.Visibility == "public");
    }

    [Fact]
    public async Task ScanAsync_FormatsConstantFieldValuesInvariantly()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var document = project.AddDocument("Constants.cs", """
            namespace StructureTests;
            public class Constants
            {
                public const double Ratio = 1.5;
                public const int Offset = -7;
                public const string Greeting = "hello";
                public const string? Missing = null;
                public const char Marker = 'x';
                public const bool Enabled = true;
            }
            """);

        var payload = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(document.Project.Solution, "StructureTests.Constants", SortBy: "name"));

        Assert.NotNull(payload);
        Assert.Contains(payload.Members, member => member.Name == "Ratio" && member.Kind == "Constant" && member.Signature.Contains("1.5", System.StringComparison.Ordinal));
        Assert.Contains(payload.Members, member => member.Name == "Offset" && member.Signature.Contains("-7", System.StringComparison.Ordinal));
        Assert.Contains(payload.Members, member => member.Name == "Greeting" && member.Signature.Contains("\"hello\"", System.StringComparison.Ordinal));
        Assert.Contains(payload.Members, member => member.Name == "Missing" && member.Signature.Contains("null", System.StringComparison.Ordinal));
        Assert.Contains(payload.Members, member => member.Name == "Marker" && member.Signature.Contains("'x'", System.StringComparison.Ordinal));
        Assert.Contains(payload.Members, member => member.Name == "Enabled" && member.Signature.Contains("true", System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_ListsConstructorsFieldsPropertiesEventsAndMethodVisibilities()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var project = fixture.Solution.Projects.Single(p => p.Name == "Sample.Core");
        var document = project.AddDocument("MemberCatalog.cs", """
            namespace StructureTests;
            public class MemberCatalog
            {
                public MemberCatalog() { }
                static MemberCatalog() { }
                public int PublicProperty { get; set; }
                protected int ProtectedProperty { get; set; }
                internal string InternalProperty { get; set; }
                private int _privateField;
                public event System.EventHandler? Changed;
                public void PublicMethod() { }
                protected void ProtectedMethod() { }
                internal void InternalMethod() { }
                private void PrivateMethod() { }
            }
            """);

        var payload = await ClassStructureScanner.ScanAsync(
            new ClassStructureScanRequest(document.Project.Solution, "StructureTests.MemberCatalog"));

        Assert.NotNull(payload);
        Assert.Contains(payload.Members, member => member.Kind == "Constructor" && member.Signature.Contains("MemberCatalog()", System.StringComparison.Ordinal));
        Assert.Contains(payload.Members, member => member.Kind == "Field" && member.Name == "_privateField" && member.Visibility == "private");
        Assert.Contains(payload.Members, member => member.Kind == "Property" && member.Name == "PublicProperty" && member.Visibility == "public");
        Assert.Contains(payload.Members, member => member.Kind == "Property" && member.Name == "ProtectedProperty" && member.Visibility == "protected");
        Assert.Contains(payload.Members, member => member.Kind == "Property" && member.Name == "InternalProperty" && member.Visibility == "internal");
        Assert.Contains(payload.Members, member => member.Kind == "Event" && member.Name == "Changed" && member.Visibility == "public");
        Assert.Contains(payload.Members, member => member.Kind == "Method" && member.Name == "PublicMethod" && member.Visibility == "public");
        Assert.Contains(payload.Members, member => member.Kind == "Method" && member.Name == "ProtectedMethod" && member.Visibility == "protected");
        Assert.Contains(payload.Members, member => member.Kind == "Method" && member.Name == "InternalMethod" && member.Visibility == "internal");
        Assert.Contains(payload.Members, member => member.Kind == "Method" && member.Name == "PrivateMethod" && member.Visibility == "private");
    }

    [Fact]
    public async Task ScanAsync_MemberHandoffRoundTripsToFeatureContext()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        var payload = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(fixture.Solution, "Greeter"));
        Assert.NotNull(payload);
        var entry = Assert.Single(payload.Members, member => member.Name == "Greet");
        Assert.StartsWith("h:", entry.HandoffId);

        var context = await FeatureContextScanner.ScanAsync(new FeatureContextRequest(fixture.Solution, entry.HandoffId!));

        Assert.NotNull(context);
        Assert.Null(context.Error);
        Assert.Equal("Greet", context.Declaration.SymbolName);
    }

    [Fact]
    public async Task ScanAsync_RejectsNullRequestAndSolution()
    {
        await Assert.ThrowsAsync<System.ArgumentNullException>(() => ClassStructureScanner.ScanAsync(null!));
        await Assert.ThrowsAsync<System.ArgumentNullException>(() =>
            ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(null!, "Greeter")));
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();
        await Assert.ThrowsAsync<System.ArgumentException>(() =>
            ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(fixture.Solution, " ")));
    }

    [Fact]
    public async Task ScanAsync_InvalidHandoffReturnsStructuredError()
    {
        using var fixture = SampleCodeFixtures.CreateStandardTestSolution();

        var payload = await ClassStructureScanner.ScanAsync(new ClassStructureScanRequest(fixture.Solution, "h:invalid"));

        Assert.NotNull(payload);
        Assert.Equal("HANDOFF_UNKNOWN", payload.Error?.Code);
        Assert.Empty(payload.Members);
    }

    [Fact]
    public void RenderMarkdown_RejectsNullPayload()
    {
        Assert.Throws<System.ArgumentNullException>(() => ClassStructureScanner.RenderMarkdown(null!));
    }
}
