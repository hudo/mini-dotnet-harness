using System.Text.Json.Nodes;

// Python: cli.py main (the headless result: the dict, the --json line and the exit code)
namespace Mini.Cli.Commands;

/// <summary>How a headless run ended: a turn's stop reason, or a goal's status.</summary>
public sealed record HeadlessResult(string? Stop, string? Status, string? Note, int? Turns)
{
    /// <summary>A prompt's result.</summary>
    public static HeadlessResult OfTurn(string stop) => new(stop, null, null, null);

    /// <summary>A goal's result.</summary>
    public static HeadlessResult OfGoal(Goal g) => new(null, g.Status.Wire(), g.Note, g.Turns);

    /// <summary>A script must see the run (or the goal) didn't finish: 1 then, else 0.</summary>
    public int ExitCode => Stop is "error" or "interrupted" or "pause_turn" or "max_tokens" or "refusal" || (Status ?? "done") != "done" ? 1 : 0;

    /// <summary>The <c>--json</c> line, byte for byte what <c>json.dumps</c> prints: ", " and ": " separators, non-ASCII as lowercase \uXXXX
    /// (a surrogate pair as two), and only <c>"</c>, <c>\</c> and control characters escaped otherwise.</summary>
    /// <remarks>The cost is printed as Python's float repr, so a run that cost nothing is <c>0.0</c>, not <c>0</c>.</remarks>
    public string ToJsonLine(string session, double cost, string answer)
    {
        var line = new JsonObject();
        if (Stop is not null) line["stop"] = Stop;
        if (Status is not null) line["status"] = Status;
        if (Note is not null) line["note"] = Note;
        if (Turns is { } turns) line["turns"] = turns;
        line["session"] = session;
        line["cost"] = JsonNode.Parse(PythonJson.FormatFloat(Math.Round(cost, 4))); // the parsed number keeps its spelling when written
        line["answer"] = answer;
        return PythonJson.Dumps(line);
    }
}
