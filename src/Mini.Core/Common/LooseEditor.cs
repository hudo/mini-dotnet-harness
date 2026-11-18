// Python: tools.py loose_edit
namespace Mini.Core.Common;

/// <summary>The edit tool's second chance, when `old` is not found exactly.</summary>
public static class LooseEditor
{
    /// <summary>Retry whole lines, ignoring line endings and trailing whitespace: models copy those wrong, mostly on Windows.
    /// Returns the edited text if exactly one place matches, and how many places did.</summary>
    // Python: tools.py loose_edit
    public static LooseEditResult Edit(string text, string old, string @new)
    {
        var lines = LineSplitter.SplitKeepEnds(text);
        var want = old.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Select(x => x.TrimEnd()).ToList();
        if (!want.Any(x => x.Length > 0)) return new(null, 0);
        var hits = new List<int>();
        for (var i = 0; i + want.Count <= lines.Count; i++)
            if (Enumerable.Range(0, want.Count).All(k => lines[i + k].TrimEnd() == want[k])) hits.Add(i);
        if (hits.Count != 1) return new(null, hits.Count);
        int a = hits[0], j = a + want.Count;
        var eol = lines[a].EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var last = lines[j - 1];
        var end = last[last.TrimEnd('\r', '\n').Length..];  // the last line keeps its own ending, if it had one
        var body = @new.Length > 0 ? string.Join(eol, @new.Replace("\r\n", "\n").TrimEnd('\n').Split('\n')) + end : "";
        return new(string.Concat(lines.Take(a)) + body + string.Concat(lines.Skip(j)), 1);
    }
}
