// Python: loop.py Agent.call (the retry loop; one attempt is the wrapped client's StreamAsync)
namespace Mini.Core.Models;

/// <summary>
/// Streams through another client and tries again when a stream fails mid-way. A transient failure after the stream started gets three
/// more tries, 2, 4 and 8 s apart; before the stream starts, the SDK's own retries apply, so they aren't stacked. A failed attempt
/// leaves no trace in the transcript: the caller appends only a response that came back whole. Only streaming is retried.
/// </summary>
public sealed class RetryingModelClient(IModelClient inner, TimeProvider time) : IModelClient
{
    /// <summary>The waits before each retry, in seconds; the 0 is the last attempt, whose failure is the caller's.</summary>
    static readonly int[] Waits = [2, 4, 8, 0];

    /// <inheritdoc/>
    // Python: loop.py Agent.call (the retry loop)
    public async Task<ModelResponse> StreamAsync(MessageRequest request, IStreamObserver observer, CancellationToken ct)
    {
        foreach (var wait in Waits)
        {
            var attempt = new AttemptObserver(observer); // per attempt: subagents share the wrapped client, so a shared flag would race
            try
            {
                return await inner.StreamAsync(request, attempt, ct);
            }
            catch (Exception e) when (wait > 0 && attempt.Started && !ct.IsCancellationRequested && TransientErrorClassifier.IsTransient(e))
            {
                observer.OnRetrying(e, wait);
                await Task.Delay(TimeSpan.FromSeconds(wait), time, ct); // Ctrl-C here interrupts the turn, as during the stream
            }
        }
        throw new InvalidOperationException("unreachable: the last attempt returns or throws");
    }

    /// <inheritdoc/>
    public Task<ModelResponse> CreateAsync(MessageRequest request, CancellationToken ct) => inner.CreateAsync(request, ct);

    /// <inheritdoc/>
    public Task<long> CountTokensAsync(MessageRequest request, CancellationToken ct) => inner.CountTokensAsync(request, ct);

    /// <summary>Remembers that the stream started, and passes everything on.</summary>
    sealed class AttemptObserver(IStreamObserver next) : IStreamObserver
    {
        public bool Started { get; private set; }

        public void OnStarted()
        {
            Started = true;
            next.OnStarted();
        }

        public void OnText(string delta) => next.OnText(delta);

        public void OnThinking(string delta) => next.OnThinking(delta);

        public void OnRetrying(Exception error, int seconds) => next.OnRetrying(error, seconds);
    }
}
