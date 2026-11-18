using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests;

/// <summary>The two Python loop tests that prove the shared infrastructure (fake client, workspace, append-only check).</summary>
public class AgentLoopSmokeTests
{
    // Python test: test_tool_loop_is_append_only_and_replays_thinking
    [Fact]
    public async Task ToolLoopIsAppendOnlyAndReplaysThinking()
    {
        using var ws = new Workspace();
        ws.Write("a.txt", "hello\n");
        var t = Tool("read", Json.Obj(("path", "a.txt")));
        var (agent, fake) = ws.Make([Reply("tool_use", Thinking("s1"), t), Reply(Thinking("s2"), Text("done"))]);

        Assert.Equal("end_turn", await agent.TurnAsync("read a.txt", ct: TestContext.Current.CancellationToken));

        var r = fake.Requests;
        Assert.Equal(2, r.Count);
        TranscriptAssert.AssertAppendOnly(r);
        var assistant = r[1]["messages"]![1]!;
        Assert.Equal("""{"type":"thinking","thinking":"","signature":"s1"}""", assistant["content"]![0]!.ToJsonString());
        var result = r[1]["messages"]![2]!["content"]![0]!.AsObject();
        Assert.Equal(Json.Str(t["id"]), Json.Str(result["tool_use_id"]));
        Assert.Contains("hello", Json.Str(result["content"]));
        Assert.False(result.ContainsKey("is_error"));
    }

    // Python test: test_request_shape
    [Fact]
    public async Task RequestShape()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([Reply(Text("hi"))]);

        await agent.TurnAsync("hi", ct: TestContext.Current.CancellationToken);

        var kw = fake.Requests[0];
        Assert.Equal("claude-opus-5-5", Json.Str(kw["model"]));
        Assert.Equal("adaptive", Json.Str(kw["thinking"]!["type"]));
        Assert.Equal("""{"prefix_mismatch_behavior":"drop_block"}""", kw["thinking"]!["block_binding"]!.ToJsonString());
        Assert.Equal("""{"effort":"high"}""", kw["output_config"]!.ToJsonString());
        var betas = kw["betas"]!.AsArray().Select(b => Json.Str(b)).ToHashSet();
        Assert.Superset(new HashSet<string?> { "compact-2026-09-04", "thinking-binding-controls-2026-08-01" }, betas);
        Assert.Equal("default", Json.Str(kw["fallbacks"]));
        Assert.False(kw.ContainsKey("tool_choice"));
        var names = kw["tools"]!.AsArray().Select(x => Json.Str(x!["name"])).ToList();
        Assert.Equal(["read", "write", "edit", "bash"], names.Take(4));
        Assert.Contains("task", names);
        Assert.Contains("web_search", names);
    }
}
