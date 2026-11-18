namespace Mini.Core.Approval;

/// <summary>The reviewer model's decision and the reason shown to the user and the model.</summary>
// Python: loop.py Agent.judge (the (verdict, why) it returns)
public sealed record JudgeVerdict(JudgeChoice Choice, string Why);
