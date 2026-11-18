namespace Mini.Core.Agents.Goals;

/// <summary>The --verify, --max-turns and --budget a new goal starts with.</summary>
// Python: cli.py main (agent.goal_defaults)
public sealed record GoalLimits(string? Verify, int MaxTurns, double? Budget);
