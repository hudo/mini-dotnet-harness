using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mini.Core.Common;

/// <summary>
/// Python's <c>json.dumps</c>, <c>repr</c>, float formatting and truthiness. Text the model sees (tool approvals, the judge's
/// action, "Closest:" hints, goal notes) and some CLI output must match Python's byte for byte: this is the parity contract
/// (REFACTOR-PLAN §2). .NET's own serializers differ: separators, non-ASCII escaping, quote choice, float spelling.
/// </summary>
// Python: none (C#-only: reproduces Python's json.dumps, repr and truthiness, which Python gets from its runtime)
public static class PythonJson
{
    // shortcut: Python's float repr for ordinary values only (2 -> "2.0"); exponent forms differ, upgrade if budgets that small matter.
    internal static string FormatFloat(double d)
    {
        var s = d.ToString("R", CultureInfo.InvariantCulture);
        return s.Any(c => c is '.' or 'E' or 'N' or 'I') ? s : s + ".0";
    }

    // Python truthiness, for `args.get("command") or args.get("path") or ...`
    internal static bool Truthy(JsonNode? n) => n switch
    {
        null => false,
        JsonArray a => a.Count > 0,
        JsonObject o => o.Count > 0,
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>().Length > 0,
            JsonValueKind.Number => double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture) != 0,
            JsonValueKind.True => true,
            _ => false,
        },
        _ => true,
    };

    // json.dumps: ", " and ": " separators, non-ASCII escaped (the compact ToJsonString would look different)
    internal static string Dumps(JsonNode? n) => n switch
    {
        null => "null",
        JsonObject o => "{" + string.Join(", ", o.Select(p => Quote(p.Key) + ": " + Dumps(p.Value))) + "}",
        JsonArray a => "[" + string.Join(", ", a.Select(Dumps)) + "]",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => Quote(v.GetValue<string>()),
        _ => n.ToJsonString(),
    };

    private static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            sb.Append(c switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", '\b' => "\\b", '\f' => "\\f",
                < ' ' or > '~' => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        }

        return sb.Append('"').ToString();
    }

    // Python's repr(str), for the "Closest:" text.
    internal static string Repr(string s)
    {
        var q = s.Contains('\'') && !s.Contains('"') ? '"' : '\'';
        var sb = new StringBuilder().Append(q);
        for (var i = 0; i < s.Length; i++)
        {
            var c = char.IsSurrogatePair(s, i) ? char.ConvertToUtf32(s, i++) : s[i];
            if (c == q)
            {
                sb.Append('\\').Append(q);
            }
            else if (c is >= 0x20 and < 0x7f and not '\\')
            {
                sb.Append((char)c);
            }
            else if (c >= 0x80 && CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.Control or UnicodeCategory.Format
                     or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned
                     or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.SpaceSeparator))
            {
                sb.Append(char.ConvertFromUtf32(c));
            }
            else
            {
                sb.Append(UnicodeEscape(c));
            }
        }
        return sb.Append(q).ToString();
    }

    // Python's str.encode("unicode_escape") for one code point.
    internal static string UnicodeEscape(int c) => c switch
    {
        '\\' => @"\\",
        '\t' => @"\t",
        '\n' => @"\n",
        '\r' => @"\r",
        < 0x100 => $"\\x{c:x2}",  // every other value below 0x100, as unicode_escape does (Latin-1 included)
        < 0x10000 => $"\\u{c:x4}",
        _ => $"\\U{c:x8}",
    };
}
