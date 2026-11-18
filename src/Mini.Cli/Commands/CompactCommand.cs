// Python: cli.py command (the /compact branch)
namespace Mini.Cli.Commands;

/// <summary>/compact: summarizes the history now.</summary>
public sealed class CompactCommand : ISlashCommand
{
    public string Name => "/compact";

    public string? Help => "/compact · /exit     summarize the history now · quit (every message is already saved)"; // /exit is the REPL's own

    public async Task ExecuteAsync(ReplContext context, string args, CancellationToken ct) => await context.Agent.CompactAsync(ct);
}
