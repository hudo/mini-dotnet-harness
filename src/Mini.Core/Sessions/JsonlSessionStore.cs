using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Python: session.py Session, Session.__init__, Session.append, Session.events
namespace Mini.Core.Sessions;

/// <summary>
/// Sessions: one append-only JSONL file per conversation.
///
/// The file is the transcript, the resume source and the audit log. Nothing in it
/// is ever rewritten. A compaction starts a new segment: on replay, everything
/// before the latest `compact` event is skipped (see <see cref="SessionReplayer"/>).
/// </summary>
// Python: session.py Session, Session.__init__
public sealed class JsonlSessionStore(string path, TimeProvider time) : ISessionStore
{
    // Inside this class `Path` is the property below, so System.IO.Path is always spelled out.
    public string Path { get; } = path;
    public string Id { get; } = System.IO.Path.GetFileNameWithoutExtension(path);

    // Python: session.py Session.append
    public void Append(SessionEvent e)
    {
        var line = SessionEventJson.ToLine(e, time.GetUtcNow().ToUnixTimeMilliseconds() / 1000.0);

        // FileMode.Append only allows FileAccess.Write, and we must read the last byte, so open
        // ReadWrite and seek to the end (this is not O_APPEND).
        // shortcut: one writer per file, which holds because subagents log to their own files, upgrade if two processes ever share a session.
        using var f = new FileStream(Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        var cut = false;
        if (f.Length > 0)
        {
            f.Seek(-1, SeekOrigin.End); // a crash cut the last line; don't glue onto it
            cut = f.ReadByte() != '\n'; // ReadByte leaves the position at the end
        }
        // One write for the repair newline and the event, so a crash can't leave only the newline.
        f.Write(new UTF8Encoding(false).GetBytes((cut ? "\n" : "") + line.ToJsonString(Json.Relaxed) + "\n"));
    }

    // Python: session.py Session.events
    public IReadOnlyList<SessionEvent> Read()
    {
        var events = new List<SessionEvent>();
        if (!File.Exists(Path))
            return events;
        // UTF8.GetString keeps a BOM as ﻿ (File.ReadAllText would strip it), like Python's utf-8.
        foreach (var line in LineSplitter.Split(Encoding.UTF8.GetString(File.ReadAllBytes(Path))))
        {
            JsonObject? raw;
            try
            {
                raw = JsonNode.Parse(line) as JsonObject;
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException) // a line cut off by a crash; all before it is intact
            {
                continue;
            }
            if (raw is not null)
                events.Add(SessionEventJson.Parse(raw));
        }
        return events;
    }
}
