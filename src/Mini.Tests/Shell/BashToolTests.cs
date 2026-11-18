using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests.Shell;

/// <summary>The bash tool: results, timeouts, the output cap, what a command that ends leaves behind.</summary>
public class BashToolTests : ShellTestBase
{
    // Python test: test_bash_reports_exit_code
    [Fact]
    public async Task BashReportsExitCode() => Assert.EndsWith("[exit 3]", await Bash("echo out; exit 3"));

    // Python test: test_a_command_still_running_at_the_timeout_goes_on_in_the_background
    // Adapted to what C# does: the note's stop wording is the accepted gap (pgrep -P PID, then kill PID: no per-job group),
    // and there is no own session to check (no setsid).
    [Fact]
    public async Task ACommandStillRunningAtTheTimeoutGoesOnInTheBackground()
    {
        var start = Stopwatch.StartNew();
        var output = await Bash("echo started; sleep 2; echo finished; touch done", timeout: 1);
        var m = Regex.Match(output, @"^\[still running after 1s as pid (\d+), until it ends or mini exits; its output goes on in (\S+), "
            + @"which ends with \[exit N\] when it's done\. Stop it by killing its children first \(pgrep -P (\d+) lists them\) "
            + @"and then pid (\d+): kill (\d+) alone stops the shell, and its children can survive\]\nstarted$");
        Assert.True(m.Success, output); // the note first: the screen shows line one
        Assert.Equal(m.Groups[1].Value, m.Groups[3].Value);
        Assert.Equal(m.Groups[1].Value, m.Groups[4].Value);
        Assert.Equal(m.Groups[1].Value, m.Groups[5].Value);
        Assert.True(start.Elapsed < TimeSpan.FromSeconds(1.8), output);
        var pid = int.Parse(m.Groups[1].Value);
        var log = m.Groups[2].Value;

        Assert.Equal([log], Directory.GetFiles(JobsDir)); // one log
        Assert.True(await LogEnded(log), "the command was not killed: it ends on its own");
        Assert.True(File.Exists(Ws.Path("done")));
        Assert.Equal("started\nfinished\n[exit 0]\n", ReadLog(log));
        Assert.True(await Gone(pid)); // reaped: no zombie for kill -0 to find
        Assert.Contains("finished", await Bash($"tail {log}", approver: new ApprovalSettings(interactive: false))); // job logs read freely
    }

    // Python test: test_binary_output_is_decoded_not_crashed
    [Fact]
    public async Task BinaryOutputIsDecodedNotCrashed()
    {
        File.WriteAllBytes(Ws.Path("b.bin"), [0xff, 0xfe, 0x00, (byte)'a', (byte)'b', (byte)'c']);
        Assert.Contains("abc", await Bash("cat b.bin"));
    }

    // Python test: test_negative_timeout_is_clamped
    [Fact]
    public async Task NegativeTimeoutIsClamped() => Assert.Equal("ok", await Bash("echo ok", timeout: -1));

    // Python test: test_the_bash_tool_runs_bash_when_there_is_one
    // The find_bash half uses the one `private` -> `internal` change (ShellLocator.FindBash) instead of Python's monkeypatched shutil.which.
    [Fact, UnsupportedOSPlatform("windows")]
    public async Task TheBashToolRunsBashWhenThereIsOne()
    {
        Assert.NotNull(Ws.Runtime.Locator.Bash);
        Assert.Equal("bash", Path.GetFileName(Ws.Runtime.Locator.Bash));
        Assert.Equal("bash\n0", await Bash("echo ${BASH_VERSION:+bash} | cat; echo ${PIPESTATUS[0]}"));
        Assert.Contains("(the bash tool runs bash)", Ws.Make([]).Agent.System);

        Assert.Null(ShellLocator.FindBash(Ws.Runtime.Options with { SearchPath = "" })); // no bash on PATH: then /bin/sh, and the system prompt says so
        var bin = Ws.Path("bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "bash"), "#!/bin/sh\n");
        File.SetUnixFileMode(Path.Combine(bin, "bash"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Assert.Null(ShellLocator.FindBash(Ws.Runtime.Options with { SearchPath = "bin" })); // a relative PATH entry would pick the project's own
        Assert.Equal(Path.Combine(bin, "bash"), ShellLocator.FindBash(Ws.Runtime.Options with { SearchPath = bin })); // the same folder, absolute: found
    }

    // Python test: test_a_timed_out_command_s_output_is_still_cut_to_the_cap_keeping_the_note
    [Fact]
    public async Task ATimedOutCommandSOutputIsStillCutToTheCapKeepingTheNote()
    {
        var t = Tool("bash", Json.Obj(("command", "head -c 40000 /dev/zero | tr '\\0' x; sleep 30"), ("timeout", 1)));
        var (agent, fake) = Ws.Make([Reply("tool_use", t), Reply(Text("ok"))]);

        await agent.TurnAsync("go", ct: Ct);

        var output = Json.Str(fake.Requests[1]["messages"]!.AsArray().Last()!["content"]![0]!["content"])!;
        Track(PidOf(output));
        Assert.True(output.Length < 30_100, output.Length.ToString());
        Assert.Contains("characters cut", output);
        Assert.StartsWith("[still running after 1s", output);
    }

    // Python test: test_a_job_left_running_with_and_does_not_hold_the_call
    [Fact]
    public async Task AJobLeftRunningWithAndDoesNotHoldTheCall()
    {
        var start = Stopwatch.StartNew();
        var output = await Bash("sleep 30 & echo $! > child; echo hi; echo oops >&2", timeout: 20);
        Assert.Equal("hi\noops", output);
        Assert.True(start.Elapsed < TimeSpan.FromSeconds(5), "1.0.2 waited out the timeout, then killed it");
        var child = int.Parse(File.ReadAllText(Ws.Path("child")).Trim());
        Track(child);
        Assert.True(ShellProcesses.Alive(child)); // still running: & does what it says
        Assert.Empty(Directory.GetFiles(JobsDir)); // the command ended, so no log is kept
    }

    // Python test: test_ctrl_c_during_bash_still_kills_the_whole_tree
    // Ctrl-C is a cancelled token here. The shell, its & job and its foreground child are all under the shell, so the tree kill finds them.
    [Fact]
    public async Task CtrlCDuringBashStillKillsTheWholeTree()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = Tools().BashAsync("echo $$ > shell; sleep 30 & echo $! > bg; sleep 30", 60, false, cts.Token);
        Assert.True(await WaitUntil(() => File.Exists(Ws.Path("bg")) && File.ReadAllText(Ws.Path("bg")).EndsWith('\n')));
        var shell = int.Parse(File.ReadAllText(Ws.Path("shell")).Trim());
        Assert.True(await WaitUntil(() => ShellProcesses.Descendants(shell).Count >= 2)); // the & job and the foreground child
        var tree = Track(shell);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        foreach (var pid in tree)
            Assert.True(await Gone(pid), $"pid {pid} is still running");
        Assert.Empty(Directory.GetFiles(JobsDir));
    }

    // Python test: test_a_headless_run_may_stop_what_it_left_running_and_nothing_else
    // The words the model is told differ (accepted gap): exactly `kill PID`, not `kill -- -PID`.
    [Fact]
    public async Task AHeadlessRunMayStopWhatItLeftRunningAndNothingElse()
    {
        var headless = new ApprovalSettings(interactive: false, allow: ["sleep"]); // nobody to approve a kill
        var output = await Bash("sleep 40", timeout: 1, approver: headless);
        var pid = PidOf(output);
        var log = Regex.Match(output, @"goes on in (\S+),").Groups[1].Value;
        Track(pid);

        foreach (var other in new[] { $"kill {pid + 1}", $"kill -9 {pid}", $"kill {pid}; rm -rf x", $"kill -- -{pid}", "kill -1",
            $"kill {pid}\x0b" }) // padding that bash keeps as part of the word
        {
            var e = await Assert.ThrowsAsync<ToolException>(() => Bash(other, approver: headless));
            Assert.Contains("denied", e.Message);
        }
        Assert.True(ShellProcesses.Alive(pid)); // none of those ran

        var stopped = await Bash($"kill {pid}", approver: headless); // the stop it was told, word for word: no approval
        Assert.StartsWith("[no output]", stopped); // what follows depends on whether the shell is gone yet
        Assert.True(await Gone(pid));
        Assert.True(await LogEnded(log), ReadLog(log));
        Assert.EndsWith("[exit 143]\n", ReadLog(log)); // SIGTERM, written as bash writes it: 128 + 15
        var again = await Assert.ThrowsAsync<ToolException>(() => Bash($"kill {pid}", approver: headless)); // once it's gone, the pid isn't its own any more
        Assert.Contains("denied", again.Message);
    }

    // Python test: test_output_so_far_includes_what_a_python_child_printed
    [Fact]
    public async Task OutputSoFarIncludesWhatAPythonChildPrinted()
    {
        Assert.SkipUnless((await Bash("command -v python3")) != "[no output]", "python3 is not installed");
        var output = await Bash("python3 -c \"print('started'); import time; time.sleep(30)\"", timeout: 2);
        Track(PidOf(output));
        Assert.EndsWith("]\nstarted", output);
    }

    // Python test: test_a_command_that_cannot_start_leaves_no_log
    [Fact]
    public async Task ACommandThatCannotStartLeavesNoLog()
    {
        Directory.CreateDirectory(Ws.Path("gone"));
        var tools = new ToolKit(Ws.Path("gone"), new ApprovalSettings(auto: true), Ws.Runtime);
        Directory.Delete(Ws.Path("gone"));

        await Assert.ThrowsAnyAsync<Exception>(() => tools.BashAsync("echo hi", ct: Ct));

        Assert.Empty(Directory.GetFiles(JobsDir));
    }

    // Python test: test_a_command_killed_by_a_signal_reports_128_plus_n
    [Fact]
    public async Task ACommandKilledByASignalReports128PlusN() => Assert.Equal("hi\n[exit 143]", await Bash("echo hi; kill -TERM $$"));

    // Python test: test_the_command_text_is_not_in_the_process_list_or_inherited
    [Fact]
    public async Task TheCommandTextIsNotInTheProcessListOrInherited()
    {
        const string probe = "pgrep -f mini_marker_7731 || echo none; env | grep -c '^MINI_COMMAND=' || true";
        Assert.Equal("none\n0", await Bash(probe)); // the model's own pkill -f can't hit the shell running its command

        var started = await Bash(probe, background: true);
        var log = Regex.Match(started, @"output in (\S+) ").Groups[1].Value;
        Assert.True(await LogEnded(log), ReadLog(log));
        Assert.Equal("none\n0\n[exit 0]\n", ReadLog(log));
    }

    // C#-only: a command left running at its timeout is in JobRegistry.Running (and not in Background), the list every later result and the exit cleanup use.
    [Fact]
    public async Task ACommandLeftRunningAtItsTimeoutIsListedInShellRunning()
    {
        var output = await Bash("sleep 30", timeout: 1);

        var pid = PidOf(output);
        Assert.Equal([pid], Jobs.Running.Select(j => j.Pid));
        Assert.Empty(Jobs.Background);
    }
}
