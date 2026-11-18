namespace Mini.Core.Sessions;

/// <summary>What the user typed to answer the agent's question (an answer picked from the options is not recorded: the model wrote it).</summary>
// Python: session.py Session.append, Session.load (the "answer" line)
public sealed record AnswerEvent(string Question, string Answer) : SessionEvent
{
    public override SessionEventKind Kind => SessionEventKind.Answer;
}
