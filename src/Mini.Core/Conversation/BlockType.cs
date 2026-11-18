using System.Text.Json.Nodes;

namespace Mini.Core.Conversation;

/// <summary>A content block's "type": an open set (the API adds values), so a struct around the string, not an enum.</summary>
// Python: none (C#-only: Python compares the block "type" strings inline)
public readonly record struct BlockType(string Value)
{
    public static readonly BlockType Text = new("text");
    public static readonly BlockType Thinking = new("thinking");
    public static readonly BlockType ToolUse = new("tool_use");
    public static readonly BlockType ToolResult = new("tool_result");
    public static readonly BlockType ServerToolUse = new("server_tool_use");
    public static readonly BlockType McpToolUse = new("mcp_tool_use");
    public static readonly BlockType Image = new("image");
    public static readonly BlockType Compaction = new("compaction");
    public static readonly BlockType Fallback = new("fallback");

    /// <summary>The "type" of a block; a block without one is the empty type, which matches none of the above.</summary>
    public static BlockType Of(JsonNode? block) => new(Json.Str(block?["type"]) ?? "");

    public override string ToString() => Value;
}
