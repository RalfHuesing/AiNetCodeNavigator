using System;
using AiNetCodeNavigator.Core.Assemblies;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Mcp.Tools.Relationships;

internal static class AssemblyIdentityMatcher
{
    internal static bool Matches(AssemblyIdentity actual, AssemblyIdentityDto expected) =>
        string.Equals(actual.Name, expected.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(actual.Version?.ToString(), expected.Version, StringComparison.Ordinal)
        && string.Equals(NormalizeCulture(actual.CultureName), NormalizeCulture(expected.Culture), StringComparison.OrdinalIgnoreCase)
        && string.Equals(Convert.ToHexString(actual.PublicKeyToken.ToArray()), expected.PublicKeyToken, StringComparison.OrdinalIgnoreCase);

    internal static bool Matches(AssemblyIdentityDto? actual, AssemblyIdentityDto expected) => actual is not null
        && string.Equals(actual.Name, expected.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(actual.Version, expected.Version, StringComparison.Ordinal)
        && string.Equals(NormalizeCulture(actual.Culture), NormalizeCulture(expected.Culture), StringComparison.OrdinalIgnoreCase)
        && string.Equals(actual.PublicKeyToken, expected.PublicKeyToken, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCulture(string? culture) =>
        string.IsNullOrWhiteSpace(culture) ? "neutral" : culture;
}
