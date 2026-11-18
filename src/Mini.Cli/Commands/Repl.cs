using static Mini.Cli.Terminal.AnsiColors;

// Python: cli.py repl
namespace Mini.Cli.Commands;

/// <summary>The interactive loop: a prompt, a line, a command or a turn, and back.</summary>
public sealed class Repl(SlashCommandRouter router, ConsoleCancellation cancellation)
{
    /// <summary>Runs until <c>/exit</c> or end of input.</summary>
    public async Task RunAsync(Agent agent, SessionCatalog sessions, AgentStarter start)
    {
        var context = new ReplContext(agent, sessions, start, router);
        Console.Out.WriteLine($"{Dim}mini · {agent.Model} · permissions {agent.Approval.Mode.ToCliName()} · session {agent.Session.Id} · /help for commands{Off}");
        while (true)
        {
            Console.Out.Write($"\n{Cya}›{Off} ");
            Console.Out.Flush();
            string? line;
            using (cancellation.EnterPrompt())
                line = Console.ReadLine()?.Trim();
            if (line is null)
            {
                Console.Out.WriteLine();
                return;
            }
            if (line == "/exit") return;
            if (line.Length == 0) continue; // an empty Enter just asks again
            using var run = cancellation.BeginRun();
            try
            {
                await router.RunAsync(context, line, run.Token);
            }
            catch (OperationCanceledException) when (run.WasCancelled)
            {
                Console.Out.WriteLine($"{Yel}! interrupted{Off}");
            }
            catch (Exception e) // keep the REPL alive; the session file is still valid
            {
                Console.Out.WriteLine($"{Red}! {e.GetType().Name}: {e.Message}{Off}");
            }
        }
    }
}
