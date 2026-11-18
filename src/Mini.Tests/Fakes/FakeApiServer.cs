using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Mini.Tests.Fakes;

/// <summary>A local server that plays the Messages API: it answers the n-th request with the n-th scripted server-sent-event stream and keeps the request bodies.
/// Point the real client or a real <c>mini</c> process at <see cref="Url"/> (<c>ANTHROPIC_BASE_URL</c>) to run the real transport without a key.</summary>
// Python test helper: tests/test_mini.py test_with_the_real_sdk_only_a_failure_after_the_stream_starts_gets_retried (its http.server API)
public sealed class FakeApiServer : IDisposable
{
    readonly HttpListener _listener = new();
    readonly Queue<string> _replies;

    /// <summary>The server's base URL.</summary>
    public string Url { get; }

    /// <summary>The body of every request received, in order.</summary>
    public List<string> Bodies { get; } = [];

    /// <summary>Starts serving; one scripted stream per request.</summary>
    public FakeApiServer(IEnumerable<string> replies)
    {
        _replies = new Queue<string>(replies);
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add($"{Url}/");
        _listener.Start();
        _ = Task.Run(Serve);
    }

    async Task Serve()
    {
        try
        {
            while (_replies.Count > 0)
            {
                var context = await _listener.GetContextAsync();
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                    Bodies.Add(await reader.ReadToEndAsync());
                var bytes = Encoding.UTF8.GetBytes(_replies.Dequeue());
                context.Response.ContentType = "text/event-stream";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { } // stopped by Dispose
    }

    /// <summary>One reply as server-sent events: one <c>tool_use</c> block per call (its input JSON streamed in two pieces, as a real stream does), or with no
    /// calls a text answer. The blocks are numbered from 0 and the ids are <c>toolu_0</c>, <c>toolu_1</c>, …</summary>
    public static string Sse(params (string Name, string InputJson)[] calls) => Sse(null, calls);

    /// <summary>The same, after a thinking block (index 0) with this text and a signature, as the extended-thinking models stream one.</summary>
    public static string Sse(string? thinking, params (string Name, string InputJson)[] calls)
    {
        var sb = new StringBuilder("event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"m\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-haiku-5-5\","
            + "\"content\":[],\"stop_reason\":null,\"stop_sequence\":null,\"usage\":{\"input_tokens\":10,\"output_tokens\":0}}}\n\n");
        void Block(int i, string start, params string[] deltas)
        {
            sb.Append($"event: content_block_start\ndata: {{\"type\":\"content_block_start\",\"index\":{i},\"content_block\":{start}}}\n\n");
            foreach (var d in deltas)
                sb.Append($"event: content_block_delta\ndata: {{\"type\":\"content_block_delta\",\"index\":{i},\"delta\":{d}}}\n\n");
            sb.Append($"event: content_block_stop\ndata: {{\"type\":\"content_block_stop\",\"index\":{i}}}\n\n");
        }
        var first = 0;
        if (thinking is not null)
        {
            Block(first++, "{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"\"}", $"{{\"type\":\"thinking_delta\",\"thinking\":{JsonSerializer.Serialize(thinking)}}}",
                "{\"type\":\"signature_delta\",\"signature\":\"sig-1\"}");
        }

        if (calls.Length == 0)
            Block(first, "{\"type\":\"text\",\"text\":\"\"}", "{\"type\":\"text_delta\",\"text\":\"ok\"}");
        for (var n = 0; n < calls.Length; n++)
        {
            var i = first + n;
            var half = calls[n].InputJson.Length / 2;
            Block(i, $"{{\"type\":\"tool_use\",\"id\":\"toolu_{n}\",\"name\":\"{calls[n].Name}\",\"input\":{{}}}}",
                $"{{\"type\":\"input_json_delta\",\"partial_json\":{JsonSerializer.Serialize(calls[n].InputJson[..half])}}}",
                $"{{\"type\":\"input_json_delta\",\"partial_json\":{JsonSerializer.Serialize(calls[n].InputJson[half..])}}}");
        }
        return sb.Append($"event: message_delta\ndata: {{\"type\":\"message_delta\",\"delta\":{{\"stop_reason\":\"{(calls.Length == 0 ? "end_turn" : "tool_use")}\",\"stop_sequence\":null}},"
            + "\"usage\":{\"output_tokens\":1}}\n\nevent: message_stop\ndata: {\"type\":\"message_stop\"}\n\n").ToString();
    }

    /// <summary>Stops listening.</summary>
    public void Dispose() => _listener.Close();
}
