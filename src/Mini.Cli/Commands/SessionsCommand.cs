using System.Globalization;

// Python: cli.py command (the /sessions branch)
namespace Mini.Cli.Commands;

/// <summary>/sessions: this directory's sessions, the current one marked.</summary>
public sealed class SessionsCommand : ISlashCommand
{
    public string Name => "/sessions";

    public string? Help => "/sessions · /resume [id]  list sessions here · resume the latest, or one by id prefix"; // the /resume line too

    public Task ExecuteAsync(ReplContext context, string args, CancellationToken ct)
    {
        foreach (var r in context.Sessions.List(context.Cwd))
        {
            var mark = r.Id == context.Agent.Session.Id ? "*" : " "; // the session you're in
            var when = r.Updated.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            context.Out.WriteLine($"{mark} {r.Id}  {when}  {r.Turns,3} prompt{(r.Turns == 1 ? "" : "s")}  {r.Title}");
        }
        return Task.CompletedTask;
    }
}
