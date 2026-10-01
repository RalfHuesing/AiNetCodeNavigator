#nullable enable

using System;
using System.Collections.Immutable;
using System.Reflection;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class AssemblyIdentityMatcherTests
{
    [Fact]
    public void MatchesRoslynIdentity_IgnoresNameCultureAndPublicKeyTokenCasing()
    {
        var actual = new AssemblyIdentity(
            "navigator",
            new Version(1, 2, 3, 4),
            "neutral",
            ImmutableArray.Create<byte>(0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89),
            hasPublicKey: false,
            isRetargetable: false,
            AssemblyContentType.Default);
        var expected = new AssemblyIdentityDto("NAVIGATOR", "1.2.3.4", "NEUTRAL", "abcdef0123456789");

        Assert.True(AssemblyIdentityMatcher.Matches(actual, expected));
    }

    [Fact]
    public void MatchesRoslynIdentity_RequiresExactVersion()
    {
        var actual = new AssemblyIdentity(
            "navigator",
            new Version(1, 2, 3, 4),
            "neutral",
            ImmutableArray.Create<byte>(0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89),
            hasPublicKey: false,
            isRetargetable: false,
            AssemblyContentType.Default);
        var expected = new AssemblyIdentityDto("navigator", "1.2.3.5", "neutral", "ABCDEF0123456789");

        Assert.False(AssemblyIdentityMatcher.Matches(actual, expected));
    }

    [Fact]
    public void MatchesDtoIdentity_IgnoresNameCultureAndTokenCasingButRequiresExactVersion()
    {
        var actual = new AssemblyIdentityDto("navigator", "1.2.3.4", "en-us", "abcdef01");
        var casingVariant = new AssemblyIdentityDto("NAVIGATOR", "1.2.3.4", "EN-US", "ABCDEF01");
        var differentVersion = casingVariant with { Version = "1.2.3.5" };
        var differentName = casingVariant with { Name = "another" };
        var differentCulture = casingVariant with { Culture = "fr-fr" };
        var differentToken = casingVariant with { PublicKeyToken = "01234567" };

        Assert.True(AssemblyIdentityMatcher.Matches(actual, casingVariant));
        Assert.False(AssemblyIdentityMatcher.Matches(actual, differentVersion));
        Assert.False(AssemblyIdentityMatcher.Matches(actual, differentName));
        Assert.False(AssemblyIdentityMatcher.Matches(actual, differentCulture));
        Assert.False(AssemblyIdentityMatcher.Matches(actual, differentToken));
        Assert.True(AssemblyIdentityMatcher.Matches(actual with { Culture = "  " }, casingVariant with { Culture = "neutral" }));
    }

    [Fact]
    public void MatchesDtoIdentity_ReturnsFalseForNullLeftIdentity()
    {
        var expected = new AssemblyIdentityDto("navigator", "1.2.3.4", "neutral", "");

        Assert.False(AssemblyIdentityMatcher.Matches((AssemblyIdentityDto?)null, expected));
    }
}
