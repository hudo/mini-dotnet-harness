// Python: none (C#-only: Python's message dicts carry the role as a bare string)
namespace Mini.Core.Conversation;

/// <summary>Who a transcript message is from, as the session file and the API spell it. A struct around the string, not an enum, so a
/// session that holds a role this code does not know still loads and replays exactly as written.</summary>
public readonly record struct Role(string Value)
{
    public static readonly Role User = new("user");
    public static readonly Role Assistant = new("assistant");

    /// <summary>A harness note (see <see cref="PendingNotes"/>): kept in the transcript, replayed to the model as it is.</summary>
    public static readonly Role System = new("system");

    public override string ToString() => Value;
}
