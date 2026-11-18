using System.Text.Json.Nodes;

// Python: loop.py Agent.dispatch (the mcp__ branch)
namespace Mini.Core.Tools.Mcp;

/// <summary>One tool of an MCP server, as the model calls it (mcp__server__tool). Every call goes through approval.</summary>
public sealed class McpTool(McpHub hub, JsonObject schema, IApprovalService approval) : ITool
{
    // Python: ext.py MCPHub.__init__ (self.schemas)
    public ToolDefinition Definition { get; } = new(Json.Str(schema["name"])!, schema);

    // Python: loop.py Agent.dispatch (the mcp__ branch)
    public async Task<ToolOutput> InvokeAsync(ToolCall call, CancellationToken ct)
    {
        var decision = await approval.RequestAsync(new ApprovalRequest(ApprovalKind.Mcp, $"{call.Name} {PythonJson.Dumps(call.Input)}"), ct);
        if (!decision.Allowed)
            throw new ToolException("User denied this tool call." + decision.Reason);
        var (text, isError) = await hub.CallAsync(call.Name, call.Input, ct);
        if (isError) throw new ToolException(text);
        return ToolOutput.Of(text);
    }
}
