using Mini.Tests.Fakes;

namespace Mini.Tests.Tools;

/// <summary>The small pieces behind the shell and file tools: launchers, the read tracker and the image budget.</summary>
// Python test helper: tests/test_mini.py (spawn's command line, LocalTools.seen and .held)
public class ShellAndFileTypesTests
{
    [Fact]
    public void BashPutsTheCommandInTheEnvironmentNotTheArguments()
    {
        var start = new BashLauncher("/bin/bash").CreateStart("echo hi; pkill -f x", "/work", "/logs/a.log", job: false, killJobs: false);

        Assert.Equal("/bin/bash", start.FileName);
        Assert.Equal("/work", start.WorkingDirectory);
        Assert.Equal(["-c", "exec >>\"$MINI_LOG\" 2>&1; trap '' INT; eval \"unset MINI_COMMAND MINI_LOG;$MINI_COMMAND\""], start.ArgumentList);
        Assert.Equal("echo hi; pkill -f x", start.Environment["MINI_COMMAND"]);
        Assert.Equal("/logs/a.log", start.Environment["MINI_LOG"]);
        Assert.Equal("cat", start.Environment["PAGER"]);
        Assert.Equal("1", start.Environment["PYTHONUNBUFFERED"]);
        Assert.True(start.RedirectStandardInput);
        Assert.DoesNotContain("pkill", string.Concat(start.ArgumentList)); // `pkill -f` must not match the shell that runs it
    }

    [Fact]
    public void ABackgroundJobRunsInASubshellAndEndsItsLogWithTheExitCode()
    {
        var start = new BashLauncher("/bin/sh").CreateStart("exit 3", "/w", "/l", job: true, killJobs: false);

        Assert.Equal("exec >>\"$MINI_LOG\" 2>&1; trap '' INT; (eval \"unset MINI_COMMAND MINI_LOG;$MINI_COMMAND\"); echo \"[exit $?]\"", start.ArgumentList[1]);
    }

    [Fact]
    public void AForegroundCommandThatMustLeaveNothingBehindGetsAnExitTrap()
    {
        var start = new BashLauncher("/bin/sh").CreateStart("make", "/w", "/l", job: false, killJobs: true);

        Assert.Contains("trap 'kill -9 $(jobs -p) 2>/dev/null' EXIT; ", start.ArgumentList[1]);
        Assert.DoesNotContain("trap 'kill -9", new BashLauncher("/bin/sh").CreateStart("make", "/w", "/l", job: false, killJobs: false).ArgumentList[1]);
    }

    [Fact]
    public void CmdRunsByFullPathWithTheLogRedirect()
    {
        var start = new CmdLauncher(@"D:\Win").CreateStart("dir", @"C:\w", @"C:\l.log", job: false, killJobs: false);

        Assert.Equal(Path.Combine(@"D:\Win", @"System32\cmd.exe"), start.FileName);
        Assert.Equal("/d /s /c \"(dir) >>\"C:\\l.log\" 2>&1\"", start.Arguments);
        Assert.Empty(start.ArgumentList);
        Assert.Equal(Path.Combine(@"C:\Windows", @"System32\cmd.exe"), new CmdLauncher(null).CreateStart("x", "/", "/", false, false).FileName);
    }

    [Fact]
    public void TheReadTrackerKnowsWhenAFileWasLastSeen()
    {
        using var ws = new Workspace();
        var file = ws.Write("a.txt", "x");
        var seen = new FileReadTracker();

        Assert.False(seen.TryGet(file, out _));
        seen.RecordCurrent(file);
        Assert.True(seen.TryGet(file, out var mtime));
        Assert.Equal(FileReadTracker.MtimeOf(file), mtime);
        seen.Record(file, DateTime.UnixEpoch);
        Assert.True(seen.TryGet(file, out var old));
        Assert.Equal(DateTime.UnixEpoch, old);
    }

    [Fact]
    public void TheImageBudgetStopsAtTwentyMegabytesAndCanBeReset()
    {
        var held = new ImageBudget();

        Assert.False(held.WouldExceed(ImageBudget.MaxHeld));
        Assert.True(held.WouldExceed(ImageBudget.MaxHeld + 1));
        held.Add(1_000);
        Assert.Equal(1_000, held.Held);
        Assert.True(held.WouldExceed(ImageBudget.MaxHeld));
        held.Reset(0); // a compaction: the summary holds no images
        Assert.Equal(0, held.Held);
        Assert.Equal(3000, ImageBudget.MaxPixels);
        Assert.Equal(5_000_000, ImageBudget.MaxFile);
    }

    [Fact]
    public void ABackgroundJobShowsItsPidAndWhetherItEnded()
    {
        using var ws = new Workspace();
        var started = ws.Runtime.Shell.StartJob("true", ws.Root);
        try
        {
            var job = Assert.Single(ws.Runtime.Jobs.Background);
            Assert.Contains($"pid {job.Pid},", started);
            Assert.Contains(job.Log, started);
            SpinWait.SpinUntil(() => job.HasExited, TimeSpan.FromSeconds(5));
            Assert.True(job.HasExited);
            Assert.Equal(0, job.ExitCode);
        }
        finally
        {
            foreach (var j in ws.Runtime.Jobs.All()) ws.Runtime.Jobs.Stop(j);
        }
    }
}
