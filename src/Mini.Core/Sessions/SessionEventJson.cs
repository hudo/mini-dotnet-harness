using System.Text.Json;
using System.Text.Json.Nodes;

// Python: session.py Session.append (the line it writes), Session.load (what it reads back)
namespace Mini.Core.Sessions;

/// <summary>
/// Turns events into the lines of a session file and back. The lines are the file format: key order, the timestamp and the
/// number formats are what sessions written by every earlier version look like, and what they must keep looking like.
/// </summary>
// Python: none (C#-only: Python writes the kwargs of Session.append as they come)
internal static class SessionEventJson
{
    /// <summary>The line: timestamp, kind, then the event's own keys.</summary>
    public static JsonObject ToLine(SessionEvent e, double t)
    {
        var line = new JsonObject { ["t"] = t, ["kind"] = e.Kind.Value };
        switch (e)
        {
            case MetaEvent m:
                line["model"] = m.Model;
                line["cwd"] = m.Cwd;
                line["system"] = m.System;
                line["tools"] = m.Tools?.DeepClone();
                break;
            case MessageEvent m:
                line["message"] = m.Message.ToJson();
                if (m.By is not null) line["by"] = m.By;
                break;
            case MessagesEvent m:
                line["messages"] = Array(m.Messages);
                break;
            case CompactEvent c:
                line["messages"] = Array(c.Messages);
                break;
            case GoalEvent g:
                line["goal"] = JsonSerializer.SerializeToNode(g.Goal);
                break;
            case UsageEvent u: // usage, cost, context for a response; cost, judge for the judge; cost, subagent for a subagent
                if (u.Usage is not null) line["usage"] = u.Usage.ToJson();
                line["cost"] = u.Cost;
                if (u.Context is { } context) line["context"] = context;
                if (u.Judge is not null) line["judge"] = u.Judge.DeepClone();
                if (u.Subagent is not null) line["subagent"] = u.Subagent;
                break;
            case AnswerEvent a:
                line["q"] = a.Question;
                line["a"] = a.Answer;
                break;
            case TransformEvent t2:
                line["items"] = new JsonArray(t2.Items.Select(i => (JsonNode?)i.ToJson()).ToArray());
                break;
            default:
                throw new ArgumentException($"no line shape for {e.GetType().Name}", nameof(e));
        }
        return line;
    }

    static JsonArray Array(IEnumerable<Message> messages) => new(messages.Select(m => (JsonNode?)m.ToJson()).ToArray());

    /// <summary>The event a line says. A kind this code does not know is an <see cref="UnknownEvent"/>; a known one with a part it
    /// cannot read (<see cref="KeyNotFoundException"/> for a missing one, <see cref="InvalidOperationException"/> for one of the wrong
    /// type, <see cref="JsonException"/> for a goal that does not fit) is an <see cref="UnreadableEvent"/> holding that exception.</summary>
    public static SessionEvent Parse(JsonObject e)
    {
        var kind = new SessionEventKind(Json.Str(e["kind"]) ?? "");
        double? t = e["t"] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
        try
        {
            return Read(e, kind) with { Timestamp = t };
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException or InvalidDataException)
        {
            return new UnreadableEvent(kind, ex) { Timestamp = t };
        }
    }

    static SessionEvent Read(JsonObject e, SessionEventKind kind)
    {
        SessionEvent parsed;
        if (kind == SessionEventKind.Meta)
        {
            parsed = new MetaEvent(Json.Str(e["model"]), Json.Str(e["cwd"]), Json.Str(e["system"]), (e["tools"] as JsonArray)?.DeepClone().AsArray());
        }
        else if (kind == SessionEventKind.Msg)
        {
            parsed = new MessageEvent(Message.From(Part(e, "message")), Json.Str(e["by"]));
        }
        else if (kind == SessionEventKind.Msgs) // several messages that only make sense together
        {
            parsed = new MessagesEvent(Messages(e, "messages"));
        }
        else if (kind == SessionEventKind.Compact)
        {
            // An empty or missing `messages` falls back to the single `message` (the older shape), as Python's `or` does.
            parsed = new CompactEvent(e["messages"] is JsonArray { Count: > 0 } ? Messages(e, "messages") : [Message.From(Part(e, "message"))]);
        }
        else if (kind == SessionEventKind.Goal)
        {
            parsed = new GoalEvent(Part(e, "goal").Deserialize<Goal>() ?? throw new InvalidDataException("a goal line without a goal"));
        }
        else if (kind == SessionEventKind.Usage)
        {
            parsed = new UsageEvent(Number(e["cost"]), e["usage"] is JsonObject u ? new Usage((JsonObject)u.DeepClone()) : null,
                e.ContainsKey("context") ? Json.N(e["context"]) : null, e["judge"] as JsonObject, Json.Str(e["subagent"]));
        }
        else if (kind == SessionEventKind.Answer)
        {
            parsed = new AnswerEvent(Json.Str(e["q"]) ?? "", Json.Str(e["a"]) ?? "");
        }
        else if (kind == SessionEventKind.Transform)
        {
            parsed = new TransformEvent((e["items"] as JsonArray)?.OfType<JsonObject>().Select(i => new InputTransformation((JsonObject)i.DeepClone())).ToList() ?? []);
        }
        else
        {
            parsed = new UnknownEvent(kind);
        }

        return parsed;
    }

    // Python's `e.get("cost", 0)`: Json.N would truncate a fractional cost to a long.
    static double Number(JsonNode? x) => x is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;

    static JsonObject Part(JsonObject e, string key) => e[key]?.AsObject() ?? throw new KeyNotFoundException(key);

    static List<Message> Messages(JsonObject e, string key) =>
        (e[key]?.AsArray() ?? throw new KeyNotFoundException(key)).Select(m => Message.From(m!.AsObject())).ToList();
}
