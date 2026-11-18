using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Mini.Tests.Fakes;

namespace Mini.Tests.Shell;

/// <summary>The real program as a child process, run on a local server that plays the API: the model asks for a command that outlives its timeout and for a
/// background job, then the REPL is ended by a signal. It is how a real <c>mini</c> gets to run tools without a key.</summary>
public class RealMiniProcessTests : ShellTestBase
{
    // Python test: test_what_a_timeout_left_running_ends_with_mini_but_background_jobs_stay
    // The REPL cases: Ctrl-C at the prompt, a kill, a closed terminal. Python runs the tools from a script, C# only through the model; the "ctrl-c" case
    // (a KeyboardInterrupt out of a script) has no counterpart, and "return" and "exit" are LeftRunningTests. Exit codes are as a shell reports them.
    [Theory]
    [InlineData("INT", 0)]
    [InlineData("TERM", 143)]
    [InlineData("HUP", 129)]
    public async Task WhatATimeoutLeftRunningEndsWithMiniButBackgroundJobsStayWhenTheReplIsEnded(string signal, int exitCode)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX: Windows leaves nothing running");
        var home = Directory.CreateTempSubdirectory("mini-tests-realproc-").FullName;
        using var server = new FakeApiServer([
            FakeApiServer.Sse(("bash", """{"command": "sleep 60", "timeout": 1}"""), ("bash", """{"command": "sleep 60", "background": true}""")),
            FakeApiServer.Sse()]);
        var info = new ProcessStartInfo("dotnet", [Path.Combine(AppContext.BaseDirectory, "mini.dll"), "--permissions", "yes", "--model", "claude-haiku-5-5"])
        {
            WorkingDirectory = Ws.Root, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        info.Environment["MINI_NET_HOME"] = home;
        info.Environment["ANTHROPIC_API_KEY"] = "test-key";
        info.Environment["ANTHROPIC_BASE_URL"] = server.Url;
        using var mini = Process.Start(info)!;
        var shown = new StringBuilder();
        _ = Task.Run(async () =>
        {
            var buffer = new char[256];
            for (int n; (n = await mini.StandardOutput.ReadAsync(buffer)) > 0;)
                lock (shown) shown.Append(buffer, 0, n);
        }, Ct);
        _ = mini.StandardError.ReadToEndAsync(Ct);
        int Prompts() { lock (shown) return shown.ToString().Count(c => c == '›'); }
        try
        {
            Assert.True(await WaitUntil(() => Prompts() == 1, 30), "no prompt");
            await mini.StandardInput.WriteLineAsync("go");
            Assert.True(await WaitUntil(() => Prompts() == 2, 30), "the turn did not end");
            await Task.Delay(300, Ct); // the REPL is at its prompt, and Ctrl-C there means "leave"
            var left = int.Parse(Regex.Match(server.Bodies[1], @"as pid (\d+),").Groups[1].Value);
            var job = int.Parse(Regex.Match(server.Bodies[1], @"background: pid (\d+),").Groups[1].Value);
            var leftTree = Track(left);
            var jobTree = Track(job);

            using (var kill = Process.Start("kill", ["-" + signal, mini.Id.ToString()])) kill.WaitForExit();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            limit.CancelAfter(TimeSpan.FromSeconds(20));
            await mini.WaitForExitAsync(limit.Token);

            Assert.Equal(exitCode, mini.ExitCode);
            foreach (var pid in leftTree)
                Assert.True(await Gone(pid), $"pid {pid}, left running at the timeout, is still there");
            Assert.All(jobTree, pid => Assert.True(ShellProcesses.Alive(pid))); // a background job is meant to outlive mini
        }
        finally
        {
            if (!mini.HasExited) mini.Kill(entireProcessTree: true);
            Directory.Delete(home, recursive: true);
        }
    }
}
