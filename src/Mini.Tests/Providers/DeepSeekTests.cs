using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;
using static Mini.Tests.Providers.ProvidersHelpers;

namespace Mini.Tests.Providers;

/// <summary>An "other" provider (DeepSeek through its Anthropic-compatible endpoint): request shape, compaction, efforts, keys.</summary>
public class DeepSeekTests
{
    // Python test: test_deepseek_request_drops_claude_only_fields
    [Fact]
    public async Task DeepseekRequestDropsClaudeOnlyFields()
    {
        using var ws = new Workspace();
        var t = Tool("task", Json.Obj(("prompt", "count files")));
        var (agent, fake) = ws.Ds([ReplyWith([t], "tool_use", "deepseek-flash"), ReplyWith([Text("3")], model: "deepseek-flash"),
            ReplyWith([Text("done")], model: "deepseek-flash")]);

        await agent.TurnAsync("hi", ct: TestContext.Current.CancellationToken);

        var main = fake.Requests[0];
        var child = fake.Requests[1];
        foreach (var kw in new[] { main, child })
        {
            Assert.Equal("deepseek-flash", Json.Str(kw["model"]));
            Assert.False(kw.ContainsKey("thinking"));
            Assert.False(kw.ContainsKey("betas"));
            Assert.False(kw.ContainsKey("fallbacks"));
        }
        Assert.Equal("""{"effort":"high"}""", child["output_config"]!.ToJsonString()); // no medium on DeepSeek: helpers work at the lead's level
        var task = main["tools"]!.AsArray().First(x => Json.Str(x!["name"]) == "task")!;
        Assert.Equal(["prompt"], task["input_schema"]!["properties"]!.AsObject().Select(p => p.Key)); // one model, nothing to choose
        TranscriptAssert.AssertAppendOnly([fake.Requests[0], fake.Requests[2]]);
    }

    // Python test: test_deepseek_compaction_is_client_side_and_resumes
    [Fact]
    public async Task DeepseekCompactionIsClientSideAndResumes()
    {
        using var ws = new Workspace(o => o with { CompactAt = 500 });
        ws.Write("a.txt", "hi\n");
        var t = Tool("read", Json.Obj(("path", "a.txt")));
        var (agent, fake) = ws.Ds([ReplyWith([t], "tool_use", "deepseek-flash", Usage(inp: 900)),
            ReplyWith([Thinking(), Text("SUMMARY: read a.txt")], model: "deepseek-flash"),
            ReplyWith([Text("done")], model: "deepseek-flash")]);

        await agent.TurnAsync("read it", ct: TestContext.Current.CancellationToken);

        var ask = fake.Creates[0];
        Assert.False(ask.ContainsKey("compaction"));
        Assert.False(ask.ContainsKey("betas"));
        var last = ask["messages"]!.AsArray()[^1]!; // the summary request is added to the tool results, never a second user turn
        Assert.Equal("user", Json.Str(last["role"]));
        Assert.Equal("tool_result", Json.Str(last["content"]![0]!["type"]));
        var parts = last["content"]!.AsArray();
        Assert.StartsWith("Context is full", Json.Str(parts[^1]!["text"]));
        Assert.EndsWith("SUMMARY: read a.txt", agent.Messages[0].Text);
        Assert.Equal(Role.Assistant, agent.Messages[1].Role);
        Assert.StartsWith("Context was compacted", agent.Messages[2].Text);
        var loaded = SessionReplayer.Replay(agent.Session).Messages;
        Assert.Equal(agent.Messages.Select(m => m.ToJson().ToJsonString()), loaded.Select(m => m.ToJson().ToJsonString()));
    }

    // Python test: test_deepseek_failed_summary_keeps_history
    [Fact]
    public async Task DeepseekFailedSummaryKeepsHistory()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Ds([ReplyWith([Text("one")], model: "deepseek-flash"), ReplyWith([], model: "deepseek-flash")]);
        await agent.TurnAsync("hello", ct: TestContext.Current.CancellationToken);
        var before = agent.Messages.Select(m => m.ToJson().ToJsonString()).ToList();

        Assert.False(await agent.CompactAsync(TestContext.Current.CancellationToken));

        Assert.Equal(before, agent.Messages.Select(m => m.ToJson().ToJsonString()));
        Assert.Equal(agent.LastContext, agent.CompactFailedAt);
    }

    // Python test: test_effort_levels_per_provider
    [Fact]
    public void EffortLevelsPerProvider()
    {
        using var ws = new Workspace();
        var (claude, _) = ws.Make([], effort: "xhigh");
        Assert.Equal("xhigh", claude.Effort);
        Assert.Contains("medium", claude.Efforts);

        var (ds, _) = ws.Ds([], effort: "xhigh");
        Assert.Equal("high", ds.Effort);
        Assert.Equal(["low", "high", "max"], ds.Efforts);
    }

    // Python test: test_effort_switch_mid_session_keeps_history
    [Fact]
    public async Task EffortSwitchMidSessionKeepsHistory()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([Reply(Text("a")), Reply(Text("b"))]);
        var ct = TestContext.Current.CancellationToken;
        await agent.TurnAsync("one", ct: ct);

        using var console = new ConsoleCapture();
        await ReplKit.CommandAsync(ws, agent, "/effort low", ct);
        await ReplKit.CommandAsync(ws, agent, "/effort bogus", ct);
        await agent.TurnAsync("two", ct: ct);

        Assert.Equal(["high", "low"], fake.Requests.Select(r => Json.Str(r["output_config"]!["effort"])));
        TranscriptAssert.AssertAppendOnly(fake.Requests);
        Assert.Contains("has no effort bogus", console.Out);
    }

    // Python test: test_deepseek_without_its_key_never_uses_the_anthropic_key
    [Fact]
    public void DeepseekWithoutItsKeyNeverUsesTheAnthropicKey()
    {
        using var ws = new Workspace();
        using (new EnvScope(("DEEPSEEK_API_KEY", null), ("ANTHROPIC_API_KEY", "sk-ant-secret")))
        {
            var s = ws.NewSession();
            var e = Assert.Throws<MissingCredentialsException>(() =>
                ws.Open(s, ws.RealSetup("deepseek-flash")));
            Assert.Contains("DEEPSEEK_API_KEY", e.Message);
            Assert.False(File.Exists(s.Path)); // nothing written

            using var withKey = new EnvScope(("DEEPSEEK_API_KEY", "ds-key"));
            var agent = ws.Open(s, ws.RealSetup("deepseek-flash"));
            var api = Assert.IsType<AnthropicModelClient>(agent.Client);
            Assert.Equal("ds-key", api.Client.ApiKey);
            Assert.Contains("deepseek", api.Client.BaseUrl.ToString());
        }
    }

    // C#-only: the Python test only checks the key; the same guard also covers Claude's own key, and a model entry
    // without key/base_url settings. AnthropicClientFactory.Create is the one place that builds clients.
    [Fact]
    public void ClaudeNeedsItsOwnKeyToo()
    {
        using var env = new EnvScope(("ANTHROPIC_API_KEY", null), ("ANTHROPIC_AUTH_TOKEN", null), ("DEEPSEEK_API_KEY", "ds-key"));
        using var ws = new Workspace();
        var s = ws.NewSession();

        var e = Assert.Throws<MissingCredentialsException>(() => ws.Open(s, ws.RealSetup()));

        Assert.Contains("ANTHROPIC_API_KEY", e.Message);
        Assert.False(File.Exists(s.Path));
    }
}
