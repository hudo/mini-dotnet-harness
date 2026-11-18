// Python: none (C#-only: Python's Path.parts and Path.parents, whose comparisons are what these two helpers spell out)
namespace Mini.Core.Security;

/// <summary>Whole-path-part comparison shared by the path policy and the secret classifier: Windows ignores case, elsewhere it is exact.</summary>
internal static class PathParts
{
    internal static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Path.parts: the root, then each name.</summary>
    internal static string[] Of(string p)
    {
        var root = Path.GetPathRoot(p) ?? "";
        return [root, .. p[root.Length..].Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)];
    }

    /// <summary>`parent in child.parents`: whole path parts, OrdinalIgnoreCase on Windows, Ordinal elsewhere.</summary>
    internal static bool IsUnder(string child, string parent)
    {
        string[] c = Of(child), q = Of(parent);
        return c.Length > q.Length && c.Take(q.Length).SequenceEqual(q, Comparer);
    }

    internal static bool Same(string a, string b) => Comparer.Equals(a, b);
}
