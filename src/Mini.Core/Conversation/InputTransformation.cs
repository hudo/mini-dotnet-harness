using System.Text.Json.Nodes;

// Python: loop.py Agent.track (the input_transformations of a response)
namespace Mini.Core.Conversation;

/// <summary>Something the API changed in the history before it answered, for instance a thinking block it dropped.</summary>
public sealed class InputTransformation
{
    readonly JsonObject _json;

    internal InputTransformation(JsonObject json) => _json = json;

    public string? Reason => Json.Str(_json["reason"]);

    /// <summary>Where in the request it happened, as text ("None" when the API gave no path).</summary>
    public string Path => _json["path"] is { } p ? Json.Str(p) ?? p.ToJsonString() : "None";

    /// <summary>The API's thinking binding found the history edited (a model switch causes this too, and is expected).</summary>
    public bool IsPrefixBindingMismatch => Reason == "prefix_binding_mismatch";

    internal JsonObject ToJson() => (JsonObject)_json.DeepClone();
}
