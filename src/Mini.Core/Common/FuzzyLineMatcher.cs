// Python: tools.py closest
namespace Mini.Core.Common;

/// <summary>Finds the line of a file that a failed edit most likely meant.</summary>
public static class FuzzyLineMatcher
{
    /// <summary>Point at the line most like old's first line, so the model sees what differs instead of guessing.</summary>
    // Python: tools.py closest
    // shortcut: Levenshtein ratio (cutoff 0.6) instead of difflib's Ratcliff/Obershelp; only the "Closest:" hint can differ,
    // upgrade by porting SequenceMatcher if the hint picks the wrong line too often.
    public static string Closest(string text, string old)
    {
        var first = old.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? "";
        var lines = LineSplitter.Split(text).Select(x => x.Trim()).ToList();
        var best = -1;
        double bestRatio = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var m = Math.Max(first.Length, lines[i].Length);
            if (5L * Math.Min(first.Length, lines[i].Length) < 3L * m) continue;  // length alone puts it under 0.6: skip the distance
            var d = Distance(first, lines[i]);
            var ratio = m == 0 ? 1 : 1 - (double)d / m;
            if (5 * (m - d) >= 3 * m && ratio > bestRatio) (best, bestRatio) = (i, ratio);  // ints: no float error at the 0.6 cutoff
        }
        return best < 0 ? "" : $" Closest: line {best + 1}: {PythonJson.Repr(lines[best])}";
    }

    private static int Distance(string a, string b)
    {
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1];
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            prev = cur;
        }
        return prev[b.Length];
    }
}
