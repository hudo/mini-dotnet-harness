// Python: loop.py Agent.max_tokens
namespace Mini.Core.Agents;

/// <summary>What an <see cref="Agent"/> is configured with besides the environment: a setting of the object, where Python has a class attribute.</summary>
public sealed record AgentOptions
{
    /// <summary>Per response; 0 means the model's max_output in models.json. Live tests set it low to reach the cap.</summary>
    // Python: loop.py Agent.max_tokens
    public int MaxTokens { get; init; }

    /// <summary>How long the goal's verify command may run before it is killed. A test shortens it, as Python's test patches <c>run_shell</c>.</summary>
    // Python: loop.py Agent.verify (the literal 600 in run_shell(cmd, self.cwd, 600))
    public int GoalVerifyTimeoutSeconds { get; init; } = 600;
}
