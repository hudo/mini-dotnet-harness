// Python: tools.py run_shell, start_job
namespace Mini.Core.Tools.Shell;

/// <summary>Runs the model's shell commands, in the foreground or detached.</summary>
public interface IShellRunner
{
    /// <summary>Run a shell command and return its exit code and output. The call waits on the shell, not on its output, so a job it
    /// leaves running with &amp; doesn't hold it. Cancelling kills the process tree, and so does the timeout (a
    /// TimeoutException), unless keep: then the command runs on until it ends or mini exits, and the code is null.</summary>
    Task<ShellResult> RunAsync(string command, string cwd, int timeoutSec, bool keep, CancellationToken ct);

    /// <summary>Starts it detached, so neither a call's timeout nor Ctrl-C kills it; the log says when it ends. Returns what the model is told.</summary>
    string StartJob(string command, string cwd);
}
