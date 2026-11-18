using System.Text.Json.Nodes;

// Python: none (C#-only: Python keeps content blocks as the SDK's models and dicts)
namespace Mini.Core.Conversation;

/// <summary>
/// One block of a message's content. It keeps its JSON privately and serializes back to exactly that JSON, so a block the API
/// returned is replayed byte for byte (thinking binding depends on it), fields this code has no property for included.
/// Blocks the harness makes come from the factories (<see cref="TextBlock.Create"/> and the like), which write the keys in the
/// order the harness always wrote them. Callers see only the typed properties of the subclasses.
/// </summary>
public abstract class ContentBlock
{
    readonly JsonObject _json;

    private protected ContentBlock(JsonObject json) => _json = json;

    /// <summary>The block's "type"; the empty type when it has none.</summary>
    public BlockType Type => BlockType.Of(_json);

    /// <summary>A copy of the JSON this block was made from.</summary>
    internal JsonObject ToJson() => (JsonObject)_json.DeepClone();

    private protected string? String(string key) => Json.Str(_json[key]);

    private protected JsonNode? Node(string key) => _json[key];

    /// <summary>The string at <paramref name="key"/>, for any block, whatever its type (the sanitizer pairs server tool calls with their results through it).</summary>
    internal string? Field(string key) => String(key);

    /// <summary>Wraps JSON that came from the API or from a session file. The block gets its own copy.</summary>
    public static ContentBlock From(JsonObject json)
    {
        var raw = (JsonObject)json.DeepClone();
        var type = BlockType.Of(raw);
        if (type == BlockType.Text) return new TextBlock(raw);
        if (type == BlockType.Thinking) return new ThinkingBlock(raw);
        if (type == BlockType.ToolUse) return new ToolUseBlock(raw);
        if (type == BlockType.ToolResult) return new ToolResultBlock(raw);
        if (type == BlockType.ServerToolUse) return new ServerToolUseBlock(raw);
        if (type == BlockType.Image) return new ImageBlock(raw);
        if (type == BlockType.Compaction) return new CompactionBlock(raw);
        if (type == BlockType.Fallback) return new FallbackBlock(raw);
        return new UnknownBlock(raw);
    }
}
