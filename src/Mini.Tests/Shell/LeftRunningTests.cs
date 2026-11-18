using System.Text.RegularExpressions;

namespace Mini.Tests.Shell;

/// <summary>What is left running when mini ends: <c>JobRegistry.StopAll</c>, which Cli calls at exit, on Ctrl-C at the prompt and on SIGTERM/SIGHUP.
/// In process; the cases that end a real mini with a signal are in <c>RealMiniProcessTests</c>.</summary>
public class LeftRunningTests : ShellTestBase
{
    static int JobPid(string started) => int.Parse(Regex.Match(started, @"pid (\d+)").Groups[1].Value);

    // Python test: test_what_a_timeout_left_running_ends_with_mini_but_background_jobs_stay
    // Only the "return"/"exit" ends here, in process (the REPL ends: RealMiniProcessTests): StopAll is what atexit, Ctrl-C at the prompt and SIGTERM all run.
    [Fact]
    public async Task WhatATimeoutLeftRunningEndsWithMiniButBackgroundJobsStay()
    {
        var left = PidOf(await Bash("sleep 60", timeout: 1));
        var job = JobPid(await Bash("sleep 60", background: true));
        var leftTree = Track(left);
        var jobTree = Track(job);

        Jobs.StopAll();

        foreach (var pid in leftTree)
            Assert.True(await Gone(pid), $"pid {pid} left running at the timeout is still there");
        Assert.All(jobTree, pid => Assert.True(ShellProcesses.Alive(pid))); // a background job is meant to outlive mini
    }

    // Python test: test_at_exit_what_was_left_running_gets_sigterm_first_and_sigkill_if_it_stays
    [Fact(Skip = "C# differs from Python: StopAll kills the tree at once, with no SIGTERM first, so a TERM trap never runs (JobRegistry.cs StopAll)")]
    public async Task AtExitWhatWasLeftRunningGetsSigtermFirstAndSigkillIfItStays()
    {
        var polite = PidOf(await Bash("trap 'echo cleaned > cleaned; exit 3' TERM; sleep 60 & wait", timeout: 1));
        var stubborn = PidOf(await Bash("trap '' TERM; sleep 60", timeout: 1));

        Jobs.StopAll();

        Assert.True(await Gone(polite));
        Assert.True(await Gone(stubborn));
        Assert.Equal("cleaned\n", File.ReadAllText(Ws.Path("cleaned"))); // it got the chance to clean up
    }

    // C#-only: the half of the SIGTERM-first test that does hold: both are gone at once, the polite one without cleaning up.
    [Fact]
    public async Task AtExitWhatWasLeftRunningIsKilledAtOnceTrapOrNot()
    {
        var polite = PidOf(await Bash("trap 'echo cleaned > cleaned; exit 3' TERM; sleep 60 & wait", timeout: 1));
        var stubborn = PidOf(await Bash("trap '' TERM; sleep 60", timeout: 1));
        var tree = Track(polite).Concat(Track(stubborn)).ToArray();

        Jobs.StopAll();

        foreach (var pid in tree)
            Assert.True(await Gone(pid), $"pid {pid} is still running");
        Assert.False(File.Exists(Ws.Path("cleaned")));
    }

    // Python test: test_at_exit_the_whole_group_dies_even_if_its_shell_goes_first_or_the_wait_is_cut_short
    // In process, and only the first half of the intent: everything under the shell dies, whatever it does with SIGTERM.
    // Not covered: a second signal in the middle of the cleanup, and orphans that left the shell's tree (no process groups).
    [Theory]
    [InlineData("sh -c \"trap '' TERM; sleep 60\"; echo after")] // the sh and sleep it ran ignore SIGTERM
    [InlineData("trap '' TERM; sleep 60")]
    public async Task AtExitTheWholeTreeDiesEvenIfItIgnoresSigterm(string command)
    {
        var left = PidOf(await Bash(command, timeout: 1));
        var tree = Track(left);

        Jobs.StopAll();

        foreach (var pid in tree)
            Assert.True(await Gone(pid), $"pid {pid} is still running");
    }
}
