using System.Text.Json.Nodes;

// Python: none (C#-only: Python builds {"type": "text", "text": ...} dicts inline)
namespace Mini.Core.Conversation;

public sealed class TextBlock : ContentBlock
{
    internal TextBlock(JsonObject json) : base(json) { }

    /// <summary>The text; empty when the block has none.</summary>
    public string Text => String("text") ?? "";

    public static TextBlock Create(string text) => new(Json.Obj(("type", BlockType.Text.Value), ("text", text)));
}
