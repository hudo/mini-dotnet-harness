using System.Text.RegularExpressions;

// Python: ext.py frontmatter
namespace Mini.Core.Prompts;

/// <summary>The YAML header of a SKILL.md (Agent Skills format): `key: value` lines, plus folded multi-line values.</summary>
// Python: ext.py frontmatter
public static partial class Frontmatter
{
    [GeneratedRegex(@"\A---\r?\n(.*?)\r?\n---", RegexOptions.Singleline)]
    private static partial Regex Block();

    [GeneratedRegex(@"^([\w-]+):\s*(.*)$")]
    private static partial Regex KeyValue();

    // Python: ext.py frontmatter
    public static Dictionary<string, string> Parse(string text)
    {
        var m = Block().Match(text);
        var fields = new Dictionary<string, string>();
        string? key = null;
        foreach (var line in m.Success ? LineSplitter.Split(m.Groups[1].Value) : [])
        {
            var kv = KeyValue().Match(line);
            if (kv.Success)
                fields[key = kv.Groups[1].Value] = kv.Groups[2].Value.Trim();
            else if (key is not null && line.Length > 0 && line[0] is ' ' or '\t') // continuation of a `>` or `|` block, or a wrapped value
                fields[key] = (fields[key] is not (">" or "|" or ">-" or "|-") ? fields[key] : "") + " " + line.Trim();
        }
        return fields.ToDictionary(f => f.Key, f => f.Value.Trim().Trim('"', '\''));
    }
}
