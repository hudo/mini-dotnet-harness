using System.Text.Json.Nodes;

// Python: cli.py UI (the methods the agent calls: text, thinking, end_message, tool_call, tool_result, note, warn, child)
namespace Mini.Core.Interaction;

/// <summary>Where an agent shows its work: the model's streamed text and thinking, one line per tool call and result, notes and warnings.
/// The console implements it; nothing in Mini.Core touches the terminal.</summary>
public interface IAgentOutput
{
    /// <summary>A piece of the reply's text, as it streams.</summary>
    void Text(string s);

    /// <summary>A piece of the model's thinking, as it streams.</summary>
    void Thinking(string s);

    /// <summary>The reply is over: a line left open by streamed text is closed.</summary>
    void EndMessage();

    /// <summary>A tool is about to run.</summary>
    void ToolCall(string name, JsonObject args);

    /// <summary>A tool's result, first line only; <paramref name="err"/> marks a failure.</summary>
    void ToolResult(string output, bool err);

    /// <summary>A quiet line about what the harness is doing (compacting, verifying).</summary>
    void Note(string s);

    /// <summary>Something went wrong or was cut short; shown even when everything else is silent.</summary>
    void Warn(string s);

    /// <summary>The output of a subagent: its work is shown indented and its streamed text is hidden.</summary>
    IAgentOutput Child();
}
