// Python: cli.py command (the /goal branch)
namespace Mini.Cli.Commands;

/// <summary>/goal: sets a goal and works until it is done; <c>resume</c>, <c>clear</c> and <c>status</c> manage the current one.</summary>
public sealed class GoalCommand : ISlashCommand
{
    public string Name => "/goal";

    public string? Help => "/goal <objective>    work until done · /goal resume|clear|status";

    public async Task ExecuteAsync(ReplContext context, string args, CancellationToken ct)
    {
        var agent = context.Agent;
        if (args is "" or "status")
        {
            GoalPrinter.Print(context.Out, agent.Goal);
        }
        else if (args == "clear")
        {
            agent.ClearGoal();
            context.Out.WriteLine("Goal cleared.");
        }
        else if (args == "resume")
        {
            if (agent.Goal is { Status: GoalStatus.Paused or GoalStatus.Active })
                GoalPrinter.Print(context.Out, await agent.ResumeGoalAsync(ct));
            else
                context.Out.WriteLine("No paused goal.");
        }
        else
        {
            GoalPrinter.Print(context.Out, await agent.SetGoalAsync(args, agent.GoalDefaults.Verify, agent.GoalDefaults.MaxTurns, agent.GoalDefaults.Budget, ct));
        }
    }
}
