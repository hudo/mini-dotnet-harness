// Python: cli.py repl, command (the agent and `new`, which the REPL passes around)
namespace Mini.Cli.Commands;

/// <summary>Starts an agent on a session: the REPL's <c>new</c> (a model of its own, or the command line's when null).</summary>
public delegate Agent AgentStarter(ISessionStore session, string? model);

/// <summary>What a slash command works on. <see cref="Agent"/> changes when <c>/new</c> or <c>/resume</c> starts another.</summary>
public sealed class ReplContext(Agent agent, SessionCatalog sessions, AgentStarter start, SlashCommandRouter router)
{
    /// <summary>The agent the next prompt goes to.</summary>
    public Agent Agent { get; set; } = agent;

    /// <summary>The sessions of this directory.</summary>
    public SessionCatalog Sessions { get; } = sessions;

    /// <summary>Starts an agent on a session, with the command line's settings.</summary>
    public AgentStarter Start { get; } = start;

    /// <summary>The commands, for <c>/help</c>.</summary>
    public SlashCommandRouter Router { get; } = router;

    /// <summary>Where the commands print.</summary>
    public TextWriter Out => Console.Out;

    /// <summary>The directory the REPL works in now.</summary>
    public string Cwd => Directory.GetCurrentDirectory();
}
