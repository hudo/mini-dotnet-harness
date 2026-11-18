using System.IO.Compression;
using System.Text.Json.Nodes;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests.Loop;

/// <summary>Small helpers for the loop lane (nothing shared: the shared Fakes/Assertions are not edited).</summary>
internal static class LoopHelpers
{
    /// <summary>The messages array of a request body.</summary>
    public static JsonArray Msgs(JsonObject request) => request["messages"]!.AsArray();

    /// <summary>The roles of a request's messages, in order.</summary>
    public static List<string?> Roles(JsonObject request) => Msgs(request).Select(m => Json.Str(m!["role"])).ToList();

    /// <summary>The roles of a list of session-file messages, in order.</summary>
    public static List<string?> Roles(IEnumerable<JsonObject> messages) => messages.Select(m => Json.Str(m["role"])).ToList();

    /// <summary>The roles of an agent's transcript, in order.</summary>
    public static List<string?> Roles(IEnumerable<Message> messages) => messages.Select(m => (string?)m.ToJson()["role"]!.GetValue<string>()).ToList();

    /// <summary>JSON text of a node, the comparison form used everywhere here (key order counts).</summary>
    public static string? J(JsonNode? node) => node?.ToJsonString();

    /// <summary>JSON text of a transcript.</summary>
    public static string J(IEnumerable<Message> messages) => J(messages.Select(m => m.ToJson()));

    /// <summary>JSON text of a list of messages.</summary>
    public static string J(IEnumerable<JsonObject> messages) => new JsonArray(messages.Select(m => (JsonNode)m.DeepClone()).ToArray()).ToJsonString();

    /// <summary>{"type": "compaction", "content": ..., "signature": ...}, as the server's summary block.</summary>
    public static JsonObject CompactionBlock(string content = "S", string signature = "c") =>
        Json.Obj(("type", "compaction"), ("content", content), ("signature", signature));

    /// <summary>A reply that is only a compaction block (what the compaction request gets back).</summary>
    public static JsonObject CompactionReply(JsonObject? block = null) => Reply("compaction", block ?? CompactionBlock());

    /// <summary>A tool_use reply with a usage of this many input tokens.</summary>
    public static JsonObject ToolReply(JsonObject call, int inp) => ReplyWith([call], "tool_use", usage: Usage(inp));

    /// <summary>A text reply with a usage of this many input tokens.</summary>
    public static JsonObject BigReply(string text, int inp) => ReplyWith([Text(text)], usage: Usage(inp));

    /// <summary>A real PNG, all white, from the standard library (tests/test_mini.py png).</summary>
    public static byte[] Png(int w, int h)
    {
        static byte[] Be(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
        static uint Crc(byte[] data)
        {
            var c = 0xFFFFFFFFu;
            foreach (var b in data)
            {
                c ^= b;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            return ~c;
        }
        static byte[] Chunk(string type, byte[] body)
        {
            var td = System.Text.Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
            return Be((uint)body.Length).Concat(td).Concat(Be(Crc(td))).ToArray();
        }
        var raw = new List<byte>();
        for (var y = 0; y < h; y++)
        {
            raw.Add(0);
            raw.AddRange(Enumerable.Repeat((byte)0xFF, w));
        }
        using var zipped = new MemoryStream();
        using (var z = new ZLibStream(zipped, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw.ToArray());
        byte[] ihdr = [.. Be((uint)w), .. Be((uint)h), 8, 0, 0, 0, 0];
        return [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR", ihdr), .. Chunk("IDAT", zipped.ToArray()), .. Chunk("IEND", [])];
    }
}
