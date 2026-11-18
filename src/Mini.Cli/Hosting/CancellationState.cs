// Python: cli.py main (KeyboardInterrupt handling)
namespace Mini.Cli.Hosting;

/// <summary>Where the program is, as far as Ctrl-C cares.</summary>
internal abstract record CancellationState
{
    /// <summary>Startup, shutdown, or between commands.</summary>
    public sealed record Idle : CancellationState
    {
        public static readonly Idle Instance = new();
    }

    /// <summary>The REPL waits for a line.</summary>
    public sealed record AtPrompt : CancellationState
    {
        public static readonly AtPrompt Instance = new();
    }

    /// <summary>A command is running; <paramref name="Source"/> is its cancellation.</summary>
    public sealed record Running(CancellationTokenSource Source) : CancellationState;
}
