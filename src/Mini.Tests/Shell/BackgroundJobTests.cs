using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Mini.Tests.Shell;

/// <summary>Background jobs, and the notes every bash result carries about what earlier calls left running.</summary>
public class BackgroundJobTests : ShellTestBase
{
    // Python test: test_background_job_returns_at_once_and_outlives_the_call
    // No own session to check (no setsid): the rest is as in Python.
    [Fact]
    public async Task BackgroundJobReturnsAtOnceAndOutlivesTheCall()
    {
        var start = Stopwatch.StartNew();
        var output = await Bash("sleep 2; echo done > out.txt", background: true);
        Assert.True(start.Elapsed < TimeSpan.FromSeconds(1.5));
        Assert.False(File.Exists(Ws.Path("out.txt")));
        var m = Regex.Match(output, @"pid (\d+), output in (\S+) ");
        var pid = int.Parse(m.Groups[1].Value);
        var log = m.Groups[2].Value;
        Assert.Equal(JobsDir, Path.GetDirectoryName(log)); // the log outside the project

        Assert.True(await LogEnded(log, "[exit 0]"));
        Assert.Equal("done\n", File.ReadAllText(Ws.Path("out.txt")));
        Assert.Equal("[exit 0]", ReadLog(log).Trim());
        Assert.True(await Gone(pid)); // reaped too: a zombie would still answer kill -0
        Assert.Contains("[exit 0]", await Bash($"tail {log}", approver: new ApprovalSettings(interactive: false))); // job logs read freely
        Assert.Matches(@"^\d{8}-\d{6}-[0-9a-f]{12}\.log$", Path.GetFileName(log));

        var outside = Directory.CreateTempSubdirectory("mini-tests-outside-");
        try
        {
            var target = Path.Combine(outside.FullName, "outside.txt");
            File.WriteAllText(target, "private\n");
            var link = Path.Combine(JobsDir, "link.log");
            File.CreateSymbolicLink(link, target);
            var e = await Assert.ThrowsAsync<ToolException>(() => // the free pass is for the logs, not what links point to
                Tools(new ApprovalSettings(interactive: false)).CallAsync("read", Json.Obj(("path", link)), Ct));
            Assert.Contains("denied this read", e.Message);
        }
        finally { outside.Delete(recursive: true); }

        output = await Bash("echo \"[$1]\"; exit 3", background: true); // exit still logs; the command isn't its own $1
        log = Regex.Match(output, @"output in (\S+) ").Groups[1].Value;
        Assert.True(await LogEnded(log));
        Assert.Equal("[]\n[exit 3]\n", ReadLog(log));

        var denied = await Assert.ThrowsAsync<ToolException>(() => // the gate is the same as for any command
            Bash("sleep 1; true", background: true, approver: new ApprovalSettings(interactive: false)));
        Assert.Contains("denied", denied.Message);
    }

    // Python test: test_every_bash_result_lists_what_earlier_calls_left_running_and_says_when_it_ended
    // Adapted: the stop wording is the accepted gap (pgrep -P PID, then kill PID), and a plain kill of the shell does not take
    // what the shell started with it (no process groups), so "ended" waits for the shell only.
    [Fact]
    public async Task EveryBashResultListsWhatEarlierCallsLeftRunningAndSaysWhenItEnded()
    {
        var kept = await Bash("sleep 30", timeout: 1);
        Assert.StartsWith("[still running after 1s", kept);
        Assert.DoesNotContain("from earlier calls", kept); // not listed twice
        var job = await Bash("sleep 30", background: true);
        var pids = new[] { kept, job }.Select(x => int.Parse(Regex.Match(x, @"pid (\d+)").Groups[1].Value)).ToArray();
        foreach (var p in pids) Track(p);

        var note = Regex.Match(await Bash("echo hi"), @"\[still running from earlier calls: (.*)\]$").Groups[1].Value;
        Assert.Equal(pids, Regex.Matches(note, @"pid (\d+)").Select(x => int.Parse(x.Groups[1].Value)));
        Assert.Equal(2, Regex.Matches(note, @"\.log").Count);
        Assert.Contains("Stop one by killing its children first (pgrep -P PID lists them), then kill PID", note);
        Assert.Contains("while kill -0 PID", note);

        ShellProcesses.Signal(pids[0], 15); // a plain kill of its shell
        Assert.True(await WaitUntil(() => Jobs.Running[0].HasExited));
        var output = await Bash("echo hi");
        Assert.Matches($@"^hi\n\[ended since the last call: pid {pids[0]}, log \S+\.log\]\n\[still running from earlier calls: pid {pids[1]}, .*\]$", output);

        foreach (var p in pids) ShellProcesses.Signal(p, 9);
        Assert.True(await WaitUntil(() => Jobs.Background[0].HasExited));
        Assert.Matches($@"^hi\n\[ended since the last call: pid {pids[1]}, log \S+\.log\]$", await Bash("echo hi"));
        Assert.Equal("hi", await Bash("echo hi")); // said once
    }

    // Python test: test_an_ended_job_is_announced_once_even_if_no_result_listed_it
    [Fact]
    public async Task AnEndedJobIsAnnouncedOnceEvenIfNoResultListedIt()
    {
        var pid = Regex.Match(await Bash("sleep 0.5", background: true), @"pid (\d+)").Groups[1].Value; // its own result said it was running

        var output = await Bash("sleep 1; echo later");

        Assert.Matches($@"^later\n\[ended since the last call: pid {pid}, log \S+\.log\]$", output);
        Assert.Equal("b", await Bash("echo b"));
    }

    // C#-only: Python 1.0.5 adds "; what it started is still running, in process group N: ..." when an ended job left something
    // running (P4). That is an accepted gap here (no per-job groups): the line stays plain. This pins the plain line.
    [Fact]
    public async Task AnEndedJobThatLeftSomethingRunningIsAnnouncedWithThePlainLine()
    {
        var started = await Bash("nohup sleep 30 >/dev/null 2>&1 & echo $! > child", background: true);
        var pid = int.Parse(Regex.Match(started, @"pid (\d+)").Groups[1].Value);
        Assert.True(await WaitUntil(() => Jobs.Background[0].HasExited)); // its shell is done; the sleep it started is not
        var child = int.Parse(File.ReadAllText(Ws.Path("child")).Trim());
        Track(child);

        var output = await Bash("echo hi");

        Assert.Matches($@"^hi\n\[ended since the last call: pid {pid}, log \S+\.log\]$", output);
        Assert.True(ShellProcesses.Alive(child));
        Assert.Equal("b", await Bash("echo b")); // said once
    }

    // Python test: test_an_ended_job_that_left_something_running_says_so
    [Fact(Skip = "C# differs from Python: the ended line never adds 'what it started is still running, in process group N' (accepted gap P4, JobRegistry.cs StillRunning)")]
    public async Task AnEndedJobThatLeftSomethingRunningSaysSo()
    {
        var pid = int.Parse(Regex.Match(await Bash("nohup sleep 30 >/dev/null 2>&1 &", background: true), @"pid (\d+)").Groups[1].Value);
        Assert.True(await WaitUntil(() => Jobs.Background[0].HasExited));
        Assert.Matches($@"^hi\n\[ended since the last call: pid {pid}, log \S+\.log; what it started is still running, "
            + $@"in process group {pid}: stop it with kill -- -{pid}\]$", await Bash("echo hi"));
    }
}
