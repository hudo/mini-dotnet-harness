using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;
using static Mini.Tests.Loop.LoopHelpers;

namespace Mini.Tests.Loop;

// Compaction thresholds: Python patches loop.COMPACT_AT to 500. In C# ModelCatalog.CompactAt is read once at startup and
// claude-opus-5-5 has no compact_at of its own: the threshold is the default 150_000 and these tests use a context of
// 200_000 input tokens where Python used 900/1000. (A test can now set CompactAt in its options; these keep the thresholds they were ported with.)

/// <summary>The agent loop: tool dispatch, interrupts, resume, compaction, output caps (tests/test_mini.py).</summary>
public class LoopTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Python test: test_parallel_calls_answered_in_one_message_and_errors_kept
    [Fact]
    public async Task ParallelCallsAnsweredInOneMessageAndErrorsKept()
    {
        using var ws = new Workspace();
        JsonObject a = Tool("edit", Json.Obj(("path", "missing.txt"), ("old", "x"), ("new", "y"))), b = Tool("bash", Json.Obj(("command", "echo hi")));
        var (agent, fake) = ws.Make([Reply("tool_use", a, b), Reply(Text("ok"))]);

        await agent.TurnAsync("go", ct: Ct);

        TranscriptAssert.AssertAppendOnly(fake.Requests);
        var results = Msgs(fake.Requests[1])[^1]!["content"]!.AsArray();
        Assert.Equal([Json.Str(a["id"]), Json.Str(b["id"])], results.Select(r => Json.Str(r!["tool_use_id"])));
        Assert.True(results[0]!["is_error"]!.GetValue<bool>());
        Assert.Contains("hi", Json.Str(results[1]!["content"]));
    }

    // Python test: test_max_tokens_with_tool_use_still_gets_results
    [Fact]
    public async Task MaxTokensWithToolUseStillGetsResults()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture();
        var t = Tool("bash", Json.Obj(("command", "echo hi")));
        var (agent, fake) = ws.Make([Reply("max_tokens", t), Reply(Text("ok"))]);

        Assert.Equal("end_turn", await agent.TurnAsync("go", ct: Ct));

        TranscriptAssert.AssertAppendOnly(fake.Requests);
        var content = Msgs(fake.Requests[1])[^1]!["content"]!.AsArray(); // the note rides with the result: roles alternate
        Assert.Equal(2, content.Count);
        Assert.Equal(Json.Str(t["id"]), Json.Str(content[0]!["tool_use_id"]));
        Assert.True(content[0]!["is_error"]!.GetValue<bool>());
        Assert.Contains("Not run", Json.Str(content[0]!["content"]));
        Assert.Equal("text", Json.Str(content[1]!["type"]));
        Assert.Contains("inside a tool call", Json.Str(content[1]!["text"]));
    }

    // Python test: test_interrupt_during_tools_backfills_every_result
    // Python makes the bash method raise KeyboardInterrupt; here the token is cancelled while a real `sleep 100` runs.
    [Fact]
    public async Task InterruptDuringToolsBackfillsEveryResult()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture();
        JsonObject a = Tool("bash", Json.Obj(("command", "sleep 100"))), b = Tool("bash", Json.Obj(("command", "echo later")));
        var (agent, _) = ws.Make([Reply("tool_use", a, b)]);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        Assert.Equal("interrupted", await agent.TurnAsync("go", ct: cts.Token));

        var results = agent.Messages[^1].Content.OfType<ToolResultBlock>().ToList();
        Assert.Equal([Json.Str(a["id"]), Json.Str(b["id"])], results.Select(r => r.ToolUseId));
        Assert.All(results, r => Assert.True(r.IsError));
        Assert.Equal("Interrupted by the user.", results[0].Text);
        Assert.Equal("Not run: interrupted by the user.", results[1].Text);
    }

    // Python test: test_interrupt_during_stream_keeps_roles_alternating
    [Fact]
    public async Task InterruptDuringStreamKeepsRolesAlternating()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var interrupt = (Func<JsonObject, object>)(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var (agent, fake) = ws.Make([interrupt, Reply(Text("back"))]);
        agent.Transcript.PendingNotes.Add("note");

        Assert.Equal("interrupted", await agent.TurnAsync("first", ct: cts.Token));

        Assert.Equal(["user", "system", "assistant"], Roles(agent.Messages));
        await agent.TurnAsync("second", ct: Ct);
        Assert.Equal(["user", "system", "assistant", "user"], Roles(fake.Requests[1]));
        TranscriptAssert.AssertAppendOnly(fake.Requests);
    }

    // Python test: test_pause_turn_resends
    [Fact]
    public async Task PauseTurnResends()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([Reply("pause_turn", Text("searching")), Reply(Text("found"))]);

        Assert.Equal("end_turn", await agent.TurnAsync("search", ct: Ct));

        Assert.Equal(2, fake.Requests.Count);
        TranscriptAssert.AssertAppendOnly(fake.Requests);
    }

    // Python test: test_resume_replays_frozen_system_and_tools
    [Fact]
    public async Task ResumeReplaysFrozenSystemAndTools()
    {
        using var ws = new Workspace();
        ws.Write("AGENTS.md", "Rule one.");
        var (agent, fake) = ws.Make([Reply(Text("one"))]);
        await agent.TurnAsync("hello", ct: Ct);
        ws.Write("AGENTS.md", "Rule two, edited mid-session.");

        var (again, fake2) = ws.Make([Reply(Text("two"))], session: ws.OpenSession(agent.Session.Path));
        await again.TurnAsync("again", ct: Ct);

        JsonObject first = fake.Requests[0], second = fake2.Requests[0];
        Assert.Equal(J(first["system"]), J(second["system"]));
        Assert.Contains("Rule one.", Json.Str(first["system"]![0]!["text"]));
        Assert.Equal(J(first["tools"]), J(second["tools"]));
        Assert.Equal(J(agent.Messages), J(Msgs(second).Take(2).Select(m => m!.AsObject())));
    }

    // Python test: test_only_agents_md_is_loaded
    [Fact]
    public void OnlyAgentsMdIsLoaded()
    {
        using var ws = new Workspace();
        ws.Write("AGENTS.md", "from agents");
        ws.Write("CLAUDE.md", "from claude");
        var (agent, _) = ws.Make([]);

        Assert.Contains("from agents", agent.System);
        Assert.DoesNotContain("from claude", agent.System);
    }

    // Python test: test_compaction_swaps_history_for_the_block
    [Fact]
    public async Task CompactionSwapsHistoryForTheBlock()
    {
        using var ws = new Workspace();
        var block = Json.Obj(("type", "compaction"), ("content", "Summary: user said hello."), ("signature", "c1"));
        var (agent, fake) = ws.Make([Reply(Text("one")), Reply("compaction", block), Reply(Text("two"))]);
        await agent.TurnAsync("hello", ct: Ct);

        Assert.True(await agent.CompactAsync(Ct));

        Assert.Equal("""{"type":"summarize"}""", J(fake.Creates[0]["compaction"]));
        Assert.False(fake.Creates[0].ContainsKey("context_management"));
        await agent.TurnAsync("next", ct: Ct);
        var msgs = Msgs(fake.Requests[^1]);
        Assert.Equal("""{"role":"assistant","content":[{"type":"compaction","content":"Summary: user said hello.","signature":"c1"}]}""", J(msgs[0]));
        Assert.Equal("""{"role":"user","content":"next"}""", J(msgs[1]));
        Assert.Equal(J(agent.Messages), J(SessionReplayer.Replay(agent.Session).Messages));
        Assert.Equal(J(fake.Requests[0]["system"]), J(fake.Requests[^1]["system"]));
    }

    // Python test: test_compaction_failure_keeps_history
    [Fact]
    public async Task CompactionFailureKeepsHistory()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture();
        var (agent, _) = ws.Make([Reply(Text("one")), Reply("max_tokens")]);
        await agent.TurnAsync("hello", ct: Ct);
        var before = J(agent.Messages);

        Assert.False(await agent.CompactAsync(Ct));

        Assert.Equal(before, J(agent.Messages));
        Assert.Contains("no summary came back", cap.Err);
    }

    // Python test: test_auto_compaction_triggers_on_threshold
    [Fact]
    public async Task AutoCompactionTriggersOnThreshold()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([BigReply("big", 200_000), CompactionReply(CompactionBlock("S", "c"))]);
        await agent.TurnAsync("x", ct: Ct);

        await agent.MaybeCompactAsync(Ct);

        Assert.IsType<CompactionBlock>(agent.Messages[0].Content[0]);
    }

    // Python test: test_dropped_thinking_blocks_are_reported
    [Fact]
    public async Task DroppedThinkingBlocksAreReported()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture();
        var drop = new JsonArray(Json.Obj(("type", "thinking_dropped"), ("path", "messages.1.content.0"), ("reason", "prefix_binding_mismatch")));
        var (agent, _) = ws.Make([ReplyWith([Text("x")], transformations: drop)]);

        await agent.TurnAsync("hi", ct: Ct);

        Assert.Contains("history was edited", cap.Err);
        Assert.Contains("transform", agent.Session.Read().Select(e => e.Kind.Value));
    }

    // Python test: test_session_listing
    [Fact]
    public async Task SessionListing()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([Reply(Text("x")), Reply(Text("y"))]);
        await agent.TurnAsync("first prompt", ct: Ct);

        var rows = ws.ListSessions();

        Assert.Equal(agent.Session.Id, rows[0].Id);
        Assert.Equal(1, rows[0].Turns);
        Assert.Equal("first prompt", rows[0].Title);
        Assert.True(rows[0].Updated > DateTime.UnixEpoch);
        await agent.TurnAsync(new string('x', 80), ct: Ct);
        Assert.Equal("first prompt", ws.ListSessions()[0].Title); // the title stays the first prompt
        Assert.Equal(agent.Session.Id, ws.FindSession()!.Id);
    }

    // Python test: test_api_error_keeps_session_valid
    [Fact]
    public async Task ApiErrorKeepsSessionValid()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture();
        var (agent, fake) = ws.Make([new InvalidOperationException("529 overloaded"), Reply(Text("ok"))]);
        agent.Transcript.PendingNotes.Add("Goal mode is on.");

        Assert.Equal("error", await agent.TurnAsync("first", ct: Ct));

        Assert.Equal(["user", "system", "assistant"], Roles(agent.Messages));
        await agent.TurnAsync("second", ct: Ct);
        Assert.Equal(["user", "system", "assistant", "user"], Roles(fake.Requests[^1]));
        Assert.Contains("request failed", cap.Err);
    }

    // Python test: test_resume_after_crash_mid_tool_closes_the_tail
    // Python writes the dead tail with add_user/add (private here); the same two events are appended to the session file.
    [Fact]
    public async Task ResumeAfterCrashMidToolClosesTheTail()
    {
        using var ws = new Workspace();
        var s = ws.NewSession();
        ws.Make([], session: s); // writes the meta event
        s.Append(new MessageEvent(Message.FromText(Role.User, "go")));
        var dead = Tool("bash", Json.Obj(("command", "sleep 9")), id: "toolu_dead");
        s.Append(new MessageEvent(Message.Assistant([ContentBlock.From(dead)])));

        var (again, fake) = ws.Make([Reply(Text("hi"))], session: ws.OpenSession(s.Path));
        await again.TurnAsync("continue", ct: Ct);

        var msgs = Msgs(fake.Requests[0]);
        Assert.Equal("toolu_dead", Json.Str(msgs[2]!["content"]![0]!["tool_use_id"]));
        Assert.True(msgs[2]!["content"]![0]!["is_error"]!.GetValue<bool>());
        Assert.Equal(["user", "assistant", "user", "assistant", "user"], Roles(fake.Requests[0]));
    }

    // Python test: test_fallback_partial_is_not_run_or_echoed
    // Python swaps the bash method to record calls; here each command writes a file, so what ran is visible on disk.
    [Fact]
    public async Task FallbackPartialIsNotRunOrEchoed()
    {
        using var ws = new Workspace();
        var declined = Tool("bash", Json.Obj(("command", "echo x > declined.txt")));
        var kept = Tool("bash", Json.Obj(("command", "echo x > kept.txt")));
        var fb = Json.Obj(("type", "fallback"), ("from", Json.Obj(("model", "claude-opus-5-5"))), ("to", Json.Obj(("model", "claude-opus-5"))),
            ("trigger", Json.Obj(("type", "refusal"), ("category", "cyber"))));
        var first = ReplyWith([Thinking("x"), Text("partial"), declined, fb, Text("again"), kept], "tool_use", model: "claude-opus-5");
        var (agent, fake) = ws.Make([first, Reply(Text("done"))]);

        await agent.TurnAsync("go", ct: Ct);

        Assert.True(File.Exists(ws.Path("kept.txt")));
        Assert.False(File.Exists(ws.Path("declined.txt")));
        var echoed = Msgs(fake.Requests[1])[1]!["content"]!.AsArray();
        Assert.Equal(["text", "text", "tool_use"], echoed.Select(b => Json.Str(b!["type"])));
        var results = Msgs(fake.Requests[1])[^1]!["content"]!.AsArray();
        Assert.Equal([Json.Str(kept["id"])], results.Select(r => Json.Str(r!["tool_use_id"])));
        TranscriptAssert.AssertAppendOnly(fake.Requests);
    }

    // Python test: test_empty_reply_becomes_a_placeholder
    [Fact]
    public async Task EmptyReplyBecomesAPlaceholder()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture();
        var (agent, fake) = ws.Make([Reply("refusal"), Reply(Text("ok"))]);

        await agent.TurnAsync("x", ct: Ct);

        Assert.Equal("""[{"type":"text","text":"(no content: refusal)"}]""", J(agent.Messages[^1].ToJson()["content"]));
        await agent.TurnAsync("y", ct: Ct);
        Assert.All(Msgs(fake.Requests[^1]), m => Assert.True(m!["content"] is JsonValue { } v ? Json.Str(v)!.Length > 0 : m["content"]!.AsArray().Count > 0));
        TranscriptAssert.AssertAppendOnly(fake.Requests);
    }

    // Python test: test_pending_system_messages_join_into_one
    [Fact]
    public async Task PendingSystemMessagesJoinIntoOne()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([Reply(Text("BLOCKED: ?")), Reply(Text("BLOCKED: again"))]);
        await agent.SetGoalAsync("first", null, 25, null, Ct);
        agent.ClearGoal();

        await agent.SetGoalAsync("second", null, 25, null, Ct);

        var roles = Roles(fake.Requests[^1]);
        Assert.DoesNotContain(roles.Zip(roles.Skip(1)), p => p.First == "system" && p.Second == "system");
        TranscriptAssert.AssertAppendOnly(fake.Requests);
    }

    // Python test: test_compaction_inside_a_long_tool_loop
    [Fact]
    public async Task CompactionInsideALongToolLoop()
    {
        using var ws = new Workspace();
        var (agent, fake) = ws.Make([ToolReply(Tool("bash", Json.Obj(("command", "echo a"))), 200_000), CompactionReply(), Reply(Text("done"))]);

        await agent.TurnAsync("go", ct: Ct);

        var msgs = Msgs(fake.Requests[^1]);
        Assert.Equal("compaction", Json.Str(msgs[0]!["content"]![0]!["type"]));
        Assert.Equal("user", Json.Str(msgs[^1]!["role"]));
        Assert.Contains("compacted", Json.Str(msgs[^1]!["content"]));
    }

    // Python test: test_resume_restores_context_size
    [Fact]
    public async Task ResumeRestoresContextSize()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([BigReply("x", 200_000)]);
        await agent.TurnAsync("hi", ct: Ct);

        var (again, _) = ws.Make([], session: ws.OpenSession(agent.Session.Path));

        Assert.Equal(agent.LastContext, again.LastContext);
        Assert.True(agent.LastContext > 200_000);
    }

    // Python test: test_refused_partial_is_discarded
    [Fact]
    public async Task RefusedPartialIsDiscarded()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture();
        var (agent, _) = ws.Make([Reply("refusal", Text("half an answer"))]);

        await agent.TurnAsync("x", ct: Ct);

        Assert.Equal("""[{"type":"text","text":"(declined)"}]""", J(agent.Messages[^1].ToJson()["content"]));
        Assert.Contains("refused", cap.Err);
    }

    // Python test: test_truncated_session_line_is_skipped
    [Fact]
    public async Task TruncatedSessionLineIsSkipped()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([Reply(Text("x"))]);
        await agent.TurnAsync("hi", ct: Ct);
        File.AppendAllText(agent.Session.Path, """{"kind": "msg", "message": {"ro""");

        var (again, _) = ws.Make([], session: ws.OpenSession(agent.Session.Path));

        Assert.Equal(2, again.Messages.Count);
        Assert.NotEmpty(ws.ListSessions());
    }

    // Python test: test_refusal_with_tool_call_leaves_no_orphan_result
    [Fact]
    public async Task RefusalWithToolCallLeavesNoOrphanResult()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture();
        var (agent, fake) = ws.Make([Reply("refusal", Tool("bash", Json.Obj(("command", "echo x")))), Reply(Text("ok"))]);

        await agent.TurnAsync("x", ct: Ct);
        await agent.TurnAsync("y", ct: Ct);

        Assert.DoesNotContain(Msgs(fake.Requests[^1]), m =>
            m!["content"] is JsonArray { Count: > 0 } c && Json.Str(c[0]!["type"]) == "tool_result");
        TranscriptAssert.AssertAppendOnly(fake.Requests);
    }

    // Python test: test_append_after_cut_off_line_is_not_lost
    [Fact]
    public void AppendAfterCutOffLineIsNotLost()
    {
        using var ws = new Workspace();
        var s = ws.NewSession();
        s.Append(new MessageEvent(Message.FromText(Role.User, "a")));
        File.AppendAllText(s.Path, """{"kind": "msg", "mess""");

        s.Append(new MessageEvent(Message.Assistant([ContentBlock.From(Text("b"))])));

        Assert.Equal([Role.User, Role.Assistant], SessionReplayer.Replay(s).Messages.Select(m => m.Role));
    }

    // Python test: test_failed_compaction_backs_off
    [Fact]
    public async Task FailedCompactionBacksOff()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture();
        var (agent, fake) = ws.Make([BigReply("x", 200_000), Reply("max_tokens")]);
        await agent.TurnAsync("hi", ct: Ct);

        await agent.MaybeCompactAsync(Ct);
        await agent.MaybeCompactAsync(Ct);

        Assert.Single(fake.Creates);
    }

    // The compaction response as the API sends it: its only iteration is the compaction, with a bigger prompt than the conversation.
    // Python test helper: compaction_usage
    static JsonObject CompactionUsage(string stop)
    {
        var u = Usage(0, 0);
        u["iterations"] = new JsonArray(Json.Obj(("type", "compaction"), ("input_tokens", 400_000), ("output_tokens", 10),
            ("cache_read_input_tokens", 0), ("cache_creation_input_tokens", 0)));
        return ReplyWith([], stop, usage: u);
    }

    // Python test: test_compaction_backoff_with_real_usage_shape
    [Fact]
    public async Task CompactionBackoffWithRealUsageShape()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture();
        var (agent, fake) = ws.Make([BigReply("x", 160_000), CompactionUsage("refusal"), BigReply("y", 170_000)]);
        await agent.TurnAsync("hi", ct: Ct);

        await agent.MaybeCompactAsync(Ct);

        Assert.True(agent.LastContext > 150_000 && agent.LastContext < 400_000); // the compaction's own iteration says nothing about size
        Assert.True(agent.CompactFailedAt > 150_000);
        await agent.TurnAsync("again", ct: Ct);
        await agent.MaybeCompactAsync(Ct);
        Assert.Single(fake.Creates);
    }

    // Python test: test_paused_server_tool_is_not_committed_if_the_continuation_fails
    [Fact]
    public async Task PausedServerToolIsNotCommittedIfTheContinuationFails()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture();
        var paused = Reply("pause_turn", Text("searching"),
            Json.Obj(("type", "server_tool_use"), ("id", "srvtoolu_1"), ("name", "web_search"), ("input", Json.Obj(("query", "x")))));
        var (agent, fake) = ws.Make([paused, new InvalidOperationException("529"), Reply(Text("ok"))]);

        Assert.Equal("error", await agent.TurnAsync("search", ct: Ct));

        Assert.DoesNotContain(agent.Messages.Where(m => m.Role == Role.Assistant).SelectMany(m => m.Content),
            b => b is ServerToolUseBlock);
        Assert.Equal("server_tool_use", Json.Str(Msgs(fake.Requests[1])[^1]!["content"]![1]!["type"])); // sent back unchanged
        TranscriptAssert.AssertAppendOnly(fake.Requests);
    }

    // Python test: test_paused_and_final_reply_are_one_session_line
    [Fact]
    public async Task PausedAndFinalReplyAreOneSessionLine()
    {
        using var ws = new Workspace();
        var paused = Reply("pause_turn", Json.Obj(("type", "server_tool_use"), ("id", "srvtoolu_1"), ("name", "web_search"), ("input", Json.Obj(("query", "x")))));
        var done = Reply(Json.Obj(("type", "web_search_tool_result"), ("tool_use_id", "srvtoolu_1"), ("content", new JsonArray())), Text("found"));
        var (agent, _) = ws.Make([paused, done]);

        await agent.TurnAsync("search", ct: Ct);

        var kinds = agent.Session.Read().Select(e => e.Kind.Value).ToList();
        Assert.Equal(1, kinds.Count(k => k == "msgs"));
        Assert.Equal(J(agent.Messages), J(SessionReplayer.Replay(agent.Session).Messages));
    }

    // Python test: test_output_cap_without_a_tool_call_continues_twice_then_stops
    [Fact]
    public async Task OutputCapWithoutAToolCallContinuesTwiceThenStops()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture();
        var (agent, fake) = ws.Make([Reply("max_tokens", Thinking("t1")), Reply("max_tokens", Thinking("t2"), Text("half an ans")), Reply(Text("the answer"))]);

        Assert.Equal("end_turn", await agent.TurnAsync("hard question", ct: Ct));
        Assert.Equal("the answer", agent.FinalText());

        var r = fake.Requests;
        Assert.Equal(3, r.Count);
        TranscriptAssert.AssertAppendOnly(r);
        Assert.Equal("""{"role":"assistant","content":[{"type":"thinking","thinking":"","signature":"t1"}]}""", J(Msgs(r[1])[^2])); // kept exactly as returned
        Assert.Contains("still thinking", Json.Str(Msgs(r[1])[^1]!["content"]));
        Assert.Contains("cut off mid-text", Json.Str(Msgs(r[2])[^1]!["content"]));
        var by = agent.Session.Read().OfType<MessageEvent>().Select(e => e.By).ToList();
        Assert.Equal(["harness", "harness"], by.TakeLast(2));

        (agent, fake) = ws.Make(Enumerable.Range(0, 4).Select(_ => (object)Reply("max_tokens", Thinking())));
        Assert.Equal("max_tokens", await agent.TurnAsync("harder", ct: Ct));
        Assert.Equal(3, fake.Requests.Count);
        TranscriptAssert.AssertAppendOnly(fake.Requests);
        Assert.Equal(Role.Assistant, agent.Messages[^1].Role);
        Assert.Contains("output limit", cap.Err);

        (agent, fake) = ws.Make([Reply("max_tokens", Thinking()), Reply(Text("ok"))]);
        await agent.TurnAsync("first", ct: Ct);
        await agent.TurnAsync("second", ct: Ct); // a new turn gets its own two
        Assert.Equal(3, fake.Requests.Count);
    }

    // Python test: test_images_held_in_the_transcript_are_capped
    // Python patches mini.tools.IMAGES_HELD; here the cap is a const (20 MB), so Held is set near it instead.
    [Fact]
    public async Task ImagesHeldInTheTranscriptAreCapped()
    {
        using var ws = new Workspace();
        File.WriteAllBytes(ws.Path("a.png"), Png(40, 30));
        var (agent, fake) = ws.Make([Reply("tool_use", Tool("read", Json.Obj(("path", "a.png")))), Reply(Text("seen")),
            CompactionReply(Json.Obj(("type", "compaction"), ("content", "Summary."), ("signature", "c")))]);
        await agent.TurnAsync("look", ct: Ct);
        var size = Json.Str(Msgs(fake.Requests[1])[^1]!["content"]![0]!["content"]![1]!["source"]!["data"])!.Length;
        Assert.Equal(size, agent.Images.Held);

        var (resumed, _) = ws.Make([], session: ws.OpenSession(agent.Session.Path));
        Assert.Equal(size, resumed.Images.Held); // counted from the transcript on resume
        resumed.Images.Reset(20_000_000 - 10);
        var ex = await Assert.ThrowsAsync<ToolException>(() => resumed.RunToolAsync("read", Json.Obj(("path", "a.png")), Ct));
        Assert.Contains("already holds 20.0 MB of images", ex.Message);

        await agent.CompactAsync(Ct);
        Assert.Equal(0, agent.Images.Held); // the summary holds no images
        var t = ws.Tools(new ApprovalSettings(auto: true));
        t.Held = 20_000_000 - 2 * size - 10;
        await t.ReadAsync("a.png", ct: Ct);
        await t.ReadAsync("a.png", ct: Ct); // parallel reads in one reply count too, before any of them is in the transcript
        var over = await Assert.ThrowsAsync<ToolException>(() => t.ReadAsync("a.png", ct: Ct));
        Assert.Contains("/compact", over.Message);
    }

    // Python test: test_output_cap_cut_offs_count_in_a_row_not_per_turn
    [Fact]
    public async Task OutputCapCutOffsCountInARowNotPerTurn()
    {
        using var ws = new Workspace();
        JsonObject Bash(string c) => Tool("bash", Json.Obj(("command", $"echo {c}")));
        var (agent, fake) = ws.Make([Reply("max_tokens", Thinking()), Reply("tool_use", Bash("a")), Reply("max_tokens", Thinking()),
            Reply("tool_use", Bash("b")), Reply("max_tokens", Thinking()), Reply(Text("done"))]);

        Assert.Equal("end_turn", await agent.TurnAsync("long job", ct: Ct));

        Assert.Equal(6, fake.Requests.Count); // 1.0.2 stopped at the third, apart
        TranscriptAssert.AssertAppendOnly(fake.Requests);
    }
}
