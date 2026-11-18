using System.Text.Json.Nodes;
using Xunit.Sdk;

namespace Mini.Tests.Fakes;

/// <summary>A stream that shows the reply's text, then throws <paramref name="Error"/>, or with none a <see cref="StreamCutException"/> (no stop reason).</summary>
// Python test helper: tests/fake.py Broken
public sealed record Broken(JsonObject Reply, Exception? Error = null);

/// <summary>
/// A scripted <see cref="IModelClient"/>. Each script item is a reply <see cref="JsonObject"/> (returned), an
/// <see cref="Exception"/> (thrown before onStart, so not retried), a <see cref="Broken"/>, or a
/// <c>Func&lt;JsonObject, object&gt;</c> that takes the request and returns one of those.
/// </summary>
// Python test helper: tests/fake.py FakeClient, _Messages, _Stream, usage, reply, text, thinking, tool
public sealed class FakeModelClient(IEnumerable<object> script) : IModelClient
{
    static int _ids; // one counter for msg_N and toolu_N, like fake.py's itertools.count; never reset

    readonly Queue<object> _script = new(script);

    /// <summary>The JSON body of every <see cref="StreamAsync"/> request, in order.</summary>
    public List<JsonObject> Requests { get; } = [];

    /// <summary>The JSON body of every <see cref="CreateAsync"/> request (compaction), in order.</summary>
    public List<JsonObject> Creates { get; } = [];

    /// <summary>The JSON body of every <see cref="CountTokensAsync"/> request.</summary>
    public List<JsonObject> Counts { get; } = [];

    /// <summary>What <see cref="CountTokensAsync"/> returns.</summary>
    public long TokenCount { get; set; }

    /// <summary>Script items not used yet.</summary>
    public int Remaining => _script.Count;

    /// <summary>Appends one more script item.</summary>
    public void Enqueue(object item) => _script.Enqueue(item);

    object Next(JsonObject body)
    {
        if (_script.Count == 0)
            throw new XunitException("fake client ran out of scripted replies");
        var item = _script.Dequeue();
        return item is Func<JsonObject, object> f ? f(body) : item;
    }

    // The real Api returns the message with nulls stripped (Json.StripNulls), so stop_sequence: null is gone.
    static JsonObject Stripped(JsonObject reply) => (JsonObject)Json.StripNulls(reply);

    /// <inheritdoc/>
    public Task<ModelResponse> StreamAsync(MessageRequest request, IStreamObserver observer, CancellationToken ct)
    {
        var body = MessageRequestJson.Body(request); // the JSON the real client would send; a fresh copy each call
        Requests.Add((JsonObject)body.DeepClone());
        var item = Next(body);
        if (item is Exception e)
            throw e;
        var (reply, error, whole) = item switch
        {
            Broken b => (b.Reply, b.Error ?? new StreamCutException("the stream ended before the response was complete"), false),
            JsonObject r => (r, null, true),
            _ => throw new XunitException($"fake client: unsupported script item {item?.GetType().Name ?? "null"}"),
        };
        observer.OnStarted();
        foreach (var block in (reply["content"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            if (Json.Str(block["type"]) == "text")
                observer.OnText(Json.Str(block["text"]) ?? "");
        }

        if (!whole)
            throw error!;
        return Task.FromResult(ModelResponse.From(Stripped(reply)));
    }

    /// <inheritdoc/>
    public Task<ModelResponse> CreateAsync(MessageRequest request, CancellationToken ct)
    {
        var body = MessageRequestJson.Body(request);
        Creates.Add((JsonObject)body.DeepClone());
        var item = Next(body);
        return item switch
        {
            Exception e => throw e,
            JsonObject r => Task.FromResult(ModelResponse.From(Stripped(r))),
            _ => throw new XunitException($"fake client: unsupported script item {item?.GetType().Name ?? "null"}"),
        };
    }

    /// <inheritdoc/>
    public Task<long> CountTokensAsync(MessageRequest request, CancellationToken ct)
    {
        Counts.Add(MessageRequestJson.CountBody(request));
        return Task.FromResult(TokenCount);
    }

    /// <summary>A minimal request, for a test that calls the client directly. The model name is the only thing in it a scripted function can read.</summary>
    public static MessageRequest Request(string model = "x") => new MessageRequestBuilder().Model(model).Messages([]).Build();

    /// <summary>A usage object; <c>@out</c> because <c>out</c> is a C# keyword.</summary>
    public static JsonObject Usage(int inp = 1000, int @out = 50, int read = 0, int write = 0) =>
        Json.Obj(("input_tokens", inp), ("output_tokens", @out), ("cache_read_input_tokens", read), ("cache_creation_input_tokens", write));

    /// <summary>An <c>end_turn</c> reply with these content blocks.</summary>
    public static JsonObject Reply(params JsonObject[] blocks) => ReplyWith(blocks);

    /// <summary>A reply with this stop reason and blocks (<c>Reply("tool_use", call)</c>).</summary>
    public static JsonObject Reply(string stop, params JsonObject[] blocks) => ReplyWith(blocks, stop);

    /// <summary>A reply with every field settable; keys in fake.py's order, ids <c>msg_N</c>. Blocks are cloned, so a test can keep using its own.</summary>
    public static JsonObject ReplyWith(IEnumerable<JsonObject> blocks, string stop = "end_turn", string model = "claude-opus-5-5",
        JsonObject? usage = null, JsonNode? transformations = null)
    {
        var data = Json.Obj(("id", $"msg_{Interlocked.Increment(ref _ids)}"), ("type", "message"), ("role", "assistant"),
            ("model", model), ("content", new JsonArray(blocks.Select(b => (JsonNode)b.DeepClone()).ToArray())),
            ("stop_reason", stop), ("stop_sequence", null), ("usage", (usage ?? Usage()).DeepClone()));
        if (transformations is not null)
            data["input_transformations"] = transformations.DeepClone();
        return data;
    }

    /// <summary>A text block.</summary>
    public static JsonObject Text(string t) => Json.Obj(("type", "text"), ("text", t));

    /// <summary>An empty thinking block with this signature.</summary>
    public static JsonObject Thinking(string sig = "sig") => Json.Obj(("type", "thinking"), ("thinking", ""), ("signature", sig));

    /// <summary>A tool_use block; the id is <c>toolu_N</c> unless given.</summary>
    public static JsonObject Tool(string name, JsonObject input, string? id = null) =>
        Json.Obj(("type", "tool_use"), ("id", id ?? $"toolu_{Interlocked.Increment(ref _ids)}"), ("name", name), ("input", input));
}
