// Python: cli.py main (KeyboardInterrupt handling)
namespace Mini.Cli.Hosting;

/// <summary>What the handler does with a press.</summary>
internal enum CancelOutcome
{
    /// <summary>Nothing: the runtime's default (end the process) applies.</summary>
    Default,

    /// <summary>The running command's token was cancelled; the program carries on.</summary>
    Cancelled,

    /// <summary>At the prompt: clean up and exit 0.</summary>
    ExitFromPrompt,
}
