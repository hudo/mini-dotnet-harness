using Microsoft.Extensions.DependencyInjection;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests.CommandLine;

/// <summary>The REPL's commands, through the router, on a scripted agent.</summary>
public class SlashCommandTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Python: cli.py HELP (the text is what the user saw before the commands became classes)
    const string Help = """
/new [model]         start a fresh session, on another model if given (see mini --models)
/effort [level]      show or change the effort for the next requests
/permissions [mode]  ask, judge or yes: who approves risky calls
/sessions · /resume [id]  list sessions here · resume the latest, or one by id prefix
/goal <objective>    work until done · /goal resume|clear|status
/dream               review memory/: merge duplicates, drop what's stale
/context            how full the context is, and what fills it
/compact · /exit     summarize the history now · quit (every message is already saved)
""";

    [Fact]
    public void HelpIsBuiltFromTheRegisteredCommandsAndReadsAsItAlwaysDid() =>
        Assert.Equal(Help, ReplKit.Router().HelpText);

    [Fact]
    public async Task HelpPrintsThatText()
    {
        using var ws = new Workspace();
        using var console = new ConsoleCapture();
        var (agent, _) = ws.Make([]);

        await ReplKit.CommandAsync(ws, agent, "/help", Ct);

        Assert.Equal(Help + "\n", console.Out);
    }

    // C#-only: every command of the old switch is registered, once
    [Fact]
    public void EveryCommandIsRegisteredOnce()
    {
        var names = new ServiceCollection().AddMiniCli().BuildServiceProvider()
            .GetServices<ISlashCommand>().Select(c => c.Name).ToList();

        Assert.Equal(["/compact", "/context", "/dream", "/effort", "/goal", "/help", "/new", "/permissions", "/resume", "/sessions"], names.Order());
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    // Python: cli.py command (an unknown /word)
    [Fact]
    public async Task AnUnknownCommandSaysSo()
    {
        using var ws = new Workspace();
        using var console = new ConsoleCapture();
        var (agent, _) = ws.Make([]);

        await ReplKit.CommandAsync(ws, agent, "/nope x", Ct);

        Assert.Equal("Unknown command /nope. /help lists them.\n", console.Out);
    }

    // Python: cli.py command (anything else is a prompt)
    [Fact]
    public async Task ALineThatIsNotACommandIsAPrompt()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([Reply(Text("hi back"))]);

        await ReplKit.CommandAsync(ws, agent, "hello there", Ct);

        Assert.Single(fake.Requests);
        Assert.Equal("hi back", agent.FinalText());
    }

    // Python: cli.py command (/new: a session of its own, the agent is replaced)
    [Fact]
    public async Task NewStartsAnotherAgentOnAFreshSession()
    {
        using var ws = new Workspace();
        using var console = new ConsoleCapture();
        var (first, _) = ws.Make([]);
        var (second, _) = ws.Make([]);
        string? modelAsked = null;

        var now = await ReplKit.CommandAsync(ws, first, "/new deepseek-flash", Ct, (_, model) =>
        {
            modelAsked = model;
            return second;
        });

        Assert.Same(second, now);
        Assert.Equal("deepseek-flash", modelAsked);
        Assert.Contains($"new session {second.Session.Id}", console.Out);
    }

    // Python: cli.py command (/resume with nothing to resume)
    [Fact]
    public async Task ResumeWithNoSessionSaysSoAndKeepsTheAgent()
    {
        using var ws = new Workspace();
        using var console = new ConsoleCapture();
        var (agent, _) = ws.Make([]);

        var now = await ReplKit.CommandAsync(ws, agent, "/resume zzzz", Ct);

        Assert.Same(agent, now);
        Assert.Equal("No session found.\n", console.Out);
    }

    // Python: cli.py command (/goal clear, /goal resume with none, /goal status)
    [Fact]
    public async Task GoalSubcommands()
    {
        using var ws = new Workspace();
        using var console = new ConsoleCapture();
        var (agent, _) = ws.Make([]);

        await ReplKit.CommandAsync(ws, agent, "/goal", Ct);
        await ReplKit.CommandAsync(ws, agent, "/goal resume", Ct);
        await ReplKit.CommandAsync(ws, agent, "/goal clear", Ct);

        Assert.Equal("No goal.\nNo paused goal.\nGoal cleared.\n", console.Out);
    }
}
