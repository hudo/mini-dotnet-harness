using System.Text.Json.Nodes;

// Python: none (C#-only: Python keeps the SDK's compaction block)
namespace Mini.Core.Conversation;

/// <summary>The summary the server wrote when it compacted a conversation.</summary>
public sealed class CompactionBlock : ContentBlock
{
    internal CompactionBlock(JsonObject json) : base(json) { }
}
