using System.ComponentModel;
using System.Diagnostics;

// Python: tools.py start_job, still_running (the .log and .listed attributes they set on a Popen)
// Python: tools.py stop (the tree kill)
namespace Mini.Core.Tools.Shell;

/// <summary>A shell the bash tool started and the log its output goes to. Python's ad-hoc attributes on Popen (.log, .listed), with the process kept private.</summary>
public sealed class BackgroundJob
{
    private readonly Process _process;

    internal BackgroundJob(Process process, string log)
    {
        _process = process;
        Log = log;
    }

    public string Log { get; }

    /// <summary>Set by <see cref="JobRegistry.StillRunning"/> each time it reports on the job.</summary>
    public bool Listed { get; set; } = true;

    /// <summary>The shell's process id. Valid only while the job has not been disposed.</summary>
    public int Pid => _process.Id;

    public bool HasExited => _process.HasExited;

    /// <summary>.NET already reports a signal death as 128+N.</summary>
    public int ExitCode => _process.ExitCode;

    internal Task WaitForExitAsync(CancellationToken ct) => _process.WaitForExitAsync(ct);

    /// <summary>Kill it and its whole process tree.</summary>
    // Python: tools.py stop (the tree kill; no process group, so no signal choice)
    internal void Kill()
    {
        try { _process.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or AggregateException) { }
        // Already gone or only part of the tree could be killed; the caller still deletes the log and rethrows its own error.
        // shortcut: no process groups, so a child that left the tree (an orphaned `&` job) survives; upgrade to setsid + killpg when it must die too.
    }

    internal void Dispose() => _process.Dispose();
}
