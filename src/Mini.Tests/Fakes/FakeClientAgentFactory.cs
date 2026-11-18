namespace Mini.Tests.Fakes;

/// <summary>The program's <see cref="IAgentFactory"/> with one change: every agent talks to the given (scripted) model client instead of the real one.
/// It is how a test drives <c>MiniApp.RunAsync</c> end to end without a network.</summary>
// Python test helper: tests/test_mini.py (monkeypatch.setattr(cli, "Agent", ...): the one place the CLI makes its agent)
sealed class FakeClientAgentFactory(IAgentFactory inner, IModelClient client) : IAgentFactory
{
    public bool CanResume(ISessionStore session) => inner.CanResume(session);

    public Agent CreateNew(ISessionStore session, AgentSetup setup) => inner.CreateNew(session, setup with { Client = client });

    public Agent Resume(ISessionStore session, AgentSetup setup) => inner.Resume(session, setup with { Client = client });
}
