// Python: tools.py Approver.ok (the `self.auto` check)
namespace Mini.Core.Approval;

/// <summary>--permissions yes: everything is approved.</summary>
public sealed class AutoApproveStep(ApprovalSettings settings) : IApprovalStep
{
    public ValueTask<ApprovalDecision?> TryDecideAsync(ApprovalRequest request, ApprovalTrail trail, CancellationToken ct) =>
        ValueTask.FromResult<ApprovalDecision?>(settings.Auto ? ApprovalDecision.Allow : null);
}
