// Python: loop.py Agent.save_goal, Agent.set_goal, Agent.clear_goal, Agent.run_goal
namespace Mini.Core.Agents.Goals;

/// <summary>Works toward a goal, turn after turn, until it is accepted, blocked, interrupted or out of turns or money. It owns the goal's
/// state changes and writes each to the session; the checking is the <see cref="GoalVerifier"/>'s.</summary>
public sealed class GoalRunner(GoalSlot goal, GoalClaimSlot claim, TurnRunner turns, Compactor compactor, GoalVerifier verifier, UsageTracker usage,
    Transcript transcript, ISessionStore session)
{
    /// <summary>Writes the goal as it is now to the session.</summary>
    // Python: loop.py Agent.save_goal
    public void Save() => session.Append(new GoalEvent(goal.Value!));

    // Python: loop.py Agent.set_goal
    public Task<Goal> SetAsync(string objective, string? verify, int maxTurns, double? budget, CancellationToken ct = default)
    {
        goal.Value = new Goal(objective, maxTurns: maxTurns, verify: verify, budget: budget);
        return RunAsync($"Work toward this goal until it is done:\n{objective}", ct);
    }

    // Python: loop.py Agent.clear_goal
    public void Clear()
    {
        if (goal.Value is not { } g) return;
        g.Clear();
        Save();
        transcript.PendingNotes.Add(HarnessPrompts.GoalOff.Render(("status", "cleared by the user")));
        goal.Value = null;
    }

    // Python: loop.py Agent.run_goal
    public async Task<Goal> RunAsync(string first = "Resume work toward the goal.", CancellationToken ct = default)
    {
        var g = goal.Value!;
        g.Activate();
        Save();
        transcript.PendingNotes.Add(goal.Rules()); // restated on every start, so a resume after compaction has it
        var startCost = usage.Cost - g.Spent;
        var runStart = g.Spent;
        var limit = g.Turns + g.MaxTurns; // limits apply per run, so a resume gets a fresh allowance
        var message = first;
        try
        {
            while (true)
            {
                await compactor.MaybeCompactAsync(ct);
                var stop = await turns.TurnAsync(message, harness: true, ct: ct); // the objective reaches the judge from the goal itself
                g.RecordTurn(usage.Cost - startCost);
                if (stop == StopReason.Interrupted || stop == StopReason.Error || stop == StopReason.Refusal || stop == StopReason.MaxTokens) // max_tokens: it already got two retries
                {
                    g.Pause(stop.Value);
                    break;
                }
                if (claim.Value is not null)
                {
                    bool ok;
                    string output;
                    try
                    {
                        (ok, output) = await verifier.VerifyAsync(ct);
                    }
                    catch (ToolException e) // the worker can't fix a broken reviewer
                    {
                        g.Pause($"reviewer failed: {e.Message}".TakeScalars(300));
                        break;
                    }
                    finally
                    {
                        g.RecordSpent(usage.Cost - startCost); // the review counts toward the budget
                    }
                    if (ok)
                    {
                        g.Complete(claim.Value);
                        break;
                    }
                    message = $"The goal isn't accepted yet.\n{output}\nKeep working.";
                }
                else if (transcript.FinalText().Contains("BLOCKED:", StringComparison.Ordinal))
                {
                    g.Pause("blocked: waiting for the user");
                    break;
                }
                else
                {
                    message = HarnessPrompts.GoalNext;
                }
                if (g.Turns >= limit)
                {
                    g.Pause($"turn limit ({g.MaxTurns} per run)");
                    break;
                }
                if (g.IsOverBudget(runStart))
                {
                    g.Pause($"budget (${PythonJson.FormatFloat(g.Budget!.Value)} per run)");
                    break;
                }
            }
        }
        catch // a BaseException in Python; whatever ended the run leaves the goal paused
        {
            g.Pause("interrupted");
            throw;
        }
        finally
        {
            Save();
            transcript.PendingNotes.Add(HarnessPrompts.GoalOff.Render(("status", g.Status.Wire())));
        }
        return g;
    }
}
