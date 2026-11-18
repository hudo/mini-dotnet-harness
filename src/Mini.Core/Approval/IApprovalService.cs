// Python: tools.py Approver.ok
namespace Mini.Core.Approval;

/// <summary>What a tool asks before it does something risky.</summary>
public interface IApprovalService
{
    Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken ct);
}
