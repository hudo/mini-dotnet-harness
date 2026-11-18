using System.Text;
using System.Text.RegularExpressions;

// Python: tools.py CONTROL, printable
namespace Mini.Core.Common;

/// <summary>Text that is safe to show where the user decides something: nothing can hide in it or drive the terminal.</summary>
public static partial class Printable
{
    // Terminal control codes; keeps \n, \t and readable Unicode.
    // Python: tools.py CONTROL
    [GeneratedRegex(@"[\x00-\x08\x0b-\x1f\x7f-\x9f]")]
    public static partial Regex Control { get; }

    [GeneratedRegex(" {16,}")] private static partial Regex Spaces16();
    [GeneratedRegex(" {3,}")] private static partial Regex Spaces3();
    [GeneratedRegex(@"(\n[ \t]*){3,}")] private static partial Regex BlankRuns();

    /// <summary>Nothing can hide in a prompt: padding is counted, everything outside plain ASCII is escaped, and real line
    /// breaks are kept only where asked (file content).</summary>
    // Python: tools.py printable
    public static string Of(string text, bool lines = false)
    {
        text = (lines ? Spaces16() : Spaces3()).Replace(text, m => $"[{m.Length} spaces]");  // code keeps its indent
        if (lines) text = BlankRuns().Replace(text, m => $"\n[{m.Value.Count(c => c == '\n') - 1} blank lines]\n");
        var sb = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = char.IsSurrogatePair(text, i) ? char.ConvertToUtf32(text, i++) : text[i];  // a lone surrogate escapes as itself
            if (c is >= 0x20 and <= 0x7e and not '\\' || lines && c == '\n') sb.Append((char)c);
            else sb.Append(PythonJson.UnicodeEscape(c));
        }
        return sb.ToString();
    }
}
