// Python: none (C#-only: the parts the factory wires into an Agent; Python's Agent holds them as attributes)
namespace Mini.Core.Agents;

/// <summary>What <see cref="AgentFactory"/> builds an <see cref="Agent"/> from. Internal: only the factory makes it, and the agent hands out
/// the few parts the CLI and the tests need (<c>Transcript</c>, <c>Session</c>).</summary>
internal sealed record AgentParts
{
    public required SessionManifest Manifest { get; init; }
    public required ModelConfig Config { get; init; }
    public required IProviderProfile Profile { get; init; }
    public required IModelClient Client { get; init; }
    public required ISessionStore Session { get; init; }
    public required ApprovalSettings Approval { get; init; }
    public required Transcript Transcript { get; init; }
    public required UsageTracker Usage { get; init; }
    public required CompactionPolicy Policy { get; init; }
    public required Compactor Compactor { get; init; }
    public required TurnRunner Turns { get; init; }
    public required EffortSetting Effort { get; init; }
    public required GoalSlot Goal { get; init; }
    public required GoalClaimSlot GoalClaim { get; init; }
    public required GoalRunner Goals { get; init; }
    public required GoalVerifier Verifier { get; init; }
    public required ImageBudget Images { get; init; }
    public required ToolExecutor Executor { get; init; }
    public required IJudgeContext JudgeContext { get; init; }
}
