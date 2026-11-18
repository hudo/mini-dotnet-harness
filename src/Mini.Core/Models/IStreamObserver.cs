namespace Mini.Core.Models;

/// <summary>Where a streamed response is shown as it arrives. The transport tells it what happened; what it does with it (print,
/// ignore, record) is its own business. An exception it throws is ours, never the network's: the transport marks it
/// (<see cref="OwnFailure"/>) so a retry loop does not take a closed stdout for a broken connection.</summary>
// Python: none (C#-only: Python passes the UI object and an on_start callback into the SDK call)
public interface IStreamObserver
{
    /// <summary>The request went through (the response headers arrived); runs once, before the first event. An exception thrown
    /// before it means the SDK's own retries already gave up.</summary>
    void OnStarted();

    /// <summary>A piece of the reply's text.</summary>
    void OnText(string delta);

    /// <summary>A piece of the model's thinking.</summary>
    void OnThinking(string delta);

    /// <summary>A transient failure after the stream started: the same request is sent again in <paramref name="seconds"/> s.</summary>
    void OnRetrying(Exception error, int seconds);
}
