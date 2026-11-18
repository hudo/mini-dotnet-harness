using Mini.Tests.Fakes;

namespace Mini.Tests.Providers;

/// <summary>
/// The one test that sets ANTHROPIC_CUSTOM_HEADERS. AnthropicClientFactory.Create freezes the SDK's process-wide header table on its first
/// call (the factory's static _frozen/_captured), and a first call with that variable set poisons every later <c>Create(model, other)</c>
/// in the process ("the SDK captured them earlier"). The class name sorts last for that reason, but the test does not
/// rely on ordering: it first makes a clean call (variable unset) so the table is frozen empty, and only then sets the
/// variable. Whatever order the classes run in, nothing is poisoned afterwards, so no other test's
/// <c>Create</c> (DeepseekWithoutItsKeyNeverUsesTheAnthropicKey builds a real DeepSeek client) can be broken by it.
/// </summary>
public class ZCustomHeadersTests
{
    // Python test: test_custom_headers_block_other_providers
    [Fact]
    public void CustomHeadersBlockOtherProviders()
    {
        using var ws = new Workspace();
        using (new EnvScope(("ANTHROPIC_API_KEY", "sk-ant-test"), ("ANTHROPIC_CUSTOM_HEADERS", null)))
            Assert.NotNull(ws.Runtime.Clients.Create("claude-opus-5-5", null)); // freezes the SDK's header table while the variable is unset

        using var env = new EnvScope(("DEEPSEEK_API_KEY", "ds"), ("ANTHROPIC_CUSTOM_HEADERS", "X-Gateway-Token: secret"));

        var e = Assert.Throws<MissingCredentialsException>(() =>
            ws.Open(ws.NewSession(), ws.RealSetup("deepseek-flash")));

        Assert.Contains("ANTHROPIC_CUSTOM_HEADERS", e.Message);
    }

    // C#-only: the guard is on the variable now, so unsetting it lifts it (while the table was frozen clean)
    [Fact]
    public void AnotherProviderWorksOnceTheHeadersAreGone()
    {
        using var ws = new Workspace();
        using var env = new EnvScope(("ANTHROPIC_API_KEY", "sk-ant-test"), ("DEEPSEEK_API_KEY", "ds"), ("ANTHROPIC_CUSTOM_HEADERS", null));
        Assert.NotNull(ws.Runtime.Clients.Create("claude-opus-5-5", null));

        var agent = ws.Open(ws.NewSession(), ws.RealSetup("deepseek-flash"));

        Assert.Equal("deepseek-flash", agent.Model);
    }
}
