using System.Text.Json;
using System.Text.Json.Nodes;
using static Mini.Cli.Terminal.AnsiColors;

// Python: cli.py UI, UI.__init__, UI._out, UI.text, UI.thinking, UI.end_message, UI._say, UI.tool_call, UI.tool_result, UI.note
// Python: cli.py UI.warn, UI.child
namespace Mini.Cli.Terminal;

/// <summary>The agent's output on stdout and stderr. A child's lines are indented by its own prefix rather than by a decorator, because
/// the prefix goes in front of the colored line this class formats.</summary>
// Python: cli.py UI, UI.__init__ (the color constants are in AnsiColors)
public sealed class ConsoleAgentOutput(string indent = "", bool quiet = false, bool silent = false) : IAgentOutput
{
    /// <summary>quiet hides streamed text and thinking (subagents); silent hides everything but warnings (--json).</summary>
    public string Indent { get; } = indent;
    public bool Quiet { get; } = quiet || silent;
    public bool Silent { get; } = silent;
    string? _mode;
    bool _fresh = true;

    // Python: cli.py UI._out
    void Out(string newMode, string s, string color = "")
    {
        if (Quiet || s.Length == 0) return;
        if (_mode != newMode) // a new block starts on a fresh line; thinking is labeled
        {
            Console.Out.Write((_fresh ? "" : "\n") + (newMode == "think" ? $"{Dim}thinking: {Off}" : ""));
            s = s.TrimStart('\n');
        }
        Console.Out.Write(color + s + (color.Length > 0 ? Off : ""));
        Console.Out.Flush();
        _mode = newMode;
        if (s.Length > 0) _fresh = s.EndsWith('\n');
    }

    // Python: cli.py UI.text
    public void Text(string s) => Out("text", Printable.Control.Replace(s, "")); // the model's text can't drive the terminal

    // Python: cli.py UI.thinking
    public void Thinking(string s) => Out("think", Printable.Control.Replace(s, ""), Dim);

    // Python: cli.py UI.end_message
    public void EndMessage()
    {
        if (_mode is not null && !_fresh) Console.Out.Write("\n");
        _mode = null;
        _fresh = true;
    }

    // Python: cli.py UI._say
    void Say(string s)
    {
        if (!Silent) Console.Out.WriteLine(Indent + s);
    }

    static readonly string[] _briefKeys = ["command", "path", "prompt"]; // the arguments worth showing, in order

    // Python: cli.py UI.tool_call
    public void ToolCall(string name, JsonObject args)
    {
        var brief = name == "find" ? null : _briefKeys.Select(k => args[k]).FirstOrDefault(PythonJson.Truthy);
        var text = brief is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>()
            : brief is not null ? PythonJson.Dumps(brief) : PythonJson.Dumps(args).TakeCodePoints(120);
        // brief is never empty here: Dumps(args) is at least "{}", so Python's `if brief` is always true
        Say($"{Cya}→ {name}{Off} {Printable.Of(LineSplitter.Split(text)[0].TakeCodePoints(150))}");
    }

    // Python: cli.py UI.tool_result
    public void ToolResult(string output, bool err)
    {
        var lines = LineSplitter.Split(output.Trim());
        Say($"  {(err ? Red : Dim)}{Printable.Of((lines.Count > 0 ? lines[0] : "").TakeCodePoints(150))}{Off}");
    }

    // Python: cli.py UI.note
    public void Note(string s) => Say($"{Dim}· {s}{Off}");

    // Python: cli.py UI.warn
    public void Warn(string s) => Console.Error.WriteLine($"{Indent}{Yel}! {Printable.Control.Replace(s, "")}{Off}");

    // Python: cli.py UI.child
    public IAgentOutput Child() => new ConsoleAgentOutput(Indent + "    ", quiet: true, silent: Silent);
}
