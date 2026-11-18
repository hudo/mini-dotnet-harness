namespace Mini.Core.Tools;

/// <summary>A tool call failed in a way the model should read: the message goes back as an is_error result.</summary>
// Python: tools.py ToolError
public sealed class ToolException(string message) : MiniException(message);
