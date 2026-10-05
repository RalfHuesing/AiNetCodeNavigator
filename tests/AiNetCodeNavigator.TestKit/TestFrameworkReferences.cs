#nullable enable

using System;
using System.Collections.Immutable;
using System.IO;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using AiNetCodeNavigator.TestKit.Builders;

namespace AiNetCodeNavigator.TestKit;

/// <summary>Metadata-backed framework contracts for semantic test discovery fixtures.</summary>
public static class TestFrameworkReferences
{
    private const string Source = """
            using System;
            namespace Xunit
            {
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class FactAttribute : Attribute
                {
                    public FactAttribute(string? skip = null) { Skip = skip; }
                    public string? Skip { get; set; }
                    public bool Explicit { get; set; }
                    public string? SkipWhen { get; set; }
                    public string? SkipUnless { get; set; }
                }
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class TheoryAttribute : FactAttribute { public TheoryAttribute(string? skip = null) : base(skip) { } }
                public sealed class CustomFactAttribute : FactAttribute { }
                public sealed class CustomTheoryAttribute : TheoryAttribute { }
            }
            namespace Xunit.v3
            {
                public interface IFactAttribute
                {
                    string? Skip { get; }
                    bool Explicit { get; }
                    string? SkipWhen { get; }
                    string? SkipUnless { get; }
                }
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class InterfaceFactAttribute : Attribute, IFactAttribute
                {
                    public string? Skip { get; set; }
                    public bool Explicit { get; set; }
                    public string? SkipWhen { get; set; }
                    public string? SkipUnless { get; set; }
                }
                public sealed class DerivedInterfaceFactAttribute : InterfaceFactAttribute { }
            }
            namespace NUnit.Framework
            {
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class TestAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class TestCaseAttribute : TestAttribute
                {
                    public TestCaseAttribute(params object[] arguments) { }
                    public string? Ignore { get; set; }
                    public bool Explicit { get; set; }
                }
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class TestCaseSourceAttribute : TestAttribute { }
                public sealed class CustomTestAttribute : TestAttribute { }
                public sealed class CustomTestCaseAttribute : TestCaseAttribute { }
                public sealed class CustomTestCaseSourceAttribute : TestCaseSourceAttribute { }
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
                public class TestFixtureAttribute : Attribute
                {
                    public string? Ignore { get; set; }
                    public bool Explicit { get; set; }
                }
                public sealed class CustomTestFixtureAttribute : TestFixtureAttribute { }
                [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
                public class IgnoreAttribute : Attribute { }
                public sealed class CustomIgnoreAttribute : IgnoreAttribute { }
                [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
                public class ExplicitAttribute : Attribute { }
                public sealed class CustomExplicitAttribute : ExplicitAttribute { }
            }
            namespace Microsoft.VisualStudio.TestTools.UnitTesting
            {
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class TestMethodAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public class DataTestMethodAttribute : TestMethodAttribute { }
                public sealed class CustomTestMethodAttribute : TestMethodAttribute { }
                public sealed class CustomDataTestMethodAttribute : DataTestMethodAttribute { }
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
                public class TestClassAttribute : Attribute { }
                public sealed class CustomTestClassAttribute : TestClassAttribute { }
                [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
                public class IgnoreAttribute : Attribute { }
                public sealed class CustomIgnoreAttribute : IgnoreAttribute { }
            }
            """;

    private static readonly Lazy<byte[]> Image = new(CreateImage);
    public static MetadataReference Reference { get; } = CapturedMetadataReference.CreateFromImage(
        ImmutableArray.CreateRange(Image.Value), filePath: "xunit.nunit.mstest.contracts.dll");

    private static byte[] CreateImage()
    {
        var compilation = CSharpCompilation.Create("FrameworkContracts",
            [CSharpSyntaxTree.ParseText(Source)], TestWorkspaceBuilder.CoreReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }
}
