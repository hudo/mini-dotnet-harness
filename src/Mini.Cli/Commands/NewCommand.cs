using static Mini.Cli.Terminal.AnsiColors;

// Python: cli.py command (the /new branch)
namespace Mini.Cli.Commands;

/// <summary>/new [model]: a fresh session, on another model if given.</summary>
public sealed class NewCommand : ISlashCommand
{
    public string Name => "/new";

    public string? Help => "/new [model]         start a fresh session, on another model if given (see mini --models)";

    public Task ExecuteAsync(ReplContext context, string args, CancellationToken ct)
    {
        context.Agent = context.Start(context.Sessions.Create(context.Cwd), args.Length > 0 ? args : null);
        context.Out.WriteLine($"{Dim}new session {context.Agent.Session.Id}{Off}");
        return Task.CompletedTask;
    }
}
