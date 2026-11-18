namespace Mini.Core.Models;

/// <summary>The model transport the Agent talks to. <see cref="AnthropicModelClient"/> is the real one,
/// <see cref="RetryingModelClient"/> wraps any client with the mid-stream retries; tests script a fake.</summary>
// Python: none (C#-only: the test seam Python gets from passing a fake `client` to Agent)
public interface IModelClient
{
    /// <summary>Stream one response. <see cref="IStreamObserver.OnStarted"/> runs once the request went through, before the first
    /// event; an exception thrown before it means the caller must not retry.</summary>
    Task<ModelResponse> StreamAsync(MessageRequest request, IStreamObserver observer, CancellationToken ct);

    /// <summary>One whole response (compaction).</summary>
    Task<ModelResponse> CreateAsync(MessageRequest request, CancellationToken ct);

    /// <summary>The input tokens the API counts in this request (it sends nothing to the model).</summary>
    Task<long> CountTokensAsync(MessageRequest request, CancellationToken ct);
}
