using System.Text.Json.Nodes;
using Mini.Tests.Fakes;

namespace Mini.Tests.Providers;

/// <summary>C#-only: the request builder, its JSON writer and the provider profiles, without an Agent. The request goldens pin the whole body of a turn; these pin the parts.</summary>
public class MessageRequestTests
{
    static MessageRequestBuilder Basic() => new MessageRequestBuilder().Model("m").Messages([Message.FromText(Role.User, "hi")]);

    static List<string> Keys(JsonObject o) => o.Select(kv => kv.Key).ToList();

    // C#-only: a request has the keys it was given, in the order the harness always sent them, whatever order they were set in
    [Fact]
    public void KeysComeInTheOrderOfTheHarnessNotOfTheCalls()
    {
        var request = Basic().Fallbacks("default").Betas(["b1"]).Effort("high").Thinking(ThinkingOptions.Adaptive).CacheControl()
            .Tools([]).System("sys").MaxTokens(5).Build();

        Assert.Equal(["model", "max_tokens", "system", "tools", "messages", "cache_control", "thinking", "output_config", "betas", "fallbacks"],
            Keys(MessageRequestJson.Body(request)));
    }

    // C#-only: the server compaction request has no thinking, effort or fallbacks, and compaction comes before cache_control
    [Fact]
    public void ServerCompactionRequestKeys()
    {
        var request = Basic().MaxTokens(16000).System("sys").Tools([]).ServerCompaction().CacheControl().Betas(AnthropicProfile.Betas).Build();

        var body = MessageRequestJson.Body(request);

        Assert.Equal(["model", "max_tokens", "system", "tools", "messages", "compaction", "cache_control", "betas"], Keys(body));
        Assert.Equal("""{"type":"summarize"}""", body["compaction"]!.ToJsonString());
    }

    // C#-only: the summary request's system is the bare string; every other request's is one text block
    [Fact]
    public void SystemIsABlockUnlessAskedForAsAString()
    {
        Assert.Equal("""[{"type":"text","text":"sys"}]""", MessageRequestJson.Body(Basic().System("sys").Build())["system"]!.ToJsonString());
        Assert.Equal("\"sys\"", MessageRequestJson.Body(Basic().SystemAsString("sys").Build())["system"]!.ToJsonString());
    }

    // C#-only: a token count sends the model and messages, then the system prompt and the tools only when there are some
    [Fact]
    public void TokenCountBodyLeavesOutWhatIsNotThere()
    {
        Assert.Equal(["model", "messages"], Keys(MessageRequestJson.CountBody(Basic().MaxTokens(3).CacheControl().Build())));
        var tool = new ToolDefinition("t", Json.Obj(("name", "t")));
        Assert.Equal(["model", "messages", "system", "tools"], Keys(MessageRequestJson.CountBody(Basic().System("s").Tools([tool]).Build())));
        Assert.Equal(["model", "messages", "system"], Keys(MessageRequestJson.CountBody(Basic().System("s").Tools([]).Build())));
    }

    // C#-only: a request without a model or messages is a bug of the caller, not something to send
    [Fact]
    public void ModelAndMessagesAreRequired()
    {
        Assert.Throws<InvalidOperationException>(() => new MessageRequestBuilder().Messages([]).Build());
        Assert.Throws<InvalidOperationException>(() => new MessageRequestBuilder().Model("m").Build());
    }

    // C#-only: the request copies what it serializes, so the same request can be written twice (a retry) and the tools stay whole
    [Fact]
    public void WritingARequestTwiceGivesTheSameJsonAndLeavesTheToolsAlone()
    {
        var schema = Json.Obj(("name", "t"), ("strict", true));
        var request = Basic().Tools([new ToolDefinition("t", schema)]).Build();

        var first = MessageRequestJson.Body(request).ToJsonString();
        var second = MessageRequestJson.Body(request).ToJsonString();

        Assert.Equal(first, second);
        Assert.Null(schema.Parent);
    }

    // C#-only: Claude's profile adds thinking and both betas; fallbacks add the key and the third beta, at the end
    [Fact]
    public void ClaudeProfileShapesThinkingBetasAndFallbacks()
    {
        var plain = Basic();
        new AnthropicProfile(fallbacks: false).Shape(plain);
        var withFallbacks = Basic();
        new AnthropicProfile(fallbacks: true).Shape(withFallbacks);

        var a = MessageRequestJson.Body(plain.Build());
        var b = MessageRequestJson.Body(withFallbacks.Build());

        Assert.Equal(["model", "messages", "thinking", "betas"], Keys(a));
        Assert.Equal(["compact-2026-09-04", "thinking-binding-controls-2026-08-01"], a["betas"]!.AsArray().Select(x => (string)x!));
        Assert.Equal(["model", "messages", "thinking", "betas", "fallbacks"], Keys(b));
        Assert.Equal("server-side-fallback-2026-07-01", (string)b["betas"]!.AsArray()[^1]!);
        Assert.Equal("default", (string)b["fallbacks"]!);
    }

    // C#-only: another provider's profile adds nothing; it asks the model for a summary and cannot count tokens
    [Fact]
    public void CompatibleProfileAddsNothingAndSummarizesByAsking()
    {
        var builder = Basic();
        CompatibleProfile.Instance.Shape(builder);

        Assert.Equal(["model", "messages"], Keys(MessageRequestJson.Body(builder.Build())));
        Assert.Equal(CompactionMode.Summary, CompatibleProfile.Instance.Compaction);
        Assert.False(CompatibleProfile.Instance.CanCountTokens);
        Assert.Equal(CompactionMode.Server, new AnthropicProfile(false).Compaction);
        Assert.True(new AnthropicProfile(false).CanCountTokens);
    }

    // C#-only: the profile is chosen from the model's entry in models.json
    [Fact]
    public void TheProfileFollowsTheModelsEndpoint()
    {
        using var ws = new Workspace();
        var catalog = ws.Runtime.Catalog;

        Assert.IsType<AnthropicProfile>(IProviderProfile.For(null, catalog.For("claude-opus-5-5")));
        Assert.Same(CompatibleProfile.Instance, IProviderProfile.For(catalog.Other["deepseek-flash"], catalog.For("deepseek-flash")));
    }
}
