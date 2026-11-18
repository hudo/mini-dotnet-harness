using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Mini.Tests.Fakes;

namespace Mini.Tests.Shell;

/// <summary>
/// The base of every shell test: a Workspace, a clean jobs folder, and a guarantee that nothing is left running.
/// The Workspace's runtime has a job registry of its own, so Dispose kills what the test started (the jobs'
/// process trees and every pid the test registered with <see cref="Track"/>) and asserts that none is left.
/// </summary>
// Python test helper: tests/test_mini.py no_leftovers, wait_until, gone
public abstract class ShellTestBase : IDisposable
{
    protected readonly Workspace Ws = new();
    readonly HashSet<int> _pids = [];

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static string JobsDir => Path.Combine(TestEnvironment.Home, "jobs");

    /// <summary>What this test's bash calls left running (its own registry, not shared with other tests).</summary>
    protected JobRegistry Jobs => Ws.Runtime.Jobs;

    protected ShellTestBase()
    {
        Directory.CreateDirectory(JobsDir);
        foreach (var f in Directory.GetFileSystemEntries(JobsDir))
            File.Delete(f);
    }

    protected ToolKit Tools(ApprovalSettings? approver = null) => Ws.Tools(approver ?? new ApprovalSettings(auto: true));

    /// <summary>Remember a pid, and everything under it now, to be killed when the test ends.</summary>
    protected int[] Track(int pid)
    {
        var all = new[] { pid }.Concat(ShellProcesses.Descendants(pid)).ToArray();
        foreach (var p in all) _pids.Add(p);
        return all;
    }

    /// <summary>Tools().BashAsync with the test's token.</summary>
    protected Task<string> Bash(string command, int timeout = 120, bool background = false, ApprovalSettings? approver = null) =>
        Tools(approver).BashAsync(command, timeout, background, Ct);

    /// <summary>Kill what the test started and wait for it to be gone.</summary>
    void KillLeft()
    {
        var jobs = Jobs.All();
        foreach (var j in jobs)
        {
            try { foreach (var p in ShellProcesses.Descendants(j.Pid)) _pids.Add(p); _pids.Add(j.Pid); }
            catch (InvalidOperationException) { } // already disposed
        }
        foreach (var j in jobs) Jobs.Stop(j);
        foreach (var p in _pids) ShellProcesses.Signal(p, 9);
        var end = Environment.TickCount64 + 5000;
        while (_pids.Any(ShellProcesses.Alive) && Environment.TickCount64 < end)
            Thread.Sleep(20);
        if (jobs.Count > 0) Thread.Sleep(150); // a reaper appends "[exit N]" to a log after the kill: let it, before the next test clears the folder
    }

    public void Dispose()
    {
        KillLeft();
        var alive = _pids.Where(ShellProcesses.Alive).ToList();
        Ws.Dispose();
        Assert.Empty(alive);
    }

    // -- polling ------------------------------------------------------------------------------------------

    protected static async Task<bool> WaitUntil(Func<bool> check, double within = 5.0)
    {
        var end = Environment.TickCount64 + (long)(within * 1000);
        while (!check() && Environment.TickCount64 < end)
            await Task.Delay(50, Ct);
        return check();
    }

    /// <summary>Has the process ended (and been reaped)? Orphans go to init, so give it a moment.</summary>
    protected static Task<bool> Gone(int pid, double within = 5.0) => WaitUntil(() => !ShellProcesses.Alive(pid), within);

    protected static string ReadLog(string log)
    {
        using var f = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(f).ReadToEnd();
    }

    protected static Task<bool> LogEnded(string log, string marker = "[exit", double within = 10) =>
        WaitUntil(() => ReadLog(log).Contains(marker, StringComparison.Ordinal), within);

    /// <summary>The pid in the "[still running after Ns as pid N," note.</summary>
    protected static int PidOf(string note) => int.Parse(Regex.Match(note, @"as pid (\d+),").Groups[1].Value);
}

/// <summary>Real processes by pid: liveness, signals, children. POSIX only.</summary>
static class ShellProcesses
{
#pragma warning disable SYSLIB1054 // a plain DllImport of one int function is clearer than a source-generated stub
    [DllImport("libc", SetLastError = true)]
    static extern int kill(int pid, int sig);
#pragma warning restore SYSLIB1054

    const int Esrch = 3; // no such process; EPERM (1) means one exists that we can't signal: macOS says it for a zombie

    public static bool Alive(int pid) => kill(pid, 0) == 0 || Marshal.GetLastPInvokeError() != Esrch;

    public static void Signal(int pid, int sig) => _ = kill(pid, sig);

    public static IEnumerable<int> Children(int pid)
    {
        var psi = new ProcessStartInfo("pgrep") { RedirectStandardOutput = true };
        psi.ArgumentList.Add("-P");
        psi.ArgumentList.Add(pid.ToString());
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
    }

    public static List<int> Descendants(int pid)
    {
        var all = new List<int>();
        var queue = new Queue<int>([pid]);
        while (queue.Count > 0)
        {
            foreach (var c in Children(queue.Dequeue()))
            {
                all.Add(c);
                queue.Enqueue(c);
            }
        }

        return all;
    }
}
