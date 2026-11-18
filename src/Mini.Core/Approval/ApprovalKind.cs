// Python: tools.py Approver.ok (the kind strings: read, write, find, bash, mcp)
namespace Mini.Core.Approval;

/// <summary>What is being approved.</summary>
public enum ApprovalKind
{
    Read,
    Write,
    Find,
    Bash,
    Mcp,
}

public static class ApprovalKindExtensions
{
    /// <summary>The word the prompt and the judge are shown: "read", "write", "find", "bash" or "mcp".</summary>
    // Python: tools.py Approver.ok (kind)
    public static string Wire(this ApprovalKind kind) => kind switch
    {
        ApprovalKind.Read => "read",
        ApprovalKind.Write => "write",
        ApprovalKind.Find => "find",
        ApprovalKind.Bash => "bash",
        ApprovalKind.Mcp => "mcp",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
