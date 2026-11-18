using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;
using static Mini.Tests.Tools.ToolsHelpers;

namespace Mini.Tests.Tools;

/// <summary>Image reads: the image block the model gets, sizes from headers, the size limits and the damaged-PNG refusal.</summary>
public class ImageToolsTests
{
    // Python test: test_an_image_read_reaches_the_model_as_an_image_block
    [Fact]
    public async Task AnImageReadReachesTheModelAsAnImageBlock()
    {
        using var ws = new Workspace();
        File.WriteAllBytes(ws.Path("shot.png"), Png(40, 30));
        var t = Tool("read", Json.Obj(("path", "shot.png")));
        var (agent, fake) = ws.Make([Reply("tool_use", t), Reply(Text("a white box"))]);

        await agent.TurnAsync("what's in shot.png?", ct: TestContext.Current.CancellationToken);

        TranscriptAssert.AssertAppendOnly(fake.Requests);
        var result = FirstResult(fake.Requests[1]);
        var content = result["content"]!.AsArray();
        Assert.Equal(2, content.Count);
        Assert.Contains("40x30 px", Json.Str(content[0]!["text"]));
        Assert.Null(result["is_error"]);
        var image = content[1]!;
        Assert.Equal("image", Json.Str(image["type"]));
        Assert.Equal("image/png", Json.Str(image["source"]!["media_type"]));
        Assert.Equal(File.ReadAllBytes(ws.Path("shot.png")), Convert.FromBase64String(Json.Str(image["source"]!["data"])!));
        // the image is in the session file, so resume replays it
        var loaded = SessionReplayer.Replay(agent.Session).Messages;
        Assert.Equal(new JsonArray(agent.Messages.Select(m => (JsonNode?)m.ToJson()).ToArray()).ToJsonString(),
            new JsonArray(loaded.Select(m => (JsonNode?)m.ToJson()).ToArray()).ToJsonString());
    }

    // Python test: test_image_sizes_come_from_the_header_and_big_ones_are_refused
    [Fact]
    public async Task ImageSizesComeFromTheHeaderAndBigOnesAreRefused()
    {
        using var ws = new Workspace();
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, .. "JFIF\0"u8.ToArray(), .. new byte[9], 0xFF, 0xFF, // APP0, a fill byte
            0xFF, 0xC2, 0, 17, 8, 1, 0x2C, 1, 0x90, .. new byte[10]]; // SOF2: 300 high, 400 wide
        static byte[] Webp(string kind, byte[] size) => [.. "RIFF"u8.ToArray(), .. new byte[4], .. "WEBP"u8.ToArray(), .. System.Text.Encoding.ASCII.GetBytes(kind), .. new byte[4], .. size];
        static byte[] Le3(int v) => [(byte)v, (byte)(v >> 8), (byte)(v >> 16)];
        static byte[] Le2(int v) => [(byte)v, (byte)(v >> 8)];
        static byte[] Le4(long v) => [(byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24)];

        Assert.Equal(("image/png", 5, 7), ImageSniffer.Read(Png(5, 7)));
        Assert.Equal(("image/jpeg", 400, 300), ImageSniffer.Read(jpeg));
        Assert.Equal(("image/gif", 12, 9), ImageSniffer.Read([.. "GIF89a"u8.ToArray(), .. Le2(12), .. Le2(9)]));
        Assert.Equal(("image/webp", 640, 480), ImageSniffer.Read(Webp("VP8X", [.. new byte[4], .. Le3(639), .. Le3(479)])));
        Assert.Equal(("image/webp", 100, 50), ImageSniffer.Read(Webp("VP8L", [0x2F, .. Le4(99 | 49 << 14)])));
        Assert.Equal(("image/webp", 320, 200), ImageSniffer.Read(Webp("VP8 ", [.. new byte[6], .. Le2(320), .. Le2(200)])));
        Assert.Null(ImageSniffer.Read("plain text"u8.ToArray()));
        Assert.Equal(("image/jpeg", 0, 0), ImageSniffer.Read([0xFF, 0xD8, 0xFF]));

        var t = Headless(ws);
        File.WriteAllBytes(ws.Path("wide.png"), Png(3001, 2)); // over 20 images, the API refuses any past 3000 px, and for good
        File.WriteAllBytes(ws.Path("odd.jpg"), [0xFF, 0xD8, 0xFF, 0x00]);
        foreach (var (name, why) in new[] { ("wide.png", "3001x2 px"), ("odd.jpg", "size unreadable") })
        {
            var e = await Assert.ThrowsAsync<ToolException>(() => t.ReadAsync(name, ct: TestContext.Current.CancellationToken));
            Assert.Matches(why, e.Message);
        }
        ws.Write("notes.txt", "not an image\n");
        Assert.Equal("    1\tnot an image", ReadText(await t.ReadAsync("notes.txt", ct: TestContext.Current.CancellationToken)));
        var outside = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ws.Root)!, "elsewhere.png");
        var denied = await Assert.ThrowsAsync<ToolException>(() => t.ReadAsync(outside, ct: TestContext.Current.CancellationToken)); // the read gate is the same for images
        Assert.Matches("denied this read", denied.Message);
    }

    // Python test: test_a_damaged_png_is_refused_before_it_reaches_the_model
    [Fact]
    public async Task ADamagedPngIsRefusedBeforeItReachesTheModel()
    {
        using var ws = new Workspace();
        var good = Png(5, 7);
        var i = IndexOf(good, "IDAT"u8);
        var bad = (byte[])good.Clone();
        bad[i + 4] ^= 1; // one bit of the image data flipped

        Assert.Null(PngValidator.Damage(good));
        Assert.Equal($"chunk IDAT at byte {i - 4} fails its CRC check", PngValidator.Damage(bad));
        Assert.Equal("it ends before its IEND chunk", PngValidator.Damage(good[..(IndexOf(good, "IEND"u8) - 4)]));
        var t = Auto(ws);
        File.WriteAllBytes(ws.Path("bad.png"), bad);

        var e = await Assert.ThrowsAsync<ToolException>(() => t.ReadAsync("bad.png", ct: TestContext.Current.CancellationToken));

        Assert.Matches("This PNG is damaged .chunk IDAT at byte .* fails its CRC check", e.Message);
        Assert.Equal(0, t.Held); // nothing of it was kept for the transcript
    }

    // C#-only: the 20 MB held-images budget is checked before another image is accepted, and nothing is added when it refuses.
    [Fact]
    public async Task ReadRefusesAnImageThatWouldExceedTheHeldBudget()
    {
        using var ws = new Workspace();
        File.WriteAllBytes(ws.Path("a.png"), Png(40, 30));
        var t = Auto(ws);
        t.Held = 20_000_000 - 10; // base64 of any real PNG is longer than 10 bytes

        var e = await Assert.ThrowsAsync<ToolException>(() => t.ReadAsync("a.png", ct: TestContext.Current.CancellationToken));

        Assert.Matches("already holds .* MB of images", e.Message);
        Assert.Equal(20_000_000 - 10, t.Held);
    }

    static int IndexOf(byte[] data, ReadOnlySpan<byte> needle) => data.AsSpan().IndexOf(needle);
}
