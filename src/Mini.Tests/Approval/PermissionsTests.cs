using Mini.Tests.Fakes;
using static Mini.Tests.Approval.ApprovalHelpers;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests.Approval;

/// <summary>Permission modes, what the system prompt says about who approves, and how a change is told to the model.</summary>
public class PermissionsTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    const string YesLine = "Approval in this session (permissions yes): every tool call runs without asking, so nothing is denied.";

    static Task<Agent> Command(Workspace ws, Agent agent, string line) => ReplKit.CommandAsync(ws, agent, line, Ct);

    // Python test: test_permissions_switch
    [Fact]
    public async Task PermissionsSwitch()
    {
        using var ws = new Workspace();
        var (approver, local, _) = Judged(ws, "deny");
        var (agent, _) = ws.Make([], approver: approver);
        using var cap = new ConsoleCapture();
        await Command(ws, agent, "/permissions yes");
        Assert.StartsWith("a", (await local.BashAsync("echo a; true", ct: Ct)).Trim());
        await Command(ws, agent, "/permissions judge");
        await Assert.ThrowsAsync<ToolException>(() => local.BashAsync("echo b; true", ct: Ct));
        await Command(ws, agent, "/permissions ask");
        var e = await Assert.ThrowsAsync<ToolException>(() => local.BashAsync("echo c; true", ct: Ct));
        Assert.Contains("User denied this command. Nobody", e.Message);
    }

    // Python test: test_the_system_prompt_says_who_approves_and_the_tools_never_claim_it
    [Fact]
    public void TheSystemPromptSaysWhoApprovesAndTheToolsNeverClaimIt()
    {
        using var ws = new Workspace();
        Agent Fresh(ApprovalSettings approver, bool sub = false) => ws.Make([], approver: approver, sub: sub).Agent;

        var yes = Fresh(new ApprovalSettings(auto: true));
        Assert.Contains(YesLine, yes.System);
        var ask = Fresh(new ApprovalSettings()).System;
        Assert.Contains("(permissions ask)", ask);
        Assert.Contains("ask the user, who may say no", ask);
        var headless = Fresh(new ApprovalSettings(interactive: false, allow: ["python3 -m pytest"])).System;
        Assert.Contains("are denied, since nobody can approve", headless);
        Assert.Contains("pre-approved calls starting with: python3 -m pytest.", headless);
        var judge = Fresh(new ApprovalSettings(useJudge: true)).System;
        Assert.Contains("(permissions judge)", judge);
        Assert.Contains("reviewer model", judge);
        Assert.Contains("go to the user", judge);
        var sub = Fresh(new ApprovalSettings(auto: true), sub: true);
        Assert.Contains("nothing is denied", sub.System); // subagents are told too
        foreach (var agent in new[] { yes, Fresh(new ApprovalSettings()), Fresh(new ApprovalSettings(useJudge: true)) })
        {
            var words = agent.ToolsJson().ToJsonString(Json.Relaxed);
            Assert.DoesNotContain("ask the user", words);
            Assert.DoesNotContain("can be denied", words);
            Assert.DoesNotContain("may say no", words);
            Assert.Equal(4, PythonString.Count(words, "goes through approval") + PythonString.Count(words, "go through approval")); // read, write, bash, find
        }
        var bash = (System.Text.Json.Nodes.JsonObject)yes.ToolsJson().First(t => Json.Str(t!["name"]) == "bash")!;
        var description = Json.Str(bash["description"])!;
        Assert.Contains(OperatingSystem.IsWindows() ? "timeout is killed." : "isn't killed: it goes on in the background", description);
        Assert.DoesNotContain("&", description);
        Assert.Contains("don't add & or nohup", bash["input_schema"]!.ToJsonString(Json.Relaxed));
    }

    // Python test: test_a_permissions_change_is_a_note_and_resume_tells_the_latest
    [Fact]
    public async Task APermissionsChangeIsANoteAndResumeTellsTheLatest()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([Reply(Text("a")), Reply(Text("b"))]);
        agent.Approval.Auto = false; // as if started in ask (the settings the tools share)
        using var cap = new ConsoleCapture();
        await agent.TurnAsync("one", ct: Ct);
        await Command(ws, agent, "/permissions yes");
        await Command(ws, agent, "/permissions yes"); // no change, no second note
        await agent.TurnAsync("two", ct: Ct);
        TranscriptAssert.AssertAppendOnly(fake.Requests);
        var notes = fake.Requests[1]["messages"]!.AsArray().Where(m => Json.Str(m!["role"]) == "system").Select(m => Json.Str(m!["content"])).ToList();
        Assert.Equal([YesLine], notes);

        Agent Resume(ApprovalSettings ap) => ws.Make([], session: ws.OpenSession(agent.Session.Path), approver: ap).Agent;
        Assert.Empty(Resume(new ApprovalSettings(auto: true)).Transcript.PendingNotes); // the last word was yes, and it's yes again
        var back = Resume(new ApprovalSettings()); // started in ask, switched to yes, resumed in ask: the model must hear it's ask again
        Assert.Equal(agent.System, back.System);
        Assert.Equal(agent.ToolsJson().ToJsonString(), back.ToolsJson().ToJsonString());
        Assert.Contains("(permissions ask)", back.Transcript.PendingNotes[^1]);

        var old = ws.NewSession(); // a session from 1.0.1: no approval line anywhere
        old.Append(new MetaEvent("claude-opus-5-5", ws.Root, "old prompt", agent.ToolsJson().DeepClone().AsArray()));
        var (first, _) = ws.Make([Reply(Text("ok"))], session: old, approver: new ApprovalSettings(auto: true));
        Assert.Contains("nothing is denied", first.Transcript.PendingNotes[^1]);
        await first.TurnAsync("hi", ct: Ct);
        Assert.Empty(ws.Make([], session: ws.OpenSession(old.Path), approver: new ApprovalSettings(auto: true)).Agent.Transcript.PendingNotes);

        var (allowed, _) = ws.Make([], approver: new ApprovalSettings(allow: ["pytest"]));
        Assert.Contains("pre-approved calls starting with: pytest", allowed.System); // resumed without --allow: a prefix of that line
        var (plain, _) = ws.Make([], session: ws.OpenSession(allowed.Session.Path), approver: new ApprovalSettings());
        Assert.NotEmpty(plain.Transcript.PendingNotes);
        Assert.DoesNotContain("pre-approved", plain.Transcript.PendingNotes[^1]);
    }
}
