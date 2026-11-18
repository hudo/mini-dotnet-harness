// Python: cli.py command (the /effort branch)
namespace Mini.Cli.Commands;

/// <summary>/effort [level]: shows the effort, and changes it when the model takes the level given.</summary>
public sealed class EffortCommand : ISlashCommand
{
    public string Name => "/effort";

    public string? Help => "/effort [level]      show or change the effort for the next requests";

    public Task ExecuteAsync(ReplContext context, string args, CancellationToken ct)
    {
        var agent = context.Agent;
        if (args.Length > 0 && !agent.ChangeEffort(args))
            context.Out.WriteLine($"{agent.Model} has no effort {args}");
        context.Out.WriteLine($"effort {agent.Effort} · {agent.Model} takes {string.Join(", ", agent.Efforts)}");
        return Task.CompletedTask;
    }
}
