// Python: context.py read_if
namespace Mini.Core.Prompts;

/// <summary>A text file that goes into the prompt: AGENTS.md, the memory index, a skill's header.</summary>
// Python: context.py read_if
public static class PromptFile
{
    // Python: context.py read_if
    public static string? ReadIf(string path, int cap = 20_000)
    {
        if (!File.Exists(path))
            return null;
        // UTF8.GetString keeps a BOM and replaces bad bytes, like Python's utf-8 + errors="replace".
        // Python's text mode also turns \r\n and \r into \n, and the prompt must match it.
        var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).Replace("\r\n", "\n").Replace('\r', '\n');
        return text.Length <= cap ? text : text.TakeCodePoints(cap); // Python slices by code point
    }
}
