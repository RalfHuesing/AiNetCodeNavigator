#nullable enable

namespace AiNetCodeNavigator.TestKit.Fixtures;

using AiNetCodeNavigator.TestKit.Builders;

/// <summary>
/// Predefined, semantically rich C# code templates for navigation and AST tests.
/// </summary>
public static class SampleCodeFixtures
{
    public const string GreeterSource = """
        namespace SampleNamespace;

        public class Greeter
        {
            public string Prefix { get; set; } = "Hello";

            public string Greet(string name)
            {
                return $"{Prefix}, {name}!";
            }

            public string GreetLoud(string name)
            {
                return Greet(name).ToUpperInvariant();
            }
        }
        """;

    public const string CallerSource = """
        namespace SampleNamespace;

        public class ServiceCaller
        {
            private readonly Greeter _greeter = new();

            public string ExecuteSingle(string name)
            {
                return _greeter.Greet(name);
            }

            public string ExecuteMultiple(string name)
            {
                var a = _greeter.Greet(name);
                var b = _greeter.GreetLoud(name);
                return $"{a} | {b}";
            }
        }
        """;

    public const string HierarchySource = """
        namespace SampleNamespace.Hierarchy;

        using System;

        public interface IProcessor
        {
            void Process();
        }

        public interface IAdvancedProcessor : IProcessor
        {
            int Compute(int input);
        }

        public abstract class BaseProcessor : IProcessor
        {
            public abstract void Process();

            public virtual string GetName() => "Base";
        }

        public class FastProcessor : BaseProcessor, IAdvancedProcessor
        {
            public override void Process()
            {
            }

            public int Compute(int input) => input * 2;
        }

        public sealed class SafeProcessor : FastProcessor, IDisposable
        {
            public void Dispose()
            {
            }
        }
        """;

    public const string RecordAndStructSource = """
        namespace SampleNamespace.Types;

        public record Person(string FirstName, string LastName);

        public readonly record struct Coordinate(double X, double Y);

        public enum ProcessingStatus
        {
            Pending,
            Active,
            Completed
        }
        """;

    public const string ExtensionSource = """
        namespace SampleNamespace.Extensions;

        public static class StringExtensions
        {
            public static string DoubleString(this string input)
            {
                return input + input;
            }
        }
        """;

    /// <summary>
    /// Creates a two-project in-memory solution with cross-references for integration tests.
    /// </summary>
    public static TestSolutionHandle CreateStandardTestSolution()
    {
        var coreProj = new ProjectSpec(
            Name: "Sample.Core",
            Documents:
            [
                ("Greeter.cs", GreeterSource),
                ("Hierarchy.cs", HierarchySource),
                ("Types.cs", RecordAndStructSource),
                ("Extensions.cs", ExtensionSource)
            ],
            VirtualProjectDirectory: "src/Sample.Core");

        var appProj = new ProjectSpec(
            Name: "Sample.App",
            Documents:
            [
                ("Caller.cs", CallerSource)
            ],
            ProjectReferences: ["Sample.Core"],
            VirtualProjectDirectory: "src/Sample.App");

        return TestWorkspaceBuilder.CreateSolution(
            @"C:\VirtualRepo\SampleSolution.slnx",
            coreProj,
            appProj);
    }
}
