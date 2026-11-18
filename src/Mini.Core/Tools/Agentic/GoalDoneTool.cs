// Python: loop.py Agent.dispatch (the goal_done branch)
// Python: loop.py EXTRA_SCHEMAS["goal_done"]
namespace Mini.Core.Tools.Agentic;

/// <summary>The arguments of goal_done. A missing evidence is an empty claim.</summary>
// Python: loop.py Agent.dispatch (the "Bad arguments" text)
public sealed record GoalDoneArgs(string Evidence) : IToolArgs<GoalDoneArgs>
{
    public static GoalDoneArgs Bind(ToolArgs args) => new(Json.Str(args["evidence"]) ?? "");
}

/// <summary>Declares the active goal complete. The harness then checks the claim after the turn.</summary>
/// <param name="claim">Where the claim goes.</param>
/// <param name="goalIsActive">Whether a goal set with /goal is running.</param>
public sealed class GoalDoneTool(GoalClaimSlot claim, Func<bool> goalIsActive) : ITool
{
    // Python: loop.py EXTRA_SCHEMAS["goal_done"]
    public ToolDefinition Definition { get; } = ToolDefinition.Function("goal_done",
        """Declare the active goal complete. Only for a goal the user set with /goal: without one, don't """
        + """call it. Only call it when the goal is fully met and you have """
        + """checked it. The harness then runs the user's verify command, if any, and has a reviewer """
        + """that can't see this conversation check the work; you get their objections if they disagree.""",
        Json.Obj(("evidence", Json.Obj(("type", "string"), ("description", """What you ran and what it showed""")))),
        "evidence");

    // Python: loop.py Agent.dispatch (the goal_done branch)
    public Task<ToolOutput> InvokeAsync(ToolCall call, CancellationToken ct)
    {
        if (!goalIsActive())
            return Task.FromResult(ToolOutput.Of("No goal is active."));
        claim.Value = ToolArgs.Bind<GoalDoneArgs>(call.Input).Evidence;
        return Task.FromResult(ToolOutput.Of("Recorded. The harness will check it after this turn."));
    }
}
