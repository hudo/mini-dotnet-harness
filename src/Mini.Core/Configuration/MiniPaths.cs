using System.Security.Cryptography;
using System.Text;
using Mini.Core.Security;

// Python: session.py HOME, project_dir
namespace Mini.Core.Configuration;

/// <summary>Where mini keeps its files: the separate home for the .NET port, per-directory session folders, job logs, and the user's home for `~`.</summary>
// Python: session.py HOME, project_dir (the class holds the paths; the options say where Home is)
public sealed class MiniPaths
{
    /// <summary>MINI_NET_HOME, or ~/.mini-net. Separate from Python's MINI_HOME so the two can't mix.</summary>
    // Python: session.py HOME (MINI_NET_HOME and ~/.mini-net instead of MINI_HOME and ~/.mini)
    public string Home { get; }

    /// <summary>Background job logs, under Home.</summary>
    // Python: tools.py spawn (session.HOME / "jobs")
    public string JobsDir { get; }

    /// <summary>The user's home folder, what `~` means to the tools: USERPROFILE on Windows, HOME elsewhere, else the OS's profile folder. Empty when there is none.</summary>
    // Python: tools.py LocalTools._path, LocalTools._inside (Path.home())
    public string UserHome { get; }

    /// <summary>Computes the paths once from the options.</summary>
    public MiniPaths(MiniOptions options)
    {
        Home = options.NetHome ?? Path.Combine(
            !string.IsNullOrEmpty(options.UserProfileFolder) ? options.UserProfileFolder : options.Home ?? ".", ".mini-net");
        JobsDir = Path.Combine(Home, "jobs");
        var home = OperatingSystem.IsWindows() ? options.UserProfile : options.Home;
        UserHome = !string.IsNullOrEmpty(home) ? home : options.UserProfileFolder;
    }

    /// <summary>HOME/sessions/&lt;first 12 hex of SHA256(realpath(cwd))&gt;, created if missing.</summary>
    // Python: session.py project_dir
    public string ProjectDir(string cwd)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PathResolver.RealPath(cwd)))).ToLowerInvariant();
        var dir = Path.Combine(Home, "sessions", key[..12]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
