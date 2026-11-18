// Python: tools.py Approver.ok (its bool, and Approver.reason, which the caller read afterwards)
namespace Mini.Core.Approval;

/// <summary>The answer to an <see cref="ApprovalRequest"/>, with the sentence a denial adds to the tool's error (empty, or starting with a space).</summary>
public sealed record ApprovalDecision(bool Allowed, string Reason)
{
    /// <summary>Allowed, with nothing to say.</summary>
    public static ApprovalDecision Allow { get; } = new(true, "");

    /// <summary>Denied for this reason (it starts with a space, as it is appended to a message).</summary>
    public static ApprovalDecision Deny(string reason) => new(false, reason);
}
