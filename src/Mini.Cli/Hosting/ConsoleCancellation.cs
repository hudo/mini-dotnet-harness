// Python: cli.py main (KeyboardInterrupt handling)
namespace Mini.Cli.Hosting;

/// <summary>
/// What Ctrl-C does, by where the program is. Three states, set by the code that is about to block:
/// <list type="bullet">
/// <item><see cref="CancellationState.Idle"/> (startup, shutdown): the default stays, so the process ends.</item>
/// <item><see cref="CancellationState.AtPrompt"/> (the REPL waits in <c>ReadLine</c>, which can't be interrupted): the program cleans up and exits 0.
/// In Python, <c>input()</c> raises KeyboardInterrupt, <c>repl</c> prints a newline and returns, and <c>main</c>'s finally closes the hub.</item>
/// <item><see cref="CancellationState.Running"/> (a command or headless run): the first press cancels that command's token and the program stays;
/// a second press keeps the default, as a KeyboardInterrupt raised again does.</item>
/// </list>
/// One handler on <see cref="Console.CancelKeyPress"/>, installed by <see cref="Install"/>.
/// </summary>
public sealed class ConsoleCancellation(JobRegistry jobs)
{
    volatile CancellationState _state = CancellationState.Idle.Instance;
    McpHub? _hub;

    /// <summary>Where the program is now.</summary>
    internal CancellationState State => _state;

    /// <summary>The hub the exit from the prompt closes (each server's close is bounded at 5 s).</summary>
    public void UseHub(McpHub? hub) => _hub = hub;

    /// <summary>Starts handling Ctrl-C; dispose the result to stop.</summary>
    public IDisposable Install()
    {
        Console.CancelKeyPress += OnCancelKeyPress;
        return new Uninstaller(this);
    }

    /// <summary>The REPL is about to read a line: until the result is disposed, Ctrl-C exits the program the normal way.</summary>
    public IDisposable EnterPrompt()
    {
        _state = CancellationState.AtPrompt.Instance;
        return new Leave(this);
    }

    /// <summary>A command starts: until the result is disposed, Ctrl-C cancels its token.</summary>
    public CommandRun BeginRun()
    {
        var run = new CommandRun(this);
        _state = new CancellationState.Running(run.Source);
        return run;
    }

    /// <summary>What a Ctrl-C press means now, and the cancellation it asks for. The handler acts on the answer.</summary>
    internal CancelOutcome Press()
    {
        switch (_state)
        {
            case CancellationState.Running(var cts):
                if (cts.IsCancellationRequested) return CancelOutcome.Default; // a second press keeps the default
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { } // the command ended just now
                return CancelOutcome.Cancelled;
            case CancellationState.AtPrompt:
                return CancelOutcome.ExitFromPrompt;
            default: // not at the prompt and nothing running (startup): the default ends the process
                return CancelOutcome.Default;
        }
    }

    void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        var outcome = Press();
        if (outcome == CancelOutcome.Default) return;
        e.Cancel = true;
        if (outcome == CancelOutcome.ExitFromPrompt)
            ExitFromPrompt();
    }

    // ReadLine can't be cancelled, so the cleanup Python's finally does runs here, then the process exits 0.
    void ExitFromPrompt()
    {
        Console.Out.WriteLine();
        try { _hub?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(6)); } // each server's close is bounded at 5 s
        catch (Exception) { } // a hub that won't close must not block exit
        jobs.StopAll();
        Environment.Exit(0);
    }

    void Back() => _state = CancellationState.Idle.Instance;

    sealed class Uninstaller(ConsoleCancellation owner) : IDisposable
    {
        public void Dispose() => Console.CancelKeyPress -= owner.OnCancelKeyPress;
    }

    sealed class Leave(ConsoleCancellation owner) : IDisposable
    {
        public void Dispose() => owner.Back();
    }

    /// <summary>One command's cancellation: its token, and whether Ctrl-C asked for the stop. Disposing returns the program to Idle.</summary>
    public sealed class CommandRun : IDisposable
    {
        readonly ConsoleCancellation _owner;

        internal CommandRun(ConsoleCancellation owner) => _owner = owner;

        internal CancellationTokenSource Source { get; } = new();

        /// <summary>Cancelled by Ctrl-C.</summary>
        public CancellationToken Token => Source.Token;

        /// <summary>Ctrl-C was pressed while this command ran.</summary>
        public bool WasCancelled => Source.IsCancellationRequested;

        public void Dispose()
        {
            _owner.Back();
            Source.Dispose();
        }
    }
}
