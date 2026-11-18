namespace Mini.Core.Agents;

/// <summary>What running one reply's tool calls produced: the tool_result blocks, and whether the user interrupted.</summary>
// Python: loop.py Agent.run_tools (its return value)
public sealed record ToolRunResult(IReadOnlyList<ToolResultBlock> Results, bool Interrupted);
