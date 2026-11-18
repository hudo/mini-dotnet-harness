// Python: cli.py main (the nested `new`)
namespace Mini.Cli.Commands;

/// <summary>Starts agents with the command line's settings: the same output, approval, MCP servers, skills and effort for every session of one run.</summary>
public sealed class AgentLauncher(IAgentFactory factory, string cwd, IAgentOutput ui, IUserInteraction interaction, ApprovalSettings approval, McpHub hub,
    IReadOnlyList<Skill> skills, CliOptions options)
{
    /// <summary>The agent for a session: resumed when the session has a past, new otherwise. <paramref name="model"/> overrides <c>--model</c> when not empty.</summary>
    // Python: cli.py main (the nested `new`)
    public Agent Start(ISessionStore session, string? model = null)
    {
        var setup = new AgentSetup(cwd, ui, approval)
        {
            Interaction = interaction, Hub = hub, Skills = skills, Model = string.IsNullOrEmpty(model) ? options.Model : model, Effort = options.Effort,
        };
        var agent = factory.CanResume(session) ? factory.Resume(session, setup) : factory.CreateNew(session, setup);
        if (agent.Effort != options.Effort)
            ui.Warn($"{agent.Model} doesn't take effort {options.Effort}; using {agent.Effort} ({string.Join(", ", agent.Efforts)})");
        agent.GoalDefaults = new GoalLimits(options.Verify, options.MaxTurns, options.Budget);
        return agent;
    }
}
