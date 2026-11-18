// Python: tools.py Approver.ok (the self.reason that the judge step wrote and the headless step added to)
namespace Mini.Core.Approval;

/// <summary>What the steps of one request have said so far. It lives for that request only: no state is shared between requests.</summary>
public sealed class ApprovalTrail
{
    /// <summary>The sentences earlier steps left, for a later step to extend.</summary>
    public string Reason { get; private set; } = "";

    public void Add(string reason) => Reason += reason;
}
