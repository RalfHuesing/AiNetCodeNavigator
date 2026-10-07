#nullable enable

using AiNetCodeNavigator.Core.Symbols;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class SymbolNameMatcherTests
{
    [Theory]
    [InlineData("gReEt", "PrefixGreeter", true)]
    [InlineData("greet", "Welcome", false)]
    [InlineData("*gReEt*", "PrefixGreeter", true)]
    [InlineData("*greet*", "Welcome", false)]
    [InlineData("*^greet$*", "Prefix^Greet$Suffix", true)]
    [InlineData("*^greet$*", "Greet", false)]
    [InlineData("Greet*", "GreetLoud", true)]
    [InlineData("Greet*", "PrefixGreet", false)]
    [InlineData("*Greet", "PrefixGreet", true)]
    [InlineData("*Greet", "GreetSuffix", false)]
    [InlineData("Gr?et", "Greet", true)]
    [InlineData("Gr?et", "Greeet", false)]
    [InlineData("Gr?et", "PrefixGreet", false)]
    [InlineData("*Gr?et*", "PrefixGreetSuffix", true)]
    [InlineData("*Gr?et*", "PrefixGreeetSuffix", false)]
    [InlineData("^Greet*$", "Greet", false)]
    [InlineData("^Greet*$", "^GreetSuffix$", true)]
    [InlineData("(?i)greet", "Greet", false)]
    [InlineData("(?i)greet", "(Xi)greet", true)]
    [InlineData("^greet[0-9]+$", "Greet12", true)]
    [InlineData("^greet[0-9]+$", "PrefixGreet12", false)]
    [InlineData("Greet\\d+", "PrefixGreet12Suffix", true)]
    [InlineData("Greet\\d+", "GreetLoud", false)]
    [InlineData("^[", "Prefix^[Suffix", true)]
    [InlineData("^[", "Greet", false)]
    [InlineData("[Greet]", "G", false)]
    [InlineData("[Greet]", "Prefix[Greet]Suffix", true)]
    [InlineData("  `Greet()`  ", "Greet", true)]
    [InlineData("\"Greeter<T>\"", "Greeter", true)]
    [InlineData("'Greet ()'", "Welcome", false)]
    public void SimplePatterns_PreserveMatchingModesAndPrefilter(string pattern, string name, bool expected)
    {
        // Error-type symbols allow literal fallback/precedence probes that are not valid C# identifiers.
        var symbol = CSharpCompilation.Create("Matching").CreateErrorTypeSymbol(null, name, 0);

        Assert.Equal(expected, SymbolNameMatcher.CreateDeclarationNameFilter(pattern)(name));
        Assert.Equal(expected, SymbolNameMatcher.MatchesSymbol(symbol, pattern));
    }
}
