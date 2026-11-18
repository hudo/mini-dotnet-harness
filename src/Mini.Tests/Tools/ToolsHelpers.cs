using System.IO.Compression;
using System.Text;
using Mini.Tests.Fakes;

namespace Mini.Tests.Tools;

/// <summary>Helpers private to the Tools lane: a real PNG, a directory outside the workspace, tool construction.</summary>
static class ToolsHelpers
{
    /// <summary>The tools over the workspace with auto-approving settings (Python: LocalTools(str(ws), Approver(auto=True))).</summary>
    public static ToolKit Auto(Workspace ws) => ws.Tools(new ApprovalSettings(auto: true));

    /// <summary>The tools over the workspace whose settings deny whatever needs approval (Approver(interactive=False)).</summary>
    public static ToolKit Headless(Workspace ws, TestRuntime? runtime = null) => ws.Tools(new ApprovalSettings(interactive: false), runtime);

    /// <summary>Python: os.utime(f, (1, 1)).</summary>
    public static void Touch1(string path) => File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddSeconds(1));

    /// <summary>The text of a read result (a string, or a list of blocks whose first is the text).</summary>
    public static string ReadText(System.Text.Json.Nodes.JsonNode node) => Json.Str(node)!;

    /// <summary>The first block of the last message of a request: the first tool_result the loop sent back.</summary>
    public static System.Text.Json.Nodes.JsonNode FirstResult(System.Text.Json.Nodes.JsonObject request)
    {
        var messages = request["messages"]!.AsArray();
        return messages[messages.Count - 1]!["content"]![0]!;
    }

    /// <summary>A new directory outside the workspace, as the OS reports it (macOS's /var resolved).</summary>
    public static string OutsideDir() => PathResolver.RealPath(Directory.CreateTempSubdirectory("mini-tests-out-").FullName);

    // zlib.crc32
    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(k =>
    {
        var c = (uint)k;
        for (var b = 0; b < 8; b++) c = (c & 1) != 0 ? 0xEDB88320 ^ c >> 1 : c >> 1;
        return c;
    }).ToArray();

    static uint Crc32(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ c >> 8;
        return c ^ 0xFFFFFFFF;
    }

    static byte[] Be32(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    static byte[] Chunk(string type, byte[] data)
    {
        var body = Encoding.Latin1.GetBytes(type).Concat(data).ToArray();
        return [.. Be32((uint)data.Length), .. body, .. Be32(Crc32(body))];
    }

    // Python test helper: png(w, h) — a real PNG, all white
    public static byte[] Png(int w, int h)
    {
        var raw = new List<byte>();
        for (var y = 0; y < h; y++)
        {
            raw.Add(0);
            raw.AddRange(Enumerable.Repeat((byte)0xFF, w));
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw.ToArray());
        byte[] ihdr = [.. Be32((uint)w), .. Be32((uint)h), 8, 0, 0, 0, 0];
        return [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR", ihdr), .. Chunk("IDAT", ms.ToArray()), .. Chunk("IEND", [])];
    }
}
