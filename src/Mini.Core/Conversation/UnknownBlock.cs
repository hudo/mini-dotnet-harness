using System.Text.Json.Nodes;

// Python: none (C#-only: Python keeps whatever block the SDK returns)
namespace Mini.Core.Conversation;

/// <summary>A block type this code has no class for (a server tool's result, a future type). It still replays exactly as returned.</summary>
public sealed class UnknownBlock : ContentBlock
{
    internal UnknownBlock(JsonObject json) : base(json) { }
}
