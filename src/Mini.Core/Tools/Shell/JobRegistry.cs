// Python: tools.py RUNNING, JOBS, stop, stop_running, still_running (the two module-level lists and the functions that act on them)
namespace Mini.Core.Tools.Shell;

/// <summary>What the bash tool left running: commands that outlived their timeout (<see cref="Running"/>) and background jobs
/// (<see cref="Background"/>). One per process in the real program, one per test; <see cref="ShellRunner"/> fills it and every bash
/// result reads it.</summary>
/// <remarks>Several threads touch it: a bash call adds to it on a thread-pool continuation, parallel bash calls read it, and
/// <see cref="StopAll"/> runs on the ProcessExit or Ctrl-C thread. Python's lists were touched from the same places, under the GIL;
/// here a lock guards the two lists, and what it hands out is a snapshot.</remarks>
public sealed class JobRegistry
{
    private readonly object _gate = new();
    private readonly List<BackgroundJob> _running = new();
    private readonly List<BackgroundJob> _background = new();

    /// <summary>Commands left running at their timeout; background jobs aren't here, since they're meant to outlive mini.</summary>
    // Python: tools.py RUNNING
    public IReadOnlyList<BackgroundJob> Running { get { lock (_gate) return _running.ToArray(); } }

    /// <summary>Background jobs started in this session; with <see cref="Running"/>, every bash result says which are still going.</summary>
    // Python: tools.py JOBS
    public IReadOnlyList<BackgroundJob> Background { get { lock (_gate) return _background.ToArray(); } }

    /// <summary>Running, then Background: what a bash result reports on.</summary>
    // Python: tools.py LocalTools.bash (earlier = RUNNING + JOBS)
    public IReadOnlyList<BackgroundJob> All() { lock (_gate) return [.. _running, .. _background]; }

    // Python: tools.py run_shell (RUNNING.append)
    internal void AddRunning(BackgroundJob job) { lock (_gate) _running.Add(job); }

    // Python: tools.py start_job (JOBS.append)
    internal void AddBackground(BackgroundJob job) { lock (_gate) _background.Add(job); }

    // Python: tools.py run_shell (proc in RUNNING)
    internal bool IsRunning(BackgroundJob job) { lock (_gate) return _running.Contains(job); }

    /// <summary>Kill it and its whole process tree.</summary>
    // Python: tools.py stop (the tree kill; no process group, so no signal choice)
    public void Stop(BackgroundJob job) => job.Kill();

    /// <summary>When mini exits, so does what it left running. Background jobs stay.</summary>
    // Python: tools.py stop_running (SIGKILL only; no SIGTERM first, no group wait)
    public void StopAll()
    {
        // shortcut: no SIGTERM first and SIGKILL a second later, since there is no group signal; upgrade with killpg when a test run or server must clean up first.
        foreach (var job in Running.Where(j => !j.HasExited).ToList())
            Stop(job);
    }

    /// <summary>What earlier calls left running, and what has ended since the model last heard it was running (the call that
    /// started it said so), so it needn't poll. An entry that has ended is never probed again: its pid may be reused.</summary>
    // Python: tools.py still_running (without the group_running note)
    public string StillRunning(IReadOnlyList<BackgroundJob> earlier)
    {
        var live = earlier.Where(j => !j.HasExited).ToList();
        var ended = earlier.Where(j => !live.Contains(j) && j.Listed).ToList();
        foreach (var j in earlier)
            j.Listed = live.Contains(j);
        // shortcut: Python 1.0.5 adds "; what it started is still running, in process group N: stop it with kill -- -N" to an
        // ended entry whose group lives on (group_running). Simple mode has no per-job groups, so a probe would see mini's own
        // group; the line stays plain. Upgrade with setsid via P/Invoke, then port group_running.
        return string.Concat(ended.Select(j => $"\n[ended since the last call: pid {j.Pid}, log {j.Log}]"))
            + (live.Count == 0 ? "" : "\n[still running from earlier calls: " + string.Join("; ", live.Select(j => $"pid {j.Pid}, log {j.Log}"))
                + ". Stop one by killing its children first (pgrep -P PID lists them), then kill PID: kill PID alone stops the "
                + "shell, and its children can survive; for one that should finish, wait with: while kill -0 PID 2>/dev/null; do sleep 5; done, "
                + "in a call whose timeout is long enough]");
    }
}
