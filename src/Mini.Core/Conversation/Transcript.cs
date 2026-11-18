// Python: loop.py Agent.add, Agent.add_user, Agent.placeholder, Agent.repair, Agent.final_text, Agent.compact (the swap), Agent.turn (the commit of a reply)
namespace Mini.Core.Conversation;

/// <summary>
/// The conversation so far, and the only code that adds a message to it. It is append-only: a message is never edited, reordered
/// or removed (the API rejects replayed thinking blocks after a history edit); the one exception is
/// <see cref="ReplaceWithSummary"/>, where a compaction starts a new segment. Every addition goes to the session file first,
/// then to memory: memory may run ahead of the disk, never behind. Outside code only reads <see cref="Messages"/>.
/// </summary>
public sealed class Transcript
{
    readonly ISessionStore _session;
    List<Message> _messages;
    IReadOnlyList<Message> _view;

    /// <param name="session">Where every message is written first.</param>
    /// <param name="messages">What a resumed session already holds (not written again).</param>
    public Transcript(ISessionStore session, IEnumerable<Message>? messages = null)
    {
        _session = session;
        _messages = messages?.ToList() ?? [];
        _view = _messages.AsReadOnly();
    }

    /// <summary>What the model is shown next, oldest first.</summary>
    public IReadOnlyList<Message> Messages => _view;

    /// <summary>Notes that go out with the next user message.</summary>
    public PendingNotes PendingNotes { get; } = new();

    /// <summary>Appends one message. <paramref name="harness"/>: the harness wrote it, not the user (the judge and /sessions skip those).</summary>
    // Python: loop.py Agent.add
    public void Add(Message message, bool harness = false)
    {
        _session.Append(new MessageEvent(message, harness ? MessageEvent.Harness : null));
        _messages.Add(message);
    }

    /// <summary>Appends a user message, and after it one system message with the pending notes, if there are any.</summary>
    // Python: loop.py Agent.add_user
    public void AddUser(string text, bool harness = false) => AddUser(Message.FromText(Role.User, text), harness);

    /// <summary>Appends a user message made of blocks (tool results).</summary>
    // Python: loop.py Agent.add_user
    public void AddUser(IEnumerable<ContentBlock> blocks, bool harness = false) => AddUser(Message.User(blocks), harness);

    void AddUser(Message message, bool harness)
    {
        Add(message, harness);
        if (PendingNotes.Drain() is { } notes) // one system message, right after a user message
            Add(Message.FromText(Role.System, notes));
    }

    /// <summary>Closes a turn that did not produce a reply, so roles keep alternating and no request is rejected.</summary>
    // Python: loop.py Agent.placeholder
    public void Placeholder(string why) => Add(Message.Assistant([TextBlock.Create($"({why})")]));

    /// <summary>A session that died mid-turn gets its tail closed, by appending only: every unanswered call gets a result, and a
    /// transcript that ends on a user or system message gets an assistant placeholder.</summary>
    // Python: loop.py Agent.repair
    public void Repair()
    {
        var last = _messages.Count > 0 ? _messages[^1] : null;
        if (last is not null && last.Role == Role.Assistant)
        {
            var uses = last.Content.OfType<ToolUseBlock>().ToList();
            if (uses.Count > 0)
            {
                AddUser(uses.Select(use => (ContentBlock)ToolResultBlock.NotRun(use.Id)));
                last = _messages[^1];
            }
        }
        if (last is not null && (last.Role == Role.User || last.Role == Role.System))
            Placeholder("the previous session ended here");
    }

    /// <summary>Appends the replies that paused on a server tool together with the one that completed them, as one session
    /// line: a crash can't split a paused search.</summary>
    // Python: loop.py Agent.turn (the commit of the reply)
    public void CommitPaused(IReadOnlyList<Message> paused, Message reply)
    {
        var done = paused.Append(reply).ToList();
        _session.Append(new MessagesEvent(done));
        _messages.AddRange(done);
    }

    /// <summary>A compaction: the whole history gives way to a summary. The session file starts a new segment (older turns stay
    /// on disk), and only then does memory change.</summary>
    // Python: loop.py Agent.compact (the swap)
    public void ReplaceWithSummary(IReadOnlyList<Message> summary)
    {
        _session.Append(new CompactEvent(summary));
        _messages = summary.ToList();
        _view = _messages.AsReadOnly();
    }

    /// <summary>The text of the last assistant message; empty when there is none.</summary>
    // Python: loop.py Agent.final_text
    public string FinalText()
    {
        for (var i = _messages.Count - 1; i >= 0; i--)
        {
            if (_messages[i].Role == Role.Assistant)
                return string.Join("\n", _messages[i].Content.OfType<TextBlock>().Select(b => b.Text));
        }

        return "";
    }
}
