// Python: loop.py Agent.judge
namespace Mini.Core.Approval;

/// <summary>A reviewer that rules on risky actions.</summary>
public interface IApprovalJudge
{
    /// <summary>Allow, deny, or ask the user. Fails closed: a judge that can't be reached denies.</summary>
    Task<JudgeVerdict> JudgeAsync(ApprovalRequest request, IJudgeContext context, CancellationToken ct);
}
