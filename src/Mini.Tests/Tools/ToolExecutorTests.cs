using System.Text.Json.Nodes;
using Mini.Tests.Fakes;

namespace Mini.Tests.Tools;

/// <summary>The one-result-per-call guarantee of the executor, with fake tools: no files, no model.</summary>
// Python test helper: tests/test_mini.py (test_interrupt_backfills_tool_results and the run_tools cases)
public class ToolExecutorTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    sealed class FakeTool(string name, Func<ToolCall, CancellationToken, Task<ToolOutput>> run) : ITool
    {
        public ToolDefinition Definition { get; } = ToolDefinition.Function(name, "d", Json.Obj(), []);
        public List<ToolCall> Calls { get; } = [];

        public Task<ToolOutput> InvokeAsync(ToolCall call, CancellationToken ct)
        {
            Calls.Add(call);
            return run(call, ct);
        }
    }

    static ToolExecutor Executor(params ITool[] tools) => new(new ToolRegistry(tools), new ConsoleAgentOutput(silent: true));

    static ToolCall Call(string id, string name, JsonObject? input = null) => new(id, name, input ?? new JsonObject());

    [Fact]
    public async Task EveryCallGetsOneResultInOrderWithItsId()
    {
        var echo = new FakeTool("echo", (c, _) => Task.FromResult(ToolOutput.Of($"got {Json.Str(c.Input["x"])}")));
        var run = await Executor(echo).RunAsync([Call("a", "echo", Json.Obj(("x", "1"))), Call("b", "echo", Json.Obj(("x", "2")))], StopReason.ToolUse, Ct);

        Assert.False(run.Interrupted);
        Assert.Equal(
            ["""{"type":"tool_result","tool_use_id":"a","content":"got 1"}""", """{"type":"tool_result","tool_use_id":"b","content":"got 2"}"""],
            run.Results.Select(r => r.ToJson().ToJsonString()));
    }

    [Fact]
    public async Task AToolExceptionIsAnErrorResultWithItsMessage()
    {
        var bad = new FakeTool("bad", (_, _) => throw new ToolException("no good"));

        var run = await Executor(bad).RunAsync([Call("a", "bad")], StopReason.ToolUse, Ct);

        Assert.Equal("""{"type":"tool_result","tool_use_id":"a","content":"no good","is_error":true}""", run.Results.Single().ToJson().ToJsonString());
    }

    [Fact]
    public async Task AnyOtherExceptionNamesItsTypeAndTheLoopGoesOn()
    {
        var boom = new FakeTool("boom", (_, _) => throw new InvalidOperationException("kaput"));
        var fine = new FakeTool("fine", (_, _) => Task.FromResult(ToolOutput.Of("ok")));

        var run = await Executor(boom, fine).RunAsync([Call("a", "boom"), Call("b", "fine")], StopReason.ToolUse, Ct);

        Assert.Equal("InvalidOperationException: kaput", run.Results[0].Text);
        Assert.True(run.Results[0].IsError);
        Assert.Equal("ok", run.Results[1].Text);
        Assert.False(run.Results[1].IsError);
    }

    [Fact]
    public async Task AnUnknownToolIsAnErrorResult()
    {
        var run = await Executor().RunAsync([Call("a", "nope")], StopReason.ToolUse, Ct);

        Assert.Equal("Tool nope is not available in this session.", run.Results.Single().Text);
        Assert.True(run.Results.Single().IsError);
    }

    [Fact]
    public async Task ACancelInTheMiddleInterruptsThatCallAndBackfillsTheRest()
    {
        using var cts = new CancellationTokenSource();
        var first = new FakeTool("first", (_, _) => Task.FromResult(ToolOutput.Of("one")));
        var second = new FakeTool("second", (_, ct) =>
        {
            cts.Cancel(); // Ctrl-C while this one runs
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(ToolOutput.Of("never"));
        });
        var third = new FakeTool("third", (_, _) => Task.FromResult(ToolOutput.Of("never")));

        var run = await Executor(first, second, third).RunAsync([Call("a", "first"), Call("b", "second"), Call("c", "third"), Call("d", "third")], StopReason.ToolUse, cts.Token);

        Assert.True(run.Interrupted);
        Assert.Equal(["a", "b", "c", "d"], run.Results.Select(r => r.ToolUseId));
        Assert.Equal(["one", "Interrupted by the user.", "Not run: interrupted by the user.", "Not run: interrupted by the user."], run.Results.Select(r => r.Text));
        Assert.Equal([false, true, true, true], run.Results.Select(r => r.IsError));
        Assert.Empty(third.Calls);
    }

    [Fact]
    public async Task AFailureWhileCancellingCountsAsTheInterrupt()
    {
        using var cts = new CancellationTokenSource();
        var wrapped = new FakeTool("wrapped", (_, _) =>
        {
            cts.Cancel();
            throw new InvalidOperationException("the SDK wrapped the cancellation");
        });

        var run = await Executor(wrapped).RunAsync([Call("a", "wrapped")], StopReason.ToolUse, cts.Token);

        Assert.True(run.Interrupted);
        Assert.Equal("Interrupted by the user.", run.Results.Single().Text);
    }

    [Fact]
    public async Task APreCancelledTokenRunsNothingButStillAnswersEveryCall()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var tool = new FakeTool("t", (_, _) => Task.FromResult(ToolOutput.Of("x")));

        var run = await Executor(tool).RunAsync([Call("a", "t"), Call("b", "t")], StopReason.ToolUse, cts.Token);

        Assert.Empty(tool.Calls);
        Assert.Equal(["Interrupted by the user.", "Not run: interrupted by the user."], run.Results.Select(r => r.Text));
    }

    [Theory]
    [InlineData("max_tokens")]
    [InlineData("refusal")]
    [InlineData("pause_turn")]
    public async Task ACallFromAReplyThatEndedEarlyIsNotRunButStillAnswered(string stop)
    {
        var tool = new FakeTool("t", (_, _) => Task.FromResult(ToolOutput.Of("x")));

        var run = await Executor(tool).RunAsync([Call("a", "t")], new StopReason(stop), Ct);

        Assert.Empty(tool.Calls);
        Assert.False(run.Interrupted);
        Assert.Equal($"Not run: the response ended ({stop}) before this call was complete.", run.Results.Single().Text);
        Assert.True(run.Results.Single().IsError);
    }

    [Fact]
    public async Task ALongTextResultIsCutInTheMiddleAndABlockResultIsLeftAlone()
    {
        var long50k = new FakeTool("long", (_, _) => Task.FromResult(ToolOutput.Of(new string('a', 50_000))));
        var blocks = new FakeTool("blocks", (_, _) => Task.FromResult(ToolOutput.OfBlocks([TextBlock.Create("hello")])));

        var run = await Executor(long50k, blocks).RunAsync([Call("a", "long"), Call("b", "blocks")], StopReason.ToolUse, Ct);

        Assert.Contains("[... 20000 characters cut ...]", run.Results[0].Text);
        Assert.Equal("""[{"type":"text","text":"hello"}]""", run.Results[1].ToJson()["content"]!.ToJsonString());
    }

    [Fact]
    public void TheRegistryFindsToolsByNameAndRefusesTwoWithOne()
    {
        var a = new FakeTool("a", (_, _) => Task.FromResult(ToolOutput.Of("")));
        var registry = new ToolRegistry([a]);

        Assert.True(registry.TryGet("a", out var found));
        Assert.Same(a, found);
        Assert.False(registry.TryGet("b", out _));
        Assert.Same(a, registry.Get("a"));
        Assert.Throws<ArgumentException>(() => new ToolRegistry([a, a]));
    }
}
