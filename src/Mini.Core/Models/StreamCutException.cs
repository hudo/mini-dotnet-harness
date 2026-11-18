namespace Mini.Core.Models;

/// <summary>The stream ended before the response was complete; worth retrying.</summary>
// Python: loop.py Agent.call (the APIConnectionError it raises when the stream ended before the response was complete)
public sealed class StreamCutException(string message) : Exception(message);
