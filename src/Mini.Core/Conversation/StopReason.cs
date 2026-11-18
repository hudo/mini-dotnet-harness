namespace Mini.Core.Conversation;

/// <summary>A response's stop reason: an open set (the API adds values), so a struct around the string, not an enum.</summary>
// Python: none (C#-only: Python compares the stop_reason strings inline)
public readonly record struct StopReason(string Value)
{
    public static readonly StopReason EndTurn = new("end_turn");
    public static readonly StopReason ToolUse = new("tool_use");
    public static readonly StopReason MaxTokens = new("max_tokens");
    public static readonly StopReason PauseTurn = new("pause_turn");
    public static readonly StopReason Refusal = new("refusal");
    public static readonly StopReason Compaction = new("compaction");

    /// <summary>Not from the API: the turn was cut short by the user.</summary>
    public static readonly StopReason Interrupted = new("interrupted");

    /// <summary>Not from the API: the request failed.</summary>
    public static readonly StopReason Error = new("error");

    public override string ToString() => Value;
}
