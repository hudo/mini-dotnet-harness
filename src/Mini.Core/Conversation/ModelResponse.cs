using System.Text.Json.Nodes;

// Python: loop.py Agent.turn, Agent.track, Agent.server_summary (what they read from a response)
namespace Mini.Core.Conversation;

/// <summary>
/// One whole response from the model, as the stream accumulator or a non-streaming call returned it. The content blocks keep
/// the JSON they came with, so appending <see cref="Content"/> to the transcript replays the response unchanged.
/// </summary>
public sealed class ModelResponse
{
    readonly JsonObject _json;

    ModelResponse(JsonObject json) => _json = json;

    /// <summary>Wraps a response's JSON (the response gets its own copy).</summary>
    public static ModelResponse From(JsonObject json) => new((JsonObject)json.DeepClone());

    /// <summary>Why the model stopped; null if the response has no stop reason.</summary>
    public StopReason? StopReason => Json.Str(_json["stop_reason"]) is { } s ? new StopReason(s) : null;

    /// <summary>The model that served the response (after a fallback, not the one asked for); null if not stated.</summary>
    public string? Model => Json.Str(_json["model"]);

    public IReadOnlyList<ContentBlock> Content => (_json["content"] as JsonArray)?.OfType<JsonObject>().Select(ContentBlock.From).ToList() ?? [];

    public Usage Usage => _json["usage"] is JsonObject u ? new Usage((JsonObject)u.DeepClone()) : Usage.Empty;

    public IReadOnlyList<InputTransformation> InputTransformations =>
        (_json["input_transformations"] as JsonArray)?.OfType<JsonObject>().Select(d => new InputTransformation((JsonObject)d.DeepClone())).ToList() ?? [];

    /// <summary>A copy of the JSON the response was made from.</summary>
    internal JsonObject ToJson() => (JsonObject)_json.DeepClone();

    /// <summary>The category of a refusal ("None" when there is none to give).</summary>
    public string RefusalCategory => Json.Str(_json["stop_details"]?["category"]) ?? "None";
}
