// Python: loop.py Agent.__init__ (self.goal)
// Python: loop.py Agent.goal_rules
namespace Mini.Core.Agents.Goals;

/// <summary>The agent's goal, where everything that reads it can reach it: the goal runner changes it, the compactor restates its rules, the
/// judge reads its objective and goal_done checks it is running. One holder, so none of them has to depend on the goal runner.</summary>
public sealed class GoalSlot
{
    /// <summary>The goal, or null when there is none (never set, or cleared).</summary>
    public Goal? Value { get; set; }

    /// <summary>A /goal run is going on.</summary>
    public bool IsActive => Value is { Status: GoalStatus.Active };

    /// <summary>The objective of the goal that is running, or null.</summary>
    public string? ActiveObjective => IsActive ? Value!.Objective : null;

    /// <summary>The goal's rules as a note for the model, when the goal is running; else null.</summary>
    public string? ActiveRules => IsActive ? Rules() : null;

    /// <summary>The note that tells the model what the goal is and how it is accepted.</summary>
    // Python: loop.py Agent.goal_rules
    public string Rules()
    {
        var g = Value!;
        var verify = string.IsNullOrEmpty(g.Verify)
            ? ""
            : $"""After you call it, the harness runs `{g.Verify}` and only accepts the goal if it passes. """;
        return HarnessPrompts.GoalOn.Render(("objective", g.Objective), ("verify", verify));
    }
}
