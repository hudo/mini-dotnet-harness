using System.Text;

namespace Mini.Core.Common;

/// <summary>
/// Python slices and counts strings by code point, .NET by UTF-16 unit. These helpers cut and count by code point so a
/// surrogate pair (an emoji) is never split in half.
/// </summary>
// Python: none (C#-only: Python's str already indexes and slices by code point; .NET strings are UTF-16)
public static class CodePointExtensions
{
    /// <summary>len(s) in code points. A lone surrogate counts as one.</summary>
    public static int CodePointLength(this string s)
    {
        var count = 0;
        for (var i = 0; i < s.Length; i += char.IsSurrogatePair(s, i) ? 2 : 1) count++;
        return count;
    }

    /// <summary>s[:n] by code point. A lone surrogate is kept as it is.</summary>
    public static string TakeCodePoints(this string s, int n)
    {
        var i = 0;
        for (var k = 0; k < n && i < s.Length; k++) i += char.IsSurrogatePair(s, i) ? 2 : 1;
        return s[..i];
    }

    /// <summary>s[-n:] by code point (the whole string if it has n or fewer). A lone surrogate is kept as it is.</summary>
    public static string TakeLastCodePoints(this string s, int n)
    {
        var skip = s.CodePointLength() - n;
        if (skip <= 0) return s;
        var i = 0;
        for (var k = 0; k < skip; k++) i += char.IsSurrogatePair(s, i) ? 2 : 1;
        return s[i..];
    }

    /// <summary>
    /// s[:n] by code point, but a lone surrogate in a string that is cut comes out as U+FFFD (what enumerating runes gives).
    /// A string that already fits is returned untouched. Agent's judge and goal text relied on this, so it stays its own method.
    /// </summary>
    public static string TakeScalars(this string s, int n)
    {
        if (s.Length <= n) return s;
        var sb = new StringBuilder();
        foreach (var r in s.EnumerateRunes().Take(n)) sb.Append(r.ToString());
        return sb.ToString();
    }

    /// <summary>s[-n:] by code point, with the lone-surrogate behavior of <see cref="TakeScalars"/>.</summary>
    public static string TakeLastScalars(this string s, int n)
    {
        if (s.Length <= n) return s;
        var runes = s.EnumerateRunes().ToList();
        return runes.Count <= n ? s : string.Concat(runes.Skip(runes.Count - n).Select(r => r.ToString()));
    }
}
