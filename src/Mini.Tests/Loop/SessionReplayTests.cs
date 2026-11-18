using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Mini.Tests.Fakes;
using static Mini.Tests.Fakes.FakeModelClient;
using static Mini.Tests.Loop.LoopHelpers;

namespace Mini.Tests.Loop;

/// <summary>C#-only pins on the session file and on replay, ahead of the Phase 3 typed-record rewrite of the transcript.</summary>
public class SessionReplayTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static readonly string[] AllKinds = ["meta", "msg", "msgs", "usage", "answer", "transform", "goal", "compact"];

    /// <summary>
    /// One real agent session that writes every event kind the C# writes: meta, msg (also by: harness), msgs, usage, answer
    /// (ask_user), transform (dropped thinking), goal (a goal that pauses on BLOCKED) and compact. Ends with "again", so no
    /// system note is still waiting in memory.
    /// </summary>
    static async Task<(Agent Agent, FakeModelClient Fake)> RunEveryKind(Workspace ws)
    {
        var drop = new JsonArray(Json.Obj(("type", "thinking_dropped"), ("path", "messages.1.content.0"), ("reason", "prefix_binding_mismatch")));
        var ask = Tool("ask_user", Json.Obj(("question", "which?")));
        var (agent, fake) = ws.Make([
            ReplyWith([Thinking("s1"), ask], "tool_use", usage: Usage(5000, 40, 100, 200)),
            ReplyWith([Text("ok")], transformations: drop),
            Reply(Text("BLOCKED: ?")), // the goal turn
            CompactionReply(),
            Reply(Text("after")),
        ]);
        await agent.TurnAsync("héllo ✓ 日本", ct: Ct);
        await agent.SetGoalAsync("ship it", null, 5, null, Ct);
        Assert.True(await agent.CompactAsync(Ct));
        await agent.TurnAsync("again", ct: Ct);
        return (agent, fake);
    }

    // C#-only: pins every event kind the C# writes and what SessionReplayer restores from them (the Python test suite has no such round trip)
    [Fact]
    public async Task SessionJsonlRoundTripRestoresEveryEventKind()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture("my typed answer");
        var (agent, _) = await RunEveryKind(ws);

        var events = agent.Session.Read();
        var kinds = events.Select(e => e.Kind.Value).ToHashSet();
        Assert.Superset(AllKinds.ToHashSet(), kinds);
        Assert.All(events, e => Assert.NotNull(e.Timestamp)); // every line is timestamped
        var answer = events.OfType<AnswerEvent>().Single();
        Assert.Equal(("which?", "my typed answer"), (answer.Question, answer.Answer));
        Assert.Contains(events.OfType<MessageEvent>(), e => e.By == "harness");
        Assert.DoesNotContain(events.OfType<MessageEvent>(), e => e.By is not (null or "harness"));
        Assert.Equal("prefix_binding_mismatch", events.OfType<TransformEvent>().Single().Items[0].Reason);

        var state = SessionReplayer.Replay(ws.OpenSession(agent.Session.Path));
        Assert.Equal(agent.Model, state.Meta!.Model);
        Assert.Equal(ws.Root, state.Meta.Cwd);
        Assert.Equal(agent.System, state.Meta.System);
        Assert.Equal(J(agent.ToolsJson()), J(state.Meta.Tools));
        Assert.Equal(J(agent.Messages), J(state.Messages)); // only what follows the compact event
        Assert.IsType<CompactionBlock>(state.Messages[0].Content[0]);
        Assert.Equal(1, state.Messages.Count(m => m.Role == Role.System)); // the "Goal mode is off" note after the goal run
        Assert.Equal(agent.Cost, state.Cost, 5);
        Assert.True(state.Cost > 0);
        Assert.Equal(agent.LastContext, state.Context);
        Assert.NotNull(state.Goal);
        Assert.Equal(("ship it", "paused"), (state.Goal.Objective, state.Goal.Status.Wire()));

        var (resumed, _) = ws.Make([], session: ws.OpenSession(agent.Session.Path));
        Assert.Equal(J(agent.Messages), J(resumed.Messages));
        Assert.Equal(("ship it", "paused"), (resumed.Goal!.Objective, resumed.Goal.Status.Wire()));
        Assert.Equal(agent.LastContext, resumed.LastContext);
        Assert.Empty(resumed.Transcript.PendingNotes);
    }

    // C#-only: SessionReplayer's rules on a hand-written log, event kind by event kind
    [Fact]
    public void ReplayAppliesCompactGoalAndUsageRules()
    {
        using var ws = new Workspace();
        var s = ws.NewSession();
        Message User(string c) => Message.FromText(Role.User, c);
        Message Asst(string c) => Message.Assistant([TextBlock.Create(c)]);
        Goal GoalOf(GoalStatus status) => new("o", status, 1, 5, spent: 0.5);
        s.Append(new MetaEvent("claude-opus-5-5", ws.Root, "SYS", new JsonArray()));
        s.Append(new MessageEvent(User("one")));
        s.Append(new MessageEvent(User("by the harness"), MessageEvent.Harness));
        s.Append(new MessagesEvent([Asst("two"), User("three")]));
        s.Append(new UsageEvent(0.25, Mini.Core.Conversation.Usage.Empty, 1234));
        s.Append(new UsageEvent(0.125, Subagent: "x-sub-1.jsonl")); // no context: leaves it alone
        s.Append(new AnswerEvent("q", "a"));
        s.Append(new TransformEvent([]));
        s.Append(new GoalEvent(GoalOf(GoalStatus.Active)));
        var before = SessionReplayer.Replay(s);
        Assert.Equal([Role.User, Role.User, Role.Assistant, Role.User], before.Messages.Select(m => m.Role));
        Assert.Equal(0.375, before.Cost);
        Assert.Equal(1234, before.Context);
        Assert.Equal(GoalStatus.Active, before.Goal!.Status);

        s.Append(new CompactEvent([Asst("summary")]));
        s.Append(new MessageEvent(User("four")));
        s.Append(new UsageEvent(0.5));
        var after = SessionReplayer.Replay(s);
        Assert.Equal([Role.Assistant, Role.User], after.Messages.Select(m => m.Role)); // everything before the compact event is skipped
        Assert.Equal(0, after.Context); // a compaction resets the size; the cost keeps counting
        Assert.Equal(0.875, after.Cost);
        Assert.Equal(1, after.Goal!.Turns); // the goal is not part of a segment

        // the older shape of a compact line: one `message`, not `messages`
        File.AppendAllText(s.Path, """{"t":1,"kind":"compact","message":{"role":"assistant","content":[{"type":"text","text":"single"}]}}""" + "\n");
        Assert.Equal([Role.Assistant], SessionReplayer.Replay(s).Messages.Select(m => m.Role));

        // a kind a newer version wrote is skipped, and the lines around it still count
        File.AppendAllText(s.Path, """{"t":2,"kind":"from-the-future","x":1}""" + "\n");
        s.Append(new MessageEvent(User("five")));
        Assert.IsType<UnknownEvent>(s.Read().Single(e => e.Kind.Value == "from-the-future"));
        Assert.Equal(2, SessionReplayer.Replay(s).Messages.Count);

        s.Append(new GoalEvent(GoalOf(GoalStatus.Cleared)));
        Assert.Null(SessionReplayer.Replay(s).Goal);
        Assert.Equal("SYS", SessionReplayer.Replay(s).Meta!.System);
        Assert.Equal(3, ws.ListSessions()[0].Turns); // "one", "four" and "five": the list counts typed prompts, not the harness's
    }

    // C#-only: the lines the typed events write are the lines every earlier version wrote: key order, the timestamp, the number and Unicode formats
    [Fact]
    public void EveryEventKindWritesTodaysLineByteForByte()
    {
        using var ws = new Workspace();
        var s = ws.NewSession();
        Directory.CreateDirectory(Path.GetDirectoryName(s.Path)!);
        var goal = new Goal("ship it", GoalStatus.Paused, 1, 5, spent: 0.5, note: "n");
        var response = ModelResponse.From(JsonNode.Parse("""{"usage":{"input_tokens":5,"output_tokens":1,"service_tier":"standard"},"input_transformations":[{"type":"thinking_dropped","reason":"r","path":"p"}]}""")!.AsObject());
        var verdict = JsonNode.Parse("""{"choice":"allow","confidence":0.9}""")!.AsObject();
        var messages = new[] { Message.FromText(Role.User, "héllo ✓ 日本"), Message.Assistant([TextBlock.Create("ok")]) };
        void At(double seconds) => ws.Runtime.Time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000)));

        At(1793932200);
        s.Append(new MetaEvent("claude-opus-5-5", "/w", "S\n\"quoted\"", new JsonArray(Json.Obj(("name", "read")))));
        s.Append(new MessageEvent(messages[0]));
        s.Append(new MessageEvent(messages[0], MessageEvent.Harness));
        s.Append(new MessagesEvent(messages));
        At(1793932201.234);
        s.Append(new CompactEvent(messages));
        s.Append(new GoalEvent(goal));
        s.Append(new UsageEvent(0.021820, response.Usage, 5340));
        s.Append(new UsageEvent(1.2E-05, Judge: verdict));
        s.Append(new UsageEvent(0.5, Subagent: "x-sub-ab12.jsonl"));
        s.Append(new AnswerEvent("which?", "my \"typed\" answer"));
        s.Append(new TransformEvent(response.InputTransformations));

        Assert.Equal(
            """
            {"t":1793932200,"kind":"meta","model":"claude-opus-5-5","cwd":"/w","system":"S\n\"quoted\"","tools":[{"name":"read"}]}
            {"t":1793932200,"kind":"msg","message":{"role":"user","content":"héllo ✓ 日本"}}
            {"t":1793932200,"kind":"msg","message":{"role":"user","content":"héllo ✓ 日本"},"by":"harness"}
            {"t":1793932200,"kind":"msgs","messages":[{"role":"user","content":"héllo ✓ 日本"},{"role":"assistant","content":[{"type":"text","text":"ok"}]}]}
            {"t":1793932201.234,"kind":"compact","messages":[{"role":"user","content":"héllo ✓ 日本"},{"role":"assistant","content":[{"type":"text","text":"ok"}]}]}
            {"t":1793932201.234,"kind":"goal","goal":{"objective":"ship it","status":"paused","turns":1,"max_turns":5,"verify":null,"budget":null,"spent":0.5,"note":"n"}}
            {"t":1793932201.234,"kind":"usage","usage":{"input_tokens":5,"output_tokens":1,"service_tier":"standard"},"cost":0.02182,"context":5340}
            {"t":1793932201.234,"kind":"usage","cost":1.2E-05,"judge":{"choice":"allow","confidence":0.9}}
            {"t":1793932201.234,"kind":"usage","cost":0.5,"subagent":"x-sub-ab12.jsonl"}
            {"t":1793932201.234,"kind":"answer","q":"which?","a":"my \"typed\" answer"}
            {"t":1793932201.234,"kind":"transform","items":[{"type":"thinking_dropped","reason":"r","path":"p"}]}

            """.ReplaceLineEndings("\n"),
            File.ReadAllText(s.Path));
    }

    static void Raw(ISessionStore s, params string[] lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(s.Path)!);
        File.AppendAllText(s.Path, string.Concat(lines.Select(l => l + "\n")));
    }

    // C#-only: what the listing did before the typed events (and Python's list_sessions does for most of these): a line whose parts it can't read is skipped
    [Fact]
    public void TheSessionListingSkipsAKnownLineItCannotRead()
    {
        using var ws = new Workspace();
        var s = ws.NewSession();
        Raw(s,
            """{"t":1,"kind":"msg","message":{"role":"user","content":"the title"}}""",
            """{"t":1,"kind":"msg"}""", // no message
            """{"t":1,"kind":"msg","message":"not an object"}""",
            """{"t":1,"kind":"msg","message":{"role":"user","content":["a","list"]}}""", // not a typed prompt
            """{"t":1,"kind":"msgs","messages":"nope"}""",
            """{"t":1,"kind":"compact"}""",
            """{"t":1,"kind":"goal","goal":{"objective":"o","status":"running"}}""", // a status the code has none for
            """{"t":1,"kind":"goal"}""",
            """{"t":1,"kind":"usage","cost":"free","usage":"x","context":null}""", // a cost that is no number counts as 0
            """{"t":1,"kind":"usage","cost":0.25}""",
            """{"t":1,"kind":"answer"}""",
            """{"t":1,"kind":"transform","items":7}""",
            """{"t":1,"kind":"from-the-future","x":1}""",
            """{"t":1,"kind":"msg","message":{"role":"user","content":"second prompt"}}""");

        var row = Assert.Single(ws.ListSessions());

        Assert.Equal((s.Id, 2, 0.25, "the title"), (row.Id, row.Turns, row.Cost, row.Title));
        Assert.IsType<UnreadableEvent>(s.Read()[1]);
    }

    // C#-only: replay as it was before the typed events: a role the code has no name for loads and writes back as it was
    [Fact]
    public void ReplayKeepsARoleItDoesNotKnowAsWritten()
    {
        using var ws = new Workspace();
        var s = ws.NewSession();
        Raw(s,
            """{"t":1,"kind":"msg","message":{"role":"developer","content":"a note"}}""",
            """{"t":1,"kind":"msg","message":{"role":"user","content":"hi"}}""");

        var messages = SessionReplayer.Replay(s).Messages;

        Assert.Equal("""[{"role":"developer","content":"a note"},{"role":"user","content":"hi"}]""", J(messages));
    }

    // C#-only: replay as it was before the typed events: a line it needs but cannot read stops the resume, with the exception the load raised
    [Fact]
    public void ReplayRaisesTheOriginalExceptionForALineItNeedsButCannotRead()
    {
        using var ws = new Workspace();
        void Check<T>(string line) where T : Exception
        {
            var s = ws.NewSession("bad-" + Guid.NewGuid().ToString("N")[..6]);
            Raw(s, """{"t":1,"kind":"msg","message":{"role":"user","content":"ok"}}""", line);
            Assert.Throws<T>(() => SessionReplayer.Replay(s));
        }

        Check<KeyNotFoundException>("""{"t":1,"kind":"msg"}""");
        Check<InvalidOperationException>("""{"t":1,"kind":"msg","message":"not an object"}""");
        Check<KeyNotFoundException>("""{"t":1,"kind":"msgs"}""");
        Check<KeyNotFoundException>("""{"t":1,"kind":"compact"}""");
        Check<System.Text.Json.JsonException>("""{"t":1,"kind":"goal","goal":{"objective":"o","status":"running"}}"""); // an agent resuming it fails the same way
        var resume = ws.NewSession("resume-bad");
        Raw(resume, """{"t":1,"kind":"meta","model":"claude-opus-5-5","cwd":"/w","system":"S","tools":[]}""",
            """{"t":1,"kind":"goal","goal":{"objective":"o","status":"running"}}""");
        Assert.Throws<System.Text.Json.JsonException>(() => ws.Make([], session: resume));
    }

    static string FixturePath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Fixtures", "session-p34.jsonl");

    /// <summary>
    /// <c>Fixtures/session-p34.jsonl</c> was recorded from the code of branch dotnet-p34, before the Phase 3 rewrite (a thinking
    /// block, ask_user and its answer, a transform, a goal that paused, a compaction, harness and system notes). It must keep
    /// resuming to the same transcript, goal, cost and context, and the next request must replay it byte for byte.
    /// </summary>
    // C#-only: replay of a session file written by the code that came before the typed transcript
    [Fact]
    public async Task ASessionRecordedBeforePhase3StillResumes()
    {
        using var ws = new Workspace();
        var events = File.ReadAllLines(FixturePath()).Select(l => JsonNode.Parse(l)!.AsObject()).ToList();
        var last = events.FindLastIndex(e => Json.Str(e["kind"]) == "compact");
        var expected = new JsonArray(); // the transcript as the file says it, read without the code under test
        foreach (var e in events.Skip(last))
        {
            switch (Json.Str(e["kind"]))
            {
                case "compact" or "msgs": foreach (var m in e["messages"]!.AsArray()) expected.Add(m!.DeepClone()); break;
                case "msg": expected.Add(e["message"]!.DeepClone()); break;
            }
        }
        var cost = events.Where(e => Json.Str(e["kind"]) == "usage").Sum(e => e["cost"]!.GetValue<double>());

        var session = ws.NewSession();
        Directory.CreateDirectory(Path.GetDirectoryName(session.Path)!);
        File.Copy(FixturePath(), session.Path);
        var (agent, fake) = ws.Make([Reply(Text("next reply"))], session: ws.OpenSession(session.Path));

        Assert.Equal(expected.ToJsonString(), J(agent.Messages));
        Assert.Equal("ship it", agent.Goal!.Objective);
        Assert.Equal("paused", agent.Goal.Status.Wire());
        Assert.Equal("blocked: waiting for the user", agent.Goal.Note);
        Assert.Equal(cost, agent.Cost, 6);
        Assert.Equal(1050, agent.LastContext);
        Assert.Equal(Json.Str(events[0]["system"]), agent.System);
        Assert.Equal(J(events[0]["tools"]), J(agent.ToolsJson()));
        Assert.Empty(agent.Transcript.PendingNotes);

        await agent.TurnAsync("one more", ct: Ct);
        expected.Add(Json.Obj(("role", "user"), ("content", "one more")));
        Assert.Equal(expected.ToJsonString(), J(fake.Requests[0]["messages"]));
        Assert.Equal(File.ReadAllLines(FixturePath()).Length + 3, File.ReadAllLines(session.Path).Length); // the user message, the reply and its usage: only appended
        Assert.Equal(File.ReadAllText(FixturePath()), File.ReadAllText(session.Path)[..File.ReadAllText(FixturePath()).Length]);
    }

    // C#-only: pins that replaying a session rebuilds the exact request (the Phase 3 typed-record rewrite must keep it byte for byte)
    [Fact]
    public async Task ResumedSessionsFirstRequestIsByteIdenticalToTheOriginalsNext()
    {
        using var ws = new Workspace();
        using var cap = new ConsoleCapture("my typed answer");
        var (agent, fake) = await RunEveryKind(ws);
        var copy = Path.Combine(Path.GetDirectoryName(agent.Session.Path)!, "copy.jsonl"); // the file as the original left it
        File.Copy(agent.Session.Path, copy);
        var (resumed, fake2) = ws.Make([Reply(Text("final"))], session: ws.OpenSession(copy));
        fake.Enqueue(Reply(Text("final")));

        await agent.TurnAsync("next", ct: Ct);
        await resumed.TurnAsync("next", ct: Ct);

        Assert.Equal(fake.Requests[^1].ToJsonString(), fake2.Requests[0].ToJsonString());
        Assert.Equal(J(agent.Messages), J(resumed.Messages));
    }
}
