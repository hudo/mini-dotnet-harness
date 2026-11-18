using Mini.Tests.Fakes;

namespace Mini.Tests.CommandLine;

/// <summary>What Ctrl-C does in each state: <c>Idle</c>, <c>AtPrompt</c> and <c>Running</c>. The handler only acts on the answer
/// (<c>e.Cancel</c>, the exit); the decision is tested here without a terminal.</summary>
public class ConsoleCancellationTests
{
    static ConsoleCancellation Create() => new(new JobRegistry());

    // Python: cli.py main (at startup a KeyboardInterrupt ends the process: the default)
    [Fact]
    public void WhenIdleTheDefaultStays()
    {
        var c = Create();

        Assert.IsType<CancellationState.Idle>(c.State);
        Assert.Equal(CancelOutcome.Default, c.Press());
    }

    // Python: cli.py repl (input() raises KeyboardInterrupt: print a newline, return, main's finally closes the hub; exit 0)
    [Fact]
    public void AtThePromptTheProgramExitsTheNormalWay()
    {
        var c = Create();

        using (c.EnterPrompt())
        {
            Assert.IsType<CancellationState.AtPrompt>(c.State);
            Assert.Equal(CancelOutcome.ExitFromPrompt, c.Press());
        }

        Assert.IsType<CancellationState.Idle>(c.State);
        Assert.Equal(CancelOutcome.Default, c.Press());
    }

    // Python: cli.py main (the first KeyboardInterrupt stops the command; a second one, raised again, is not caught)
    [Fact]
    public void WhileRunningTheFirstPressCancelsTheCommandAndTheSecondKeepsTheDefault()
    {
        var c = Create();
        using var run = c.BeginRun();

        Assert.IsType<CancellationState.Running>(c.State);
        Assert.False(run.Token.IsCancellationRequested);
        Assert.Equal(CancelOutcome.Cancelled, c.Press());
        Assert.True(run.Token.IsCancellationRequested);
        Assert.True(run.WasCancelled);
        Assert.Equal(CancelOutcome.Default, c.Press());
    }

    // C#-only: each command has its own token; a press never reaches a command that already ended
    [Fact]
    public void ACommandThatEndedIsNotCancelledByTheNextPress()
    {
        var c = Create();
        var run = c.BeginRun();
        var token = run.Token;
        run.Dispose();

        Assert.IsType<CancellationState.Idle>(c.State);
        Assert.Equal(CancelOutcome.Default, c.Press());
        Assert.False(token.IsCancellationRequested);

        using var next = c.BeginRun();
        Assert.Equal(CancelOutcome.Cancelled, c.Press());
        Assert.False(token.IsCancellationRequested);
        Assert.True(next.WasCancelled);
    }

    // C#-only: the REPL goes prompt, command, prompt: the state follows
    [Fact]
    public void TheStateFollowsThePromptAndTheCommandsOfTheRepl()
    {
        var c = Create();

        using (c.EnterPrompt()) Assert.IsType<CancellationState.AtPrompt>(c.State);
        using (c.BeginRun()) Assert.IsType<CancellationState.Running>(c.State);
        using (c.EnterPrompt()) Assert.IsType<CancellationState.AtPrompt>(c.State);
        Assert.IsType<CancellationState.Idle>(c.State);
    }
}
