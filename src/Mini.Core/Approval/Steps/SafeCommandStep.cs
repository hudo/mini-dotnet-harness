// Python: tools.py Approver.ok (the `kind == "bash" and self.pre_approved(...)` check)
namespace Mini.Core.Approval;

/// <summary>A bash command that is a plain read, or starts with one of the user's allow rules.</summary>
public sealed class SafeCommandStep(ApprovalSettings settings) : IApprovalStep
{
    public ValueTask<ApprovalDecision?> TryDecideAsync(ApprovalRequest request, ApprovalTrail trail, CancellationToken ct) =>
        ValueTask.FromResult<ApprovalDecision?>(
            request.Kind == ApprovalKind.Bash && CommandSafetyRules.PreApproved(request.Detail, settings.Allow, request.IsPrivate) ? ApprovalDecision.Allow : null);
}
