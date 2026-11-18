// Python: tools.py Approver.SAFE, Approver.GIT, Approver.SHELL, Approver.pre_approved
namespace Mini.Core.Approval;

/// <summary>Which bash commands run without asking: a short list that can't run other programs or write files, read-only git, and the user's --allow rules.</summary>
public static class CommandSafetyRules
{
    // Commands that can't run other programs or write files, whatever their flags.
    // grep isn't here: `grep -r` reads every file, secrets included. The find tool searches without a shell.
    // Python: tools.py Approver.SAFE
    private static readonly HashSet<string> Safe = ["ls", "cat", "head", "tail", "wc", "pwd", "echo", "which", "stat", "diff"];

    // Read-only git. It runs only what git config says, and writes into .git need approval; `--output` writes,
    // and a `rev:path` word could show a committed secret, so those go to review.
    // Python: tools.py Approver.GIT
    private static readonly HashSet<string> Git = ["status", "diff", "log", "show"];

    // Python: tools.py Approver.SHELL
    private const string ShellChars = ";&|<>`$(){}\n\r\\";

    /// <param name="command">The command line.</param>
    /// <param name="allow">The user's pre-approved prefixes, each already split into words.</param>
    /// <param name="isPrivate">The command names a path outside the project or a secret: only an allow rule can pass it.</param>
    // Python: tools.py Approver.pre_approved
    public static bool PreApproved(string command, IReadOnlyList<List<string>> allow, bool isPrivate = false)
    {
        if (command.Any(c => ShellChars.Contains(c)))
            return false;
        List<string> words;
        try { words = ShellWords.Split(command); }
        catch (FormatException) { return false; }
        var git = words.Count > 1 && words[0] == "git" && Git.Contains(words[1]) && !words.Any(w =>
            w.StartsWith("--output", StringComparison.Ordinal) || w.StartsWith("--ext-diff", StringComparison.Ordinal)
            || w.StartsWith("--textconv", StringComparison.Ordinal) || w.Contains(':'));
        return words.Count > 0 && (((Safe.Contains(words[0]) || git) && !isPrivate)
            || allow.Any(a => words.Take(a.Count).SequenceEqual(a)));
    }
}
