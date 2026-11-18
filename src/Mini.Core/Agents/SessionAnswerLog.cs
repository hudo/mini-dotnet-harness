// Python: loop.py Agent.dispatch (ask_user: self.session.append("answer", ...))
namespace Mini.Core.Agents;

/// <summary>Keeps what the user typed in answer to ask_user in the session log, where the judge reads it.</summary>
public sealed class SessionAnswerLog(ISessionStore session) : IAnswerLog
{
    public void Record(string question, string answer) => session.Append(new AnswerEvent(question, answer));
}
