using System.Text.Json.Serialization;

namespace Mini.Core.Agents.Goals;

/// <summary>
/// The goal of a /goal run, and the rules for how it changes: only the methods below move it from one state to another, so a
/// status and its note can never disagree. Snake_case in the session file, in the order the file has always had.
/// </summary>
// Python: loop.py Agent.set_goal (the goal dict it builds)
public sealed class Goal
{
    /// <summary>A goal as it is made (paused, no turns yet) or as the session file says it was (the parameters beyond the objective).</summary>
    [JsonConstructor]
    public Goal(string objective, GoalStatus status = GoalStatus.Paused, int turns = 0, int maxTurns = 25, string? verify = null,
        double? budget = null, double spent = 0, string note = "")
    {
        Objective = objective;
        Status = status;
        Turns = turns;
        MaxTurns = maxTurns;
        Verify = verify;
        Budget = budget;
        Spent = spent;
        Note = note;
    }

    [JsonPropertyName("objective")] public string Objective { get; }
    [JsonPropertyName("status")] public GoalStatus Status { get; private set; }
    [JsonPropertyName("turns")] public int Turns { get; private set; }

    /// <summary>The turns one run may take; a resume gets a fresh allowance.</summary>
    [JsonPropertyName("max_turns")] public int MaxTurns { get; }

    /// <summary>A command that must pass before the goal is accepted; null for none.</summary>
    [JsonPropertyName("verify")] public string? Verify { get; }

    /// <summary>The dollars one run may spend; null or zero for no limit.</summary>
    [JsonPropertyName("budget")] public double? Budget { get; }

    [JsonPropertyName("spent")] public double Spent { get; private set; }

    /// <summary>Why the goal is in its status: the reason it paused, or the evidence it was completed on.</summary>
    [JsonPropertyName("note")] public string Note { get; private set; }

    /// <summary>A run starts (or resumes) the goal.</summary>
    // Python: loop.py Agent.run_goal (the start of the run)
    public void Activate()
    {
        Status = GoalStatus.Active;
        Note = "";
    }

    /// <summary>The run stops with work left: interrupted, failed, blocked on the user, or out of turns or money.</summary>
    // Python: loop.py Agent.run_goal (every stop short of done)
    public void Pause(string reason)
    {
        Status = GoalStatus.Paused;
        Note = reason;
    }

    /// <summary>The goal was met and verified. The note keeps the start of the agent's evidence.</summary>
    // Python: loop.py Agent.run_goal (the accepted claim)
    public void Complete(string evidence)
    {
        Status = GoalStatus.Done;
        Note = evidence.TakeScalars(500);
    }

    /// <summary>The user cleared the goal. The note stays as it was.</summary>
    // Python: loop.py Agent.clear_goal
    public void Clear() => Status = GoalStatus.Cleared;

    /// <summary>A turn ran: count it, and record what the goal has cost so far.</summary>
    // Python: loop.py Agent.run_goal (a turn's accounting)
    public void RecordTurn(double spent)
    {
        Turns++;
        Spent = spent;
    }

    /// <summary>Money was spent outside a turn (the reviewer): it counts toward the budget.</summary>
    // Python: loop.py Agent.run_goal (the review's cost)
    public void RecordSpent(double spent) => Spent = spent;

    /// <summary>True once this run has spent its budget; <paramref name="spentAtStart"/> is what the goal had cost when the run began.</summary>
    // Python: loop.py Agent.run_goal (the budget check)
    public bool IsOverBudget(double spentAtStart) => Budget is { } budget && budget != 0 && Spent - spentAtStart >= budget;
}
