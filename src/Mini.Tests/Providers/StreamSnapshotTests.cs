using System.Text.Json.Nodes;

namespace Mini.Tests.Providers;

/// <summary>
/// C#-only: the Python SDK folds the stream in accumulate_event; the C# port folds raw event JSON in StreamAccumulator, so its
/// output is characterized here by feeding it recorded-shape events and comparing what Finish() returns.
/// </summary>
public class StreamSnapshotTests
{
    static JsonObject Ev(string json) => JsonNode.Parse(json)!.AsObject();

    const string Start = """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":1,"cache_read_input_tokens":5}}}""";

    static StreamAccumulator Fold(params string[] events)
    {
        var snapshot = new StreamAccumulator();
        var ui = new OutputStreamObserver(new ConsoleAgentOutput(silent: true));
        foreach (var e in events)
            snapshot.Apply(Ev(e), ui);
        return snapshot;
    }

    static string Delta(int index, string delta) => $$"""{"type":"content_block_delta","index":{{index}},"delta":{{delta}}}""";

    static string BlockStart(int index, string block) => $$"""{"type":"content_block_start","index":{{index}},"content_block":{{block}}}""";

    static string End(string stop, string usage = """{"output_tokens":5}""") =>
        $$"""{"type":"message_delta","delta":{"stop_reason":"{{stop}}","stop_sequence":null},"usage":{{usage}}}""";

    static void AssertJson(string expected, JsonObject actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), $"expected {JsonNode.Parse(expected)!.ToJsonString()}\n but was {actual.ToJsonString()}");

    // C#-only: text pieces join in order and a citation delta lands in the block's citations
    [Fact]
    public void TextWithCitations()
    {
        var snapshot = Fold(Start,
            BlockStart(0, """{"type":"text","text":""}"""),
            Delta(0, """{"type":"text_delta","text":"Hel"}"""),
            Delta(0, """{"type":"citations_delta","citation":{"type":"web_search_result_location","url":"https://a","cited_text":"x"}}"""),
            Delta(0, """{"type":"text_delta","text":"lo"}"""),
            Delta(0, """{"type":"citations_delta","citation":{"type":"web_search_result_location","url":"https://b","cited_text":"y"}}"""),
            """{"type":"content_block_stop","index":0}""",
            End("end_turn"));

        AssertJson("""
            {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5",
             "content":[{"type":"text","text":"Hello","citations":[
               {"type":"web_search_result_location","url":"https://a","cited_text":"x"},
               {"type":"web_search_result_location","url":"https://b","cited_text":"y"}]}],
             "stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":5}}
            """, snapshot.Finish());
    }

    // C#-only: thinking text is joined; the signature arrives in its own delta and is kept
    [Fact]
    public void ThinkingWithSignature()
    {
        var snapshot = Fold(Start,
            BlockStart(0, """{"type":"thinking","thinking":"","signature":""}"""),
            Delta(0, """{"type":"thinking_delta","thinking":"let me "}"""),
            Delta(0, """{"type":"thinking_delta","thinking":"think"}"""),
            Delta(0, """{"type":"signature_delta","signature":"sig-123"}"""),
            End("end_turn"));

        var block = snapshot.Finish()["content"]![0]!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"type":"thinking","thinking":"let me think","signature":"sig-123"}"""), block));
    }

    // C#-only: tool input arrives as JSON text in pieces and is parsed once at the end
    [Fact]
    public void ToolUseWithInputJsonSplitAcrossPieces()
    {
        var snapshot = Fold(Start,
            BlockStart(0, """{"type":"tool_use","id":"toolu_1","name":"bash","input":{}}"""),
            Delta(0, """{"type":"input_json_delta","partial_json":"{\"comm"}"""),
            Delta(0, """{"type":"input_json_delta","partial_json":"and\": \"ls -"}"""),
            Delta(0, """{"type":"input_json_delta","partial_json":"la\", \"timeout\": 5}"}"""),
            End("tool_use"));

        var result = snapshot.Finish();

        Assert.Equal("tool_use", (string?)result["stop_reason"]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"type":"tool_use","id":"toolu_1","name":"bash","input":{"command":"ls -la","timeout":5}}"""),
            result["content"]![0]));
    }

    // C#-only: a call without arguments streams no JSON (or an empty piece) and keeps the starting {}
    [Fact]
    public void AToolWithoutArgumentsKeepsAnEmptyInput()
    {
        var snapshot = Fold(Start,
            BlockStart(0, """{"type":"tool_use","id":"toolu_1","name":"goal_done","input":{}}"""),
            BlockStart(1, """{"type":"tool_use","id":"toolu_2","name":"find","input":{}}"""),
            Delta(1, """{"type":"input_json_delta","partial_json":""}"""),
            End("tool_use"));

        var content = snapshot.Finish()["content"]!.AsArray();

        Assert.Equal("""{"type":"tool_use","id":"toolu_1","name":"goal_done","input":{}}""", content[0]!.ToJsonString());
        Assert.Equal("""{"type":"tool_use","id":"toolu_2","name":"find","input":{}}""", content[1]!.ToJsonString());
    }

    // C#-only: a compaction block gets its summary and encrypted content from compaction_delta
    [Fact]
    public void CompactionDelta()
    {
        var snapshot = Fold(Start,
            BlockStart(0, """{"type":"compaction","content":null}"""),
            Delta(0, """{"type":"compaction_delta","content":"The summary.","encrypted_content":"enc=="}"""),
            End("compaction"));

        var result = snapshot.Finish();

        Assert.Equal("compaction", (string?)result["stop_reason"]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"type":"compaction","content":"The summary.","encrypted_content":"enc=="}"""),
            result["content"]![0]));
    }

    // C#-only: the final hop's fallback block names the model that served the response; the block itself stays in content
    [Fact]
    public void AFallbackBlockSetsTheServingModel()
    {
        var snapshot = Fold(Start,
            BlockStart(0, """{"type":"fallback","from":{"model":"claude-opus-5-5"},"to":{"model":"claude-opus-5"}}"""),
            BlockStart(1, """{"type":"text","text":""}"""),
            Delta(1, """{"type":"text_delta","text":"hi"}"""),
            End("end_turn"));

        var result = snapshot.Finish();

        Assert.Equal("claude-opus-5", (string?)result["model"]);
        Assert.Equal("fallback", (string?)result["content"]![0]!["type"]);
        Assert.Equal("hi", (string?)result["content"]![1]!["text"]);
    }

    // C#-only: message_delta usage values overwrite message_start's (they are cumulative), an omitted one is kept, a null is
    // ignored; context_management and input_transformations are copied; a null container is not
    [Fact]
    public void MessageDeltaUsageOverwritesAndCarriesContextManagementAndTransformations()
    {
        var snapshot = Fold(Start,
            BlockStart(0, """{"type":"text","text":""}"""),
            Delta(0, """{"type":"text_delta","text":"x"}"""),
            """
            {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null,"container":null},
             "usage":{"input_tokens":12,"output_tokens":42,"cache_creation_input_tokens":3,"server_tool_use":{"web_search_requests":2},"cache_read_input_tokens":null},
             "context_management":{"applied_edits":[{"type":"clear_thinking","cleared_input_tokens":7}]},
             "input_transformations":[{"reason":"prefix_binding_mismatch","path":"messages[1]"}]}
            """);

        AssertJson("""
            {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5",
             "content":[{"type":"text","text":"x"}],
             "stop_reason":"end_turn",
             "usage":{"input_tokens":12,"output_tokens":42,"cache_read_input_tokens":5,"cache_creation_input_tokens":3,"server_tool_use":{"web_search_requests":2}},
             "context_management":{"applied_edits":[{"type":"clear_thinking","cleared_input_tokens":7}]},
             "input_transformations":[{"reason":"prefix_binding_mismatch","path":"messages[1]"}]}
            """, snapshot.Finish());
    }

    // C#-only: a delta that sets a container is kept
    [Fact]
    public void AContainerInMessageDeltaIsKept()
    {
        var snapshot = Fold(Start,
            """{"type":"message_delta","delta":{"stop_reason":"end_turn","container":{"id":"c1","expires_at":"2026-01-01T00:00:00Z"}},"usage":{"output_tokens":1}}""");

        Assert.Equal("c1", (string?)snapshot.Finish()["container"]!["id"]);
    }

    // C#-only: the token limit cut the call mid-JSON: the block stays valid with an empty input and the turn can go on
    [Fact]
    public void MaxTokensCuttingToolJsonLeavesAnEmptyInput()
    {
        var snapshot = Fold(Start,
            BlockStart(0, """{"type":"tool_use","id":"toolu_1","name":"write","input":{}}"""),
            Delta(0, """{"type":"input_json_delta","partial_json":"{\"path\": \"a.txt\", \"content\": \"half"}"""),
            End("max_tokens"));

        var result = snapshot.Finish();

        Assert.Equal("max_tokens", (string?)result["stop_reason"]);
        Assert.Equal("""{"type":"tool_use","id":"toolu_1","name":"write","input":{}}""", result["content"]![0]!.ToJsonString());
    }

    // C#-only: invalid tool JSON for any other stop reason is an error (Python parses leniently; see the shortcut note in StreamAccumulator.cs)
    [Fact]
    public void InvalidToolJsonWithoutMaxTokensThrows()
    {
        var snapshot = Fold(Start,
            BlockStart(0, """{"type":"tool_use","id":"toolu_1","name":"write","input":{}}"""),
            Delta(0, """{"type":"input_json_delta","partial_json":"{\"path\": "}"""),
            End("tool_use"));

        var e = Assert.Throws<InvalidOperationException>(snapshot.Finish);

        Assert.Contains("Unable to parse tool parameter JSON", e.Message);
        Assert.Contains("{\"path\": ", e.Message);
    }

    // C#-only: a stream with no stop reason was cut off; nothing is raised by the SDK, so Finish says so (it is retried)
    [Fact]
    public void AStreamWithNoStopReasonIsCut()
    {
        var snapshot = Fold(Start,
            BlockStart(0, """{"type":"text","text":""}"""),
            Delta(0, """{"type":"text_delta","text":"half an ans"}"""));

        Assert.Throws<StreamCutException>(snapshot.Finish);
        Assert.True(TransientErrorClassifier.IsTransient(new StreamCutException("x")));
    }

    // C#-only: no message_start at all (and events of an unknown type before it) is a cut stream too
    [Fact]
    public void AStreamWithoutAMessageIsCut()
    {
        var snapshot = Fold("""{"type":"ping"}""", """{"type":"future_event","x":1}""");

        Assert.Throws<StreamCutException>(snapshot.Finish);
    }
}
