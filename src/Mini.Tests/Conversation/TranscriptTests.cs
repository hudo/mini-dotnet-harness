using System.Text.Json.Nodes;
using Mini.Tests.Fakes;

namespace Mini.Tests.Conversation;

/// <summary>C#-only: the transcript's own rules (append-only, write before memory, notes after the next user message) on the type that now owns them.</summary>
public class TranscriptTests
{
    static string Json_(IEnumerable<Message> messages) => new JsonArray(messages.Select(m => (JsonNode?)m.ToJson()).ToArray()).ToJsonString();

    static ToolUseBlock Use(string id) => Assert.IsType<ToolUseBlock>(ContentBlock.From(JsonNode.Parse("{\"type\":\"tool_use\",\"id\":\"" + id + "\",\"name\":\"bash\",\"input\":{}}")!.AsObject()));

    // Python test helper: tests/test_mini.py assert_append_only (the same rule, checked on the transcript instead of on requests)
    [Fact]
    public void EveryOperationOnlyAppendsToTheMessagesAndTheSessionFile()
    {
        using var ws = new Workspace();
        var session = ws.NewSession();
        Directory.CreateDirectory(Path.GetDirectoryName(session.Path)!);
        var transcript = new Transcript(session);
        var snapshots = new List<(string Messages, string File)>();
        void Snap() => snapshots.Add((Json_(transcript.Messages), File.Exists(session.Path) ? File.ReadAllText(session.Path) : ""));

        transcript.AddUser("one"); Snap();
        transcript.PendingNotes.Add("note"); transcript.PendingNotes.Add("note 2");
        transcript.AddUser("two", harness: true); Snap();
        transcript.CommitPaused([Message.Assistant([TextBlock.Create("paused")])], Message.Assistant([Use("t1"), Use("t2")])); Snap();
        transcript.Repair(); Snap(); // closes both calls, then the user message with a placeholder
        transcript.Placeholder("because"); Snap();
        transcript.AddUser([TextBlock.Create("blocks")]); Snap();
        transcript.Repair(); Snap();

        Assert.Equal(["user", "user", "system", "assistant", "assistant", "user", "assistant", "assistant", "user", "assistant"],
            transcript.Messages.Select(m => m.ToJson()["role"]!.GetValue<string>()));
        for (var n = 0; n + 1 < snapshots.Count; n++)
        {
            var (a, b) = (snapshots[n], snapshots[n + 1]);
            Assert.StartsWith(a.Messages[..^1], b.Messages); // the earlier array is a prefix of the later one
            Assert.StartsWith(a.File, b.File);
        }
    }

    [Fact]
    public void AMessageIsWrittenToTheSessionBeforeItIsInMemory()
    {
        using var ws = new Workspace();
        var broken = ws.OpenSession(Path.Combine(ws.Root, "no-such-folder", "s.jsonl")); // appending to it throws
        var transcript = new Transcript(broken);

        Assert.ThrowsAny<IOException>(() => transcript.AddUser("lost"));
        Assert.ThrowsAny<IOException>(() => transcript.Placeholder("lost"));
        Assert.ThrowsAny<IOException>(() => transcript.CommitPaused([], Message.Assistant([TextBlock.Create("lost")])));
        Assert.ThrowsAny<IOException>(() => transcript.ReplaceWithSummary([Message.FromText(Role.User, "lost")]));

        Assert.Empty(transcript.Messages);
    }

    [Fact]
    public void MessagesIsAReadOnlyViewAndAReplacementKeepsEarlierViewsIntact()
    {
        using var ws = new Workspace();
        var session = ws.NewSession();
        Directory.CreateDirectory(Path.GetDirectoryName(session.Path)!);
        var transcript = new Transcript(session);
        transcript.AddUser("one");
        var before = transcript.Messages;

        transcript.ReplaceWithSummary([Message.FromText(Role.User, "summary"), Message.Assistant([TextBlock.Create("ok")])]);

        Assert.IsNotType<List<Message>>(transcript.Messages);
        Assert.Single(before);
        Assert.Equal(2, transcript.Messages.Count);
        Assert.Contains("\"kind\":\"compact\"", File.ReadAllText(session.Path));
        Assert.Equal(Json_(transcript.Messages), Json_(SessionReplayer.Replay(session).Messages));
    }

    [Fact]
    public void PendingNotesGoOutAsOneSystemMessageRightAfterTheUserMessageAndARepeatKeepsItsLatestPlace()
    {
        using var ws = new Workspace();
        var session = ws.NewSession();
        Directory.CreateDirectory(Path.GetDirectoryName(session.Path)!);
        var transcript = new Transcript(session);
        transcript.PendingNotes.AddRange(["A", "B", "A", "C"]);
        Assert.Equal(4, transcript.PendingNotes.Count);

        transcript.AddUser("go");

        Assert.Equal(("B\n\nA\n\nC", Role.System), (transcript.Messages[1].Text, transcript.Messages[1].Role));
        Assert.Empty(transcript.PendingNotes);
        transcript.AddUser("again");
        Assert.Equal(3, transcript.Messages.Count); // no notes, no system message
    }

    [Fact]
    public void RepairAnswersEveryOpenCallInOneUserMessageInTheOrderTheyWereMadeThenClosesTheTail()
    {
        using var ws = new Workspace();
        var session = ws.NewSession();
        Directory.CreateDirectory(Path.GetDirectoryName(session.Path)!);
        var transcript = new Transcript(session, [Message.FromText(Role.User, "go"), Message.Assistant([Use("t1"), Use("t2")])]);

        transcript.Repair();

        Assert.Equal("""{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","is_error":true,"content":"Not run: the session ended before this call."},{"type":"tool_result","tool_use_id":"t2","is_error":true,"content":"Not run: the session ended before this call."}]}""",
            transcript.Messages[2].ToJson().ToJsonString());
        Assert.Equal("""{"role":"assistant","content":[{"type":"text","text":"(the previous session ended here)"}]}""", transcript.Messages[3].ToJson().ToJsonString());
        Assert.Equal(4, transcript.Messages.Count);
        transcript.Repair(); // a closed tail stays as it is
        Assert.Equal(4, transcript.Messages.Count);
    }

    [Fact]
    public void FinalTextIsTheLastAssistantMessagesTextBlocks()
    {
        var transcript = new Transcript(null!, [
            Message.FromText(Role.User, "q"),
            Message.Assistant([TextBlock.Create("a"), Use("t1"), TextBlock.Create("b")]),
            Message.User([ToolResultBlock.Create("t1", "x", false)])]);

        Assert.Equal("a\nb", transcript.FinalText());
        Assert.Equal("", new Transcript(null!).FinalText());
    }
}
