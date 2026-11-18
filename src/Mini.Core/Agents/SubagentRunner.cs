using System.Security.Cryptography;

// Python: loop.py Agent.run_subagent
namespace Mini.Core.Agents;

/// <summary>Runs a prompt in a fresh agent with an empty context, on its own session log (<c>&lt;lead's id&gt;-sub-&lt;hex&gt;.jsonl</c>), and
/// returns its final answer. The child shares its lead's transport and approval; what it spends is added to the lead's total.</summary>
/// <param name="factory">Makes the child.</param>
/// <param name="sessions">Opens the child's log next to the lead's.</param>
/// <param name="session">The lead's session: where the child's log goes, and where its cost is recorded.</param>
/// <param name="lead">How the lead was set up: its directory, approval settings, model and shared transport and approval service.</param>
/// <param name="singleModel">Another provider has one model, so subagents run on it too, whatever the task asked for.</param>
/// <param name="efforts">The efforts the lead's model takes.</param>
/// <param name="effort">The lead's effort now: a child works at "medium" when the model has it, else at the lead's level.</param>
/// <param name="usage">The lead's spending, which the child's cost is added to.</param>
public sealed class SubagentRunner(IAgentFactory factory, SessionCatalog sessions, ISessionStore session, AgentSetup lead, bool singleModel,
    IReadOnlyList<string> efforts, EffortSetting effort, UsageTracker usage) : ISubagentRunner
{
    // Python: loop.py Agent.run_subagent
    public async Task<string> RunAsync(string prompt, string? model, CancellationToken ct)
    {
        var name = $"{session.Id}-sub-{RandomNumberGenerator.GetHexString(6, lowercase: true)}.jsonl";
        var path = Path.Combine(Path.GetDirectoryName(session.Path) ?? "", name);
        // Same client instance as Python's shared one, even on another provider; the child's streams carry their own observer.
        var child = factory.CreateNew(sessions.Open(path), lead with
        {
            Ui = lead.Ui.Child(),
            Model = singleModel ? lead.Model : model, // one model, so subagents run on it too
            Sub = true,
            Effort = efforts.Contains("medium") ? "medium" : effort.Value, // no medium: work at the lead's level
            Hub = null,
            Skills = null,
        });
        var stop = new StopReason(await child.TurnAsync(prompt, ct: ct));
        usage.Add(child.Cost);
        session.Append(new UsageEvent(Math.Round(child.Cost, 6), Subagent: name));
        if (stop == StopReason.Interrupted)
            throw new OperationCanceledException("the subagent was interrupted", ct);
        if (stop == StopReason.Error || stop == StopReason.Refusal)
            throw new ToolException($"The subagent stopped ({stop.Value}): {child.FinalText()}");
        var answer = child.FinalText();
        return answer.Length > 0 ? answer : $"[subagent stopped ({stop.Value}) without an answer]";
    }
}
