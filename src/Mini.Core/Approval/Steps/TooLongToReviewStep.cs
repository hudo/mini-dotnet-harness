// Python: tools.py Approver.ok (the `len(detail) > 4000` check)
namespace Mini.Core.Approval;

/// <summary>An action too long to review, by the user or by the judge, which must see the whole of it.</summary>
public sealed class TooLongToReviewStep : IApprovalStep
{
    public ValueTask<ApprovalDecision?> TryDecideAsync(ApprovalRequest request, ApprovalTrail trail, CancellationToken ct) =>
        ValueTask.FromResult<ApprovalDecision?>(request.Detail.Length > 4000
            ? ApprovalDecision.Deny(" Too long to review: split it into smaller steps.") : null);
}
