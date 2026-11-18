using static Mini.Cli.Terminal.AnsiColors;

// Python: cli.py command (the /resume branch)
namespace Mini.Cli.Commands;

/// <summary>/resume [id]: the latest session here, or one by id prefix.</summary>
public sealed class ResumeCommand : ISlashCommand
{
    public string Name => "/resume";

    public string? Help => null; // on /sessions' line

    public Task ExecuteAsync(ReplContext context, string args, CancellationToken ct)
    {
        var session = context.Sessions.Find(context.Cwd, args.Length > 0 ? args : null);
        if (session is null)
        {
            context.Out.WriteLine("No session found.");
            return Task.CompletedTask;
        }
        context.Agent = context.Start(session, null);
        context.Out.WriteLine($"{Dim}resumed {context.Agent.Session.Id} ({context.Agent.Messages.Count} messages){Off}");
        if (context.Agent.Goal is not null) GoalPrinter.Print(context.Out, context.Agent.Goal);
        return Task.CompletedTask;
    }
}
