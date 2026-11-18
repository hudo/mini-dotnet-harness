using System.Text.Json.Nodes;

// Python: tools.py LocalTools.read (the image block it returns)
namespace Mini.Core.Conversation;

public sealed class ImageBlock : ContentBlock
{
    internal ImageBlock(JsonObject json) : base(json) { }

    /// <summary>The base64 payload; null if the block has none.</summary>
    public string? Data => Json.Str(Node("source")?["data"]);

    /// <summary>A base64 image, as the read tool returns one (<paramref name="mediaType"/> is the image's MIME type).</summary>
    public static ImageBlock Create(string mediaType, string base64) =>
        new(Json.Obj(("type", BlockType.Image.Value), ("source", Json.Obj(("type", "base64"), ("media_type", mediaType), ("data", base64)))));
}
