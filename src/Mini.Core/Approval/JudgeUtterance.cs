// Python: loop.py Agent.judge (the "msg" and "answer" events it picks from the session)
namespace Mini.Core.Approval;

/// <summary>Something the user said: a message, or the answer to a question the agent asked.</summary>
public sealed record JudgeUtterance(JudgeUtteranceKind Kind, string Text);

public enum JudgeUtteranceKind
{
    Message,
    Answer,
}
