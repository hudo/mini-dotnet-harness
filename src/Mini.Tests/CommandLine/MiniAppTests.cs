using Microsoft.Extensions.DependencyInjection;
using Mini.Core;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests.CommandLine;

/// <summary>The whole command line, from argv to the exit code: <c>MiniApp.RunAsync</c> on the program's own services, with the model client swapped for a scripted one.</summary>
public class MiniAppTests
{
    static async Task<(int Exit, string Out)> RunAsync(Workspace ws, FakeModelClient model, params string[] argv)
    {
        using var console = new ConsoleCapture(); // nothing here may read the real stdin
        var services = new ServiceCollection().AddSingleton<TimeProvider>(ws.Runtime.Time).AddMiniCore(ws.Runtime.Options, Path.Combine(AppContext.BaseDirectory, "models.json"))
            .AddMiniCli()
            .AddSingleton<IAgentFactory>(new FakeClientAgentFactory(ws.Runtime.Factory, model))
            .BuildServiceProvider();
        await using (services)
            return (await MiniApp.RunAsync(argv, services), console.Out);
    }

    static object[] GoalDoneThenVerdict(string verdict) =>
    [
        Reply("tool_use", Tool("goal_done", Json.Obj(("evidence", "it is")))), Reply(Text("claimed")),
        Reply(Text($"checked.\nVERDICT: {verdict}")), // the reviewer
    ];

    /// <summary>Starts nothing: records the setup the command line chose, then fails, so no REPL runs and nothing reads the terminal.</summary>
    sealed class RecordingFactory : IAgentFactory
    {
        public List<AgentSetup> Setups { get; } = [];
        public bool CanResume(ISessionStore session) => false;
        public Agent CreateNew(ISessionStore session, AgentSetup setup)
        {
            Setups.Add(setup);
            throw new InvalidOperationException("stub agent");
        }
        public Agent Resume(ISessionStore session, AgentSetup setup) => throw new InvalidOperationException("never resumed");
    }

    // Python test: test_judge_is_the_default_only_when_its_key_is_set_and_someone_watches
    // Python stubs Approver, repl and Agent and reads Approver's use_judge; here the factory records the approval settings the command line built.
    [Theory]
    [InlineData("k", new string[0], true)]
    [InlineData("", new string[0], false)]
    [InlineData("k", new[] { "-p", "hi" }, false)]
    [InlineData("k", new[] { "--permissions", "ask" }, false)]
    public async Task JudgeIsTheDefaultOnlyWhenItsKeyIsSetAndSomeoneWatches(string key, string[] argv, bool judge)
    {
        using var ws = new Workspace(o => o with { OpenRouterApiKey = key });
        using var console = new ConsoleCapture(); // the REPL is never reached, and nothing reads the real stdin
        var factory = new RecordingFactory();
        await using var services = new ServiceCollection().AddSingleton<TimeProvider>(ws.Runtime.Time)
            .AddMiniCore(ws.Runtime.Options, Path.Combine(AppContext.BaseDirectory, "models.json")).AddMiniCli().AddSingleton<IAgentFactory>(factory).BuildServiceProvider();
        var command = services.GetRequiredService<MiniCommand>();

        var exit = await RootCommandFactory.InvokeAsync(argv, command.ExecuteAsync); // the stub can't run a prompt; only the approval settings matter here

        Assert.Equal(1, exit);
        Assert.Contains("stub agent", console.Err);
        var approval = Assert.Single(factory.Setups).Approval;
        Assert.Equal(judge, approval.UseJudge);
        Assert.Equal(!argv.Contains("-p"), approval.Interactive); // headless: nobody to ask
    }

    // Python test: test_headless_goal_exit_code_says_whether_it_was_done
    // Python stubs the agent's set_goal to return "done", then "paused"; here the scripted model gets there for real.
    [Fact]
    public async Task AHeadlessGoalExitCodeSaysWhetherItWasDone()
    {
        using var ws = new Workspace();

        var done = await RunAsync(ws, new FakeModelClient(GoalDoneThenVerdict("DONE")), "--goal", "fix it", "--json");
        Assert.Equal(0, done.Exit);
        Assert.Contains("\"status\": \"done\"", done.Out);

        var paused = await RunAsync(ws, new FakeModelClient([Reply(Text("Need input.\nBLOCKED: which file?"))]), "--goal", "fix it", "--json");
        Assert.Equal(1, paused.Exit);
        Assert.Contains("\"status\": \"paused\"", paused.Out);
    }

    // Python test: test_a_refused_headless_run_exits_non_zero
    [Fact]
    public async Task ARefusedHeadlessRunExitsNonZero()
    {
        using var ws = new Workspace();

        var refused = await RunAsync(ws, new FakeModelClient([Reply("refusal", Text("no"))]), "-p", "do it", "--json"); // a script must not read "refused" as done

        Assert.Equal(1, refused.Exit);
        Assert.Contains("\"stop\": \"refusal\"", refused.Out);
    }
}
