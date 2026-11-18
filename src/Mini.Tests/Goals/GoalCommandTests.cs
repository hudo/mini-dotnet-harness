using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests.Goals;

/// <summary>The REPL's /goal command.</summary>
public class GoalCommandTests
{
    // Python test: test_repl_goal_uses_command_line_limits
    [Fact]
    public async Task ReplGoalUsesCommandLineLimits()
    {
        using var ws = new Workspace();
        using var console = new ConsoleCapture();
        var (agent, _) = ws.Make([
            Reply("tool_use", Tool("goal_done", Json.Obj(("evidence", "e")))), Reply(Text("done")),
            Reply(Text("BLOCKED: tests fail"))]);
        agent.GoalDefaults = new GoalLimits("false", 7, null);

        await ReplKit.CommandAsync(ws, agent, "/goal make it pass", TestContext.Current.CancellationToken);

        Assert.Equal("false", agent.Goal!.Verify);
        Assert.Equal(7, agent.Goal.MaxTurns);
        Assert.Equal(GoalStatus.Paused, agent.Goal.Status);
        Assert.Contains("Goal [paused] make it pass", console.Out);
    }
}
