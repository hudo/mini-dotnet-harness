// Python: tools.py Approver.ok (one of its early returns)
namespace Mini.Core.Approval;

/// <summary>One link of the approval chain: decides, or passes the request to the next.</summary>
public interface IApprovalStep
{
    /// <summary>Null: not this step's to decide.</summary>
    ValueTask<ApprovalDecision?> TryDecideAsync(ApprovalRequest request, ApprovalTrail trail, CancellationToken ct);
}
