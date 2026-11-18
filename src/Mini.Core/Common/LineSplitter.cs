// Python: none (C#-only: Python's str.splitlines, a library call there)
namespace Mini.Core.Common;

/// <summary>str.splitlines: splits on every line boundary Python knows (\n, \r\n, \r, \v, \f, \x1c-\x1e, \x85, U+2028, U+2029).</summary>
public static class LineSplitter
{
    /// <summary>str.splitlines(keepends=True).</summary>
    public static List<string> SplitKeepEnds(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\n' or '\r' or '\v' or '\f' or '\x1c' or '\x1d' or '\x1e' or '\x85' or '\u2028' or '\u2029')) continue;
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            lines.Add(text[start..(i + 1)]);
            start = i + 1;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }

    /// <summary>Like str.splitlines().</summary>
    public static List<string> Split(string text) =>
        SplitKeepEnds(text).Select(l => l.TrimEnd('\r', '\n', '\v', '\f', '\x1c', '\x1d', '\x1e', '\x85', '\u2028', '\u2029')).ToList();  // a line has at most one ending
}
