namespace Mini.Tests.Common;

/// <summary>The pure text helpers split out of Text.cs: Python's splitlines, count and replace, printable, truncate, loose edit and closest.</summary>
// Python test helper: tests/test_mini.py (printable, truncate, loose_edit and closest, which the edit and prompt tests reach only through the tools)
public class TextHelpersTests
{
    [Fact]
    public void SplitLinesBreaksOnEveryLineBoundaryPythonKnows()
    {
        Assert.Equal(["a", "b", "c", "d", "e", "f"], LineSplitter.Split("a\nb\r\nc\rd\u000be\u000cf"));
        Assert.Equal(["a", "b"], LineSplitter.Split("a\u2028b\u2029"));
        Assert.Equal(["a", "", "b"], LineSplitter.Split("a\n\nb\n"));
        Assert.Empty(LineSplitter.Split(""));
        Assert.Equal(["a\r\n", "b\n", "c"], LineSplitter.SplitKeepEnds("a\r\nb\nc"));
        Assert.Equal(["a\n", "\r"], LineSplitter.SplitKeepEnds("a\n\r")); // \n\r is two breaks; only \r\n is one
    }

    [Fact]
    public void CountIsNonOverlappingAndOrdinal()
    {
        Assert.Equal(2, PythonString.Count("aaaa", "aa"));
        Assert.Equal(0, PythonString.Count("abc", "B"));
        Assert.Equal(4, PythonString.Count("abc", "")); // len + 1, as in Python
    }

    [Fact]
    public void ReplaceFirstReplacesOnlyTheFirstMatch()
    {
        Assert.Equal("Xbab", PythonString.ReplaceFirst("abab", "a", "X"));
        Assert.Equal("abab", PythonString.ReplaceFirst("abab", "z", "X"));
    }

    [Fact]
    public void PrintableEscapesWhatIsNotPlainAscii()
    {
        Assert.Equal("a\\xe9b", Printable.Of("aéb")); // Python's escapes: \xNN below 256, \uNNNN above
        Assert.Equal("a\\nb", Printable.Of("a\nb")); // line breaks are escaped unless the text is file content
        Assert.Equal("a\nb", Printable.Of("a\nb", lines: true));
        Assert.Equal("a\\\\b", Printable.Of("a\\b")); // a backslash is escaped too
        Assert.Equal("a\\u3164b", Printable.Of("a\u3164b"));
    }

    [Fact]
    public void PrintableCountsLongPaddingInsteadOfShowingIt()
    {
        Assert.Equal("a[3 spaces]b", Printable.Of("a   b"));
        Assert.Equal("a  b", Printable.Of("a  b")); // two are plain
        Assert.Equal("    x", Printable.Of("    x", lines: true)); // file content keeps its indent...
        Assert.Equal("[16 spaces]x", Printable.Of(new string(' ', 16) + "x", lines: true)); // ...up to a point
        Assert.Equal("a\n[3 blank lines]\nb", Printable.Of("a\n\n\n\nb", lines: true));
    }

    [Fact]
    public void ControlMatchesTerminalCodesButNotNewlinesOrTabs()
    {
        Assert.Equal("ab", Printable.Control.Replace("a\e[0mb".Replace("[0m", ""), ""));
        Assert.Equal("a\nb\tc", Printable.Control.Replace("a\nb\tc", ""));
        Assert.Equal("ab", Printable.Control.Replace("a\u009bb", ""));
    }

    [Fact]
    public void TruncateKeepsBothEndsAndSaysHowMuchWasCut()
    {
        Assert.Equal("short", OutputLimit.Truncate("short"));
        var text = new string('a', 15_000) + new string('b', 15_000) + "c";
        var cut = OutputLimit.Truncate(text);

        Assert.StartsWith(new string('a', 15_000), cut);
        Assert.EndsWith(new string('b', 14_999) + "c", cut);
        Assert.Contains("\n\n[... 1 characters cut ...]\n\n", cut);
        Assert.Equal(30_000, OutputLimit.Max);
        Assert.Equal(new string('x', 30_000), OutputLimit.Truncate(new string('x', 30_000)));
    }

    [Fact]
    public void TruncateCountsCodePointsNotUtf16Units()
    {
        var emoji = string.Concat(Enumerable.Repeat("😀", 20_000)); // 40,000 units, 20,000 code points: under the limit
        Assert.Equal(emoji, OutputLimit.Truncate(emoji));
    }

    [Fact]
    public void LooseEditIgnoresLineEndingsAndTrailingWhitespace()
    {
        var r = LooseEditor.Edit("a  \r\nb\r\nc\r\n", "a\nb", "X\nY");

        Assert.Equal(new LooseEditResult("X\r\nY\r\nc\r\n", 1), r);
    }

    [Fact]
    public void LooseEditReportsAmbiguityAndNothingFound()
    {
        Assert.Equal(new LooseEditResult(null, 2), LooseEditor.Edit("a \nb\na\nb\n", "a\nb", "x"));
        Assert.Equal(new LooseEditResult(null, 0), LooseEditor.Edit("a\nb\n", "nope", "x"));
        Assert.Equal(new LooseEditResult(null, 0), LooseEditor.Edit("a\nb\n", "\n\n", "x")); // nothing but blank lines
    }

    [Fact]
    public void LooseEditWithEmptyReplacementDeletesTheLines() => Assert.Equal(new LooseEditResult("a\nc\n", 1), LooseEditor.Edit("a\nb \nc\n", "b", ""));

    [Fact]
    public void ClosestPointsAtTheLineMostLikeTheFirstOne()
    {
        var hint = FuzzyLineMatcher.Closest("alpha\n  def compute(x):\n    return x\n", "def compute(y):\n    return y");

        Assert.Equal(" Closest: line 2: 'def compute(x):'", hint);
    }

    [Fact]
    public void ClosestSaysNothingWhenNoLineIsNear()
    {
        Assert.Equal("", FuzzyLineMatcher.Closest("alpha\nbeta\n", "completely different words here"));
        Assert.Equal("", FuzzyLineMatcher.Closest("", "x"));
    }
}
