namespace Mini.Cli.Terminal;

/// <summary>Terminal columns and rows.</summary>
// Python: tools.py Approver.ok (shutil.get_terminal_size's result)
sealed record TerminalSize(int Width, int Height);
