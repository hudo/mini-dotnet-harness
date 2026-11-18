using System.Diagnostics;

// Python: tools.py spawn, EVAL
namespace Mini.Core.Tools.Shell;

/// <summary>Runs the command in bash (or /bin/sh when none was found). The command goes in through the environment, not the arguments,
/// so the model's own `pkill -f` can't match the shell running it.</summary>
public sealed class BashLauncher(string shellPath) : IShellLauncher
{
    // Expanded before eval runs, so the command and its children don't inherit MINI_COMMAND or MINI_LOG.
    // After `exec >>"$MINI_LOG"` the shell's own output goes to the log, not to a pipe, so a detached job outlives mini.
    // shortcut: no setsid, so the shell shares mini's process group and terminal (a `/dev/tty` read or a terminal close reaches
    // it, and Ctrl-C too); `trap '' INT` covers SIGINT, the harness kills foreground commands itself. Simple mode per
    // MIGRATION-PLAN; upgrade to a real session if SIGINT must be per-command or a job must outlive the terminal.
    // Python: tools.py spawn (the redirect into the log, as a shell prefix)
    private const string Prelude = """exec >>"$MINI_LOG" 2>&1; trap '' INT; """;
    // Python: tools.py run_shell (the group kill when not keep, as an EXIT trap)
    private const string KillJobs = "trap 'kill -9 $(jobs -p) 2>/dev/null' EXIT; ";
    // Python: tools.py EVAL (this one also unsets MINI_LOG)
    private const string Eval = "eval \"unset MINI_COMMAND MINI_LOG;$MINI_COMMAND\"";

    // Python: tools.py spawn
    public ProcessStartInfo CreateStart(string command, string cwd, string log, bool job, bool killJobs)
    {
        var psi = new ProcessStartInfo { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardInput = true, FileName = shellPath };
        psi.Environment["PAGER"] = "cat";
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.ArgumentList.Add("-c");
        // A subshell for a job, so `exit 3` still logs [exit 3].
        // killJobs: an EXIT trap, not code after the eval, since `exit 3` inside the command ends the shell at once; the
        // trap runs then and keeps the exit status. Python's run_shell kills the group when `not keep`.
        // shortcut: this reaches only the top-level `&` jobs of that shell: not nested, setsid'd or disowned ones, and
        // a trap the command sets itself replaces it. Upgrade with setsid and a group kill.
        psi.ArgumentList.Add(Prelude + (killJobs ? KillJobs : "") + (job ? $"({Eval}); echo \"[exit $?]\"" : Eval));
        psi.Environment["MINI_COMMAND"] = command;
        psi.Environment["MINI_LOG"] = log;
        return psi;
    }
}
