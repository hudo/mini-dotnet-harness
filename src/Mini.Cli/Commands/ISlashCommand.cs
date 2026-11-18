// Python: cli.py command (one branch of its if/elif chain)
namespace Mini.Cli.Commands;

/// <summary>One slash command of the REPL.</summary>
public interface ISlashCommand
{
    /// <summary>With the slash: <c>/help</c>.</summary>
    string Name { get; }

    /// <summary>The line (or lines) <c>/help</c> shows for this command; null when another command's line already covers it.</summary>
    string? Help { get; }

    /// <summary>Runs the command. <paramref name="args"/> is what follows the name, trimmed.</summary>
    Task ExecuteAsync(ReplContext context, string args, CancellationToken ct);
}
