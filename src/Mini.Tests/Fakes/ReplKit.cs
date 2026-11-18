using Microsoft.Extensions.DependencyInjection;

namespace Mini.Tests.Fakes;

/// <summary>The REPL's slash commands, registered as the program registers them, run on a given agent. It takes the place of the old
/// <c>Cli.CommandAsync</c> in the tests: the assertions are what they were, only the way they reach the command changed.</summary>
// Python test helper: tests/test_mini.py (cli.command(agent, line, new))
static class ReplKit
{
    /// <summary>The router over the registered commands.</summary>
    public static SlashCommandRouter Router() =>
        new ServiceCollection().AddMiniCli().BuildServiceProvider().GetRequiredService<SlashCommandRouter>();

    /// <summary>A context on <paramref name="agent"/>; <c>new</c> hands back the same agent unless another start is given.</summary>
    public static ReplContext Context(Workspace ws, Agent agent, AgentStarter? start = null) =>
        new(agent, ws.Runtime.Sessions, start ?? ((_, _) => agent), Router());

    /// <summary>Runs one line and returns the agent the REPL has afterwards.</summary>
    public static async Task<Agent> CommandAsync(Workspace ws, Agent agent, string line, CancellationToken ct, AgentStarter? start = null)
    {
        var context = Context(ws, agent, start);
        await context.Router.RunAsync(context, line, ct);
        return context.Agent;
    }
}
