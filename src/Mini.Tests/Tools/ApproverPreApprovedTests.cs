using Mini.Tests.Fakes;
using static Mini.Tests.Tools.ToolsHelpers;

namespace Mini.Tests.Tools;

/// <summary>CommandSafetyRules.PreApproved: which bash commands run without asking, and the headless approver's verdicts.</summary>
public class ApproverPreApprovedTests
{
    static bool PreApproved(ApprovalSettings settings, string command, bool isPrivate = false) =>
        CommandSafetyRules.PreApproved(command, settings.Allow, isPrivate);

    // C#-only: a table of safe vs unsafe commands against PreApproved itself, with no allow rules.
    [Theory]
    [InlineData("ls", true)]
    [InlineData("ls -la", true)]
    [InlineData("cat app.py", true)]
    [InlineData("head -n 5 README.md", true)]
    [InlineData("tail log.txt", true)]
    [InlineData("wc -l a b", true)]
    [InlineData("pwd", true)]
    [InlineData("echo hi", true)]
    [InlineData("which python3", true)]
    [InlineData("stat file", true)]
    [InlineData("diff a b", true)]
    [InlineData("echo 'a b'", true)]            // quotes are fine: shlex splits them, they are not shell syntax here
    [InlineData("git status", true)]
    [InlineData("git diff", true)]
    [InlineData("git diff --stat src", true)]
    [InlineData("git log --oneline -5", true)]
    [InlineData("git show HEAD", true)]
    [InlineData("", false)]                      // no words
    [InlineData("   ", false)]
    [InlineData("rm -rf build", false)]
    [InlineData("make", false)]
    [InlineData("lsof", false)]                  // starts with "ls" but is another program
    [InlineData("python3 -c 1", false)]
    [InlineData("find . -delete", false)]
    [InlineData("grep -r TOKEN .", false)]       // grep isn't safe: -r reads every file
    [InlineData("rg --pre /bin/sh x", false)]
    [InlineData("git push", false)]
    [InlineData("git -C /x log", false)]         // an option between git and the subcommand
    [InlineData("git diff --output=x", false)]   // --output writes a file
    [InlineData("git diff --ext-diff", false)]
    [InlineData("git log --textconv", false)]
    [InlineData("git show HEAD:.env", false)]    // rev:path could show a committed secret
    [InlineData("git", false)]
    [InlineData("ls; rm -rf /", false)]
    [InlineData("ls && rm x", false)]
    [InlineData("ls | sh", false)]
    [InlineData("ls\nrm -rf x", false)]
    [InlineData("ls\rrm -rf x", false)]
    [InlineData("echo hi > f", false)]
    [InlineData("cat < f", false)]
    [InlineData("cat $(whoami)", false)]
    [InlineData("cat `whoami`", false)]
    [InlineData("echo $HOME", false)]
    [InlineData("echo {a,b}", false)]
    [InlineData("echo (x)", false)]
    [InlineData("ls \\", false)]
    [InlineData("cat 'unterminated", false)]     // bad quoting: can't be split, so never pre-approved
    public void PreApprovedTable(string command, bool expected) => Assert.Equal(expected, PreApproved(new ApprovalSettings(interactive: false), command));

    // C#-only: a safe command that names a private path (outside, secret, glob) loses its pass; an allow rule does not.
    [Fact]
    public void PrivatePathsRemoveTheSafePassButNotAnAllowRule()
    {
        var plain = new ApprovalSettings(interactive: false);
        Assert.True(PreApproved(plain, "cat app.py", isPrivate: false));
        Assert.False(PreApproved(plain, "cat app.py", isPrivate: true));
        Assert.False(PreApproved(plain, "git status", isPrivate: true));

        var allowing = new ApprovalSettings(interactive: false, allow: ["cat app.py"]);
        Assert.True(PreApproved(allowing, "cat app.py", isPrivate: true)); // the user said so, whatever the path
    }

    // C#-only: --allow prefixes match whole words from the start of the command, never a substring or a shorter command.
    [Theory]
    [InlineData("python3 -m pytest", true)]
    [InlineData("python3 -m pytest -q", true)]
    [InlineData("python3 -m pytest tests/test_x.py::test_y", true)]
    [InlineData("python3 -m pytes", false)]            // not a whole word
    [InlineData("python3 -m", false)]                  // shorter than the rule
    [InlineData("python3", false)]
    [InlineData("python3 -c 1", false)]
    [InlineData("sudo python3 -m pytest", false)]      // must start the command
    [InlineData("python3 -m pytest; rm -rf /", false)] // shell syntax is refused whatever the prefix
    [InlineData("python3 -m pytest $(x)", false)]
    [InlineData("python3 -m pytest > out", false)]
    [InlineData("dotnet test", true)]                  // the second rule
    [InlineData("dotnet build", false)]
    public void AllowPrefixes(string command, bool expected)
    {
        var ap = new ApprovalSettings(interactive: false, allow: ["python3 -m pytest", "dotnet test"]);

        Assert.Equal(expected, PreApproved(ap, command));
    }

    // C#-only: quoting in a rule is shlex-split, so a quoted word is one word; bad quoting in a rule throws FormatException (ValueError).
    [Fact]
    public void AllowRulesAreSplitLikeShlex()
    {
        var ap = new ApprovalSettings(interactive: false, allow: ["printf 'a b'"]); // printf isn't a "safe" command, so only the rule can pass it

        Assert.True(PreApproved(ap, "printf 'a b'"));
        Assert.True(PreApproved(ap, "printf \"a b\" extra"));
        Assert.False(PreApproved(ap, "printf a b"));
        Assert.Throws<FormatException>(() => new ApprovalSettings(allow: ["printf 'oops"]));
    }

    // C#-only: through BashAsync an --allow rule lets a command that isn't "safe" run in a headless run; others are denied.
    [Fact]
    public async Task AllowRuleLetsABashCommandRunHeadless()
    {
        using var ws = new Workspace();
        ws.Write("f.txt", "hello\n");
        var tools = ws.Tools(new ApprovalSettings(interactive: false, allow: ["sort"]));

        Assert.Contains("hello", await tools.BashAsync("sort f.txt", ct: TestContext.Current.CancellationToken));
        var e = await Assert.ThrowsAsync<ToolException>(() => tools.BashAsync("uniq f.txt", ct: TestContext.Current.CancellationToken));
        Assert.Matches("denied", e.Message);
    }
}
