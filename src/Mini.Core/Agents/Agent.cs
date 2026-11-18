using System.Text.Json.Nodes;

// Python: loop.py Agent, Agent.__init__ (the object the CLI talks to; its parts are built by AgentFactory)
// Python: cli.py main (agent.goal_defaults)
namespace Mini.Core.Agents;

/// <summary>
/// One conversation with a model, as the CLI sees it: ask a question, compact, run a goal, change the effort or the permissions, and
/// read its state. It holds no logic of its own: the turn loop, compaction, goals, spending and subagents are the parts it forwards to.
/// </summary>
// Python: loop.py Agent
public sealed class Agent
{
    private readonly AgentParts _parts;

    internal Agent(AgentParts parts) => _parts = parts;

    public string Model => _parts.Manifest.Model;

    /// <summary>The system prompt, frozen when the session started.</summary>
    public string System => _parts.Manifest.System;

    /// <summary>The tools the model is told about, frozen when the session started.</summary>
    public IReadOnlyList<ToolDefinition> Tools => _parts.Manifest.Tools;

    /// <summary>What the next requests ask for; <see cref="ChangeEffort"/> changes it.</summary>
    public string Effort => _parts.Effort.Value;

    /// <summary>The efforts this model takes.</summary>
    public IReadOnlyList<string> Efforts => _parts.Config.Efforts;

    /// <summary>The model's context window, in tokens.</summary>
    public int Window => _parts.Config.Window;

    /// <summary>The input tokens past which the context is compacted.</summary>
    public int CompactAt => _parts.Policy.CompactAt;

    /// <summary>What differs for this model's provider: its request shape, how it compacts, whether tokens can be counted.</summary>
    public IProviderProfile Profile => _parts.Profile;

    /// <summary>The transport the agent streams through.</summary>
    public IModelClient Client => _parts.Client;

    public ISessionStore Session => _parts.Session;

    /// <summary>The settings the agent's tools share; the CLI reads the mode here.</summary>
    public ApprovalSettings Approval => _parts.Approval;

    /// <summary>The conversation, and the only way to add to it.</summary>
    public Transcript Transcript => _parts.Transcript;

    /// <summary>Read-only: only the <see cref="Transcript"/> adds to it (a compaction swaps it whole).</summary>
    public IReadOnlyList<Message> Messages => _parts.Transcript.Messages;

    public double Cost => _parts.Usage.Cost;

    /// <summary>The conversation's size at the last reply; zero before the first and after a compaction.</summary>
    public long LastContext { get => _parts.Usage.LastContext; internal set => _parts.Usage.LastContext = value; } // tests set it to stand in for a long conversation

    /// <summary>The goal, or null.</summary>
    public Goal? Goal { get => _parts.Goal.Value; internal set => _parts.Goal.Value = value; } // tests set it to start from a given goal

    /// <summary>What a new /goal starts with. The CLI sets it once, from its flags.</summary>
    // Python: cli.py main (agent.goal_defaults)
    public GoalLimits GoalDefaults { get; set; } = new(null, 0, null);

    // Test seams: the parts a test reaches to stand in for a call, or to look inside.
    internal AgentParts Parts => _parts;
    internal ImageBudget Images => _parts.Images;
    internal GoalClaimSlot GoalClaim => _parts.GoalClaim;
    internal IJudgeContext JudgeContext => _parts.JudgeContext;
    internal long CompactFailedAt => _parts.Policy.FailedAt;

    /// <summary>Runs one of this agent's tools by name, as a model's call would (a test seam).</summary>
    internal Task<ToolOutput> RunToolAsync(string name, JsonObject args, CancellationToken ct) =>
        _parts.Executor.InvokeAsync(new ToolCall(null, name, args), ct);

    /// <summary>Run until the model stops asking for tools. Returns the stop reason.</summary>
    // Python: loop.py Agent.turn
    public async Task<string> TurnAsync(string content, bool harness = false, CancellationToken ct = default) =>
        (await _parts.Turns.TurnAsync(content, harness, ct)).Value;

    /// <summary>Swap the whole history for a summary. Claude's server writes it; other models are asked for one.</summary>
    // Python: loop.py Agent.compact
    public Task<bool> CompactAsync(CancellationToken ct = default) => _parts.Compactor.CompactAsync(ct);

    /// <summary>Compacts when the context has grown past the model's threshold.</summary>
    // Python: loop.py Agent.maybe_compact
    public Task MaybeCompactAsync(CancellationToken ct = default) => _parts.Compactor.MaybeCompactAsync(ct);

    // Python: loop.py Agent.set_goal
    public Task<Goal> SetGoalAsync(string objective, string? verify, int maxTurns, double? budget, CancellationToken ct = default) =>
        _parts.Goals.SetAsync(objective, verify, maxTurns, budget, ct);

    /// <summary>Works on the goal that is paused (or was running when the process ended) from where it stopped.</summary>
    // Python: loop.py Agent.run_goal
    public Task<Goal> ResumeGoalAsync(CancellationToken ct = default) => _parts.Goals.RunAsync(ct: ct);

    // Python: loop.py Agent.clear_goal
    public void ClearGoal() => _parts.Goals.Clear();

    /// <summary>Changes the effort of the next requests. False (nothing changes) when the model does not take it.</summary>
    // Python: cli.py command (/effort)
    public bool ChangeEffort(string effort)
    {
        if (!Efforts.Contains(effort)) return false;
        _parts.Effort.Value = effort;
        return true;
    }

    /// <summary>Changes who approves risky calls. The system prompt is frozen, so the model hears of it in the next message.</summary>
    // Python: cli.py command (/permissions)
    public void ChangePermissions(PermissionMode mode)
    {
        if (mode == Approval.Mode) return;
        Approval.Auto = mode == PermissionMode.Yes;
        Approval.UseJudge = mode == PermissionMode.Judge;
        _parts.Transcript.PendingNotes.Add(ApprovalStatement.Describe(Approval));
    }

    /// <summary>The text of the last assistant message.</summary>
    // Python: loop.py Agent.final_text
    public string FinalText() => _parts.Transcript.FinalText();
}
