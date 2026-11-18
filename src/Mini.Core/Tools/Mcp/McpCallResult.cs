namespace Mini.Core.Tools.Mcp;

/// <summary>What an MCP tool answered: its text, and whether the server marked it an error.</summary>
// Python: ext.py MCPHub.call (the (text, is_error) it returns)
public sealed record McpCallResult(string Text, bool IsError);
