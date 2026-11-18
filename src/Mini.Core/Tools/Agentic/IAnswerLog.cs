// Python: loop.py Agent.dispatch (ask_user: self.session.append("answer", ...))
namespace Mini.Core.Tools.Agentic;

/// <summary>Where what the user typed in answer to a question is kept, so the judge can read it.</summary>
public interface IAnswerLog
{
    void Record(string question, string answer);
}
