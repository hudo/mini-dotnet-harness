using System.Text.Json.Nodes;

// Python: none (C#-only: Python keeps the SDK's server_tool_use block)
namespace Mini.Core.Conversation;

/// <summary>A tool the API itself ran (web search). Its result block follows it in the same reply.</summary>
public sealed class ServerToolUseBlock : ContentBlock
{
    internal ServerToolUseBlock(JsonObject json) : base(json) { }

    public string? Id => String("id");
}
