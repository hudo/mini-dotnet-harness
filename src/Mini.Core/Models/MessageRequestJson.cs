using System.Text.Json.Nodes;

// Python: loop.py Agent.request_args, Agent.server_summary, Agent.summarize (the key order of each body)
namespace Mini.Core.Models;

/// <summary>Writes a <see cref="MessageRequest"/> as the JSON body the API takes. The key order is the one the harness always sent
/// (the request goldens pin it); a part the request does not have is left out.</summary>
// Python: none (C#-only: Python builds these dicts inline where it sends them)
internal static class MessageRequestJson
{
    /// <summary>The body of a message or compaction request. Every part is copied: a JsonNode has one parent, and the transcript stays untouched.</summary>
    internal static JsonObject Body(MessageRequest r)
    {
        var body = new JsonObject { ["model"] = r.Model };
        if (r.MaxTokens is { } max) body["max_tokens"] = max;
        if (r.System is { } system) body["system"] = SystemJson(system);
        if (r.Tools is { } tools) body["tools"] = ToolsJson(tools);
        body["messages"] = MessagesJson(r.Messages);
        if (r.ServerCompaction) body["compaction"] = Json.Obj(("type", "summarize"));
        if (r.CacheControl) body["cache_control"] = Json.Obj(("type", "ephemeral"));
        if (r.Thinking is { } thinking)
        {
            body["thinking"] = Json.Obj(("type", thinking.Type), ("display", thinking.Display),
                ("block_binding", Json.Obj(("prefix_mismatch_behavior", thinking.PrefixMismatchBehavior))));
        }

        if (r.Effort is { } effort) body["output_config"] = Json.Obj(("effort", effort));
        if (r.Betas.Count > 0) body["betas"] = new JsonArray(r.Betas.Select(b => (JsonNode)b).ToArray());
        if (r.Fallbacks is { } fallbacks) body["fallbacks"] = fallbacks;
        return body;
    }

    /// <summary>The body of a token count: the model and the messages, then the system prompt and the tools when there are any.</summary>
    internal static JsonObject CountBody(MessageRequest r)
    {
        var body = new JsonObject { ["model"] = r.Model, ["messages"] = MessagesJson(r.Messages) };
        if (r.System is { } system) body["system"] = SystemJson(system);
        if (r.Tools is { Count: > 0 } tools) body["tools"] = ToolsJson(tools);
        return body;
    }

    static JsonNode SystemJson(SystemPrompt system) => system.AsPlainString
        ? JsonValue.Create(system.Text)
        : new JsonArray(Json.Obj(("type", "text"), ("text", system.Text)));

    static JsonArray ToolsJson(IReadOnlyList<ToolDefinition> tools) => new(tools.Select(t => (JsonNode)t.ToJson()).ToArray());

    static JsonArray MessagesJson(IReadOnlyList<Message> messages) => new(messages.Select(m => (JsonNode)m.ToJson()).ToArray());
}
