namespace Mini.Core.Agents.Goals;

/// <summary>Whether the goal was accepted, and what to tell the worker when it was not.</summary>
// Python: loop.py Agent.verify (its return value)
public sealed record GoalVerification(bool Ok, string Output);
