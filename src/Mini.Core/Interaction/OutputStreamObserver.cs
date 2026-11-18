namespace Mini.Core.Interaction;

/// <summary>Shows a streamed response on the agent's output.</summary>
// Python: none (C#-only: Python hands the UI to the SDK call; here the transport only knows <see cref="IStreamObserver"/>)
public sealed class OutputStreamObserver(IAgentOutput output) : IStreamObserver
{
    /// <inheritdoc/>
    public void OnStarted() { }

    /// <inheritdoc/>
    public void OnText(string delta) => output.Text(delta);

    /// <inheritdoc/>
    public void OnThinking(string delta) => output.Thinking(delta);

    /// <inheritdoc/>
    public void OnRetrying(Exception error, int seconds)
    {
        output.EndMessage();
        output.Warn($"request failed ({error.Message}); retrying in {seconds}s");
    }
}
