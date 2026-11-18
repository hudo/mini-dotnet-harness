// Python: loop.py Agent.__init__ (its parameters: cwd, ui, approver, hub, skills, model, effort, sub, client)
namespace Mini.Core.Agents;

/// <summary>What one agent is asked to be: where it works, how it talks to the user, who approves, and which model it uses. The factory
/// adds what it needs from the services (the catalog, the clock, the shell) and the session.</summary>
/// <param name="Cwd">The working directory.</param>
/// <param name="Ui">Where the agent shows its work.</param>
/// <param name="Approval">The approval settings the agent's tools share (the CLI changes them while the agent runs).</param>
public sealed record AgentSetup(string Cwd, IAgentOutput Ui, ApprovalSettings Approval)
{
    /// <summary>The MCP servers whose tools the agent has; a subagent has none.</summary>
    public McpHub? Hub { get; init; }

    /// <summary>The skills the system prompt lists.</summary>
    public IReadOnlyList<Skill>? Skills { get; init; }

    /// <summary>The model; null: the catalog's main model, or its subagent model for a subagent.</summary>
    public string? Model { get; init; }

    /// <summary>The effort asked for; a model that has no such level gets "high" ("low" for a subagent).</summary>
    public string Effort { get; init; } = "high";

    /// <summary>A subagent: the short system prompt, three tools, no MCP, no goal.</summary>
    public bool Sub { get; init; }

    /// <summary>The transport (a test's scripted one, or a lead's, shared with its subagent); null: the catalog's, after the credentials check.</summary>
    public IModelClient? Client { get; init; }

    /// <summary>A subagent is given its lead's, so the judge reads the lead's session; null: the agent makes its own.</summary>
    public IApprovalService? Approvals { get; init; }

    /// <summary>Where the user is asked (the approval prompt, <c>ask_user</c>). A subagent shares its lead's.</summary>
    public required IUserInteraction Interaction { get; init; }
}
