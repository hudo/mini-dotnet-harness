using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mini.Core.Models;

/// <summary>The message so far, built from the raw events. Python's accumulate_event does this on its models; the
/// SDK's BetaMessageContentAggregator rebuilds thinking blocks from two fields, drops unknown usage fields and
/// needs message_stop, which would silently lose new beta data.</summary>
// Python: none (C#-only: Python gets the message from the SDK's stream helper)
internal sealed class StreamAccumulator
{
    JsonObject? _message;
    // text, thinking and tool input arrive in many small pieces: collect them, and set each field once at the end
    readonly Dictionary<(int Index, string Field), StringBuilder> _pieces = new();

    void Piece(int index, string field, string s)
    {
        if (!_pieces.TryGetValue((index, field), out var sb)) _pieces[(index, field)] = sb = new StringBuilder();
        sb.Append(s);
    }

    public void Apply(JsonObject ev, IStreamObserver observer)
    {
        var type = Json.Str(ev["type"]);
        if (type == "message_start")
        {
            _message = (JsonObject)ev["message"]!.DeepClone();
            _message["content"] ??= new JsonArray();
            return;
        }
        // a future event type the SDK has no model for (it drops ping itself) can arrive before the message: ignore it
        if (_message is not { } msg) return;
        var content = (JsonArray)msg["content"]!;
        switch (type)
        {
            case "content_block_start":
                var block = (JsonObject)ev["content_block"]!.DeepClone();
                content.Add(block);
                // the final hop's fallback block names the model that served the response
                if (BlockType.Of(block) == BlockType.Fallback && Json.Str(block["to"]?["model"]) is { } served) msg["model"] = served;
                break;
            case "content_block_delta":
                var index = (int)Json.N(ev["index"]);
                var target = (JsonObject)content[index]!;
                var kind = BlockType.Of(target);
                var delta = (JsonObject)ev["delta"]!;
                switch (Json.Str(delta["type"]))
                {
                    case "text_delta":
                        var text = Json.Str(delta["text"]) ?? "";
                        OwnFailure.Ours(() => observer.OnText(text));
                        if (kind == BlockType.Text) Piece(index, "text", text);
                        break;
                    case "thinking_delta":
                        var thought = Json.Str(delta["thinking"]) ?? "";
                        OwnFailure.Ours(() => observer.OnThinking(thought));
                        if (kind == BlockType.Thinking) Piece(index, "thinking", thought);
                        break;
                    case "input_json_delta":
                        // by block kind, not by an existing "input" key: a start block may omit input and still stream it
                        if (kind == BlockType.ToolUse || kind == BlockType.ServerToolUse || kind == BlockType.McpToolUse)
                            Piece(index, "partial_json", Json.Str(delta["partial_json"]) ?? "");
                        break;
                    case "citations_delta":
                        if (kind == BlockType.Text)
                        {
                            if (target["citations"] is not JsonArray citations) target["citations"] = citations = new JsonArray();
                            citations.Add(delta["citation"]?.DeepClone());
                        }
                        break;
                    case "signature_delta":
                        if (kind == BlockType.Thinking) target["signature"] = delta["signature"]?.DeepClone();
                        break;
                    case "compaction_delta":
                        if (kind == BlockType.Compaction)
                        {
                            target["content"] = delta["content"]?.DeepClone();
                            target["encrypted_content"] = delta["encrypted_content"]?.DeepClone();
                        }
                        break;
                }
                break;
            case "message_delta":
                // stop_reason, stop_sequence, stop_details; a container only when one is sent
                foreach (var (key, value) in ev["delta"]!.AsObject())
                    if (key != "container" || value is not null) msg[key] = value?.DeepClone();
                // usage counts are cumulative totals: they overwrite, and an omitted one leaves message_start's
                msg["usage"] ??= new JsonObject();
                if (ev["usage"] is JsonObject usage)
                {
                    foreach (var (key, value) in usage)
                        if (value is not null) msg["usage"]![key] = value.DeepClone();
                }
                // context_management is only sent here; input_transformations only after a mid-stream fallback
                foreach (var key in new[] { "context_management", "input_transformations" })
                    if (ev[key] is { } sent) msg[key] = sent.DeepClone();
                break;
        }
    }

    public JsonObject Finish()
    {
        // no stop reason: cut off, which raises nothing
        if (_message is not { } msg || msg["stop_reason"] is null)
            throw new StreamCutException("the stream ended before the response was complete");
        var content = (JsonArray)msg["content"]!;
        var stop = Json.Str(msg["stop_reason"]);
        foreach (var ((index, field), sb) in _pieces)
        {
            var block = (JsonObject)content[index]!;
            if (field != "partial_json")
            {
                block[field] = Json.Str(block[field]) + sb;
                continue;
            }
            if (sb.Length == 0) continue; // a tool without arguments keeps its starting input, usually {}
            var json = sb.ToString();
            try { block["input"] = JsonNode.Parse(json); }
            catch (JsonException) when (stop == StopReason.MaxTokens.Value)
            {
                // the token limit cut the call mid-JSON; phase 2 never runs it, so an empty input keeps the block valid
                block["input"] = new JsonObject();
            }
            // shortcut: parsed once at the end, so fine-grained tool streaming that leaves invalid JSON fails here,
            // where Python parses leniently on every delta; upgrade if that beta is ever turned on
            catch (JsonException e)
            {
                throw new InvalidOperationException("Unable to parse tool parameter JSON from model. Please retry your request "
                    + $"or adjust your prompt. Error: {e.Message}. JSON: {json}", e);
            }
        }
        return (JsonObject)Json.StripNulls(msg);
    }
}
