using System.Text.RegularExpressions;

// Python: tools.py LocalTools._words, LocalTools._names_private
namespace Mini.Core.Security;

/// <summary>Reads a shell command for the paths it names, so a bash call that reads outside the project or a secret file is not waved through as a plain read.</summary>
public sealed partial class CommandPathInspector(WorkspacePolicy policy)
{
    [GeneratedRegex(@"[*?\[]")] private static partial Regex GlobChars();

    /// <summary>The paths a command names (every word after the first that isn't a flag), resolved; null if it can't be split.</summary>
    // Python: tools.py LocalTools._words
    public List<string>? Words(string command)
    {
        try
        {
            if (OperatingSystem.IsWindows()) // shlex eats the backslashes in C:\\x\\.env, hiding the secret's name: read them as /
                command = command.Replace('\\', '/');
            return ShellWords.Split(command).Skip(1).Where(w => !w.StartsWith('-')).Select(policy.Resolve).ToList();
        }
        catch (Exception e) when (e is FormatException or ArgumentException or IOException) // bad quoting, a path that can't exist (NUL), or a loop of links: ask
        {
            return null;
        }
    }

    /// <summary>Does a command name a path outside the project, a secret file, or a glob that could match one?</summary>
    // Python: tools.py LocalTools._names_private
    public bool NamesPrivate(string command)
    {
        var paths = Words(command);
        return paths is null || GlobChars().IsMatch(command) || paths.Any(policy.IsPrivate);
    }
}
