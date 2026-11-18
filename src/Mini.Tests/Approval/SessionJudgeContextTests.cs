using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Approval.ApprovalHelpers;

namespace Mini.Tests.Approval;

/// <summary>C#-only: what the judge reads of a session is read from its log, typed event by typed event.</summary>
public class SessionJudgeContextTests
{
    [Fact]
    public void TheJudgeReadsTheUsersTypedMessagesAndAnswersNewestFirstAndNothingElse()
    {
        using var ws = new Workspace();
        var store = ws.NewSession();
        Directory.CreateDirectory(Path.GetDirectoryName(store.Path)!);
        var context = new SessionJudgeContext(store, ws.Root, () => null, _ => { });
        store.Append(new MessageEvent(Message.FromText(Role.User, "first")));
        store.Append(new MessageEvent(Message.FromText(Role.User, "from the harness"), MessageEvent.Harness));
        store.Append(new MessageEvent(Message.Assistant([TextBlock.Create("the agent's own words")])));
        store.Append(new MessageEvent(Message.User([ToolResultBlock.Create("t1", "tool output a web page could steer", false)])));
        store.Append(new MessageEvent(Message.FromText(Role.System, "a note")));
        store.Append(new AnswerEvent("which?", "the second"));
        store.Append(new UsageEvent(0.5));
        store.Append(new MessageEvent(Message.FromText(Role.User, "last")));

        var seen = context.Utterances().ToList();

        Assert.Equal([(JudgeUtteranceKind.Message, "last"), (JudgeUtteranceKind.Answer, "the second"), (JudgeUtteranceKind.Message, "first")],
            seen.Select(u => (u.Kind, u.Text)));
        Assert.Equal(ws.Root, context.WorkingDirectory);
    }

    [Fact]
    public void TheJudgeSeesWhatIsWrittenAfterItWasMadeAndTheGoalComesFromTheAgent()
    {
        using var ws = new Workspace();
        var store = ws.NewSession();
        Directory.CreateDirectory(Path.GetDirectoryName(store.Path)!);
        string? objective = null;
        var context = new SessionJudgeContext(store, ws.Root, () => objective, _ => { });
        Assert.Empty(context.Utterances());

        store.Append(new MessageEvent(Message.FromText(Role.User, "later")));
        objective = "ship it";

        Assert.Single(context.Utterances());
        Assert.Equal("ship it", context.ActiveGoalObjective);
    }

    [Fact]
    public void RecordSpendAddsTheCostToTheAgentAndLogsTheVerdictAsAUsageEvent()
    {
        using var ws = new Workspace();
        var store = ws.NewSession();
        Directory.CreateDirectory(Path.GetDirectoryName(store.Path)!);
        var total = 1.0;
        var context = new SessionJudgeContext(store, ws.Root, () => null, c => total += c);
        var verdict = JsonNode.Parse("""{"choice":"allow","confidence":0.9}""")!.AsObject();

        context.RecordSpend(0.25, verdict);

        Assert.Equal(1.25, total);
        var logged = Assert.IsType<UsageEvent>(Assert.Single(store.Read()));
        Assert.Equal((0.25, verdict.ToJsonString()), (logged.Cost, logged.Judge!.ToJsonString()));
    }

    [Fact]
    public void AnAgentsJudgeReadsItsOwnSessionAndCountsItsSpend()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([]);
        agent.Transcript.AddUser("please run the tests");
        agent.Transcript.AddUser("do not touch the docs", harness: true);
        agent.Goal = new Goal("make the docs build", GoalStatus.Active);

        Assert.Equal(["please run the tests"], agent.JudgeContext.Utterances().Select(u => u.Text));
        Assert.Equal("make the docs build", agent.JudgeContext.ActiveGoalObjective);
        agent.JudgeContext.RecordSpend(0.125, new JsonObject());
        Assert.Equal(0.125, agent.Cost);
        Assert.Contains(agent.Session.Read(), e => e is UsageEvent { Judge: not null });
    }
}
