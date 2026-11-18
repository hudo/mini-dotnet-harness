using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;
using static Mini.Tests.Goals.GoalHelpers;

namespace Mini.Tests.Goals;

/// <summary>/goal mode: the loop, its stops, persistence across resume, and the system notices around it.</summary>
public class GoalTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static JsonObject GoalDone(string evidence) => Tool("goal_done", Json.Obj(("evidence", evidence)));

    // Python test: test_goal_loop_verifies_before_accepting
    [Fact]
    public async Task GoalLoopVerifiesBeforeAccepting()
    {
        using var ws = new Workspace();
        var script = new object[]
        {
            Reply("tool_use", GoalDone("trust me")), Reply(Text("claimed")),
            Reply("tool_use", Tool("write", Json.Obj(("path", "done.txt"), ("content", "x")))),
            Reply("tool_use", GoalDone("wrote done.txt")), Reply(Text("now done")),
            ReplyWith([Text("done.txt exists.\nVERDICT: DONE")], model: "claude-sonnet-5-5"), // the reviewer
        };
        var (agent, fake) = ws.Make(script);

        var g = await SetGoal(agent, "create done.txt", verify: "test -f done.txt");

        Assert.Equal(GoalStatus.Done, g.Status);
        Assert.Equal(2, g.Turns);
        Assert.True(File.Exists(ws.Path("done.txt")));
        var first = Messages(fake.Requests[0]);
        Assert.Equal("user", Json.Str(first[0]["role"]));
        Assert.Equal("system", Json.Str(first[1]["role"]));
        Assert.Contains("Goal mode is on", Json.Str(first[1]["content"]));
        var retry = StringUserMessages(fake.Requests[2]);
        Assert.Contains("failed", Json.Str(retry[^1]["content"]));
        TranscriptAssert.AssertAppendOnly(fake.Requests[..^1]); // the last request is the reviewer's own conversation
    }

    // Python test: test_goal_stops_when_blocked_and_on_turn_limit
    [Fact]
    public async Task GoalStopsWhenBlockedAndOnTurnLimit()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([Reply(Text("Need input.\nBLOCKED: which file?"))]);
        Assert.Equal(GoalStatus.Paused, (await SetGoal(agent, "do it")).Status);

        var (agent2, _) = ws.Make([Reply(Text("working")), Reply(Text("still working"))]);
        var g = await SetGoal(agent2, "do it", maxTurns: 2);

        Assert.Equal(GoalStatus.Paused, g.Status);
        Assert.Contains("turn limit", g.Note);
    }

    // Python test: test_goal_state_survives_resume
    [Fact]
    public async Task GoalStateSurvivesResume()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([Reply(Text("BLOCKED: ?"))]);
        await SetGoal(agent, "ship it");

        var (again, _) = ws.Make([], session: ws.OpenSession(agent.Session.Path));

        Assert.Equal("ship it", again.Goal!.Objective);
        Assert.Equal(GoalStatus.Paused, again.Goal.Status);
    }

    // Python test: test_goal_rules_restated_on_resume_after_compaction
    [Fact]
    public async Task GoalRulesRestatedOnResumeAfterCompaction()
    {
        using var ws = new Workspace();
        var block = Json.Obj(("type", "compaction"), ("content", "S"), ("signature", "c"));
        var (agent, fake) = ws.Make([Reply(Text("BLOCKED: which?")), Reply("compaction", block),
            Reply("tool_use", GoalDone("e")), Reply(Text("done")), Reply(Text("VERDICT: DONE"))]);
        await SetGoal(agent, "ship it");
        await agent.CompactAsync(Ct);

        var g = await agent.ResumeGoalAsync(Ct);

        Assert.Equal(GoalStatus.Done, g.Status);
        var sys = SystemMessages(fake.Requests[^2]);
        Assert.NotEmpty(sys);
        Assert.Contains("ship it", Json.Str(sys[^1]["content"]));
    }

    // Python test: test_cleared_goal_is_not_restored
    [Fact]
    public async Task ClearedGoalIsNotRestored()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([Reply(Text("BLOCKED: ?"))]);
        await SetGoal(agent, "x");
        agent.ClearGoal();

        var (again, _) = ws.Make([], session: ws.OpenSession(agent.Session.Path));

        Assert.Null(again.Goal);
    }

    // Python test: test_goal_mode_is_switched_off_when_it_stops
    [Fact]
    public async Task GoalModeIsSwitchedOffWhenItStops()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([Reply(Text("BLOCKED: ?")), Reply(Text("hi"))]);
        await SetGoal(agent, "x");

        await agent.TurnAsync("unrelated", ct: Ct);

        Assert.Contains("Goal mode is off", Json.Str(SystemMessages(fake.Requests[^1])[^1]["content"]));
    }

    // Python test: test_goal_off_notice_survives_restart
    [Fact]
    public async Task GoalOffNoticeSurvivesRestart()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([Reply(Text("BLOCKED: ?"))]);
        await SetGoal(agent, "ship it");
        var (again, fake) = ws.Make([Reply(Text("hi"))], session: ws.OpenSession(agent.Session.Path));

        await again.TurnAsync("unrelated", ct: Ct);

        Assert.Contains("Goal mode is off", Json.Str(SystemMessages(fake.Requests[^1])[^1]["content"]));
    }

    // Python test: test_goal_left_active_by_a_killed_process_is_paused_on_resume
    [Fact]
    public async Task GoalLeftActiveByAKilledProcessIsPausedOnResume()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([]);
        agent.Goal = PausedGoal("x", status: GoalStatus.Active, turns: 1);
        agent.Parts.Goals.Save();
        agent.Transcript.AddUser("go");
        agent.Transcript.Add(Message.FromText(Role.System, "Goal mode is on. Objective: x"));

        var (again, fake) = ws.Make([Reply(Text("hi"))], session: ws.OpenSession(agent.Session.Path));

        Assert.Equal(GoalStatus.Paused, again.Goal!.Status);
        await again.TurnAsync("chat", ct: Ct);
        Assert.Contains("Goal mode is off", Json.Str(SystemMessages(fake.Requests[^1])[^1]["content"]));
    }

    // Python test: test_goal_rules_win_over_an_earlier_off_notice
    [Fact]
    public async Task GoalRulesWinOverAnEarlierOffNotice()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([Reply(Text("hi"))]);
        agent.Goal = PausedGoal("x");
        agent.Transcript.PendingNotes.Clear();
        agent.Transcript.PendingNotes.AddRange([agent.Parts.Goal.Rules(), "Goal mode is off (status: paused).", agent.Parts.Goal.Rules()]);

        await agent.TurnAsync("go", ct: Ct);

        var content = Json.Str(Messages(fake.Requests[0])[1]["content"])!;
        Assert.EndsWith(agent.Parts.Goal.Rules().TrimEnd(), content.TrimEnd());
    }

    // Python test: test_goal_needs_a_fresh_reviewer_to_agree
    [Fact]
    public async Task GoalNeedsAFreshReviewerToAgree()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([
            Reply("tool_use", GoalDone("all good")), Reply(Text("claimed")),
            Reply(Text("README.md is missing.\nVERDICT: NOT DONE")), // reviewer, fresh context
            Reply("tool_use", GoalDone("added README.md")), Reply(Text("again")),
            Reply(Text("VERDICT: DONE"))]);

        var g = await SetGoal(agent, "write a README");

        Assert.Equal(GoalStatus.Done, g.Status);
        Assert.Equal(2, g.Turns);
        var review = fake.Requests[2];
        Assert.StartsWith("An agent says it met the goal", Json.Str(review["messages"]![0]!["content"]));
        Assert.Single(review["messages"]!.AsArray());
        Assert.DoesNotContain("goal_done", ToolNames(review));
        var worker = StringUserMessages(fake.Requests[3]);
        Assert.Contains("README.md is missing", Json.Str(worker[^1]["content"]));
        TranscriptAssert.AssertAppendOnly([fake.Requests[0], fake.Requests[1], fake.Requests[3], fake.Requests[4]]);
    }

    // Python test: test_reviewer_verdict_is_the_exact_last_line
    [Theory]
    [InlineData("**VERDICT: DONE**", true)]
    [InlineData("all fine\nVerdict: done", true)]
    [InlineData("VERDICT: NOT DONE: it would be VERDICT: DONE once migrated", false)]
    [InlineData("VERDICT: DONE\nbut the README is missing", false)]
    public async Task ReviewerVerdictIsTheExactLastLine(string review, bool ok)
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([Reply("tool_use", GoalDone("e")), Reply(Text("x")), Reply(Text(review))]);

        var g = await SetGoal(agent, "ship", maxTurns: 1);

        Assert.Equal(ok, g.Status == GoalStatus.Done);
    }

    // Python test: test_a_failing_reviewer_pauses_the_goal_and_its_cost_counts
    // The Python test replaces run_subagent; C# has no seam, so the reviewer fails for real: its reply is a refusal,
    // which makes run_subagent raise ToolException("The subagent stopped (refusal)") exactly as the stub did.
    [Fact]
    public async Task AFailingReviewerPausesTheGoalAndItsCostCounts()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([Reply("tool_use", GoalDone("e")), Reply(Text("x")), Reply("refusal")]);

        var g = await SetGoal(agent, "ship");

        Assert.Equal(GoalStatus.Paused, g.Status);
        Assert.StartsWith("reviewer failed", g.Note);
        Assert.Equal(1, g.Turns);
        var logged = agent.Session.Read().OfType<UsageEvent>().Where(e => e.Subagent is not null).ToList();
        Assert.Single(logged); // the failed reviewer's cost was still added and recorded
        Assert.True(agent.Cost > 0);
        Assert.Equal(agent.Cost, g.Spent, 9);
    }

    // Python test: test_reviewer_is_told_the_verify_command_passed
    [Fact]
    public async Task ReviewerIsToldTheVerifyCommandPassed()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([Reply("tool_use", GoalDone("e")), Reply(Text("x")), Reply(Text("VERDICT: DONE"))]);

        await SetGoal(agent, "ship", verify: "true");

        Assert.Contains("already ran `true` and it passed", Json.Str(fake.Requests[2]["messages"]![0]!["content"]));
    }

    // Python test: test_a_goal_pauses_when_replies_keep_hitting_the_output_cap
    [Fact]
    public async Task AGoalPausesWhenRepliesKeepHittingTheOutputCap()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make(Enumerable.Range(0, 3).Select(_ => (object)Reply("max_tokens", Thinking())));

        var g = await SetGoal(agent, "prove it", maxTurns: 5);

        Assert.Equal(GoalStatus.Paused, g.Status);
        Assert.Equal("max_tokens", g.Note);
        Assert.Equal(3, fake.Requests.Count);
        TranscriptAssert.AssertAppendOnly(fake.Requests);
    }

    // Python test: test_the_goal_verify_command_leaves_nothing_running
    // The Python command records only the shell's pid; here it also records the `&` job's, which is the thing that must be gone.
    [Fact]
    public async Task TheGoalVerifyCommandLeavesNothingRunning()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([Reply(Text("VERDICT: DONE"))]);
        agent.Goal = new Goal("x", verify: "echo $$ > shell; sleep 30 & echo $! > job; true");
        agent.GoalClaim.Value = "done";

        var (ok, _) = await agent.Parts.Verifier.VerifyAsync(Ct);

        Assert.True(ok); // it passed, and the server-like job it left with & is gone after every check
        Assert.True(await Gone(int.Parse((await File.ReadAllTextAsync(ws.Path("shell"), Ct)).Trim())));
        Assert.True(await Gone(int.Parse((await File.ReadAllTextAsync(ws.Path("job"), Ct)).Trim())));
    }

    // Python test: test_the_goal_verify_command_is_still_killed_at_its_timeout
    // Python shrinks the 600 s by wrapping run_shell; here it is AgentOptions.GoalVerifyTimeoutSeconds. Python also asserts the call had no `keep`:
    // the command is not kept as a job, so no log is left in jobs/ and nothing is registered.
    [Fact]
    public async Task TheGoalVerifyCommandIsStillKilledAtItsTimeout()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX process groups");
        using var ws = new Workspace();
        var runtime = new TestRuntime(agentOptions: new AgentOptions { GoalVerifyTimeoutSeconds = 1 });
        var (agent, _) = ws.Make([], runtime: runtime);
        var logsBefore = Logs(runtime);
        agent.Goal = new Goal("x", verify: "echo $$ > shell; sleep 30 & sleep 30");
        agent.GoalClaim.Value = "done";

        var (ok, why) = await agent.Parts.Verifier.VerifyAsync(Ct);

        Assert.False(ok);
        Assert.Contains("timed out after 1s", why);
        Assert.True(await Gone(int.Parse((await File.ReadAllTextAsync(ws.Path("shell"), Ct)).Trim())));
        Assert.Empty(runtime.Jobs.Running);
        Assert.Equal(logsBefore, Logs(runtime)); // no log is left (other tests share the folder, so compare with before)
    }

    static string[] Logs(TestRuntime runtime) =>
        Directory.Exists(runtime.Paths.JobsDir) ? [.. Directory.GetFileSystemEntries(runtime.Paths.JobsDir).Order()] : [];

    // C#-only: the default is Python's 600
    [Fact]
    public void TheGoalVerifyTimeoutIsSixHundredSecondsByDefault() => Assert.Equal(600, new AgentOptions().GoalVerifyTimeoutSeconds);

    // Python test: test_subagents_can_be_switched_off_but_the_goal_reviewer_stays
    [Fact]
    public void SubagentsCanBeSwitchedOffButTheGoalReviewerStays()
    {
        using var ws = new Workspace();
        {
            var (agent, _) = ws.Make([], runtime: new TestRuntime(o => o with { NoTask = true }));
            var names = agent.ToolsJson().Select(t => Json.Str(t!["name"])).ToList();
            Assert.DoesNotContain("task", names);
            Assert.DoesNotContain("`task`", agent.System);
            Assert.Contains("goal_done", names); // the harness, not the model, starts the reviewer
        }
        var (again, _) = ws.Make([]);
        Assert.Contains("task", again.ToolsJson().Select(t => Json.Str(t!["name"])));
        Assert.Contains("`task`", again.System);
    }
}
