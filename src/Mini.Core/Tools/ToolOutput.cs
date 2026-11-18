using System.Text.Json.Nodes;

// Python: tools.py LocalTools.read (a string, or a list of text and image blocks)
namespace Mini.Core.Tools;

/// <summary>What a tool returns: plain text, or (reading an image) a list of text and image blocks.</summary>
public sealed class ToolOutput
{
    private ToolOutput(string? text, IReadOnlyList<ContentBlock>? blocks) => (Text, Blocks) = (text, blocks);

    /// <summary>The result as text, or null when it is <see cref="Blocks"/>.</summary>
    public string? Text { get; }

    /// <summary>The result as content blocks, or null when it is <see cref="Text"/>.</summary>
    public IReadOnlyList<ContentBlock>? Blocks { get; }

    /// <summary>What the user is shown of it: the text, or the text of the first block.</summary>
    public string Summary => Text ?? (Blocks?[0] as TextBlock)?.Text ?? "";

    /// <summary>The value for the tool_result's content.</summary>
    public JsonNode Content => Text is { } t ? JsonValue.Create(t)! : new JsonArray(Blocks!.Select(b => (JsonNode)b.ToJson()).ToArray());

    public static ToolOutput Of(string text) => new(text, null);

    public static ToolOutput OfBlocks(IReadOnlyList<ContentBlock> blocks) => new(null, blocks);
}
