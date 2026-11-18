// Python: cli.py command, HELP
namespace Mini.Cli.Commands;

/// <summary>Sends a REPL line to its command: <c>/name rest</c> to the command called that, anything else to the agent as a prompt.</summary>
public sealed class SlashCommandRouter
{
    readonly Dictionary<string, ISlashCommand> _byName;

    /// <summary>The text of <c>/help</c>: each command's own lines, in registration order.</summary>
    // Python: cli.py HELP
    public string HelpText { get; }

    public SlashCommandRouter(IEnumerable<ISlashCommand> commands)
    {
        var all = commands.ToList();
        _byName = all.ToDictionary(c => c.Name);
        HelpText = string.Join("\n", all.Select(c => c.Help).OfType<string>());
    }

    /// <summary>Runs one non-empty line.</summary>
    // Python: cli.py command
    public async Task RunAsync(ReplContext context, string line, CancellationToken ct)
    {
        var space = line.IndexOf(' ');
        var name = space < 0 ? line : line[..space];
        var rest = space < 0 ? "" : line[(space + 1)..].Trim();
        if (_byName.TryGetValue(name, out var command))
        {
            await command.ExecuteAsync(context, rest, ct);
        }
        else if (name.StartsWith('/'))
        {
            context.Out.WriteLine($"Unknown command {name}. /help lists them.");
        }
        else
        {
            await context.Agent.MaybeCompactAsync(ct);
            await context.Agent.TurnAsync(line, ct: ct);
        }
    }
}
