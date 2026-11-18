using System.Text.Json.Nodes;

// Python: loop.py Agent.judge (what it reads of the session: self.cwd, self.session.events(), self.goal, and the cost it adds)
namespace Mini.Core.Approval;

/// <summary>The judge sees the user's words and the action, nothing else: not the agent's reasoning or tool output, which files and web pages can steer.
/// This is everything it may read of the session, and where it reports what it spent.</summary>
public interface IJudgeContext
{
    string WorkingDirectory { get; }

    /// <summary>What the user typed (messages not written by the harness, and answers to ask_user), newest first.</summary>
    IEnumerable<JudgeUtterance> Utterances();

    /// <summary>The objective of the goal that is running, or null.</summary>
    string? ActiveGoalObjective { get; }

    /// <summary>Adds the judge's cost to the session's, and records its verdict.</summary>
    void RecordSpend(double cost, JsonObject verdict);
}
