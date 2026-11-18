using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Mini.Tests;

/// <summary>
/// Keeps the "// Python:" tags in dotnet/src honest (REFACTOR-PLAN §5). The tags say which Python code each piece of C#
/// ports, so a Python change maps to the C# to update with one grep; these tests fail when the two drift apart.
///
/// The Python side is read with simple line regexes, not a parser:
///   - top level (column 0): "def f", "async def f", "class C", and "NAME = ..." / "A, B = ..." where every name is UPPER_CASE;
///   - inside a class (until the next column-0 line that is not a comment): methods at 4 spaces ("def m"), and class
///     attributes at 4 spaces ("NAME = ...") for the existence check.
/// Decisions (also in REFACTOR-PLAN §5, "Conventions decided in P1"):
///   - functions nested in functions (reap's wait, main's new) belong to their enclosing function and are not names;
///   - of the dunder methods only __init__ is a name; the others (none exist today) are skipped;
///   - a class-level UPPER_CASE constant is named with its class (LocalTools.SECRET); a lower-case class attribute is
///     only a valid tag target (Agent.max_tokens), not a required one;
///   - a subscript (SCHEMAS["bash"]) is checked by its base name; models.json names are dotted key paths.
/// </summary>
public sealed partial class PythonReferenceTests
{
    // src/Mini.Tests/PythonReferenceTests.cs -> the repository root, two folders up from this file's folder.
    static readonly string Repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", ".."));
    static readonly string PythonDir = Path.Combine(Repo, "src-original");
    static readonly string CsDir = Path.Combine(Repo, "src");
    static readonly string NotPortedFile = Path.Combine(Repo, "src", "docs", "python-not-ported.txt");

    static string ThisFile([CallerFilePath] string path = "") => path;

    // ---- the tag grammar (REFACTOR-PLAN §5): // Python: <file> <Name>[, <Name>…] [(<note>)]   or   // Python: none (C#-only: <why>)
    const string Name = @"[A-Za-z_][\w*-]*(?:\.[\w*-]+)*(?:\[""[^""\]]+""\])?";

    [GeneratedRegex(@"^\s*// Python: (?:none \(C#-only: .+\)|(?<file>(?:\w+\.py|models\.json)) (?<names>" + Name + "(?:, " + Name + @")*)(?: \(.+\))?)$")]
    private static partial Regex TagLine();

    // Anything that looks like a tag, however mistyped: "// python:", "// Python:loop.py", "/// … Python: …".
    [GeneratedRegex(@"//.*\bpython\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex LooksLikeTag();

    // The old breadcrumb style, "// loop.py: turn", must not survive next to the new tags.
    [GeneratedRegex(@"//.*\b(?:loop|tools|session|context|ext|cli)\.py:")]
    private static partial Regex OldBreadcrumb();

    [GeneratedRegex(@"^(?:async\s+def|def)\s+(\w+)")] private static partial Regex TopFunction();
    [GeneratedRegex(@"^class\s+(\w+)")] private static partial Regex TopClass();
    [GeneratedRegex(@"^([A-Z_][A-Z0-9_]*(?:\s*,\s*[A-Z_][A-Z0-9_]*)*)\s*(?::[^=\n]+)?=(?!=)")] private static partial Regex TopConstants();
    [GeneratedRegex(@"^    (?:async\s+def|def)\s+(\w+)")] private static partial Regex Method();
    [GeneratedRegex(@"^    ([A-Za-z_]\w*)\s*(?::[^=\n]+)?=(?!=)")] private static partial Regex ClassAttribute();
    [GeneratedRegex(@"^__\w+__$")] private static partial Regex Dunder();
    [GeneratedRegex(@"^[A-Z_][A-Z0-9_]*$")] private static partial Regex UpperCase();
    [GeneratedRegex(@"^(?<file>\w+\.py) (?<name>\S+) — (?<why>\S.*)$")] private static partial Regex NotPortedLine();

    sealed record Tag(string Where, string File, string Name);

    /// <summary>What src/mini/&lt;file&gt;.py defines: every valid tag target, and the subset that must be tagged or listed.</summary>
    sealed record PythonFile(HashSet<string> Defined, List<string> Required);

    static PythonFile ParsePython(string text)
    {
        HashSet<string> defined = [];
        List<string> required = [];
        string? cls = null;
        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0 || line[0] == '#' || line.Trim().Length == 0) continue;
            if (!char.IsWhiteSpace(line[0]))
            {
                cls = null;
                if (TopClass().Match(line) is { Success: true } c)
                {
                    cls = c.Groups[1].Value;
                    defined.Add(cls);
                    required.Add(cls);
                }
                else if (TopFunction().Match(line) is { Success: true } f)
                {
                    defined.Add(f.Groups[1].Value);
                    required.Add(f.Groups[1].Value);
                }
                else if (TopConstants().Match(line) is { Success: true } k)
                {
                    foreach (var n in k.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries))
                    {
                        defined.Add(n);
                        required.Add(n);
                    }
                }

                continue;
            }
            if (cls is null) continue;
            if (Method().Match(line) is { Success: true } m)
            {
                var n = m.Groups[1].Value;
                defined.Add($"{cls}.{n}");
                if (n == "__init__" || !Dunder().IsMatch(n)) required.Add($"{cls}.{n}");
            }
            else if (ClassAttribute().Match(line) is { Success: true } a)
            {
                var n = a.Groups[1].Value;
                defined.Add($"{cls}.{n}");
                if (UpperCase().IsMatch(n)) required.Add($"{cls}.{n}");
            }
        }
        return new(defined, required);
    }

    static Dictionary<string, PythonFile> ReadPython() =>
        Directory.GetFiles(PythonDir, "*.py").ToDictionary(p => Path.GetFileName(p), p => ParsePython(File.ReadAllText(p)));

    static IEnumerable<string> CsFiles() =>
        Directory.EnumerateFiles(CsDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "obj" or "bin" or "Mini.Tests"))
            .Order(StringComparer.Ordinal);

    static string Rel(string path) => Path.GetRelativePath(Repo, path).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>Every well-formed tag except "none", with one entry per name.</summary>
    static List<Tag> ReadTags()
    {
        List<Tag> tags = [];
        foreach (var path in CsFiles())
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (TagLine().Match(lines[i]) is { Success: true } m && m.Groups["file"].Success)
                {
                    foreach (var name in m.Groups["names"].Value.Split(", "))
                        tags.Add(new($"{Rel(path)}:{i + 1}", m.Groups["file"].Value, name));
                }
            }
        }
        return tags;
    }

    /// <summary>"SCHEMAS[\"bash\"]" is the name SCHEMAS.</summary>
    static string Base(string name) => name.Contains('[') ? name[..name.IndexOf('[')] : name;

    static bool JsonPathExists(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(PythonDir, "models.json")));
        foreach (var key in path.Split('.'))
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(key, out node)) return false;
        }
        return true;
    }

    /// <summary>The "file name" pairs of src/mini/python-not-ported entries: (file, name, line text).</summary>
    static List<(string File, string Name, string Line, int Number)> ReadNotPorted()
    {
        List<(string, string, string, int)> list = [];
        var lines = File.ReadAllLines(NotPortedFile);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0 || lines[i].StartsWith('#')) continue;
            var m = NotPortedLine().Match(lines[i]);
            list.Add(m.Success ? (m.Groups["file"].Value, m.Groups["name"].Value, lines[i], i + 1) : ("", "", lines[i], i + 1));
        }
        return list;
    }

    [Fact]
    public void EveryTagNamesExistingPython()
    {
        var python = ReadPython();
        List<string> bad = [];
        foreach (var tag in ReadTags())
        {
            if (tag.File == "models.json")
            {
                if (!JsonPathExists(tag.Name)) bad.Add($"{tag.Where}: models.json has no key path {tag.Name}");
            }
            else if (!python.TryGetValue(tag.File, out var file))
            {
                bad.Add($"{tag.Where}: src-original/{tag.File} does not exist");
            }
            else if (!file.Defined.Contains(Base(tag.Name)))
            {
                bad.Add($"{tag.Where}: {tag.File} defines no {tag.Name}");
            }
        }
        Assert.True(bad.Count == 0, $"{bad.Count} tag(s) name Python code that does not exist (renamed or removed?):\n" + string.Join("\n", bad));
    }

    [Fact]
    public void EveryPythonNameIsTaggedOrListedAsNotPorted()
    {
        var tagged = ReadTags().Select(t => $"{t.File} {Base(t.Name)}").ToHashSet();
        var listed = ReadNotPorted().Select(n => $"{n.File} {n.Name}").ToHashSet();
        var missing = ReadPython().SelectMany(f => f.Value.Required.Select(n => $"{f.Key} {n}"))
            .Where(n => !tagged.Contains(n) && !listed.Contains(n)).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0,
            $"{missing.Count} Python name(s) are neither tagged in src nor listed in src/docs/python-not-ported.txt "
            + "(a new function in Python? tag its C# port with \"// Python: <file> <Name>\", or list it with a reason):\n"
            + string.Join("\n", missing));
    }

    [Fact]
    public void NotPortedListIsNotStale()
    {
        var python = ReadPython();
        var tagged = ReadTags().Select(t => $"{t.File} {Base(t.Name)}").ToHashSet();
        List<string> bad = [];
        foreach (var (file, name, line, number) in ReadNotPorted())
        {
            var where = $"src/docs/python-not-ported.txt:{number}";
            if (file.Length == 0) bad.Add($"{where}: not \"<file>.py <Name> — <reason>\": {line}");
            else if (!python.TryGetValue(file, out var f) || !f.Defined.Contains(name)) bad.Add($"{where}: {file} defines no {name} any more, remove the line");
            else if (tagged.Contains($"{file} {name}")) bad.Add($"{where}: {file} {name} is tagged in C# now, remove the line");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void TagsAreWellFormed()
    {
        List<string> bad = [];
        foreach (var path in CsFiles())
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (LooksLikeTag().IsMatch(lines[i]) && !TagLine().IsMatch(lines[i]))
                    bad.Add($"{Rel(path)}:{i + 1}: not a tag (// Python: <file> <Name>[, <Name>…] [(<note>)] or // Python: none (C#-only: <why>)): {lines[i].Trim()}");
                else if (OldBreadcrumb().IsMatch(lines[i]))
                    bad.Add($"{Rel(path)}:{i + 1}: old breadcrumb style, use \"// Python: <file> <Name>\": {lines[i].Trim()}");
            }
        }
        Assert.True(bad.Count == 0, $"{bad.Count} malformed tag(s):\n" + string.Join("\n", bad));
    }
}
