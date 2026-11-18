using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using BetaCount = Anthropic.Models.Beta.Messages.MessageCountTokensParams;
using BetaCreate = Anthropic.Models.Beta.Messages.MessageCreateParams;

// Python: loop.py Agent.call, Agent.request_args, Agent.server_summary, Agent.summarize
// Python: cli.py context_report
namespace Mini.Core.Models;

/// <summary>
/// With <see cref="AnthropicClientFactory"/>, the only code that touches the Anthropic SDK (Anthropic 13.4.0). The SDK is only the transport: it builds the
/// request from raw JSON (FromRawUnchecked), retries before the stream starts, parses the SSE framing and raises
/// typed errors. The response is folded together here, from each event's raw JSON, so a field this SDK version has
/// no model for still reaches the transcript.
/// </summary>
public sealed class AnthropicModelClient : IModelClient
{
    /// <summary>The SDK's streaming timeout stops at the headers, so a stream that goes silent after them has no clock.
    /// Ten minutes is what the SDK would have allowed the whole call.</summary>
    static readonly TimeSpan Idle = TimeSpan.FromMinutes(10);

    internal IAnthropicClient Client { get; }

    /// <summary>Wraps a client the <see cref="AnthropicClientFactory"/> built (it checked the credentials first).</summary>
    internal AnthropicModelClient(IAnthropicClient client) => Client = client;

    /// <summary>Stream one response; the final message, nulls stripped. The observer's <see cref="IStreamObserver.OnStarted"/> runs once
    /// the request went through (the response headers arrived), before the first event. An exception thrown earlier
    /// means the SDK's own retries already gave up, so the caller must not retry it.</summary>
    // Python: loop.py Agent.call (one attempt; the retry loop is RetryingModelClient)
    public async Task<ModelResponse> StreamAsync(MessageRequest request, IStreamObserver observer, CancellationToken ct)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            using var response = await Client.Beta.Messages.WithRawResponse.CreateStreaming(ToCreate(MessageRequestJson.Body(request)), ct);
            idle.CancelAfter(Idle); // after the headers the SDK's own timeout no longer applies: start the stall clock here
            OwnFailure.Ours(observer.OnStarted);
            var snapshot = new StreamAccumulator();
            await foreach (var ev in response.Enumerate(idle.Token))
            {
                idle.CancelAfter(Idle); // rearm: the clock is the gap between events, not the length of the stream
                snapshot.Apply(JsonNode.Parse(ev.Json.GetRawText())!.AsObject(), observer);
            }
            return ModelResponse.From(snapshot.Finish());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller's cancellation stays an OperationCanceledException, so the turn ends as interrupted
        }
        catch (OperationCanceledException e) when (idle.IsCancellationRequested)
        {
            throw new HttpRequestException("the stream stalled: no event arrived for 10 minutes", e);
        }
        // enumeration reads the HTTP body, and the SDK passes the HTTP library's IO through there; our own callbacks
        // are marked, so their IOExceptions pass through untouched (TransientErrorClassifier: not transient)
        catch (IOException e) when (!OwnFailure.IsOurs(e) && ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("cancelled by the caller", e, ct);
        }
        catch (IOException e) when (!OwnFailure.IsOurs(e) && idle.IsCancellationRequested)
        {
            throw new HttpRequestException("the stream stalled: no event arrived for 10 minutes", e);
        }
        catch (IOException e) when (!OwnFailure.IsOurs(e))
        {
            throw new HttpRequestException("the connection broke while reading the response", e);
        }
    }

    /// <summary>One whole response (compaction). The wire JSON is parsed as it came, not through the SDK's model.</summary>
    // Python: loop.py Agent.server_summary, Agent.summarize (the transport)
    public async Task<ModelResponse> CreateAsync(MessageRequest request, CancellationToken ct)
    {
        try
        {
            using var response = await Client.Beta.Messages.WithRawResponse.Create(ToCreate(MessageRequestJson.Body(request)), ct);
            var message = JsonNode.Parse(await response.ReadAsString(ct)) as JsonObject
                ?? throw new InvalidOperationException("the response was not a JSON object");
            return ModelResponse.From((JsonObject)Json.StripNulls(message));
        }
        catch (IOException e) when (!OwnFailure.IsOurs(e) && ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("cancelled by the caller", e, ct);
        }
        catch (IOException e) when (!OwnFailure.IsOurs(e))
        {
            throw new HttpRequestException("the connection broke while reading the response", e);
        }
    }

    // Python: cli.py context_report (count_tokens)
    public async Task<long> CountTokensAsync(MessageRequest request, CancellationToken ct)
    {
        try
        {
            var (headers, rest) = Split(MessageRequestJson.CountBody(request));
            var count = await Client.Beta.Messages.CountTokens(BetaCount.FromRawUnchecked(headers, new Dictionary<string, JsonElement>(), rest), ct);
            return count.InputTokens;
        }
        catch (IOException e) when (!OwnFailure.IsOurs(e) && ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("cancelled by the caller", e, ct);
        }
        catch (IOException e) when (!OwnFailure.IsOurs(e))
        {
            throw new HttpRequestException("the connection broke while reading the response", e);
        }
    }

    // request_args puts "betas" in the body; the SDK wants it as the anthropic-beta header. The body is
    // copied into elements, so the caller's JsonObject is never touched.
    // Python: loop.py Agent.request_args (the "betas" key)
    static (Dictionary<string, JsonElement> Headers, Dictionary<string, JsonElement> Body) Split(JsonObject body)
    {
        var elements = Json.ToElements(body);
        var headers = new Dictionary<string, JsonElement>();
        if (elements.Remove("betas", out var betas) && betas.ValueKind == JsonValueKind.Array && betas.GetArrayLength() > 0)
            headers["anthropic-beta"] = betas; // an array becomes one comma-joined header
        return (headers, elements);
    }

    static BetaCreate ToCreate(JsonObject body)
    {
        var (headers, rest) = Split(body);
        return BetaCreate.FromRawUnchecked(headers, new Dictionary<string, JsonElement>(), rest);
    }
}
