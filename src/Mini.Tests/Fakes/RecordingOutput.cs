using System.Text.Json.Nodes;

namespace Mini.Tests.Fakes;

/// <summary>An <see cref="IAgentOutput"/> that records what was shown, one entry per call, and touches no terminal. A child shares its parent's
/// log, each entry prefixed with <c>child:</c> once per level.</summary>
public sealed class RecordingOutput : IAgentOutput
{
    readonly List<string> _log;
    readonly string _prefix;

    public RecordingOutput() : this([], "") { }

    RecordingOutput(List<string> log, string prefix) => (_log, _prefix) = (log, prefix);

    /// <summary>Everything shown so far, as <c>kind: text</c>.</summary>
    public IReadOnlyList<string> Events => _log;

    void Add(string kind, string text) => _log.Add($"{_prefix}{kind}: {text}");

    public void Text(string s) => Add("text", s);

    public void Thinking(string s) => Add("thinking", s);

    public void EndMessage() => Add("end", "");

    public void ToolCall(string name, JsonObject args) => Add("tool", name);

    public void ToolResult(string output, bool err) => Add(err ? "error" : "result", output);

    public void Note(string s) => Add("note", s);

    public void Warn(string s) => Add("warn", s);

    public IAgentOutput Child() => new RecordingOutput(_log, _prefix + "child:");
}
