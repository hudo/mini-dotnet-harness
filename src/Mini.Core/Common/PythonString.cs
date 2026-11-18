// Python: none (C#-only: Python's str.count and str.replace(old, new, 1), library calls there)
namespace Mini.Core.Common;

/// <summary>The two string methods of Python that .NET spells differently: ordinal, non-overlapping count, and replacing only the first match.</summary>
public static class PythonString
{
    /// <summary>str.count: non-overlapping, ordinal.</summary>
    public static int Count(string text, string sub)
    {
        if (sub.Length == 0) return text.Length + 1;
        var n = 0;
        for (var i = 0; (i = text.IndexOf(sub, i, StringComparison.Ordinal)) >= 0; i += sub.Length) n++;
        return n;
    }

    /// <summary>str.replace(old, new, 1).</summary>
    public static string ReplaceFirst(string text, string old, string @new)
    {
        var i = text.IndexOf(old, StringComparison.Ordinal);
        return i < 0 ? text : text[..i] + @new + text[(i + old.Length)..];
    }
}
