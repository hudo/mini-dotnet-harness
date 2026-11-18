namespace Mini.Core.Sessions;

/// <summary>The "kind" of one line in a session file; the file keeps the lowercase string. Each <see cref="SessionEvent"/> subclass has one.</summary>
// Python: session.py Session.append, Session.load (the kind strings)
public readonly record struct SessionEventKind(string Value)
{
    public static readonly SessionEventKind Meta = new("meta");
    public static readonly SessionEventKind Msg = new("msg");
    public static readonly SessionEventKind Msgs = new("msgs");
    public static readonly SessionEventKind Compact = new("compact");
    public static readonly SessionEventKind Goal = new("goal");
    public static readonly SessionEventKind Usage = new("usage");
    public static readonly SessionEventKind Answer = new("answer");
    public static readonly SessionEventKind Transform = new("transform");

    public override string ToString() => Value;
}
