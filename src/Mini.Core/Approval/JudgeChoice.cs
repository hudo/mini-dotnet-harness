namespace Mini.Core.Approval;

/// <summary>What the reviewer model decided: run it, refuse it, or leave it to the user.</summary>
// Python: none (C#-only: Agent.judge in loop.py returns "allow", "deny" or "ask" as strings)
public enum JudgeChoice { Allow, Deny, Ask }
