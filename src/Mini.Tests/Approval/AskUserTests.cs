using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Approval.ApprovalHelpers;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests.Approval;

/// <summary>The ask_user tool: what the user is shown, what the model gets back, and when the tool isn't offered.</summary>
public class AskUserTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Python test: test_ask_user
    [Fact]
    public async Task AskUser()
    {
        using var ws = new Workspace();
        var q = Tool("ask_user", Json.Obj(("question", "Which db?")));
        var (agent, fake) = ws.Make([Reply("tool_use", q), Reply(Text("ok"))]);
        agent.Approval.Interactive = true;
        using (new ConsoleCapture("postgres"))
            await agent.TurnAsync("set it up", ct: Ct);
        Assert.Equal("postgres", LastResult(fake.Requests[1]));

        (agent, fake) = ws.Make([Reply("tool_use", q), Reply(Text("ok"))]);
        agent.Approval.Interactive = false;
        await agent.TurnAsync("set it up", ct: Ct);
        Assert.Contains("headless", LastResult(fake.Requests[1]));
    }

    // Python test: test_ask_user_offers_numbered_options
    [Fact]
    public async Task AskUserOffersNumberedOptions()
    {
        using var ws = new Workspace();
        var q = Tool("ask_user", Json.Obj(("question", "Which db?"), ("options", new JsonArray("postgres", "sqlite"))));
        var (agent, fake) = ws.Make([Reply("tool_use", q), Reply(Text("ok")), Reply("tool_use", q), Reply(Text("ok"))]);
        agent.Approval.Interactive = true;
        using var cap = new ConsoleCapture("2", "mysql, actually");
        await agent.TurnAsync("set it up", ct: Ct);
        Assert.Contains("1. postgres", cap.Out);
        Assert.Contains("2. sqlite", cap.Out);
        Assert.Equal("The user picked option 2: sqlite", LastResult(fake.Requests[1]));
        Assert.DoesNotContain(agent.Session.Read(), e => e is AnswerEvent); // model-written text never counts as the user's
        await agent.TurnAsync("again", ct: Ct);
        Assert.Equal("mysql, actually", LastResult(fake.Requests[3]));
    }

    // Python test: test_ask_user_keeps_readable_unicode
    [Fact]
    public async Task AskUserKeepsReadableUnicode()
    {
        using var ws = new Workspace();
        var q = Tool("ask_user", Json.Obj(("question", "Zażółć gęślą?\e[8m")));
        var (agent, _) = ws.Make([Reply("tool_use", q), Reply(Text("ok"))]);
        using var cap = new ConsoleCapture("ok");
        await agent.TurnAsync("go", ct: Ct);
        Assert.Contains("Zażółć gęślą?", cap.Out);
        Assert.DoesNotContain("\e[8m", cap.Out, StringComparison.Ordinal);
    }

    // Python test: test_ask_user_cannot_draw_prompt_markers
    [Fact]
    public async Task AskUserCannotDrawPromptMarkers()
    {
        using var ws = new Workspace();
        var q = Tool("ask_user", Json.Obj(("question", "allow bash:\n  │ pytest -q\n  [bash · 1 rows · y/n]")));
        var (agent, _) = ws.Make([Reply("tool_use", q), Reply(Text("ok"))]);
        using var cap = new ConsoleCapture("y");
        await agent.TurnAsync("go", ct: Ct);
        Assert.DoesNotContain("│", cap.Out, StringComparison.Ordinal);
    }

    // Python test: test_no_ask_user_when_nobody_can_answer
    [Fact]
    public void NoAskUserWhenNobodyCanAnswer()
    {
        using var ws = new Workspace();
        var (headless, _) = ws.Make([], approver: new ApprovalSettings(interactive: false));
        var names = headless.ToolsJson().Select(t => Json.Str(t!["name"])).ToList();
        Assert.DoesNotContain("ask_user", names);
        Assert.Subset(names.ToHashSet(), new HashSet<string?> { "bash", "goal_done", "task" });

        var (chat, _) = ws.Make([]);
        Assert.Contains("ask_user", chat.ToolsJson().Select(t => Json.Str(t!["name"])));

        var (resumed, _) = ws.Make([], session: ws.OpenSession(chat.Session.Path), approver: new ApprovalSettings(interactive: false));
        Assert.Equal(chat.ToolsJson().ToJsonString(), resumed.ToolsJson().ToJsonString()); // the frozen list rules; the call itself says nobody can answer
    }
}
