// Python: loop.py Agent.__init__ (self.goal_claim)
namespace Mini.Core.Tools.Agentic;

/// <summary>What the model claimed in goal_done during this turn: written by <see cref="GoalDoneTool"/>, read (and cleared at the start of each turn) by the goal code.</summary>
public sealed class GoalClaimSlot
{
    /// <summary>The evidence the model gave, or null when it made no claim.</summary>
    public string? Value { get; set; }

    public void Clear() => Value = null;
}
