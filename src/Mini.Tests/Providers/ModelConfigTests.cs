using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;
using static Mini.Tests.Providers.ProvidersHelpers;

namespace Mini.Tests.Providers;

/// <summary>models.json: defaults, overrides, per-model output caps; and the tools the provider config switches on or off.</summary>
public class ModelConfigTests
{
    // Python test: test_web_search_can_be_switched_off
    [Fact]
    public void WebSearchCanBeSwitchedOff()
    {
        using var ws = new Workspace(o => o with { NoWeb = true });

        var (agent, _) = ws.Make([]);

        Assert.DoesNotContain("web_search", ToolNames(agent));
    }

    // Python test: test_models_come_from_config
    // "A new Claude model needs only a models.json entry: unlisted fields fall back to the claude-* defaults."
    [Fact]
    public async Task ModelsComeFromConfig()
    {
        using var ws = new Workspace();
        using var entries = new ModelEntries(ws.Runtime.Catalog, ("claude-next", Json.Obj(("fallbacks", false), ("compact_at", 900),
            ("prices", Json.Obj(("input", 1), ("output", 2), ("cache_read", 0.1), ("cache_write", 1.25))))));
        var ct = TestContext.Current.CancellationToken;
        var (agent, fake) = ws.Make([ReplyWith([Text("x")], model: "claude-next", usage: Usage(inp: 1_000_000, @out: 1_000_000)),
            Reply("compaction", Json.Obj(("type", "compaction"), ("content", "Summary."), ("signature", "c")))], model: "claude-next");

        await agent.TurnAsync("hi", ct: ct);

        Assert.Equal(1 + 2, agent.Cost, 6);
        Assert.Equal(1_000_000, agent.Window);
        Assert.Contains("xhigh", agent.Efforts);
        Assert.False(fake.Requests[0].ContainsKey("fallbacks"));
        Assert.True(fake.Requests[0].ContainsKey("thinking"));
        Assert.Equal(900, agent.CompactAt);
        // should_compact is private: MaybeCompactAsync runs the same check (context must be over the line, not on it)
        agent.LastContext = 900;
        await agent.MaybeCompactAsync(ct);
        Assert.Empty(fake.Creates);
        agent.LastContext = 901;
        await agent.MaybeCompactAsync(ct);
        Assert.Single(fake.Creates);

        // the shipped file names its three default models
        var catalog = ws.Runtime.Catalog;
        Assert.True(catalog.Models.ContainsKey(catalog.Main));
        Assert.True(catalog.Models.ContainsKey(catalog.SubModel));
        Assert.True(catalog.Models.ContainsKey(catalog.CheapModel));
    }

    // Python test: test_web_search_is_called_directly_without_the_code_sandbox
    [Fact]
    public async Task WebSearchIsCalledDirectlyWithoutTheCodeSandbox()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([Reply(Text("x"))]);

        await agent.TurnAsync("hi", ct: TestContext.Current.CancellationToken);

        var tools = fake.Requests[0]["tools"]!.AsArray();
        var expected = JsonNode.Parse("""{"type": "web_search_20260209", "name": "web_search", "allowed_callers": ["direct"]}""");
        Assert.Contains(tools, t => JsonNode.DeepEquals(t, expected));
        Assert.DoesNotContain("code_execution", tools.ToJsonString());
    }

    // Python test: test_each_model_gets_its_own_output_cap
    [Fact]
    public async Task EachModelGetsItsOwnOutputCap()
    {
        using var ws = new Workspace();
        var ct = TestContext.Current.CancellationToken;
        var sent = new Dictionary<string, int>();
        foreach (var model in new[] { "claude-haiku-5-5", "claude-opus-5-5", "claude-some-new-model" }) // the last has no entry: 64,000
        {
            var (agent, fake) = ws.Make([Reply(Text("hi"))], model: model);
            await agent.TurnAsync("hi", ct: ct);
            sent[model] = (int)Json.N(fake.Requests[0]["max_tokens"]);
        }
        Assert.Equal(new Dictionary<string, int> { ["claude-haiku-5-5"] = 128000, ["claude-opus-5-5"] = 128000, ["claude-some-new-model"] = 64000 }, sent);

        var (ds, dsFake) = ws.Ds([ReplyWith([Text("hi")], model: "deepseek-flash")]);
        await ds.TurnAsync("hi", ct: ct);
        Assert.Equal(64000, Json.N(dsFake.Requests[0]["max_tokens"])); // no max_output entry for it

        // what live tests do to reach the cap: the option, per runtime
        var (capped, cappedFake) = ws.Make([Reply(Text("hi")), Reply("compaction", Json.Obj(("type", "compaction"), ("content", "S."), ("signature", "c")))],
            runtime: new TestRuntime(agentOptions: new AgentOptions { MaxTokens = 2000 }));
        await capped.TurnAsync("hi", ct: ct);
        await capped.CompactAsync(ct);
        Assert.Equal(2000, Json.N(cappedFake.Requests[0]["max_tokens"]));
        Assert.Equal(16000, Json.N(cappedFake.Creates[0]["max_tokens"]));
    }

    // Python test: test_a_models_json_entry_changes_only_the_fields_it_names
    // Config reads models.json once per process, so (like the Python test) this runs the real program in a child process
    // with its own MINI_NET_HOME, pointed at a local server that plays the API: what it receives (max_tokens, from the
    // untouched claude-haiku-5-5 entry) and what mini prints (the cost, from the overridden price; the request stays under the 100k tier, whose own prices_over_100k the override does not touch) show the merge.
    [Fact]
    public async Task AModelsJsonEntryChangesOnlyTheFieldsItNames()
    {
        var ct = TestContext.Current.CancellationToken;
        var home = Directory.CreateTempSubdirectory("mini-tests-modelsjson-").FullName;
        var work = Directory.CreateTempSubdirectory("mini-tests-modelsjson-work-").FullName;
        File.WriteAllText(Path.Combine(home, "models.json"),
            """{"models": {"claude-haiku-5-5": {"prices": {"input": 9, "output": 9, "cache_read": 9, "cache_write": 9}}}}""");
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        string? requestBody = null;
        var served = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                requestBody = await reader.ReadToEndAsync(ct);
            const string stream = """
                event: message_start
                data: {"type":"message_start","message":{"id":"m","type":"message","role":"assistant","model":"claude-haiku-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":50000,"output_tokens":0}}}

                event: content_block_start
                data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

                event: content_block_delta
                data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"ok"}}

                event: content_block_stop
                data: {"type":"content_block_stop","index":0}

                event: message_delta
                data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":0}}

                event: message_stop
                data: {"type":"message_stop"}


                """;
            var bytes = Encoding.UTF8.GetBytes(stream.ReplaceLineEndings("\n"));
            context.Response.ContentType = "text/event-stream";
            await context.Response.OutputStream.WriteAsync(bytes, ct);
            context.Response.Close();
        }, ct);

        try
        {
            var info = new ProcessStartInfo("dotnet", [Path.Combine(AppContext.BaseDirectory, "mini.dll"), "-p", "hi", "--json",
                "--model", "claude-haiku-5-5"])
            {
                WorkingDirectory = work, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            info.Environment["MINI_NET_HOME"] = home;
            info.Environment["ANTHROPIC_API_KEY"] = "test-key";
            info.Environment["ANTHROPIC_BASE_URL"] = $"http://127.0.0.1:{port}";
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            Assert.True(process.ExitCode == 0, $"mini exited {process.ExitCode}: {await stderr}\n{output}");
            await served;

            var request = JsonNode.Parse(requestBody!)!;
            Assert.Equal(128000, Json.N(request["max_tokens"])); // the override named only prices: the output cap stays
            var result = JsonNode.Parse(output.Trim().Split('\n')[^1])!;
            Assert.Equal(0.45, (double)result["cost"]!, 6); // 50k input tokens at the overridden $9 per million (under haiku's 100k tier)
        }
        finally
        {
            listener.Close();
            Directory.Delete(home, recursive: true);
            Directory.Delete(work, recursive: true);
        }
    }

    static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
