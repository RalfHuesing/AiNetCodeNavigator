#nullable enable

using AiNetCodeNavigator.Core.Common;

namespace AiNetCodeNavigator.FastTests.Common;

public sealed class BoundResultCursorTests
{
    [Fact]
    public void BindingLengthSeparatesQueryPartsContainingTheLegacyDelimiter()
    {
        var left = BoundResultCursor.CreateBinding("target", "snapshot", "members", "alpha\u001fbeta", "gamma");
        var right = BoundResultCursor.CreateBinding("target", "snapshot", "members", "alpha", "beta\u001fgamma");

        Assert.NotEqual(left, right);
        var cursor = BoundResultCursor.CreateToken(7, left);
        Assert.Equal(BoundResultCursor.CursorStatus.Valid, BoundResultCursor.ReadOffset(cursor, left, out var offset));
        Assert.Equal(7, offset);
        Assert.Equal(BoundResultCursor.CursorStatus.StaleBinding, BoundResultCursor.ReadOffset(cursor, right, out _));
        Assert.Equal(BoundResultCursor.CursorStatus.InvalidFormat, BoundResultCursor.ReadOffset("v1.-1.not-a-binding", left, out _));
    }
}
