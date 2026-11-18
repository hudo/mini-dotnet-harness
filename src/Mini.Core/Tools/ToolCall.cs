using System.Text.Json.Nodes;

// Python: loop.py Agent.run_tools (the tool_use block it reads: id, name, input)
namespace Mini.Core.Tools;

/// <summary>One tool_use block of the model's reply, as the executor sees it.</summary>
/// <param name="Id">The tool_use id the result must answer.</param>
/// <param name="Name">The tool's name.</param>
/// <param name="Input">The arguments, as the model wrote them.</param>
public sealed record ToolCall(string? Id, string Name, JsonObject Input)
{
    /// <summary>The call a reply's tool_use block makes; the arguments are the block's own copy.</summary>
    public static ToolCall From(ToolUseBlock block) => new(block.Id, block.Name, block.Input);
}
