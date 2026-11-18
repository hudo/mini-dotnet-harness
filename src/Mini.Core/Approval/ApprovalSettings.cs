// Python: tools.py Approver, Approver.__init__, Approver.mode
namespace Mini.Core.Approval;

/// <summary>Who approves in this session: nobody needed (yes), the user, or a judge first; and which bash commands are pre-approved.
/// /permissions flips <see cref="Auto"/> and <see cref="UseJudge"/> while it runs; the steps read them at each request.</summary>
public sealed class ApprovalSettings(bool auto = false, bool interactive = true, IEnumerable<string>? allow = null, bool useJudge = false)
{
    // Python: tools.py Approver.__init__ (self.auto, self.interactive, self.use_judge)
    public bool Auto { get; set; } = auto;

    /// <summary>False in a headless run: nobody can answer a prompt.</summary>
    public bool Interactive { get; set; } = interactive;

    public bool UseJudge { get; set; } = useJudge;

    // Blank rules are dropped: an empty prefix list would match every command in PreApproved.
    // A rule with bad quoting throws FormatException, as shlex.split raised ValueError.
    // Python: tools.py Approver.__init__ (self.allow)
    public List<List<string>> Allow { get; } = allow?.Where(a => !string.IsNullOrWhiteSpace(a)).Select(ShellWords.Split).ToList()
        ?? new List<List<string>>();

    // Python: tools.py Approver.mode
    public PermissionMode Mode => Auto ? PermissionMode.Yes : UseJudge ? PermissionMode.Judge : PermissionMode.Ask;
}
