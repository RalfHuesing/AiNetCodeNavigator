#nullable enable

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>Recognizes framework test methods and classifies their static activity separately.</summary>
internal static class TestFrameworkClassifier
{
    private static readonly ConditionalWeakTable<PortableExecutableReference, FrameworkReferenceClassification> ReferenceClassifications = new();

    private const string XunitFactAttribute = "Xunit.FactAttribute";
    private const string XunitTheoryAttribute = "Xunit.TheoryAttribute";
    private const string XunitV3FactInterface = "Xunit.v3.IFactAttribute";
    private const string NUnitTestAttribute = "NUnit.Framework.TestAttribute";
    private const string NUnitTestCaseAttribute = "NUnit.Framework.TestCaseAttribute";
    private const string NUnitTestCaseSourceAttribute = "NUnit.Framework.TestCaseSourceAttribute";
    private const string NUnitTestFixtureAttribute = "NUnit.Framework.TestFixtureAttribute";
    private const string NUnitIgnoreAttribute = "NUnit.Framework.IgnoreAttribute";
    private const string NUnitExplicitAttribute = "NUnit.Framework.ExplicitAttribute";
    private const string MsTestMethodAttribute = "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute";
    private const string MsTestDataMethodAttribute = "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute";
    private const string MsTestClassAttribute = "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute";
    private const string MsTestIgnoreAttribute = "Microsoft.VisualStudio.TestTools.UnitTesting.IgnoreAttribute";

    /// <summary>Uses the reference's metadata image, independently of its file or assembly name.</summary>
    public static bool IsFrameworkReference(MetadataReference reference) =>
        reference is PortableExecutableReference executableReference
        && ReferenceClassifications.GetValue(executableReference, static owner =>
            new FrameworkReferenceClassification(DefinesFrameworkTestTypes(owner))).IsFramework;

    private static bool DefinesFrameworkTestTypes(PortableExecutableReference reference)
    {
        if (reference.GetMetadata() is not AssemblyMetadata assembly) return false;
        foreach (var module in assembly.GetModules())
        {
            var reader = module.GetMetadataReader();
            foreach (var handle in reader.TypeDefinitions)
            {
                var definition = reader.GetTypeDefinition(handle);
                // Skip nested definitions: their namespace/name pair is not their full metadata name.
                if (!definition.GetDeclaringType().IsNil) continue;
                var name = reader.GetString(definition.Name);
                if (name is not ("FactAttribute" or "TheoryAttribute" or "IFactAttribute"
                    or "TestAttribute" or "TestCaseAttribute" or "TestCaseSourceAttribute"
                    or "TestMethodAttribute" or "DataTestMethodAttribute")) continue;
                var metadataName = reader.GetString(definition.Namespace) + "." + name;
                if (metadataName is XunitFactAttribute or XunitTheoryAttribute or XunitV3FactInterface
                    or NUnitTestAttribute or NUnitTestCaseAttribute or NUnitTestCaseSourceAttribute
                    or MsTestMethodAttribute or MsTestDataMethodAttribute) return true;
            }
        }
        return false;
    }

    private sealed record FrameworkReferenceClassification(bool IsFramework);

    public static TestMethodClassification? Classify(IMethodSymbol method)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (method.ContainingType is null)
        {
            return null;
        }

        var attributes = method.GetAttributes();
        var classifications = attributes.Select(attribute => ClassifyAttribute(method, attribute))
            .OfType<TestMethodClassification>().ToArray();
        if (classifications.Length == 0) return null;
        var fixtureStatus = GetWholeMethodOrFixtureActivityStatus(method);
        if (fixtureStatus == TestActivityStatus.Excluded)
        {
            return classifications[0] with { ActivityStatus = TestActivityStatus.Excluded };
        }

        // Any active attribute is sufficient; conditional attributes take precedence over exclusions.
        var classification = classifications.OrderBy(static classification => classification.ActivityStatus switch
        {
            TestActivityStatus.Active => 0,
            TestActivityStatus.Conditional => 1,
            _ => 2
        }).First();
        return fixtureStatus == TestActivityStatus.Conditional && classification.ActivityStatus == TestActivityStatus.Active
            ? classification with { ActivityStatus = TestActivityStatus.Conditional }
            : classification;
    }

    private static TestMethodClassification? ClassifyAttribute(IMethodSymbol method, AttributeData attribute)
    {
        if (IsXunitAttribute(attribute))
        {
            var status = HasBooleanValue(attribute, "Explicit", true) ? TestActivityStatus.Excluded
                : HasNonNullStringValue(attribute, "SkipWhen") || HasNonNullStringValue(attribute, "SkipUnless")
                    ? TestActivityStatus.Conditional
                    : HasNonNullStringValue(attribute, "Skip") ? TestActivityStatus.Excluded : TestActivityStatus.Active;
            return new TestMethodClassification(TestFrameworkKind.Xunit, status);
        }
        if (IsNUnitTestAttribute(attribute))
        {
            // Per-row exclusions do not establish that every dynamically discovered case is excluded.
            var status = HasNonEmptyStringValue(attribute, "Ignore") || HasBooleanValue(attribute, "Explicit", true)
                ? TestActivityStatus.Conditional : TestActivityStatus.Active;
            return new TestMethodClassification(TestFrameworkKind.NUnit, status);
        }
        return HasAttribute(method.ContainingType, MsTestClassAttribute) && IsMSTestMethodAttribute(attribute)
            ? new TestMethodClassification(TestFrameworkKind.MSTest, TestActivityStatus.Active)
            : null;
    }

    private static TestActivityStatus GetWholeMethodOrFixtureActivityStatus(IMethodSymbol method)
    {
        if (HasAttribute(method, NUnitIgnoreAttribute)
            || HasAttribute(method, NUnitExplicitAttribute)
            || HasAttribute(method, MsTestIgnoreAttribute))
        {
            return TestActivityStatus.Excluded;
        }

        var status = TestActivityStatus.Active;
        for (var type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            if (HasAttribute(type, NUnitIgnoreAttribute)
                || HasAttribute(type, NUnitExplicitAttribute)
                || HasAttribute(type, MsTestIgnoreAttribute))
            {
                return TestActivityStatus.Excluded;
            }
            var fixtures = type.GetAttributes().Where(attribute =>
                DerivesFromMetadataType(attribute.AttributeClass, NUnitTestFixtureAttribute)).ToArray();
            var excludedCount = fixtures.Count(IsExcludedNUnitFixtureAttribute);
            if (excludedCount > 0 && excludedCount == fixtures.Length) return TestActivityStatus.Excluded;
            if (excludedCount > 0) status = TestActivityStatus.Conditional;
        }

        return status;
    }

    private static bool IsXunitAttribute(AttributeData attribute)
    {
        var attributeClass = attribute.AttributeClass;
        return DerivesFromMetadataType(attributeClass, XunitFactAttribute)
            || DerivesFromMetadataType(attributeClass, XunitTheoryAttribute)
            || ImplementsMetadataInterface(attributeClass, XunitV3FactInterface);
    }

    private static bool IsNUnitTestAttribute(AttributeData attribute) =>
        DerivesFromMetadataType(attribute.AttributeClass, NUnitTestAttribute)
        || DerivesFromMetadataType(attribute.AttributeClass, NUnitTestCaseAttribute)
        || DerivesFromMetadataType(attribute.AttributeClass, NUnitTestCaseSourceAttribute);

    private static bool IsMSTestMethodAttribute(AttributeData attribute) =>
        DerivesFromMetadataType(attribute.AttributeClass, MsTestMethodAttribute)
        || DerivesFromMetadataType(attribute.AttributeClass, MsTestDataMethodAttribute);

    private static bool IsExcludedNUnitFixtureAttribute(AttributeData attribute)
    {
        if (!DerivesFromMetadataType(attribute.AttributeClass, NUnitTestFixtureAttribute))
        {
            return false;
        }

        return HasNonEmptyStringValue(attribute, "Ignore") || HasBooleanValue(attribute, "Explicit", true);
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().Any(attribute => DerivesFromMetadataType(attribute.AttributeClass, metadataName));

    private static bool DerivesFromMetadataType(INamedTypeSymbol? type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (IsMetadataType(current) && GetMetadataName(current) == metadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsMetadataInterface(INamedTypeSymbol? type, string metadataName) =>
        type is not null && type.AllInterfaces.Any(
            implemented => IsMetadataType(implemented) && GetMetadataName(implemented) == metadataName);

    private static bool IsMetadataType(INamedTypeSymbol type) => type.Locations.Any(static location => location.IsInMetadata);

    private static string GetMetadataName(INamedTypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static bool HasNonNullStringValue(AttributeData attribute, string name) =>
        TryGetValue(attribute, name, out var value)
        && value.Kind == TypedConstantKind.Primitive
        && value.Value is string;

    private static bool HasNonEmptyStringValue(AttributeData attribute, string name) =>
        TryGetValue(attribute, name, out var value)
        && value.Kind == TypedConstantKind.Primitive
        && value.Value is string text
        && text.Length > 0;

    private static bool HasBooleanValue(AttributeData attribute, string name, bool expected) =>
        TryGetValue(attribute, name, out var value)
        && value.Kind == TypedConstantKind.Primitive
        && value.Value is bool actual
        && actual == expected;

    private static bool TryGetValue(AttributeData attribute, string name, out TypedConstant value)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name)
            {
                value = argument.Value;
                return true;
            }
        }

        var parameters = attribute.AttributeConstructor?.Parameters;
        var arguments = attribute.ConstructorArguments;
        if (parameters is not null)
        {
            for (var index = 0; index < Math.Min(parameters.Value.Length, arguments.Length); index++)
            {
                if (string.Equals(parameters.Value[index].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = arguments[index];
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
