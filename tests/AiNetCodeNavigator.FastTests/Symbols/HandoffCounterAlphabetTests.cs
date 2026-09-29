#nullable enable

using System;
using AiNetCodeNavigator.Core.Symbols;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class HandoffCounterAlphabetTests
{
    [Theory]
    [InlineData("a", "b")]
    [InlineData("b", "c")]
    [InlineData("y", "z")]
    [InlineData("z", "0")]
    [InlineData("0", "1")]
    [InlineData("8", "9")]
    [InlineData("9", "A")]
    [InlineData("A", "B")]
    [InlineData("Y", "Z")]
    [InlineData("Z", "aa")]
    [InlineData("aa", "ab")]
    [InlineData("aZ", "ba")]
    [InlineData("ZZ", "aaa")]
    [InlineData("aaZaZ", "aaZba")]
    public void GetNext_FollowsDefinedBase62SequenceAndCarries(string current, string expected)
    {
        var next = HandoffCounterAlphabet.GetNext(current);
        Assert.Equal(expected, next);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("z")]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("A")]
    [InlineData("Z")]
    [InlineData("aaZaZ")]
    [InlineData("Abc012XYZ")]
    public void IsValidCounter_AcceptsValidBase62Characters(string counter)
    {
        Assert.True(HandoffCounterAlphabet.IsValidCounter(counter));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a b")]
    [InlineData("a-b")]
    [InlineData("a_b")]
    [InlineData("x:b")]
    [InlineData("h:a")]
    [InlineData("äöü")]
    [InlineData("!")]
    public void IsValidCounter_RejectsInvalidCharactersAndEmpty(string? counter)
    {
        Assert.False(HandoffCounterAlphabet.IsValidCounter(counter));
    }

    [Theory]
    [InlineData("h:a")]
    [InlineData("h:Z")]
    [InlineData("h:123")]
    [InlineData("h:abcXYZ")]
    public void IsValidHandle_AcceptsPrefixedCounters(string handle)
    {
        Assert.True(HandoffCounterAlphabet.IsValidHandle(handle));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("h:")]
    [InlineData("h: ")]
    [InlineData("a")]
    [InlineData("i:a")]
    [InlineData("h:a-b")]
    [InlineData("H:a")]
    public void IsValidHandle_RejectsInvalidPrefixOrContent(string? handle)
    {
        Assert.False(HandoffCounterAlphabet.IsValidHandle(handle));
    }

    [Fact]
    public void FormatHandle_PrependsPrefix()
    {
        Assert.Equal("h:a", HandoffCounterAlphabet.FormatHandle("a"));
        Assert.Equal("h:Z9", HandoffCounterAlphabet.FormatHandle("Z9"));
    }

    [Fact]
    public void TryExtractCounter_ExtractsValidCounters()
    {
        Assert.True(HandoffCounterAlphabet.TryExtractCounter("h:abc", out var counter));
        Assert.Equal("abc", counter);

        Assert.False(HandoffCounterAlphabet.TryExtractCounter("invalid", out _));
    }
}
