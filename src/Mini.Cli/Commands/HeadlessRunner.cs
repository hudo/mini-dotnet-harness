// Python: cli.py main (the headless branch)
namespace Mini.Cli.Commands;

/// <summary>Runs one prompt, or one goal, to the end and says how it went: the answer on stdout (or one JSON line), and an exit code a script can read.</summary>
public sealed class HeadlessRunner(ConsoleCancellation cancellation)
{
    /// <summary>0: done. 1: the run (or the goal) didn't finish. 130: Ctrl-C (as Python's KeyboardInterrupt out of main).</summary>
    public async Task<int> RunAsync(Agent agent, CliOptions options)
    {
        using var run = cancellation.BeginRun();
        try
        {
            HeadlessResult result;
            if (!string.IsNullOrEmpty(options.Goal))
            {
                result = HeadlessResult.OfGoal(await agent.SetGoalAsync(options.Goal, options.Verify, options.MaxTurns, options.Budget, run.Token));
            }
            else
            {
                await agent.MaybeCompactAsync(run.Token);
                result = HeadlessResult.OfTurn(await agent.TurnAsync(options.Prompt!, ct: run.Token));
            }
            if (options.Json)
                Console.Out.WriteLine(result.ToJsonLine(agent.Session.Id, agent.Cost, agent.FinalText()));
            return result.ExitCode;
        }
        catch (OperationCanceledException) when (run.WasCancelled)
        {
            return 130;
        }
    }
}
