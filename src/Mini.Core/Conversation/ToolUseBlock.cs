using System.Text.Json.Nodes;

// Python: none (C#-only: Python keeps the SDK's tool_use block)
namespace Mini.Core.Conversation;

public sealed class ToolUseBlock : ContentBlock
{
    internal ToolUseBlock(JsonObject json) : base(json) { }

    public string? Id => String("id");

    public string Name => String("name") ?? "";

    /// <summary>The call's arguments: a copy, empty when the block has none. A tool may change its copy; the transcript is untouched.</summary>
    public JsonObject Input => Node("input") is JsonObject input ? (JsonObject)input.DeepClone() : new JsonObject();
}
