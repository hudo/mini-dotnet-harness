using System.Text.Json.Nodes;

// Python: loop.py Agent.__init__ (the resume and new-session branches, need_key, client, self.local, self.hub)
namespace Mini.Core.Agents;

/// <summary>
/// Makes <see cref="Agent"/>s over the program's services. A new session gets its system prompt and tool list built and frozen in its
/// meta line; a resumed one replays them. Either way the model's credentials are checked before anything is written, so a missing key
/// leaves no session file behind.
/// </summary>
public sealed class AgentFactory(MiniOptions options, MiniPaths paths, ModelCatalog catalog, PriceSchedule prices, TimeProvider time, SessionCatalog sessions,
    ShellLocator locator, IShellRunner shell, JobRegistry jobs, AnthropicClientFactory clients, AgentOptions settings, IApprovalJudge judge) : IAgentFactory
{
    /// <summary>Everything an agent is made of that does not depend on the frozen manifest.</summary>
    private sealed record Wiring(ISessionStore Session, AgentSetup Setup, SessionSnapshot State, string Model, ModelConfig Config, IProviderProfile Profile,
        IModelClient Client, Transcript Transcript, UsageTracker Usage, GoalSlot Goal, GoalClaimSlot Claim, ImageBudget Images, IJudgeContext JudgeContext,
        ToolRegistry Registry, SubagentRunner Subagents, EffortSetting Effort);

    /// <inheritdoc/>
    public bool CanResume(ISessionStore session) => SessionReplayer.Replay(session).Meta is not null;

    /// <inheritdoc/>
    // Python: loop.py Agent.__init__ (the new-session branch)
    public Agent CreateNew(ISessionStore session, AgentSetup setup)
    {
        var wiring = Wire(session, setup, SessionReplayer.Replay(session), setup.Model ?? (setup.Sub ? catalog.SubModel : catalog.Main));
        var manifest = NewManifest(wiring);
        session.Append(manifest.ToMeta(setup.Cwd));
        return Finish(wiring, manifest);
    }

    /// <inheritdoc/>
    // Python: loop.py Agent.__init__ (the resume branch)
    public Agent Resume(ISessionStore session, AgentSetup setup)
    {
        var state = SessionReplayer.Replay(session);
        var manifest = SessionManifest.FromMeta(state.Meta ?? throw new InvalidOperationException("the session has no meta line: nothing to resume"));
        var w = Wire(session, setup, state, manifest.Model); // the key check comes before anything is written
        w.Transcript.Repair();
        if (w.Goal.Value is { Status: GoalStatus.Active } goal) // nothing runs it in a fresh process
        {
            goal.Pause("interrupted");
            session.Append(new GoalEvent(goal));
        }
        TellWhatChanged(w, manifest);
        w.Images.Reset(w.Transcript.Messages.Sum(m => m.ImageDataLength));
        return Finish(w, manifest);
    }

    /// <summary>The model, its provider's credentials, and the tools: everything that needs no prompt yet.</summary>
    private Wiring Wire(ISessionStore session, AgentSetup setup, SessionSnapshot state, string model)
    {
        var other = catalog.Other.GetValueOrDefault(model);
        var config = catalog.For(model);
        var client = setup.Client ?? clients.Create(model, other); // before the first write: a missing key must leave no session file behind
        var efforts = config.Efforts;
        var effort = new EffortSetting(efforts.Contains(setup.Effort) ? setup.Effort : setup.Sub ? "low" : "high"); // e.g. a subagent's "medium" on DeepSeek
        var transcript = new Transcript(session, state.Messages);
        var usage = new UsageTracker(session, new PriceCalculator(catalog, prices, config), setup.Ui, state.Cost, state.Context);
        var goal = new GoalSlot { Value = state.Goal };
        var claim = new GoalClaimSlot();
        var images = new ImageBudget();
        var judgeContext = new SessionJudgeContext(session, setup.Cwd, () => goal.ActiveObjective, usage.Add);
        var ask = setup.Interaction;
        var approvals = setup.Approvals ?? ApprovalPipeline.Create(setup.Approval, ask, judge, judgeContext);
        var subagents = new SubagentRunner(this, sessions, session, setup with { Model = model, Client = client, Approvals = approvals }, other is not null, efforts, effort, usage);
        var registry = BuildTools(setup, other, approvals, ask, session, goal, claim, images, subagents);
        return new Wiring(session, setup, state, model, config, IProviderProfile.For(other, config), client, transcript, usage, goal, claim, images, judgeContext,
            registry, subagents, effort);
    }

    /// <summary>Every tool the agent can run, whether or not the session's tool list shows it (a subagent can call what it was not told of).</summary>
    // Python: loop.py Agent.__init__ (self.local, self.hub)
    private ToolRegistry BuildTools(AgentSetup setup, ModelConfig? other, IApprovalService approvals, IUserInteraction ask, ISessionStore session, GoalSlot goal,
        GoalClaimSlot claim, ImageBudget images, ISubagentRunner subagents)
    {
        var policy = new WorkspacePolicy(setup.Cwd, paths);
        var seen = new FileReadTracker();
        List<ITool> tools =
        [
            new ReadFileTool(policy, approvals, seen, images),
            new WriteFileTool(policy, approvals, seen),
            new EditFileTool(policy, approvals, seen),
            new BashTool(policy, new CommandPathInspector(policy), approvals, shell, jobs),
            new FindTool(policy, approvals),
            new TaskTool(subagents, catalog, singleModel: other is not null),
            new AskUserTool(setup.Approval, ask, new SessionAnswerLog(session)),
            new GoalDoneTool(claim, () => goal.IsActive),
        ];
        if (setup.Hub is { } hub)
            tools.AddRange(hub.CreateTools(approvals));
        return new ToolRegistry(tools);
    }

    /// <summary>The tool list and system prompt a new session freezes.</summary>
    // Python: loop.py Agent.__init__ (the new-session branch: self.tools, self.system)
    private SessionManifest NewManifest(Wiring w)
    {
        var setup = w.Setup;
        List<ToolDefinition> tools = [];
        foreach (var name in setup.Sub ? new[] { "read", "find", "bash" } : ["read", "write", "edit", "bash", "find"])
            tools.Add(w.Registry.Get(name).Definition);
        if (!setup.Sub)
        {
            foreach (var name in new[] { "task", "ask_user", "goal_done" })
            {
                if (name == "task" && options.NoTask) continue; // one loop does it all
                if (name == "ask_user" && !setup.Approval.Interactive) continue; // headless: nobody would answer
                tools.Add(w.Registry.Get(name).Definition); // another provider has one model, so subagents run on it too: TaskTool's definition has no model
            }
            if (setup.Hub is { } hub)
                tools.AddRange(hub.Schemas.Select(s => new ToolDefinition(Json.Str(s["name"]) ?? "", (JsonObject)s.DeepClone())));
        }
        if (!options.NoWeb) // e.g. benchmarks that forbid looking things up
            tools.Add(WebSearchTool.Definition);
        var system = new SystemPromptBuilder(locator, time).Build(new SystemPromptRequest(setup.Cwd, setup.Skills ?? [], setup.Hub?.Errors ?? [], setup.Sub, w.Model,
            tools.Any(t => t.Name == "task"), ApprovalStatement.Describe(setup.Approval)));
        return new SessionManifest(w.Model, system, tools);
    }

    /// <summary>Notes for the model's next message about what changed while the session was closed: the goal's end, the approval mode.</summary>
    // Python: loop.py Agent.__init__ (the resume branch: the goal and approval notes)
    private static void TellWhatChanged(Wiring w, SessionManifest manifest)
    {
        var said = string.Join("\n", w.Transcript.Messages.Where(m => m.Role == Role.System).Select(m => m.Text ?? ""));
        if (said.LastIndexOf("Goal mode is on", StringComparison.Ordinal) > said.LastIndexOf("Goal mode is off", StringComparison.Ordinal) && !w.Goal.IsActive)
            w.Transcript.PendingNotes.Add(HarnessPrompts.GoalOff.Render(("status", (w.Goal.Value?.Status ?? GoalStatus.Cleared).Wire())));
        said = manifest.System + "\n" + said;
        var now = ApprovalStatement.Describe(w.Setup.Approval); // the last word on approval, if not this one
        var last = said.LastIndexOf("Approval in this session", StringComparison.Ordinal); // -1: a session from before 1.0.2
        if (last < 0 || said[last..].Split('\n', 2)[0] != now)
            w.Transcript.PendingNotes.Add(now);
    }

    /// <summary>The parts that need the manifest: the compactor, the turn runner, the goal runner.</summary>
    private Agent Finish(Wiring w, SessionManifest manifest)
    {
        var usage = w.Usage;
        var ui = w.Setup.Ui;
        var policy = new CompactionPolicy(usage, CompactionPolicy.Threshold(w.Config, catalog, options));
        ICompactionStrategy strategy = w.Profile.Compaction == CompactionMode.Summary
            ? new SummaryCompaction(w.Client, manifest, usage)
            : new ServerCompaction(w.Client, manifest, usage, ui);
        var compactor = new Compactor(strategy, w.Transcript, usage, policy, w.Images, ui, () => w.Goal.ActiveRules);
        var executor = new ToolExecutor(w.Registry, ui);
        var turns = new TurnRunner(w.Transcript, new RetryingModelClient(w.Client, time), executor, usage, compactor, w.Claim, manifest, w.Profile,
            settings.MaxTokens != 0 ? settings.MaxTokens : w.Config.MaxOutput ?? 64000, w.Effort, ui);
        var verifier = new GoalVerifier(w.Goal, w.Claim, shell, w.Setup.Cwd, w.Subagents, ui, settings.GoalVerifyTimeoutSeconds);
        var goals = new GoalRunner(w.Goal, w.Claim, turns, compactor, verifier, usage, w.Transcript, w.Session);
        return new Agent(new AgentParts
        {
            Manifest = manifest, Config = w.Config, Profile = w.Profile, Client = w.Client, Session = w.Session, Approval = w.Setup.Approval,
            Transcript = w.Transcript, Usage = usage, Policy = policy, Compactor = compactor, Turns = turns, Effort = w.Effort, Goal = w.Goal,
            GoalClaim = w.Claim, Goals = goals, Verifier = verifier, Images = w.Images, Executor = executor, JudgeContext = w.JudgeContext,
        });
    }
}
