namespace Mini.Tests.Common;

/// <summary>Python slices by code point: a surrogate pair (an emoji) counts once and is never cut in half.</summary>
public class CodePointExtensionsTests
{
    const string Smile = "😀"; // U+1F600, two UTF-16 units
    const string Lone = "\ud800"; // an unpaired high surrogate

    [Fact]
    public void CodePointLengthCountsAPairOnce()
    {
        Assert.Equal(0, "".CodePointLength());
        Assert.Equal(3, "a😀b".CodePointLength());
        Assert.Equal(2, ("a" + Lone).CodePointLength());
    }

    [Theory]
    [InlineData("a😀b", 0, "")]
    [InlineData("a😀b", 1, "a")]
    [InlineData("a😀b", 2, "a😀")]
    [InlineData("a😀b", 3, "a😀b")]
    [InlineData("a😀b", 99, "a😀b")]
    [InlineData("", 5, "")]
    public void TakeCodePointsNeverSplitsAPair(string s, int n, string expected) =>
        Assert.Equal(expected, s.TakeCodePoints(n));

    [Theory]
    [InlineData("a😀b", 0, "")]
    [InlineData("a😀b", 1, "b")]
    [InlineData("a😀b", 2, "😀b")]
    [InlineData("a😀b", 3, "a😀b")]
    [InlineData("a😀b", 99, "a😀b")]
    [InlineData("", 5, "")]
    public void TakeLastCodePointsNeverSplitsAPair(string s, int n, string expected) =>
        Assert.Equal(expected, s.TakeLastCodePoints(n));

    [Fact]
    public void TheCutIsOnCodePointsNotUtf16Units()
    {
        var s = string.Concat(Enumerable.Repeat(Smile, 10)); // 20 units, 10 code points
        Assert.Equal(5, s.TakeCodePoints(5).CodePointLength());
        Assert.Equal(10, s.TakeCodePoints(5).Length);
        Assert.Equal(s, s.TakeCodePoints(10));
        Assert.Equal(5, s.TakeLastCodePoints(5).CodePointLength());
    }

    [Fact]
    public void ALoneSurrogateIsKeptByTheCodePointMethods()
    {
        var s = "a" + Lone + "b";
        Assert.Equal("a" + Lone, s.TakeCodePoints(2));
        Assert.Equal(Lone + "b", s.TakeLastCodePoints(2));
    }

    [Theory]
    [InlineData("a😀b", 0, "")]
    [InlineData("a😀b", 2, "a😀")]
    [InlineData("a😀b", 3, "a😀b")]
    [InlineData("a😀b", 99, "a😀b")]
    public void TakeScalarsCutsLikeTakeCodePoints(string s, int n, string expected) =>
        Assert.Equal(expected, s.TakeScalars(n));

    [Theory]
    [InlineData("a😀b", 0, "")]
    [InlineData("a😀b", 2, "😀b")]
    [InlineData("a😀b", 99, "a😀b")]
    public void TakeLastScalarsCutsLikeTakeLastCodePoints(string s, int n, string expected) =>
        Assert.Equal(expected, s.TakeLastScalars(n));

    [Fact]
    public void TheScalarMethodsReplaceALoneSurrogateOnlyWhenTheyCut()
    {
        var s = "a" + Lone + "b";
        Assert.Equal("a�", s.TakeScalars(2));
        Assert.Equal("�b", s.TakeLastScalars(2));
        Assert.Equal(s, s.TakeScalars(3)); // already fits: untouched
        Assert.Equal(s, s.TakeLastScalars(3));
    }
}
