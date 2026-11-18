using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests.CommandLine;

/// <summary>The <c>--json</c> line a script reads: byte for byte what Python's <c>json.dumps</c> prints.</summary>
public class HeadlessJsonTests
{
    // C#-only: the P0 finding. System.Text.Json escaped each of ` < > & ' + as \uXXXX (upper case, and " as \u0022) and a surrogate pair as \uD83D\uDE00.
    // json.dumps (ensure_ascii) escapes only non-ASCII, in lower case, plus ", \ and the control characters.
    [Fact]
    public void TheLineEscapesWhatJsonDumpsEscapesAndNothingElse()
    {
        var answer = "` < > & ' + \" \U0001F600 é \\ / \u007f \t\n";
        var line = HeadlessResult.OfTurn("end_turn").ToJsonLine("20261118-120000", 0, answer);

        // from: json.dumps({"stop": "end_turn", "session": "20261118-120000", "cost": round(0.0, 4), "answer": answer})
        Assert.Equal("""{"stop": "end_turn", "session": "20261118-120000", "cost": 0.0, "answer": "` < > & ' + \" \ud83d\ude00 \u00e9 \\ / \u007f \t\n"}""", line);
    }

    // C#-only: a goal's result is status, note, turns; the cost is Python's float repr (rounded to 4 places, ".0" for a whole number)
    [Fact]
    public void AGoalResultHasStatusNoteAndTurns()
    {
        var line = new HeadlessResult(null, "done", "ok ` < >", 3).ToJsonLine("s", 0.12345678, "a");
        Assert.Equal("""{"status": "done", "note": "ok ` < >", "turns": 3, "session": "s", "cost": 0.1235, "answer": "a"}""", line);
        Assert.Equal("""{"stop": "end_turn", "session": "s", "cost": 1.0, "answer": ""}""", HeadlessResult.OfTurn("end_turn").ToJsonLine("s", 1, ""));
    }

    // Python: cli.py main (stopped, status != done)
    [Theory]
    [InlineData("end_turn", null, 0)]
    [InlineData("error", null, 1)]
    [InlineData("interrupted", null, 1)]
    [InlineData("pause_turn", null, 1)]
    [InlineData("max_tokens", null, 1)]
    [InlineData("refusal", null, 1)]
    [InlineData(null, "done", 0)]
    [InlineData(null, "paused", 1)]
    [InlineData(null, "active", 1)]
    public void AScriptSeesARunThatDidNotFinish(string? stop, string? status, int exit) =>
        Assert.Equal(exit, new HeadlessResult(stop, status, status is null ? null : "", status is null ? null : 1).ExitCode);

    // C#-only: the whole path, a real agent on a scripted model, to the line on stdout
    [Fact]
    public async Task TheRunnerPrintsTheLineAndExitsZero()
    {
        using var ws = new Workspace();
        using var console = new ConsoleCapture();
        var (agent, _) = ws.Make([Reply(Text("héllo <b> & \ud83d\ude00"))]);
        var options = Options(prompt: "hi", json: true);

        var exit = await new HeadlessRunner(new ConsoleCancellation(ws.Runtime.Jobs)).RunAsync(agent, options);

        Assert.Equal(0, exit);
        var line = console.Out.TrimEnd('\n');
        Assert.StartsWith($$"""{"stop": "end_turn", "session": "{{agent.Session.Id}}", "cost": """, line);
        Assert.EndsWith(", \"answer\": \"h\\u00e9llo <b> & \\ud83d\\ude00\"}", line);
    }

    // C#-only: without --json nothing but the agent's own output is printed
    [Fact]
    public async Task WithoutJsonNoLineIsPrinted()
    {
        using var ws = new Workspace();
        using var console = new ConsoleCapture();
        var (agent, _) = ws.Make([Reply(Text("fine"))]);

        var exit = await new HeadlessRunner(new ConsoleCancellation(ws.Runtime.Jobs)).RunAsync(agent, Options(prompt: "hi"));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("\"session\"", console.Out);
    }

    // Python: cli.py main (stopped: a reply cut at the limit three times in a row)
    [Fact]
    public async Task AStoppedRunExitsOne()
    {
        using var ws = new Workspace();
        using var console = new ConsoleCapture();
        var (agent, _) = ws.Make([Reply("refusal", Text("no"))]);

        var exit = await new HeadlessRunner(new ConsoleCancellation(ws.Runtime.Jobs)).RunAsync(agent, Options(prompt: "hi", json: true));

        Assert.Equal(1, exit);
        Assert.Contains("\"stop\": \"refusal\"", console.Out);
    }

    // Python: cli.py main (a KeyboardInterrupt inside the turn is caught by it: the stop reason is "interrupted", the exit code 1)
    [Fact]
    public async Task AControlCDuringTheTurnIsAnInterruptedRun()
    {
        using var ws = new Workspace();
        using var console = new ConsoleCapture();
        var cancellation = new ConsoleCancellation(ws.Runtime.Jobs);
        Func<JsonObject, object> pressCtrlC = _ =>
        {
            Assert.Equal(CancelOutcome.Cancelled, cancellation.Press()); // the first Ctrl-C arrives while the request is out
            throw new OperationCanceledException();
        };
        var (agent, _) = ws.Make([pressCtrlC]);

        var exit = await new HeadlessRunner(cancellation).RunAsync(agent, Options(prompt: "hi", json: true));

        Assert.Equal(1, exit);
        Assert.Contains("\"stop\": \"interrupted\"", console.Out);
        Assert.IsType<CancellationState.Idle>(cancellation.State); // the run is over: Ctrl-C is the default again
    }

    internal static CliOptions Options(string? prompt = null, bool json = false) =>
        new(prompt, false, null, null, "high", false, null, [], null, null, 25, null, json);
}
