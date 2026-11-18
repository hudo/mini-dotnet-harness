using System.Text.Json.Nodes;

// Python: loop.py Agent.run_tools (the tool_result block), Agent.repair (the "Not run" block)
namespace Mini.Core.Conversation;

public sealed class ToolResultBlock : ContentBlock
{
    internal ToolResultBlock(JsonObject json) : base(json) { }

    public string? ToolUseId => String("tool_use_id");

    public bool IsError => Node("is_error") is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    /// <summary>The result's text when it is a plain string; null when it is a list of blocks.</summary>
    public string? Text => Json.Str(Node("content"));

    /// <summary>The result's blocks when it is a list (the read tool's text and image); empty for a plain string.</summary>
    public IReadOnlyList<ContentBlock> Blocks => Node("content") is JsonArray list
        ? list.OfType<JsonObject>().Select(From).ToList()
        : [];

    /// <summary>The size of the images in the result, in base64 characters.</summary>
    public long ImageDataLength => Blocks.OfType<ImageBlock>().Sum(i => (long)(i.Data?.Length ?? 0));

    /// <summary>The result of a call that ran: keys in the order <c>run_tools</c> writes them, <c>is_error</c> only when it failed.</summary>
    public static ToolResultBlock Create(string? toolUseId, string output, bool isError) => Make(toolUseId, JsonValue.Create(output), isError);

    /// <summary>The result of a call that returned blocks (the read tool's text and an image).</summary>
    public static ToolResultBlock Create(string? toolUseId, IEnumerable<ContentBlock> output, bool isError) =>
        Make(toolUseId, new JsonArray(output.Select(b => (JsonNode)b.ToJson()).ToArray()), isError);

    static ToolResultBlock Make(string? toolUseId, JsonNode content, bool isError)
    {
        var json = Json.Obj(("type", BlockType.ToolResult.Value), ("tool_use_id", toolUseId), ("content", content));
        if (isError) json["is_error"] = true;
        return new ToolResultBlock(json);
    }

    /// <summary>Closes a call whose session ended before it ran. <c>repair</c> writes <c>is_error</c> before <c>content</c>, unlike <see cref="Create(string?, string, bool)"/>.</summary>
    public static ToolResultBlock NotRun(string? toolUseId) =>
        new(Json.Obj(("type", BlockType.ToolResult.Value), ("tool_use_id", toolUseId),
            ("is_error", true), ("content", "Not run: the session ended before this call.")));
}
