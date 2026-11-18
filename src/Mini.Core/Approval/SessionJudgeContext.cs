using System.Text.Json.Nodes;

// Python: loop.py Agent.judge (the scan of self.session.events(), self.goal, and the cost it adds)
namespace Mini.Core.Approval;

/// <summary>
/// What the judge may read of a session, taken from its log: the user's own messages and their typed answers to ask_user, never
/// the agent's reasoning or tool output. Subagents are given their lead's, so the judge always reads the lead's session.
/// </summary>
/// <param name="session">The log to read, and to record the judge's verdict in.</param>
/// <param name="workingDirectory">The agent's working directory.</param>
/// <param name="activeGoalObjective">The objective of the goal that is running, or null (the goal is the agent's state, not the log's).</param>
/// <param name="addCost">Adds the judge's cost to the agent's total.</param>
public sealed class SessionJudgeContext(ISessionStore session, string workingDirectory, Func<string?> activeGoalObjective, Action<double> addCost) : IJudgeContext
{
    public string WorkingDirectory => workingDirectory;

    public string? ActiveGoalObjective => activeGoalObjective();

    // The log is read afresh on every call: the user's latest words are in it.
    // Python: loop.py Agent.judge (the scan of self.session.events())
    public IEnumerable<JudgeUtterance> Utterances()
    {
        foreach (var e in Enumerable.Reverse(session.Read()))
        {
            if (e is MessageEvent { Message: { Text: { } text } message, ByHarness: false } && message.Role == Role.User)
                yield return new(JudgeUtteranceKind.Message, text);
            else if (e is AnswerEvent answer)
                yield return new(JudgeUtteranceKind.Answer, answer.Answer);
        }
    }

    // Python: loop.py Agent.judge (self.cost += ..., self.session.append("usage", ...))
    public void RecordSpend(double cost, JsonObject verdict)
    {
        addCost(cost);
        session.Append(new UsageEvent(cost, Judge: verdict));
    }
}
