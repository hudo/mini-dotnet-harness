using Mini.Tests.Fakes;
using static Mini.Tests.Approval.ApprovalHelpers;

namespace Mini.Tests.Approval;

/// <summary>The approval chain, step by step and in order, with a scripted user and a canned judge: no terminal, no files.</summary>
// Python test helper: tests/test_mini.py (Approver.ok, one check at a time)
public class ApprovalPipelineTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static ApprovalPipeline Pipeline(ApprovalSettings settings, ScriptedUserInteraction? user = null, IApprovalJudge? judge = null) =>
        ApprovalPipeline.Create(settings, user ?? new ScriptedUserInteraction(), judge ?? new CannedJudge(_ => throw new InvalidOperationException("the judge was asked")),
            new FakeContext());

    static ApprovalRequest Bash(string command, bool isPrivate = false, bool secret = false) => new(ApprovalKind.Bash, command, isPrivate, secret);

    sealed class FakeContext : IJudgeContext
    {
        public string WorkingDirectory => "/w";
        public IEnumerable<JudgeUtterance> Utterances() => [];
        public string? ActiveGoalObjective => null;
        public void RecordSpend(double cost, System.Text.Json.Nodes.JsonObject verdict) { }
    }

    // The order of Approver.ok, pinned by what beats what: auto, safe command, MCP allow-list, too long, judge, headless, user.
    [Fact]
    public async Task TheStepsComeInTheOrderOfApproverOk()
    {
        // auto beats everything, even a command too long to review
        Assert.True((await Pipeline(new ApprovalSettings(auto: true)).RequestAsync(Bash(new string('x', 5000)), Ct)).Allowed);
        // a safe command is approved before the too-long check
        Assert.True((await Pipeline(new ApprovalSettings()).RequestAsync(Bash("ls " + new string('x', 5000)), Ct)).Allowed);
        // too long beats the judge and the user
        var user = new ScriptedUserInteraction(allow: true);
        var d = await Pipeline(new ApprovalSettings(useJudge: true), user).RequestAsync(Bash("make " + new string('x', 5000)), Ct);
        Assert.False(d.Allowed);
        Assert.Empty(user.Prompts);
        // the judge beats headless: its verdict is the answer, not "nobody can approve"
        var judged = Pipeline(new ApprovalSettings(useJudge: true, interactive: false), judge: new CannedJudge(_ => new JudgeVerdict(JudgeChoice.Allow, "allow with confidence 1.00")));
        Assert.True((await judged.RequestAsync(Bash("make"), Ct)).Allowed);
    }

    [Fact]
    public async Task AutoApprovesEverything()
    {
        var p = Pipeline(new ApprovalSettings(auto: true, interactive: false));

        Assert.Equal(ApprovalDecision.Allow, await p.RequestAsync(Bash("rm -rf /", secret: true), Ct));
        Assert.True((await p.RequestAsync(new ApprovalRequest(ApprovalKind.Write, "/etc/hosts\nx"), Ct)).Allowed);
    }

    [Fact]
    public async Task APlainReadCommandPassesAndAPrivateOneDoesNot()
    {
        var p = Pipeline(new ApprovalSettings(interactive: false));

        Assert.True((await p.RequestAsync(Bash("cat app.py"), Ct)).Allowed);
        Assert.False((await p.RequestAsync(Bash("cat /etc/passwd", isPrivate: true), Ct)).Allowed);
    }

    [Fact]
    public async Task AnAllowRulePassesAPrivateCommandToo()
    {
        var p = Pipeline(new ApprovalSettings(interactive: false, allow: ["cat"]));

        Assert.True((await p.RequestAsync(Bash("cat /etc/passwd", isPrivate: true), Ct)).Allowed);
    }

    [Fact]
    public async Task AnMcpCallPassesWhenItsNameStartsWithTheFirstWordOfARule()
    {
        var p = Pipeline(new ApprovalSettings(interactive: false, allow: ["mcp__mail__ extra words"]));

        Assert.True((await p.RequestAsync(new ApprovalRequest(ApprovalKind.Mcp, "mcp__mail__send {\"to\": 1}"), Ct)).Allowed);
        Assert.False((await p.RequestAsync(new ApprovalRequest(ApprovalKind.Mcp, "mcp__other__send {}"), Ct)).Allowed);
        Assert.False((await p.RequestAsync(Bash("mcp__mail__send"), Ct)).Allowed); // a bash command is not an MCP call
    }

    [Fact]
    public async Task ADetailOver4000CharactersIsDeniedBeforeAnyoneIsAsked()
    {
        var user = new ScriptedUserInteraction(allow: true);
        var p = Pipeline(new ApprovalSettings(), user);

        var long4001 = await p.RequestAsync(Bash(new string('x', 4001)), Ct);
        var exactly4000 = await p.RequestAsync(Bash("make " + new string('x', 3995)), Ct);

        Assert.False(long4001.Allowed);
        Assert.Equal(" Too long to review: split it into smaller steps.", long4001.Reason);
        Assert.True(exactly4000.Allowed); // the limit is "over 4000"
        Assert.Single(user.Prompts);
    }

    [Theory]
    [InlineData("allow", true)]
    [InlineData("deny", false)]
    public async Task ASureVerdictDecidesAndItsReasonIsKept(string choice, bool allowed)
    {
        var user = new ScriptedUserInteraction();
        var p = Pipeline(new ApprovalSettings(useJudge: true), user,
            new CannedJudge(_ => new JudgeVerdict(Enum.Parse<JudgeChoice>(choice, true), $"{choice} with confidence 0.95")));

        var d = await p.RequestAsync(Bash("make"), Ct);

        Assert.Equal(allowed, d.Allowed);
        Assert.Equal($" Judge: {choice} with confidence 0.95", d.Reason);
        Assert.Empty(user.Prompts);
    }

    [Fact]
    public async Task AnAskVerdictGoesToTheUserAndItsReasonSurvivesTheirNo()
    {
        var user = new ScriptedUserInteraction(); // says no
        var p = Pipeline(new ApprovalSettings(useJudge: true), user, new CannedJudge(_ => new JudgeVerdict(JudgeChoice.Ask, "unsure (allow, confidence 0.60)")));

        var d = await p.RequestAsync(Bash("make"), Ct);

        Assert.Equal(new ApprovalDecision(false, " Judge: unsure (allow, confidence 0.60)"), d);
        Assert.Single(user.Prompts);
        Assert.Equal(new ApprovalPrompt(ApprovalKind.Bash, "make"), user.Prompts[0]);
    }

    [Fact]
    public async Task AnAskVerdictInAHeadlessRunIsDeniedWithBothReasons()
    {
        var p = Pipeline(new ApprovalSettings(useJudge: true, interactive: false), judge: new CannedJudge(_ => new JudgeVerdict(JudgeChoice.Ask, "unsure (deny, confidence 0.50)")));

        var d = await p.RequestAsync(Bash("make"), Ct);

        Assert.False(d.Allowed);
        Assert.StartsWith(" Judge: unsure (deny, confidence 0.50) Nobody can approve it in this run, so it was denied: ", d.Reason);
        Assert.EndsWith("or ask the user to rerun with --allow.", d.Reason);
    }

    [Fact]
    public async Task WritesAndSecretsNeverGoToTheJudge()
    {
        var user = new ScriptedUserInteraction(allow: true);
        var p = Pipeline(new ApprovalSettings(useJudge: true), user); // a judge that throws if asked

        Assert.True((await p.RequestAsync(new ApprovalRequest(ApprovalKind.Write, "/p/conftest.py\nx"), Ct)).Allowed);
        Assert.True((await p.RequestAsync(new ApprovalRequest(ApprovalKind.Read, "/p/.env", IsSecret: true), Ct)).Allowed);
        Assert.True((await p.RequestAsync(Bash("cat .env", isPrivate: true, secret: true), Ct)).Allowed);
        Assert.Equal(3, user.Prompts.Count);
    }

    [Fact]
    public async Task TheJudgeIsOnlyAskedInJudgeMode()
    {
        var user = new ScriptedUserInteraction(allow: true);
        var p = Pipeline(new ApprovalSettings(), user); // useJudge off: the throwing judge must not be reached

        Assert.True((await p.RequestAsync(Bash("make"), Ct)).Allowed);
    }

    [Fact]
    public async Task ACancelOnceTheJudgeAnsweredStopsAnAllow()
    {
        using var cts = new CancellationTokenSource();
        var p = Pipeline(new ApprovalSettings(useJudge: true), judge: new CannedJudge(_ =>
        {
            cts.Cancel(); // Ctrl-C while the judge was deciding
            return new JudgeVerdict(JudgeChoice.Allow, "allow with confidence 1.00");
        }));

        await Assert.ThrowsAsync<OperationCanceledException>(() => p.RequestAsync(Bash("make"), cts.Token));
    }

    [Fact]
    public async Task HeadlessDeniesWhatReachesIt()
    {
        var p = Pipeline(new ApprovalSettings(interactive: false));

        var d = await p.RequestAsync(Bash("make"), Ct);

        Assert.False(d.Allowed);
        Assert.Equal(" Nobody can approve it in this run, so it was denied: try a smaller step, stay inside the project, or ask the user to rerun with --allow.", d.Reason);
    }

    [Fact]
    public async Task TheUsersYesAllowsAndTheirNoDeniesWithNothingToAdd()
    {
        Assert.Equal(ApprovalDecision.Allow, await Pipeline(new ApprovalSettings(), new ScriptedUserInteraction(allow: true)).RequestAsync(Bash("make"), Ct));
        Assert.Equal(new ApprovalDecision(false, ""), await Pipeline(new ApprovalSettings(), new ScriptedUserInteraction()).RequestAsync(Bash("make"), Ct));
    }

    [Fact]
    public async Task APromptsOwnReasonReplacesTheJudgesButAPlainNoKeepsIt()
    {
        var user = new ScriptedUserInteraction { Confirmation = ApprovalDecision.Deny(" Too long to review on one screen: split it.") };
        var p = Pipeline(new ApprovalSettings(useJudge: true), user, new CannedJudge(_ => new JudgeVerdict(JudgeChoice.Ask, "unsure (allow, confidence 0.70)")));

        var d = await p.RequestAsync(Bash("make"), Ct);

        Assert.Equal(" Too long to review on one screen: split it.", d.Reason);
    }

    [Fact]
    public async Task TheSettingsAreReadAtEachRequestSoPermissionsCanChangeBetweenCalls()
    {
        var settings = new ApprovalSettings(interactive: false);
        var p = Pipeline(settings);

        Assert.False((await p.RequestAsync(Bash("make"), Ct)).Allowed);
        settings.Auto = true; // /permissions yes
        Assert.True((await p.RequestAsync(Bash("make"), Ct)).Allowed);
        settings.Auto = false;
        Assert.False((await p.RequestAsync(Bash("make"), Ct)).Allowed);
    }

    [Fact]
    public async Task OneRequestsReasonNeverLeaksIntoTheNext()
    {
        var verdicts = new Queue<JudgeVerdict>([new(JudgeChoice.Ask, "unsure (allow, confidence 0.60)"), new(JudgeChoice.Ask, "unsure (deny, confidence 0.55)")]);
        var p = Pipeline(new ApprovalSettings(useJudge: true, interactive: false), judge: new CannedJudge(_ => verdicts.Dequeue()));

        var first = await p.RequestAsync(Bash("one"), Ct);
        var second = await p.RequestAsync(Bash("two"), Ct);

        Assert.Contains("0.60", first.Reason);
        Assert.DoesNotContain("0.60", second.Reason);
        Assert.Contains("0.55", second.Reason);
    }

    [Theory]
    [InlineData(ApprovalKind.Read, "read")]
    [InlineData(ApprovalKind.Write, "write")]
    [InlineData(ApprovalKind.Find, "find")]
    [InlineData(ApprovalKind.Bash, "bash")]
    [InlineData(ApprovalKind.Mcp, "mcp")]
    public void KindsAreSpelledAsTheyAreShownAndJudged(ApprovalKind kind, string word) => Assert.Equal(word, kind.Wire());
}
