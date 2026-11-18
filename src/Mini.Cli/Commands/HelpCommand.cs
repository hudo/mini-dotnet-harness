// Python: cli.py command (the /help branch)
namespace Mini.Cli.Commands;

/// <summary>/help: the commands, as the router lists them.</summary>
public sealed class HelpCommand : ISlashCommand
{
    public string Name => "/help";

    public string? Help => null; // it lists the others

    public Task ExecuteAsync(ReplContext context, string args, CancellationToken ct)
    {
        context.Out.WriteLine(context.Router.HelpText);
        return Task.CompletedTask;
    }
}
