using System.Text.Json.Nodes;

// Python: none (C#-only: Python keeps the SDK's fallback block)
namespace Mini.Core.Conversation;

/// <summary>Marks where a refusing model was replaced by another one mid-reply (see <see cref="FallbackSanitizer"/>).</summary>
public sealed class FallbackBlock : ContentBlock
{
    internal FallbackBlock(JsonObject json) : base(json) { }

    /// <summary>The model that took over; null if the block does not say.</summary>
    public string? ToModel => Json.Str(Node("to")?["model"]);
}
