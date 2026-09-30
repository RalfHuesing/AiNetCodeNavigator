#nullable enable

using AiNetCodeNavigator.Core.Symbols;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Symbols;

[Trait("Category", "Unit")]
public sealed class SymbolHandoffIdentifierTests
{
    [Fact]
    public void TryCreate_And_TryParse_RoundtripSuccessfully()
    {
        const string path = @"C:\work\app.sln";
        const string contentHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        const string docCommentId = "M:Company.Product.Class.Method(System.String)";

        var request = new SymbolHandoffCreationRequest(
            SymbolHandoffOrigin.Source,
            path,
            contentHash,
            docCommentId);

        Assert.True(SymbolHandoffIdentifier.TryCreate(request, out var identifier));
        var formatted = identifier.Format();
        Assert.StartsWith("i:0:", formatted, System.StringComparison.Ordinal);

        Assert.True(SymbolHandoffIdentifier.TryParse(formatted, out var parsed));
        Assert.Equal(SymbolHandoffOrigin.Source, parsed.Origin);
        Assert.Equal(identifier.TargetToken, parsed.TargetToken);
        Assert.Equal(identifier.ContentToken, parsed.ContentToken);
        Assert.Equal(docCommentId, parsed.DocumentationCommentId);
    }

    [Fact]
    public void TryCreate_RejectsUnknownOrigin()
    {
        var request = new SymbolHandoffCreationRequest(
            (SymbolHandoffOrigin)42,
            @"C:\work\app.sln",
            new string('a', 64),
            "T:Probe.Type");

        Assert.False(SymbolHandoffIdentifier.TryCreate(request, out _));
    }

    [Fact]
    public void Format_RejectsUnknownOrigin()
    {
        var identifier = new SymbolHandoffIdentifier(
            (SymbolHandoffOrigin)42,
            new string('a', SymbolHandoffToken.EncodedLength),
            new string('b', SymbolHandoffToken.EncodedLength),
            "T:Probe.Type");

        Assert.Throws<System.InvalidOperationException>(() => identifier.Format());
    }

    [Fact]
    public void TokenValidator_RejectsNull()
    {
        Assert.False(SymbolHandoffToken.IsValid(null!));
    }

    [Theory]
    [InlineData("M:Namespace.Class.Method")]
    [InlineData("T:Namespace.Class")]
    [InlineData("P:Namespace.Class.Property")]
    [InlineData("F:Namespace.Class.Field")]
    [InlineData("E:Namespace.Class.Event")]
    public void IsCanonicalDocumentationCommentId_AcceptsValidPrefixes(string id)
    {
        Assert.True(SymbolHandoffIdentifier.IsCanonicalDocumentationCommentId(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Invalid")]
    [InlineData("X:Namespace.Class")]
    [InlineData("M:Namespace.Class.Method#lf:local")]
    [InlineData("M:With Space")]
    public void IsCanonicalDocumentationCommentId_RejectsInvalid(string? id)
    {
        Assert.False(SymbolHandoffIdentifier.IsCanonicalDocumentationCommentId(id));
    }
}
