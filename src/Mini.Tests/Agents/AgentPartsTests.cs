using System.Text.Json.Nodes;
using Mini.Tests.Fakes;

namespace Mini.Tests.Agents;

/// <summary>The pieces the Agent was broken into, each on its own.</summary>
public class AgentPartsTests
{
    static CompactionPolicy Policy(Workspace ws, int compactAt, out UsageTracker usage)
    {
        var config = ws.Runtime.Catalog.For("claude-opus-5-5");
        usage = new UsageTracker(ws.NewSession(), new PriceCalculator(ws.Runtime.Catalog, ws.Runtime.Prices, config), new ConsoleAgentOutput(silent: true));
        return new CompactionPolicy(usage, compactAt);
    }

    // C#-only: the threshold is MINI_COMPACT_AT over the model's own compact_at, and the catalog's default for a model with none
    [Fact]
    public void TheThresholdIsTheEnvironmentOverTheModelOverTheCatalogDefault()
    {
        using var ws = new Workspace();
        var catalog = ws.Runtime.Catalog;
        var claude = catalog.For("claude-opus-5-5");
        var other = catalog.For("deepseek-flash");

        Assert.Equal(150_000, CompactionPolicy.Threshold(claude, catalog, ws.Runtime.Options)); // Claude's own comes from its claude-* entry
        Assert.Equal(800_000, CompactionPolicy.Threshold(other, catalog, ws.Runtime.Options));
        Assert.Equal(900, CompactionPolicy.Threshold(other, catalog, ws.Runtime.Options with { CompactAt = 900 }));
        Assert.Equal(catalog.CompactAt, CompactionPolicy.Threshold(claude with { CompactAt = null }, catalog, ws.Runtime.Options));
    }

    // C#-only: the back-off of test_failed_compaction_backs_off, on its own
    [Fact]
    public void AFailedCompactionWaitsUntilTheContextHasGrownByAFifth()
    {
        using var ws = new Workspace();
        var policy = Policy(ws, 1000, out var usage);

        usage.LastContext = 1001;
        Assert.True(policy.ShouldCompact);
        policy.Failed(1001);
        Assert.False(policy.ShouldCompact);
        usage.LastContext = 1201;
        Assert.False(policy.ShouldCompact); // 1001 * 1.2 = 1201.2
        usage.LastContext = 1202;
        Assert.True(policy.ShouldCompact);
        policy.Succeeded();
        Assert.Equal(0, policy.FailedAt);
    }

    // C#-only: money other parts spend (the judge, a subagent) is added to the total
    [Fact]
    public void SpendingFromOtherPartsIsAddedToTheTotal()
    {
        using var ws = new Workspace();
        Policy(ws, 1000, out var usage);

        usage.Add(0.25);
        usage.Add(0.5);

        Assert.Equal(0.75, usage.Cost, 9);
    }

    // C#-only: the manifest freezes what the meta line holds and gives it back byte for byte
    [Fact]
    public void TheManifestRoundTripsThroughTheMetaLine()
    {
        var tools = new JsonArray(Json.Obj(("name", "read"), ("strict", true)), Json.Obj(("type", "web_search_20250305"), ("name", "web_search")));
        var meta = new MetaEvent("claude-opus-5-5", "/work", "the prompt", tools);

        var manifest = SessionManifest.FromMeta(meta);

        Assert.Equal(["read", "web_search"], manifest.Tools.Select(t => t.Name));
        Assert.True(manifest.HasTool("read"));
        Assert.False(manifest.HasTool("task"));
        var again = manifest.ToMeta("/work");
        Assert.Equal(("claude-opus-5-5", "/work", "the prompt"), (again.Model, again.Cwd, again.System));
        Assert.Equal(tools.ToJsonString(), again.Tools!.ToJsonString());
    }

    // C#-only: a meta line that lacks something says what
    [Fact]
    public void AMetaWithoutAPartIsNotAManifest()
    {
        Assert.Equal("the session's meta has no model", Assert.Throws<InvalidOperationException>(() => SessionManifest.FromMeta(new MetaEvent(null, "/w", "s", []))).Message);
        Assert.Equal("the session's meta has no system prompt", Assert.Throws<InvalidOperationException>(() => SessionManifest.FromMeta(new MetaEvent("m", "/w", null, []))).Message);
        Assert.Equal("the session's meta has no tools", Assert.Throws<InvalidOperationException>(() => SessionManifest.FromMeta(new MetaEvent("m", "/w", "s", null))).Message);
    }

    // C#-only: Resume replays a session; a session with no meta line is for CreateNew
    [Fact]
    public void ResumeNeedsAMetaLine()
    {
        using var ws = new Workspace();
        var session = ws.NewSession();
        Assert.False(ws.Runtime.Factory.CanResume(session));
        Assert.Throws<InvalidOperationException>(() => ws.Runtime.Factory.Resume(session, ws.RealSetup()));

        var (agent, _) = ws.Make([], session: session);

        Assert.True(ws.Runtime.Factory.CanResume(ws.OpenSession(agent.Session.Path)));
    }

    // C#-only: only a model's own levels can be chosen; a change is seen by the next request
    [Fact]
    public void OnlyAnEffortTheModelTakesIsAccepted()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([]);

        Assert.True(agent.ChangeEffort("low"));
        Assert.Equal("low", agent.Effort);
        Assert.False(agent.ChangeEffort("bogus"));
        Assert.Equal("low", agent.Effort);
    }

    // C#-only: the system prompt is frozen, so a change of permissions is a note in the next message, and none when nothing changed
    [Fact]
    public void ChangingPermissionsLeavesANoteOnlyWhenTheModeChanged()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([], approver: new ApprovalSettings());

        agent.ChangePermissions(PermissionMode.Ask);
        Assert.Empty(agent.Transcript.PendingNotes);
        agent.ChangePermissions(PermissionMode.Yes);

        Assert.Equal(PermissionMode.Yes, agent.Approval.Mode);
        Assert.Single(agent.Transcript.PendingNotes);
    }

    // C#-only: the goal slot gives the rules only while the goal is running
    [Fact]
    public void TheGoalSlotHasRulesOnlyWhileTheGoalRuns()
    {
        var slot = new GoalSlot();
        Assert.False(slot.IsActive);
        Assert.Null(slot.ActiveRules);

        slot.Value = new Goal("ship it", verify: "make test");
        Assert.Null(slot.ActiveObjective);
        Assert.Contains("make test", slot.Rules());

        slot.Value.Activate();
        Assert.Equal("ship it", slot.ActiveObjective);
        Assert.Equal(slot.Rules(), slot.ActiveRules);
    }
}
