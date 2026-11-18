// Python: loop.py Agent.__init__ (the resume and new-session branches)
namespace Mini.Core.Agents;

/// <summary>Makes agents: one for a new session, one that picks a session up where it ended. A seam for the runner that makes subagents.</summary>
public interface IAgentFactory
{
    /// <summary>Whether the session has a meta line, which is what a resume replays.</summary>
    bool CanResume(ISessionStore session);

    /// <summary>An agent for a session that has none yet: builds the system prompt and tool list, and freezes them in the session's meta line.</summary>
    /// <exception cref="MissingCredentialsException">The model's provider has no API key; nothing was written.</exception>
    Agent CreateNew(ISessionStore session, AgentSetup setup);

    /// <summary>An agent that continues a session: replays its frozen prompt and tools byte for byte, closes what a crash left open, and says in the next message what changed.</summary>
    /// <exception cref="MissingCredentialsException">The model's provider has no API key; nothing was written.</exception>
    /// <exception cref="InvalidOperationException">The session has no meta line, or its meta lacks the model, the system prompt or the tools.</exception>
    Agent Resume(ISessionStore session, AgentSetup setup);
}
