using System.Text.Json.Nodes;

// Python: none (C#-only: Python keeps the SDK's thinking block)
namespace Mini.Core.Conversation;

public sealed class ThinkingBlock : ContentBlock
{
    internal ThinkingBlock(JsonObject json) : base(json) { }

    public string Thinking => String("thinking") ?? "";

    /// <summary>What binds the thought to the model that wrote it; null if the block has none.</summary>
    public string? Signature => String("signature");
}
