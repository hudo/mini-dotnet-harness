using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;
using static Mini.Tests.Goals.GoalHelpers;

namespace Mini.Tests.Goals;

/// <summary>The task tool: a subagent with a fresh context that returns only its final text.</summary>
public class SubagentTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Python test: test_subagent_gets_fresh_context_and_returns_only_text
    [Fact]
    public async Task SubagentGetsFreshContextAndReturnsOnlyText()
    {
        using var ws = new Workspace();
        var t = Tool("task", Json.Obj(("prompt", "count files"), ("model", "claude-haiku-5-5")));
        var (agent, fake) = ws.Make([
            Reply("tool_use", t),
            ReplyWith([Tool("bash", Json.Obj(("command", "ls")))], stop: "tool_use", model: "claude-haiku-5-5"),
            ReplyWith([Text("3 files")], model: "claude-haiku-5-5"),
            Reply(Text("the subagent says 3"))]);

        await agent.TurnAsync("how many files?", ct: Ct);

        var childFirst = fake.Requests[1];
        Assert.Equal("claude-haiku-5-5", Json.Str(childFirst["model"]));
        Assert.Equal("""[{"role":"user","content":"count files"}]""", childFirst["messages"]!.ToJsonString());
        Assert.Equal(["read", "find", "bash", "web_search"], ToolNames(childFirst));
        Assert.False(childFirst.ContainsKey("fallbacks"));
        var parentLast = fake.Requests[3];
        Assert.Equal("3 files", Json.Str(Messages(parentLast)[^1]["content"]![0]!["content"]));
        TranscriptAssert.AssertAppendOnly([fake.Requests[0], fake.Requests[3]]);
        var logs = Directory.GetFiles(Path.GetDirectoryName(agent.Session.Path)!, $"{agent.Session.Id}-sub-*.jsonl");
        Assert.Single(logs);
    }

    // Python test: test_resumed_parent_starts_a_fresh_subagent
    [Fact]
    public async Task ResumedParentStartsAFreshSubagent()
    {
        using var ws = new Workspace();
        var t1 = Tool("task", Json.Obj(("prompt", "a")));
        var t2 = Tool("task", Json.Obj(("prompt", "b"), ("model", "claude-haiku-5-5")));
        var (agent, _) = ws.Make([Reply("tool_use", t1), ReplyWith([Text("A")], model: "claude-sonnet-5-5"), Reply(Text("ok"))]);
        await agent.TurnAsync("one", ct: Ct);

        var (again, fake) = ws.Make([Reply("tool_use", t2), ReplyWith([Text("B")], model: "claude-haiku-5-5"), Reply(Text("ok"))],
            session: ws.OpenSession(agent.Session.Path));
        await again.TurnAsync("two", ct: Ct);

        var child = fake.Requests[1];
        Assert.Equal("claude-haiku-5-5", Json.Str(child["model"]));
        Assert.Equal("""[{"role":"user","content":"b"}]""", child["messages"]!.ToJsonString());
    }

    // Python test: test_failed_subagent_is_an_error_result
    [Fact]
    public async Task FailedSubagentIsAnErrorResult()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([Reply("tool_use", Tool("task", Json.Obj(("prompt", "a")))),
            new InvalidOperationException("down"), Reply(Text("ok"))]);

        await agent.TurnAsync("go", ct: Ct);

        Assert.True(Messages(fake.Requests[^1])[^1]["content"]![0]!["is_error"]!.GetValue<bool>());
    }
}
