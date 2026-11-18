using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

// Python: tools.py spawn, reap, run_shell, start_job (RUNNING, JOBS, stop, stop_running, still_running are in JobRegistry)
namespace Mini.Core.Tools.Shell;

/// <summary>Starts the model's shell commands through an <see cref="IShellLauncher"/>. The clock for job log names comes from the time given;
/// what is left running is recorded in the <see cref="JobRegistry"/> given.</summary>
public sealed class ShellRunner(IShellLauncher launcher, MiniPaths paths, TimeProvider time, JobRegistry registry) : IShellRunner
{
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Start with no stdin, stdout and stderr into a new log under Home/jobs/.</summary>
    // Python: tools.py spawn
    private BackgroundJob Spawn(string command, string cwd, bool job, bool killJobs = false)
    {
        var log = Path.Combine(paths.JobsDir,
            $"{time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{RandomNumberGenerator.GetHexString(12, lowercase: true)}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        File.Create(log).Dispose(); // exists before the shell appends to it, as Python's open("ab") made it
        try
        {
            var proc = Process.Start(launcher.CreateStart(command, cwd, log, job, killJobs))!;
            proc.StandardInput.Close(); // Python's DEVNULL
            return new BackgroundJob(proc, log);
        }
        catch // e.g. the working directory is gone: no empty log left behind
        {
            File.Delete(log);
            throw;
        }
    }

    /// <summary>Wait for it in the background, so it ends its log with [exit N] (a job's own shell does that for it).</summary>
    // Python: tools.py reap (without the group kill of a shell that was killed)
    private static void Reap(BackgroundJob job, bool writeLog)
    {
        // shortcut: Python also killed the group of a shell that was killed (kill PID); with no group, what it started stays until it exits on its own; upgrade to setsid + killpg when it must not.
        _ = Task.Run(async () =>
        {
            await job.WaitForExitAsync(CancellationToken.None);
            if (!writeLog) return;
            try { File.AppendAllText(job.Log, $"[exit {job.ExitCode}]\n", Utf8); } // .NET already reports a signal death as 128+N
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        });
    }

    /// <summary>Never keep on Windows: without process groups it shares mini's console, so the user's next Ctrl-C would kill it anyway.</summary>
    // Python: tools.py run_shell
    public async Task<ShellResult> RunAsync(string command, string cwd, int timeoutSec, bool keep, CancellationToken ct)
    {
        timeoutSec = Math.Clamp(timeoutSec, 1, int.MaxValue / 1000);
        ct.ThrowIfCancellationRequested(); // before Spawn: a pre-cancelled call leaves no log behind
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);
        var job = Spawn(command, cwd, job: false, killJobs: !keep && !OperatingSystem.IsWindows()); // the goal's verify: whatever it left running with & goes too
        var log = job.Log;
        var killed = false;
        try
        {
            await job.WaitForExitAsync(linked.Token);
            return new(job.ExitCode, ReadLog(log));
        }
        catch (OperationCanceledException) when (keep && timer.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            registry.AddRunning(job); // left running, like a background job
            Reap(job, writeLog: true);
            return new(null, $"[still running after {timeoutSec}s as pid {job.Pid}, until it ends or mini exits; its output goes "
                + $"on in {log}, which ends with [exit N] when it's done. Stop it by killing its children first (pgrep -P {job.Pid} "
                + $"lists them) and then pid {job.Pid}: kill {job.Pid} alone stops the shell, and its children can survive]\n"
                + ReadLog(log).Trim()); // note first: the screen shows line one
        }
        catch (Exception e)
        {
            killed = true;
            registry.Stop(job);
            try { await job.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); } // bounded: a stuck kill can't hang the caller
            catch (Exception wait) when (wait is TimeoutException or InvalidOperationException)
            {
                // shortcut: kill couldn't finish within 5 s, so the tree may linger after log cleanup; upgrade to a group kill that can be waited on.
            }
            if (e is OperationCanceledException && timer.IsCancellationRequested && !ct.IsCancellationRequested)
                throw new TimeoutException($"Command timed out after {timeoutSec} seconds");
            throw;
        }
        finally
        {
            if (!registry.IsRunning(job)) // done or killed; the log stays only for a command left running
            {
                // Windows can't delete the log until the killed children let go of it
                for (var tries = killed && OperatingSystem.IsWindows() ? 60 : 1; tries > 0; tries--)
                {
                    try { File.Delete(log); break; }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { await Task.Delay(50, CancellationToken.None); }
                }
                job.Dispose();
            }
        }
    }

    /// <summary>Whatever the shell has written so far; shared open, since Windows lets a writer hold the file.</summary>
    // Python: tools.py run_shell (reading the log)
    private static string ReadLog(string log)
    {
        using var f = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // invalid bytes become U+FFFD; \r\n and a lone \r become \n, as Python's text-mode read_text does
        return new StreamReader(f, Utf8, detectEncodingFromByteOrderMarks: false).ReadToEnd().Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>Detached, so neither the call's timeout nor Ctrl-C kills it; the log says when it ends.</summary>
    // Python: tools.py start_job
    public string StartJob(string command, string cwd)
    {
        if (OperatingSystem.IsWindows())
            throw new ToolException("background isn't supported on Windows: run it without background, or ask the user to start it.");
        var job = Spawn(command, cwd, job: true);
        registry.AddBackground(job);
        Reap(job, writeLog: false);
        // No process group to kill, so plain kill stops the shell only: `kill -- -PID` would find no such group, and
        // children of the shell survive it. Tell the model to stop them first, not to trust one kill.
        return $"Started in the background: pid {job.Pid}, output in {job.Log} (it ends with [exit N] when the job is done). "
            + $"Check it with tail {job.Log} or kill -0 {job.Pid}; stop it by killing its children first (pgrep -P {job.Pid} lists "
            + $"them) and then kill {job.Pid}: kill {job.Pid} alone stops the shell, and its children can survive.";
    }
}
