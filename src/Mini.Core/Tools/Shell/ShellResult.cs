namespace Mini.Core.Tools.Shell;

/// <summary>What a shell command left behind: its exit code (null when left running) and its output.</summary>
// Python: tools.py run_shell (the (code, output) it returns)
public sealed record ShellResult(int? Code, string Output);
