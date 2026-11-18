using System.Text.Json.Nodes;

// Python: loop.py Agent.track, Agent.price (the usage dict)
namespace Mini.Core.Conversation;

/// <summary>
/// A response's token usage. Like a block it keeps the JSON it came from, so the session file records the usage exactly as
/// the API sent it, unknown fields included.
/// </summary>
public sealed class Usage
{
    readonly JsonObject _json;

    internal Usage(JsonObject json) => _json = json;

    public static Usage Empty => new(new JsonObject());

    /// <summary>The response's own counts, as a slice.</summary>
    UsageEntry Self => Entry(_json);

    /// <summary>The iterations the API listed; empty when it listed none.</summary>
    public IReadOnlyList<UsageEntry> Iterations => _json["iterations"] is JsonArray list ? list.OfType<JsonObject>().Select(Entry).ToList() : [];

    /// <summary>What to price: each iteration, since each has its own model and price; with none listed, the usage itself.</summary>
    public IReadOnlyList<UsageEntry> Entries => _json["iterations"] is JsonArray { Count: > 0 } ? Iterations : [Self];

    /// <summary>The last real message: with iterations, the last one of type message or fallback_message (a compaction-only
    /// response says nothing about the conversation's size, so it has none); with no iterations, the usage itself.</summary>
    public UsageEntry? LastMessage => _json["iterations"] is JsonArray { Count: > 0 }
        ? Iterations.LastOrDefault(i => i.Type is "message" or "fallback_message")
        : Self;

    /// <summary>The server-side web searches this response ran.</summary>
    public long WebSearchRequests => Json.N((_json["server_tool_use"] as JsonObject)?["web_search_requests"]);

    /// <summary>A copy of the JSON this usage was made from.</summary>
    internal JsonObject ToJson() => (JsonObject)_json.DeepClone();

    static UsageEntry Entry(JsonObject o) => new(Json.Str(o["type"]), Json.Str(o["model"]), Json.N(o["input_tokens"]), Json.N(o["output_tokens"]),
        Json.N(o["cache_read_input_tokens"]), Json.N(o["cache_creation_input_tokens"]));
}
