// Python: tools.py Approver.ok (the `not self.interactive` check)
namespace Mini.Core.Approval;

/// <summary>Nobody can answer a prompt in a headless run: what reached here is denied.</summary>
public sealed class HeadlessStep(ApprovalSettings settings) : IApprovalStep
{
    public ValueTask<ApprovalDecision?> TryDecideAsync(ApprovalRequest request, ApprovalTrail trail, CancellationToken ct) =>
        ValueTask.FromResult<ApprovalDecision?>(settings.Interactive ? null : ApprovalDecision.Deny(trail.Reason
            + " Nobody can approve it in this run, so it was denied: try a smaller step, stay inside "
            + "the project, or ask the user to rerun with --allow."));
}
