// Python: tools.py GATED, approval
namespace Mini.Core.Approval;

/// <summary>The sentence that tells the model who approves in this session.</summary>
public static class ApprovalStatement
{
    // Who approves, said once in the system prompt (and again in a note when it changes): a model told that it may be
    // denied believes it, even when everything is approved.
    // One-line raw literals: the Python string has no newlines.
    // Python: tools.py GATED
    public const string Gated = """bash commands other than plain reads, reads and searches outside the working directory or of files that may """
        + """hold secrets, writes outside it or to files that run later, and MCP calls""";

    // Python: tools.py approval
    public static string Describe(ApprovalSettings settings)
    {
        if (settings.Auto)
            return """Approval in this session (permissions yes): every tool call runs without asking, so nothing is denied.""";
        const string nobody = """are denied, since nobody can approve in this run""";
        var how = settings.UseJudge
            ? """go to a reviewer model that may say no; writes, secrets and calls it's unsure about """
                + (settings.Interactive ? """go to the user""" : nobody)
            : settings.Interactive ? """ask the user, who may say no""" : nobody;
        var pre = string.Join(", ", settings.Allow.Select(ShellWords.Join));
        return $$"""Approval in this session (permissions {{settings.Mode.ToCliName()}}): {{Gated}} {{how}}."""
            + (pre.Length > 0 ? $$""" The user pre-approved calls starting with: {{pre}}.""" : "");
    }
}
