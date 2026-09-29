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
