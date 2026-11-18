using Mini.Tests.Approval;
using Mini.Tests.Fakes;
using static Mini.Tests.Tools.ToolsHelpers;

namespace Mini.Tests.Tools;

/// <summary>What asks for approval: writes into dot-folders and files that run later, anything when started in the home
/// folder, reads outside the project or of secrets, and the approver's own allow rules.</summary>
public class PathPolicyTests
{
    // Python test: test_writes_into_dot_git_need_approval
    [Fact]
    public async Task WritesIntoDotGitNeedApproval()
    {
        using var ws = new Workspace();
        Directory.CreateDirectory(ws.Path(".git"));
        var tools = Headless(ws);

        var e = await Assert.ThrowsAsync<ToolException>(() => tools.WriteAsync(".git/config", "[core]\n fsmonitor = evil", TestContext.Current.CancellationToken));

        Assert.Matches("denied", e.Message);
        Assert.False(File.Exists(ws.Path(".git/config")));
        Assert.NotEmpty(await tools.WriteAsync("ok.txt", "fine", TestContext.Current.CancellationToken));
    }

    // Python test: test_files_that_run_later_need_approval
    [Theory]
    [InlineData(".GIT/hooks/pre-commit")]
    [InlineData(".git/config")]
    [InlineData("mcp.json")]
    [InlineData("conftest.py")]
    [InlineData("sub/.husky/pre-push")]
    [InlineData("Makefile")]
    public async Task FilesThatRunLaterNeedApproval(string path)
    {
        using var ws = new Workspace();

        var e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws).WriteAsync(path, "x", TestContext.Current.CancellationToken));

        Assert.Matches("denied", e.Message);
        Assert.False(File.Exists(ws.Path(path)));
    }

    // Python test: test_git_repo_lookalike_files_need_approval
    [Theory]
    [InlineData("docs/HEAD")]
    [InlineData("docs/config")]
    public async Task GitRepoLookalikeFilesNeedApproval(string path)
    {
        using var ws = new Workspace();

        var e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws).WriteAsync(path, "x", TestContext.Current.CancellationToken));

        Assert.Matches("denied", e.Message);
    }

    // Python test: test_gnumakefile_is_protected
    [Fact]
    public async Task GnumakefileIsProtected()
    {
        using var ws = new Workspace();

        var e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws).WriteAsync("GNUmakefile", "x", TestContext.Current.CancellationToken));

        Assert.Matches("denied", e.Message);
    }

    // C#-only: every protected name in the denylist, in mixed case and in a subfolder (the check lowercases each path part).
    [Theory]
    [InlineData("justfile")]
    [InlineData("Justfile")]
    [InlineData("sub/package.json")]
    [InlineData("PYPROJECT.TOML")]
    [InlineData("setup.py")]
    [InlineData("noxfile.py")]
    [InlineData("tox.ini")]
    [InlineData("deep/er/MCP.JSON")]
    [InlineData(".mcp.json")]
    [InlineData(".env")]
    public async Task EveryProtectedNameAsksWhateverTheCase(string path)
    {
        using var ws = new Workspace();

        var e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws).WriteAsync(path, "x", TestContext.Current.CancellationToken));

        Assert.Matches("denied", e.Message);
    }

    // C#-only: the other side of the denylist: ordinary project files, nested or not, write without asking.
    [Theory]
    [InlineData("notes.txt")]
    [InlineData("src/deep/app.py")]
    [InlineData("docs/readme.md")]
    [InlineData("package.json.bak")]
    public async Task OrdinaryProjectFilesWriteWithoutAsking(string path)
    {
        using var ws = new Workspace();

        await Headless(ws).WriteAsync(path, "x", TestContext.Current.CancellationToken);

        Assert.Equal("x", File.ReadAllText(ws.Path(path)));
    }

    // C#-only: a write outside the working directory asks; the dot-dot form must not slip past the lexical prefix.
    [Theory]
    [InlineData("../outside-dotdot.txt")]
    [InlineData("sub/../../outside-dotdot.txt")]
    public async Task WritesOutsideTheProjectNeedApproval(string path)
    {
        using var ws = new Workspace();
        Directory.CreateDirectory(ws.Path("sub"));

        var e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws).WriteAsync(path, "x", TestContext.Current.CancellationToken));

        Assert.Matches("denied this write", e.Message);
        Assert.False(File.Exists(System.IO.Path.GetFullPath(System.IO.Path.Combine(ws.Root, path))));
    }

    // Python test: test_every_write_asks_when_started_in_home
    [Fact]
    public async Task EveryWriteAsksWhenStartedInHome()
    {
        using var ws = new Workspace();
        var started = new TestRuntime(o => o with { Home = ws.Root, UserProfile = ws.Root });

        var e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws, started).WriteAsync("notes.txt", "x", TestContext.Current.CancellationToken));

        Assert.Matches("denied", e.Message);
    }

    // C#-only: the home folder is a parent of the project: also not free to write (it would be too broad a project).
    [Fact]
    public async Task EveryWriteAsksWhenHomeIsInsideTheProject()
    {
        using var ws = new Workspace();
        Directory.CreateDirectory(ws.Path("fakehome"));
        var started = new TestRuntime(o => o with { Home = ws.Path("fakehome"), UserProfile = ws.Path("fakehome") });

        var e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws, started).WriteAsync("notes.txt", "x", TestContext.Current.CancellationToken));

        Assert.Matches("denied", e.Message);
    }

    // Python test: test_mcp_calls_need_approval
    [Fact]
    public async Task McpCallsNeedApproval()
    {
        using var ws = new Workspace();
        await using var hub = await StubMcp.StartAsync(ws);
        var call = FakeModelClient.Tool("mcp__mail__send", Json.Obj(("to", "x")));

        var (denied, fake) = ws.Make([FakeModelClient.Reply("tool_use", call), FakeModelClient.Reply(FakeModelClient.Text("ok"))],
            approver: new ApprovalSettings(interactive: false), hub: hub);
        await denied.TurnAsync("send", ct: TestContext.Current.CancellationToken);
        var result = FirstResult(fake.Requests[1]);
        Assert.True(result["is_error"]!.GetValue<bool>());
        Assert.Matches("denied", Json.Str(result["content"]));

        var (allowed, fake2) = ws.Make([FakeModelClient.Reply("tool_use", call), FakeModelClient.Reply(FakeModelClient.Text("ok"))],
            approver: new ApprovalSettings(interactive: false, allow: ["mcp__mail__"]), hub: hub);
        await allowed.TurnAsync("send", ct: TestContext.Current.CancellationToken);
        result = FirstResult(fake2.Requests[1]);
        Assert.Equal("sent", Json.Str(result["content"]));
        Assert.Null(result["is_error"]);
    }

    // Python test: test_empty_allow_entry_approves_nothing
    [Fact]
    public async Task EmptyAllowEntryApprovesNothing()
    {
        var ap = new ApprovalSettings(interactive: false, allow: [""]);

        Assert.False(await ap.OkAsync("bash", "rm -rf x", ct: TestContext.Current.CancellationToken));
        Assert.False(await ap.OkAsync("mcp", "mcp__x__y {}", ct: TestContext.Current.CancellationToken));
        Assert.Empty(ap.Allow);
    }

    // Python test: test_read_only_git_needs_no_approval
    [Fact]
    public async Task ReadOnlyGitNeedsNoApproval()
    {
        var ap = new ApprovalSettings(interactive: false);

        foreach (var good in new[] { "git status", "git diff", "git diff --stat src", "git log --oneline -5", "git show HEAD" })
            Assert.True(await ap.OkAsync("bash", good, ct: TestContext.Current.CancellationToken), good);
    }

    // Python test: test_headless_denial_says_why_and_what_to_try
    [Fact]
    public async Task HeadlessDenialSaysWhyAndWhatToTry()
    {
        using var ws = new Workspace();

        var e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws).BashAsync("make", ct: TestContext.Current.CancellationToken));

        Assert.Matches("Nobody can approve it in this run.*--allow", e.Message);
    }

    // Python test: test_reads_outside_the_project_or_of_secrets_ask_first
    [Fact]
    public async Task ReadsOutsideTheProjectOrOfSecretsAskFirst()
    {
        using var ws = new Workspace();
        var outsideDir = OutsideDir();
        try
        {
            // The Python test swaps approver.ok for a recorder; here the real approver prompts and the scripted stdin says no.
            using var console = new ConsoleCapture("n", "n", "n", "n");
            var t = ws.Tools(new ApprovalSettings());
            ws.Write("app.py", "x = 1\n");
            ws.Write(".env", "TOKEN=secret\n");
            ws.Write(".env.example", "TOKEN=\n");
            var outside = System.IO.Path.Combine(outsideDir, "elsewhere.txt");
            File.WriteAllText(outside, "private\n");

            Assert.Contains("x = 1", ReadText(await t.ReadAsync("app.py", ct: TestContext.Current.CancellationToken)));
            Assert.Contains("TOKEN=", ReadText(await t.ReadAsync(".env.example", ct: TestContext.Current.CancellationToken)));
            Assert.DoesNotContain("allow", console.Out);

            foreach (var path in new[] { ".env", outside, "~/.ssh/id_ed25519" })
            {
                var e = await Assert.ThrowsAsync<ToolException>(() => t.ReadAsync(path, ct: TestContext.Current.CancellationToken));
                Assert.Matches("denied this read", e.Message);
            }
            Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(console.Out, "allow read:").Count);

            Assert.DoesNotContain("secret", await t.FindAsync(contains: "TOKEN", ct: TestContext.Current.CancellationToken)); // names may show; secrets' contents aren't searched
            var s = await Assert.ThrowsAsync<ToolException>(() => t.FindAsync(path: outsideDir, ct: TestContext.Current.CancellationToken));
            Assert.Matches("denied this search", s.Message);
            Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(console.Out, "allow (read|find):").Count);
        }
        finally { Directory.Delete(outsideDir, recursive: true); }
    }

    // Python test: test_safe_commands_lose_their_pass_on_private_paths
    [Fact]
    public async Task SafeCommandsLoseTheirPassOnPrivatePaths()
    {
        using var ws = new Workspace();
        ws.Write("app.py", "print(1)\n");
        var t = Headless(ws);

        // Python asserts the private helper _names_private; the visible effect is the same: a safe command that names a
        // private path is no longer pre-approved, so a headless run denies it, and one that doesn't just runs.
        Assert.Contains("print(1)", await t.BashAsync("cat app.py", ct: TestContext.Current.CancellationToken));
        Assert.NotEmpty(await t.BashAsync("ls -la", ct: TestContext.Current.CancellationToken));
        foreach (var c in new[] { "cat .env", "head ../x", "cat /etc/passwd", "tail ~/.aws/credentials", "cat *", "wc key.pem" })
        {
            var e = await Assert.ThrowsAsync<ToolException>(() => t.BashAsync(c, ct: TestContext.Current.CancellationToken));
            Assert.Matches("denied this command", e.Message);
            Assert.False((await t.Approval.RequestAsync(new ApprovalRequest(ApprovalKind.Bash, c, IsPrivate: true), TestContext.Current.CancellationToken)).Allowed, c); // headless: no pass, so denied
        }
        Assert.False(CommandSafetyRules.PreApproved("grep -r TOKEN .", new ApprovalSettings().Allow)); // grep -r would read every file
    }

    // Python test: test_a_tilde_word_for_no_user_does_not_crash_or_hide_the_other_words
    [Fact]
    public async Task ATildeWordForNoUserDoesNotCrashOrHideTheOtherWords()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "runs bash with tilde words");
        using var ws = new Workspace();
        Assert.Equal("~nosuchuser_mini", await Auto(ws).BashAsync("echo ~nosuchuser_mini", ct: TestContext.Current.CancellationToken));

        var asked = new List<string>();
        var approver = new ApprovalSettings(interactive: false, useJudge: true);
        var judge = new ApprovalHelpers.CannedJudge(request => { asked.Add(request.Detail); return new JudgeVerdict(JudgeChoice.Allow, "fine"); });
        var e = await Assert.ThrowsAsync<ToolException>(() => ws.Tools(approver, judge: judge).BashAsync("cat ~nosuchuser_mini ~/.ssh/id_rsa", ct: TestContext.Current.CancellationToken));
        Assert.Matches("denied", e.Message); // a secret: never the judge's call, and headless nobody's
        Assert.Empty(asked);

        foreach (var word in new[] { "~+/../outside.txt", "~-/outside.txt", "~nosuchuser_mini" }) // bash expands the first two
        {
            e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws).BashAsync($"cat {word}", ct: TestContext.Current.CancellationToken));
            Assert.Matches("denied", e.Message); // cat is safe inside the project; these count as outside
        }
    }
}
