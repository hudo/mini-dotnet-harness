// Python: tools.py MAX_OUT, truncate
namespace Mini.Core.Common;

/// <summary>How much of a tool's output the model gets.</summary>
public static class OutputLimit
{
    // Python: tools.py MAX_OUT
    public const int Max = 30_000;  // characters returned to the model per tool call

    /// <summary>Keeps the first and last half of an output over <see cref="Max"/> code points, and says how much was cut.</summary>
    // Python: tools.py truncate
    public static string Truncate(string text)
    {
        if (text.Length <= Max) return text;  // UTF-16 units are never fewer than Python's code points
        int h = Max / 2, total = text.CodePointLength();  // len(text) in code points
        if (total <= Max) return text;
        return $"{text.TakeCodePoints(h)}\n\n[... {total - Max} characters cut ...]\n\n{text.TakeLastCodePoints(h)}";
    }
}
