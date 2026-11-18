using System.Text.Json.Nodes;

// Python: loop.py Agent.add, Agent.add_user (the {"role", "content"} dict)
namespace Mini.Core.Conversation;

/// <summary>
/// One transcript entry. Its content is a list of blocks, or, for the harness's own notes and the user's typed prompts, a plain
/// string: <see cref="Text"/> is set for those (and <see cref="Content"/> is empty) and the message serializes back as a string.
/// </summary>
public sealed record Message(Role Role, IReadOnlyList<ContentBlock> Content)
{
    /// <summary>The content when it is a plain string; null when it is a list of blocks. (The judge and /sessions read typed prompts through it.)</summary>
    public string? Text { get; private init; }

    /// <summary>A message whose content is a plain string.</summary>
    public static Message FromText(Role role, string text) => new(role, []) { Text = text };

    /// <summary>A user message made of blocks (tool results).</summary>
    public static Message User(IEnumerable<ContentBlock> content) => new(Role.User, content.ToList());

    /// <summary>An assistant message made of blocks, as the model returned them.</summary>
    public static Message Assistant(IEnumerable<ContentBlock> content) => new(Role.Assistant, content.ToList());

    /// <summary>The size of the images in this message's tool results, in base64 characters.</summary>
    public long ImageDataLength => Content.OfType<ToolResultBlock>().Sum(r => r.ImageDataLength);

    /// <summary>The message as the session file and the request write it: role first, then content.</summary>
    internal JsonObject ToJson() => Json.Obj(
        ("role", Role.Value),
        ("content", Text is not null ? JsonValue.Create(Text) : new JsonArray(Content.Select(b => (JsonNode)b.ToJson()).ToArray())));

    /// <summary>Reads a message from a session file. A list's non-object items are ignored, as they always were.</summary>
    internal static Message From(JsonObject json)
    {
        var role = new Role(Json.Str(json["role"]) ?? "");
        return json["content"] is JsonArray blocks
            ? new Message(role, blocks.OfType<JsonObject>().Select(ContentBlock.From).ToList())
            : FromText(role, Json.Str(json["content"]) ?? "");
    }
}
