using System.Text;

namespace Mini.Core.Common;

// Python: none (C#-only: ports the standard library's shlex, which mini only calls)
public static class ShellWords
{
    // stdlib: shlex.split
    /// <summary>POSIX shlex.split. Throws FormatException on bad quoting, like Python's ValueError.
    /// Mirrors Python, not sh: inside "…" a backslash escapes only " and \, so \$ and \` keep their backslash.</summary>
    public static List<string> Split(string command)
    {
        var words = new List<string>();
        var token = new StringBuilder();
        var inWord = false;  // a word has begun, even an empty one ('')
        var quote = '\0';
        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (quote == '\0')
            {
                if (c is ' ' or '\t' or '\r' or '\n')  // exactly shlex's whitespace, not Unicode spaces
                {
                    if (inWord) words.Add(token.ToString());
                    token.Clear();
                    inWord = false;
                }
                else if (c is '\'' or '"') { quote = c; inWord = true; }
                else if (c == '\\')
                {
                    if (++i == command.Length) throw new FormatException("No escaped character");
                    token.Append(command[i]);
                    inWord = true;
                }
                else { token.Append(c); inWord = true; }
            }
            else if (c == quote)
            {
                quote = '\0';
            }
            else if (c == '\\' && quote == '"')
            {
                if (++i == command.Length) throw new FormatException("No escaped character");
                if (command[i] is not ('\\' or '"')) token.Append('\\');
                token.Append(command[i]);
            }
            else
            {
                token.Append(c);
            }
        }
        if (quote != '\0') throw new FormatException("No closing quotation");
        if (inWord) words.Add(token.ToString());
        return words;
    }

    // stdlib: shlex.join
    public static string Join(IEnumerable<string> words) => string.Join(' ', words.Select(Quote));

    // shlex.quote: safe means ASCII [\w@%+=:,./-] only (not Unicode letters), anything else gets single quotes.
    private static string Quote(string s) =>
        s.Length == 0 ? "''"
        : s.All(c => char.IsAsciiLetterOrDigit(c) || "_@%+=:,./-".Contains(c)) ? s
        : "'" + s.Replace("'", "'\"'\"'") + "'";
}
