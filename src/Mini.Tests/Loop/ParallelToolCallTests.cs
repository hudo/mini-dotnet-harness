using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;

namespace Mini.Tests.Loop;

/// <summary>Two <c>edit</c> calls in one reply, then the request that carries both results. P2's smoke run once failed that request with
/// "request failed: ArgumentException" and the cause was never found. The shape is run here through everything that builds, sends or replays it
/// (the fake client, then the real SDK transport against a local server, on Claude's profile and on another provider's), and it passes: the
/// finding was not reproduced (P7). The tests stay as regression tests. Suspects ruled out: a JsonNode put under a second parent (that throws
/// InvalidOperationException, and every such place copies), duplicate keys in a parsed object (JsonNode.Parse on .NET 10 keeps the last), and an
/// unpaired surrogate in a string (serialized with a replacement character).</summary>
public class ParallelToolCallTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static JsonObject Edit(string path, string old, string @new, string? id = null) =>
        Tool("edit", Json.Obj(("path", path), ("old", old), ("new", @new)), id);

    static JsonObject Read(string path) => Tool("read", Json.Obj(("path", path)));

    // C#-only: the P2 finding. Two edits of one file in one reply (the second one fails on ambiguity or succeeds on its own text), then the follow-up request.
    [Fact]
    public async Task TwoParallelEditsOfOneFileLeaveAValidTranscriptAndARequestThatBuilds()
    {
        using var ws = new Workspace();
        ws.Write("a.txt", "alpha\nbeta\n");
        var (agent, fake) = ws.Make([
            Reply("tool_use", Read("a.txt")),
            Reply("tool_use", Edit("a.txt", "alpha", "ALPHA"), Edit("a.txt", "beta", "BETA")),
            Reply(Text("done")),
        ]);
        using var console = new ConsoleCapture();

        var stop = await agent.TurnAsync("edit both", ct: Ct);

        Assert.DoesNotContain("request failed", console.Err);
        Assert.Equal("end_turn", stop);
        Assert.Equal("done", agent.FinalText());
        var results = fake.Requests[^1]["messages"]!.AsArray()[^1]!["content"]!.AsArray();
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Null(r!["is_error"]));
        Assert.Equal("ALPHA\nBETA\n", File.ReadAllText(ws.Path("a.txt")).ReplaceLineEndings("\n"));
        TranscriptAssert.AssertAppendOnly(fake.Requests);
    }

    // C#-only: the P2 finding, through the real transport (the SDK builds the request from the body, the stream is folded by StreamAccumulator),
    // against a local server: two parallel edits, each input streamed in two pieces, then the follow-up request that carries both results.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoParallelEditsThroughTheRealTransport(bool sameFile)
    {
        using var ws = new Workspace();
        ws.Write("a.txt", "alpha\nbeta\n");
        ws.Write("b.txt", "gamma\n");
        var second = sameFile ? "a.txt" : "b.txt";
        using var server = new FakeApiServer([
            FakeApiServer.Sse(("read", """{"path": "a.txt"}"""), ("read", """{"path": "b.txt"}""")),
            FakeApiServer.Sse(("edit", """{"path": "a.txt", "old": "alpha", "new": "ALPHA"}"""),
                ("edit", $$"""{"path": "{{second}}", "old": "{{(sameFile ? "beta" : "gamma")}}", "new": "NEW"}""")),
            FakeApiServer.Sse()]);
        using var env = new EnvScope(("ANTHROPIC_API_KEY", "test-key"), ("ANTHROPIC_BASE_URL", server.Url));
        using var console = new ConsoleCapture();
        var agent = ws.Open(ws.NewSession(), ws.RealSetup("claude-haiku-5-5"));

        var stop = await agent.TurnAsync("edit both", ct: Ct);

        Assert.DoesNotContain("request failed", console.Err);
        Assert.Equal("end_turn", stop);
        Assert.Equal(3, server.Bodies.Count);
        var results = JsonNode.Parse(server.Bodies[2])!["messages"]!.AsArray()[^1]!["content"]!.AsArray();
        Assert.Equal(["toolu_0", "toolu_1"], results.Select(r => Json.Str(r!["tool_use_id"])));
        Assert.All(results, r => Assert.Null(r!["is_error"]));
        Assert.Equal(sameFile ? "ALPHA\nNEW\n" : "ALPHA\nbeta\n", File.ReadAllText(ws.Path("a.txt")).ReplaceLineEndings("\n"));
    }

    // C#-only: the P2 smoke run was on DeepSeek: another provider's profile (no betas or thinking options), a thinking block before the parallel calls,
    // and its own key. The model is a copy of deepseek-flash in a models.json override, pointed at the local server.
    [Fact]
    public async Task TwoParallelEditsThroughTheRealTransportOnAnotherProviderWithThinking()
    {
        var home = Directory.CreateTempSubdirectory("mini-tests-parallel-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(home, "models.json"), """
                {"models": {"ds-local": {"base_url": "http://127.0.0.1:1/anthropic", "key": "DS_LOCAL_KEY", "window": 1048576, "compact_at": 800000,
                  "efforts": ["low", "high", "max"], "prices": {"input": 0.3, "output": 1.2, "cache_read": 0.006, "cache_write": 0.3}}}}
                """);
            using var server = new FakeApiServer([
                FakeApiServer.Sse("let me read", ("read", """{"path": "a.txt"}""")),
                FakeApiServer.Sse("two edits", ("edit", """{"path": "a.txt", "old": "alpha", "new": "ALPHA"}"""), ("edit", """{"path": "a.txt", "old": "beta", "new": "BETA"}""")),
                FakeApiServer.Sse()]);
            File.WriteAllText(Path.Combine(home, "models.json"), File.ReadAllText(Path.Combine(home, "models.json")).Replace("http://127.0.0.1:1", server.Url));
            using var ws = new Workspace(o => o with { NetHome = home });
            ws.Write("a.txt", "alpha\nbeta\n");
            using var env = new EnvScope(("DS_LOCAL_KEY", "ds-key"));
            using var console = new ConsoleCapture();
            var agent = ws.Open(ws.NewSession(), ws.RealSetup("ds-local"));

            var stop = await agent.TurnAsync("edit both", ct: Ct);

            Assert.DoesNotContain("request failed", console.Err);
            Assert.Equal("end_turn", stop);
            var last = JsonNode.Parse(server.Bodies[2])!.AsObject();
            Assert.Null(last["betas"]);
            var messages = last["messages"]!.AsArray();
            Assert.Equal(["thinking", "tool_use", "tool_use"], messages[^2]!["content"]!.AsArray().Select(b => Json.Str(b!["type"])));
            Assert.Equal(["toolu_0", "toolu_1"], messages[^1]!["content"]!.AsArray().Select(b => Json.Str(b!["tool_use_id"])));
            Assert.Equal("ALPHA\nBETA\n", File.ReadAllText(ws.Path("a.txt")).ReplaceLineEndings("\n"));
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    // C#-only: the same on two files, and with the SAME tool_use id twice (a model that repeats an id must not break the request: ids are only echoed)
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoParallelEditsOfTwoFilesLeaveAValidTranscriptAndARequestThatBuilds(bool sameId)
    {
        using var ws = new Workspace();
        ws.Write("a.txt", "alpha\n");
        ws.Write("b.txt", "beta\n");
        var (agent, fake) = ws.Make([
            Reply("tool_use", Read("a.txt"), Read("b.txt")),
            Reply("tool_use", Edit("a.txt", "alpha", "ALPHA", sameId ? "toolu_same" : null), Edit("b.txt", "beta", "BETA", sameId ? "toolu_same" : null)),
            Reply(Text("done")),
        ]);
        using var console = new ConsoleCapture();

        var stop = await agent.TurnAsync("edit both", ct: Ct);

        Assert.DoesNotContain("request failed", console.Err);
        Assert.Equal("end_turn", stop);
        Assert.Equal("ALPHA\n", File.ReadAllText(ws.Path("a.txt")).ReplaceLineEndings("\n"));
        Assert.Equal("BETA\n", File.ReadAllText(ws.Path("b.txt")).ReplaceLineEndings("\n"));
        TranscriptAssert.AssertAppendOnly(fake.Requests);
    }
}
