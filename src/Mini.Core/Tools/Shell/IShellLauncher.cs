using System.Diagnostics;

// Python: tools.py spawn (the command line it builds)
namespace Mini.Core.Tools.Shell;

/// <summary>How a command becomes a process: bash (or sh) with its environment and log redirect, or cmd on Windows without Git Bash.</summary>
public interface IShellLauncher
{
    /// <summary>The process to start. The caller has made the log file and starts the process with no stdin.</summary>
    /// <param name="command">What the model wrote.</param>
    /// <param name="cwd">The working directory.</param>
    /// <param name="log">The file the output goes to.</param>
    /// <param name="job">A background job: its shell ends the log with [exit N] itself.</param>
    /// <param name="killJobs">Kill what the command left running with &amp; when the shell ends.</param>
    ProcessStartInfo CreateStart(string command, string cwd, string log, bool job, bool killJobs);
}
