// Python: tools.py BASH, find_bash
namespace Mini.Core.Tools.Shell;

/// <summary>Finds the bash the model's commands run in.</summary>
public sealed class ShellLocator(MiniOptions options)
{
    /// <summary>The bash found, or null: /bin/sh on POSIX, cmd.exe on Windows.</summary>
    // Python: tools.py BASH
    public string? Bash { get; } = FindBash(options);

    /// <summary>The model writes bash. POSIX: bash from PATH, since /bin/sh is dash on Debian and Ubuntu. Windows: Git for
    /// Windows' bash (or MINI_BASH), at fixed paths, since a lookup there searches the project folder first.
    /// Absolute paths only: a relative PATH entry is the project folder, which the project controls.</summary>
    // Python: tools.py find_bash
    internal static string? FindBash(MiniOptions options)
    {
        var tries = OperatingSystem.IsWindows()
            ? new[]
            {
                options.Bash ?? "",
                Path.Combine(options.ProgramFiles ?? @"C:\Program Files", @"Git\bin\bash.exe"),
                Path.Combine(options.LocalAppData ?? "", @"Programs\Git\bin\bash.exe"),
            }
            : (options.SearchPath ?? "").Split(Path.PathSeparator).Select(d => Path.Combine(d, "bash")).ToArray();
        return tries.FirstOrDefault(p => Path.IsPathFullyQualified(p) && File.Exists(p)
            && (OperatingSystem.IsWindows() || (File.GetUnixFileMode(p) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0));
    }
}
