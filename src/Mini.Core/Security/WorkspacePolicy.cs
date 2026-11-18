using System.Text.RegularExpressions;

// Python: tools.py LocalTools._path, LocalTools._inside, LocalTools._private, LocalTools._ask, LocalTools.PROTECTED, WIN_RUNS
namespace Mini.Core.Security;

/// <summary>What the model may touch without asking: where a path really points, whether it is inside the project, whether reading it
/// needs approval, and whether writing it does. Pure apart from reading the filesystem to follow links and to see execute bits.</summary>
public sealed partial class WorkspacePolicy
{
    // Windows has no execute bit: these run by name
    // Python: tools.py WIN_RUNS
    private static readonly HashSet<string> WinRuns = [".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".msi"];

    // Files something else executes later; "head" and "config" because git treats any folder with
    // HEAD, config, objects/ and refs/ as a repository and runs hooks from its config.
    // This is a denylist: it covers common runners, not every tool that reads a file from the project.
    // Python: tools.py LocalTools.PROTECTED
    private static readonly HashSet<string> Protected = ["mcp.json", "conftest.py", "makefile", "gnumakefile", "justfile", "package.json",
        "pyproject.toml", "setup.py", "noxfile.py", "tox.ini", "head", "config"];

    [GeneratedRegex("^/[a-zA-Z](/|$)")] private static partial Regex GitBashDrive();
    [GeneratedRegex(@"[:~]|[. ]$")] private static partial Regex WindowsAlias();

    private readonly string _userHome;
    private readonly string _jobs;

    /// <param name="cwd">The working directory (its real path is taken).</param>
    /// <param name="paths">Where the user's home and the background-job logs are.</param>
    // Python: tools.py LocalTools.__init__ (self.cwd)
    public WorkspacePolicy(string cwd, MiniPaths paths)
    {
        Cwd = PathResolver.RealPath(cwd);
        _userHome = paths.UserHome;
        _jobs = PathResolver.RealPath(paths.JobsDir); // background job logs: StartJob writes them
    }

    /// <summary>The working directory, as the OS reports it.</summary>
    public string Cwd { get; }

    /// <summary>Where a path the model wrote really points: Git Bash's /c/x on Windows, ~ and ~/x, relative to the working directory, links followed.</summary>
    // Python: tools.py LocalTools._path
    public string Resolve(string p)
    {
        if (OperatingSystem.IsWindows())
            p = GitBashToWindows(p); // Git Bash spells C:\x as /c/x
        if (p == "~" || p.StartsWith("~/", StringComparison.Ordinal) || OperatingSystem.IsWindows() && p.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = _userHome;
            var rest = p[1..].TrimStart('/'); // expanduser strips only the platform's separators: on POSIX a \ after ~/ is a filename character
            if (OperatingSystem.IsWindows()) rest = rest.TrimStart('\\');
            p = home.Length > 0 ? Path.Combine(home, rest) : "/" + p;
        }
        else if (p.StartsWith('~')) // shortcut: no ~user expansion (Python's expanduser uses pwd), so it lands outside and asks; ~word bash may (~+ is $PWD, ~- is $OLDPWD)
        {
            p = "/" + p;
        }
        // No GetFullPath here: it would collapse `link/..` lexically before RealPath can follow the link.
        return PathResolver.RealPath(Path.IsPathRooted(p) ? p : Path.Join(Cwd, p));
    }

    /// <summary>/c/x and /c are Git Bash's spelling of C:/x and C:/; any other path is returned as it is. The platform check is the caller's.</summary>
    // Python: tools.py LocalTools._path (the Windows branch)
    internal static string GitBashToWindows(string p) =>
        GitBashDrive().IsMatch(p) ? $"{p[1]}:/{(p.Length > 3 ? p[3..] : "")}" : p;

    /// <summary>A name that aliases another file on Windows: an NTFS stream (:), an 8.3 short name (~), a trailing dot or space. The platform check is the caller's.</summary>
    // Python: tools.py LocalTools._inside (the Windows alias check)
    internal static bool IsWindowsAlias(string part) => WindowsAlias().IsMatch(part);

    /// <summary>Free to write: inside the working directory, and not a file something else will execute later
    /// (dot-folders such as .git hooks and config, MCP config, test or build config). macOS paths ignore case.</summary>
    // Python: tools.py LocalTools._inside
    public bool IsInside(string path)
    {
        var h = _userHome;
        if (h.Length == 0) return false; // no home to tell the project from: nothing is free
        var home = PathResolver.RealPath(h);
        if (!PathParts.IsUnder(path, Cwd) || PathParts.Same(Cwd, home) || PathParts.IsUnder(home, Cwd)) return false;
        var parts = PathParts.Of(path)[PathParts.Of(Cwd).Length..].Select(x => x.ToLowerInvariant()).ToArray();
        // streams, 8.3 names, trailing dots alias files
        if (OperatingSystem.IsWindows() && parts.Any(IsWindowsAlias)) return false;
        return !parts.Any(x => x.StartsWith('.')) && !Protected.Contains(parts[^1]);
    }

    /// <summary>Is the path under the working directory (not the working directory itself)? Used to show paths relative to it.</summary>
    // Python: tools.py LocalTools.find (the `p.relative_to(self.cwd)` display)
    public bool IsUnderCwd(string path) => PathParts.IsUnder(path, Cwd);

    /// <summary>Is it the working directory itself or another given path? Whole parts, case-insensitive on Windows.</summary>
    public static bool IsSame(string a, string b) => PathParts.Same(a, b);

    /// <summary>Reading asks first outside the working directory (except background job logs), and for files that may
    /// hold secrets anywhere: whatever the model reads goes to the API and into the session file.</summary>
    // Python: tools.py LocalTools._private
    public bool IsPrivate(string path)
    {
        var outside = !PathParts.Same(Cwd, path) && !PathParts.IsUnder(path, Cwd) && !PathParts.IsUnder(path, _jobs);
        return outside || SecretClassifier.IsSecret(path); // job logs hold the output of commands that were already approved
    }

    /// <summary>Writes need approval outside the project, into protected files, or over an executable.</summary>
    // Python: tools.py LocalTools._ask
    public bool NeedsWriteApproval(string path)
    {
        var runs = (File.Exists(path) || Directory.Exists(path)) && (OperatingSystem.IsWindows()
            ? WinRuns.Contains(Path.GetExtension(path).ToLowerInvariant()) // every file is executable by mode there
            : (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0);
        return !IsInside(path) || runs;
    }
}
