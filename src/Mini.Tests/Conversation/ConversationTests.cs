using System.Text.Json.Nodes;

namespace Mini.Tests.Conversation;

/// <summary>C#-only: the typed transcript records keep what the API returned byte for byte and write the harness's own blocks in today's key order.</summary>
public class ConversationTests
{
    static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void ABlockTheApiReturnedSerializesBackByteForByteWithFieldsItHasNoPropertyFor()
    {
        const string thinking = """{"type":"thinking","signature":"sig","thinking":"hm","future_field":{"b":1,"a":[2,1]}}""";
        const string toolUse = """{"id":"toolu_1","type":"tool_use","caller":{"type":"direct"},"name":"read","input":{"path":"a.txt","z":null}}""";
        const string unknown = """{"type":"web_search_tool_result","tool_use_id":"srv_1","content":[{"url":"https://x"}]}""";

        var blocks = new[] { thinking, toolUse, unknown }.Select(j => ContentBlock.From(Parse(j))).ToList();

        Assert.Equal([thinking, toolUse, unknown], blocks.Select(b => b.ToJson().ToJsonString()));
        Assert.Equal("sig", Assert.IsType<ThinkingBlock>(blocks[0]).Signature);
        var use = Assert.IsType<ToolUseBlock>(blocks[1]);
        Assert.Equal(("toolu_1", "read", "a.txt"), (use.Id, use.Name, (string?)use.Input["path"]));
        Assert.Equal("web_search_tool_result", Assert.IsType<UnknownBlock>(blocks[2]).Type.Value);
    }

    [Fact]
    public void ABlocksJsonIsACopyNeitherSideCanChange()
    {
        var source = Parse("""{"type":"tool_use","id":"t","name":"bash","input":{"command":"ls"}}""");
        var block = Assert.IsType<ToolUseBlock>(ContentBlock.From(source));

        source["name"] = "changed";
        block.Input["command"] = "rm";
        block.ToJson()["id"] = "other";

        Assert.Equal("""{"type":"tool_use","id":"t","name":"bash","input":{"command":"ls"}}""", block.ToJson().ToJsonString());
    }

    [Fact]
    public void FactoriesWriteTheKeysInTheOrderTheHarnessAlwaysDid()
    {
        Assert.Equal("""{"type":"text","text":"hi"}""", TextBlock.Create("hi").ToJson().ToJsonString());
        Assert.Equal("""{"type":"tool_result","tool_use_id":"t1","content":"ok"}""", ToolResultBlock.Create("t1", "ok", false).ToJson().ToJsonString());
        Assert.Equal("""{"type":"tool_result","tool_use_id":"t1","content":"no","is_error":true}""", ToolResultBlock.Create("t1", "no", true).ToJson().ToJsonString());
        // repair writes is_error before content
        Assert.Equal("""{"type":"tool_result","tool_use_id":"t1","is_error":true,"content":"Not run: the session ended before this call."}""",
            ToolResultBlock.NotRun("t1").ToJson().ToJsonString());
        Assert.Equal("""{"type":"image","source":{"type":"base64","media_type":"image/png","data":"AAAA"}}""", ImageBlock.Create("image/png", "AAAA").ToJson().ToJsonString());
        var withImage = ToolResultBlock.Create("t2", [TextBlock.Create("a.png: image/png"), ImageBlock.Create("image/png", "AAAA")], false);
        Assert.Equal("""{"type":"tool_result","tool_use_id":"t2","content":[{"type":"text","text":"a.png: image/png"},{"type":"image","source":{"type":"base64","media_type":"image/png","data":"AAAA"}}]}""",
            withImage.ToJson().ToJsonString());
        Assert.Equal(4, withImage.ImageDataLength);
        Assert.Null(withImage.Text);
        Assert.Equal(2, withImage.Blocks.Count);
    }

    [Fact]
    public void AMessageKeepsPlainStringContentAsAStringAndListContentAsAList()
    {
        const string typed = """{"role":"user","content":"héllo ✓"}""";
        const string blocks = """{"role":"assistant","content":[{"type":"text","text":"ok","citations":[]}]}""";

        var plain = Message.From(Parse(typed));
        var listed = Message.From(Parse(blocks));

        Assert.Equal(typed, plain.ToJson().ToJsonString(Json.Relaxed));
        Assert.Equal(blocks, listed.ToJson().ToJsonString());
        Assert.Equal(("héllo ✓", Role.User), (plain.Text, plain.Role));
        Assert.Empty(plain.Content);
        Assert.Null(listed.Text);
        Assert.Equal("ok", Assert.IsType<TextBlock>(listed.Content[0]).Text);
    }

    [Fact]
    public void AMessageWithARoleThisCodeHasNoNameForIsKeptAsWritten()
    {
        const string json = """{"role":"developer","content":"x"}""";

        var message = Message.From(Parse(json));

        Assert.Equal(new Role("developer"), message.Role);
        Assert.Equal(json, message.ToJson().ToJsonString());
    }

    [Fact]
    public void FallbackSanitizerDropsTheDeclinedModelsUnpairedCallsAndThinkingBeforeTheBoundary()
    {
        ContentBlock B(string json) => ContentBlock.From(Parse(json));
        var blocks = new[]
        {
            B("""{"type":"thinking","thinking":"declined","signature":"a"}"""),
            B("""{"type":"text","text":"before"}"""),
            B("""{"type":"tool_use","id":"t1","name":"bash","input":{}}"""),
            B("""{"type":"server_tool_use","id":"s1","name":"web_search","input":{}}"""),
            B("""{"type":"server_tool_use","id":"s2","name":"web_search","input":{}}"""),
            B("""{"type":"web_search_tool_result","tool_use_id":"s1","content":[]}"""),
            B("""{"type":"fallback","to":{"model":"claude-x"}}"""),
            B("""{"type":"thinking","thinking":"served","signature":"b"}"""),
            B("""{"type":"text","text":"after"}"""),
        };

        var clean = FallbackSanitizer.Clean(blocks);

        Assert.Equal(["text", "server_tool_use", "web_search_tool_result", "thinking", "text"], clean.Select(b => b.Type.Value));
        Assert.Equal("s1", Assert.IsType<ServerToolUseBlock>(clean[1]).Id); // the unanswered s2 is gone
        var plain = blocks.Where(b => b is not FallbackBlock).ToList();
        Assert.Equal(plain, FallbackSanitizer.Clean(plain)); // no boundary: nothing is touched
    }

    [Fact]
    public void ModelResponseReadsTheStopReasonUsageIterationsAndTransformationsAndKeepsTheRawUsage()
    {
        var json = Parse("""
            {"id":"m","type":"message","role":"assistant","model":"claude-opus-5-5","content":[{"type":"text","text":"x"}],
             "stop_reason":"refusal","stop_details":{"category":"cyber"},
             "usage":{"input_tokens":10,"output_tokens":2,"cache_read_input_tokens":3,"cache_creation_input_tokens":4,"server_tool_use":{"web_search_requests":2},
                      "iterations":[{"type":"compaction","input_tokens":100,"output_tokens":5},{"type":"message","model":"claude-haiku-5-5","input_tokens":1,"output_tokens":2,"cache_read_input_tokens":3,"cache_creation_input_tokens":4}],"extra":true},
             "input_transformations":[{"type":"thinking_dropped","path":"messages.1.content.0","reason":"prefix_binding_mismatch"},{"reason":"other"}]}
            """);

        var response = ModelResponse.From(json);

        Assert.Equal(StopReason.Refusal, response.StopReason);
        Assert.Equal("cyber", response.RefusalCategory);
        Assert.Equal("claude-opus-5-5", response.Model);
        Assert.Equal(2, response.Usage.WebSearchRequests);
        Assert.Equal(2, response.Usage.Entries.Count); // the iterations, not the usage itself
        var last = response.Usage.LastMessage!;
        Assert.Equal(("claude-haiku-5-5", 10L, 8L), (last.Model, last.ContextTokens, last.PromptTokens));
        Assert.Equal(json["usage"]!.ToJsonString(), response.Usage.ToJson().ToJsonString());
        Assert.Equal(["messages.1.content.0", "None"], response.InputTransformations.Select(t => t.Path));
        Assert.Equal([true, false], response.InputTransformations.Select(t => t.IsPrefixBindingMismatch));
    }

    [Fact]
    public void AUsageWithoutIterationsIsItsOwnLastMessageAndACompactionOnlyOneHasNone()
    {
        var plain = ModelResponse.From(Parse("""{"stop_reason":"end_turn","usage":{"input_tokens":7,"output_tokens":1}}""")).Usage;
        var compaction = ModelResponse.From(Parse("""{"usage":{"iterations":[{"type":"compaction","input_tokens":9}]}}""")).Usage;

        Assert.Equal(8, plain.LastMessage!.ContextTokens);
        Assert.Single(plain.Entries);
        Assert.Null(compaction.LastMessage);
        Assert.Null(ModelResponse.From(Parse("{}")).StopReason);
        Assert.Equal(0, ModelResponse.From(Parse("{}")).Usage.LastMessage!.ContextTokens);
    }
}
