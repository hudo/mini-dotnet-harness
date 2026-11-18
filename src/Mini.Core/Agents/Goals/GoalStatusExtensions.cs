namespace Mini.Core.Agents.Goals;

/// <summary>The lowercase name of a <see cref="GoalStatus"/>, as the session file and the prompts spell it.</summary>
// Python: none (C#-only: Python's status is already the string)
public static class GoalStatusExtensions
{
    public static string Wire(this GoalStatus status) => status.ToString().ToLowerInvariant();
}
