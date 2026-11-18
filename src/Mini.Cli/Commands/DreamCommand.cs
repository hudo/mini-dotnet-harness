// Python: cli.py command (the /dream branch)
namespace Mini.Cli.Commands;

/// <summary>/dream: asks the model to tidy the project memory.</summary>
public sealed class DreamCommand : ISlashCommand
{
    /// <summary>What the model is asked.</summary>
    // Python: cli.py DREAM
    internal const string Prompt = """Review the project memory: read memory/INDEX.md and every file it routes to. Merge duplicates, delete what """
        + """is superseded or contradicted by the code (keep plans not built yet), tighten the wording, and keep INDEX.md """
        + """accurate. Then list what you changed.""";

    public string Name => "/dream";

    public string? Help => "/dream               review memory/: merge duplicates, drop what's stale";

    public async Task ExecuteAsync(ReplContext context, string args, CancellationToken ct)
    {
        await context.Agent.MaybeCompactAsync(ct);
        await context.Agent.TurnAsync(Prompt, ct: ct);
    }
}
