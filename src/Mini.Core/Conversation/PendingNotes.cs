using System.Collections;

// Python: loop.py Agent.__init__ (self.pending_system), Agent.add_user (the notes it flushes)
namespace Mini.Core.Conversation;

/// <summary>
/// Notes for the model that the harness has to send but must not slip into the middle of the transcript: a goal starting or
/// ending, a change of approval mode, a compaction. They wait here, and go out as one system message right after the next user
/// message. Only ever added to, and emptied when they are sent.
/// </summary>
public sealed class PendingNotes : IReadOnlyList<string>
{
    readonly List<string> _notes = [];

    public int Count => _notes.Count;

    public string this[int index] => _notes[index];

    public void Add(string note) => _notes.Add(note);

    public void AddRange(IEnumerable<string> notes) => _notes.AddRange(notes);

    public void Clear() => _notes.Clear();

    /// <summary>The system message these notes make, or null when there are none. A repeated note keeps its latest position:
    /// each item's last occurrence is kept, in order. The notes are cleared.</summary>
    internal string? Drain()
    {
        if (_notes.Count == 0) return null;
        var latest = Enumerable.Reverse(Enumerable.Reverse(_notes).Distinct()).ToList();
        _notes.Clear();
        return string.Join("\n\n", latest);
    }

    public IEnumerator<string> GetEnumerator() => _notes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
