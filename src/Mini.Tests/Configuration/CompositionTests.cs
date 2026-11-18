using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Mini.Core;

namespace Mini.Tests.Configuration;

/// <summary>The composition root: what AddMiniCore registers and what a test may replace.</summary>
// C#-only: Python has no container
public class CompositionTests
{
    static ServiceProvider Build(MiniOptions options, Action<IServiceCollection>? before = null)
    {
        var services = new ServiceCollection();
        before?.Invoke(services);
        return services.AddMiniCore(options, Path.Combine(AppContext.BaseDirectory, "models.json")).AddMiniCli().BuildServiceProvider();
    }

    // C#-only: every service of the runtime is one shared instance, built from the options given
    [Fact]
    public void TheRuntimeIsBuiltOnceFromTheOptions()
    {
        var options = new MiniOptions { NetHome = TestEnvironment.Home, Model = "claude-haiku-5-5" };
        using var provider = Build(options);

        var factory = provider.GetRequiredService<IAgentFactory>();

        Assert.Same(factory, provider.GetRequiredService<IAgentFactory>());
        Assert.Same(options, provider.GetRequiredService<MiniOptions>());
        Assert.Same(options, provider.GetRequiredService<IOptions<MiniOptions>>().Value);
        Assert.Same(provider.GetRequiredService<ModelCatalog>(), provider.GetRequiredService<ModelCatalog>());
        Assert.Equal("claude-haiku-5-5", provider.GetRequiredService<ModelCatalog>().Main); // MINI_MODEL, as the option
        Assert.Equal(TestEnvironment.Home, provider.GetRequiredService<MiniPaths>().Home);
    }

    // C#-only: the system clock unless one was registered first
    [Fact]
    public void ATimeProviderRegisteredFirstIsKept()
    {
        using var real = Build(new MiniOptions { NetHome = TestEnvironment.Home });
        Assert.Same(TimeProvider.System, real.GetRequiredService<TimeProvider>());

        var fake = new FakeTimeProvider();
        using var faked = Build(new MiniOptions { NetHome = TestEnvironment.Home }, s => s.AddSingleton<TimeProvider>(fake));
        Assert.NotNull(faked.GetRequiredService<IAgentFactory>()); // built over the registered clock
        Assert.Same(fake, faked.GetRequiredService<TimeProvider>());
    }

    // C#-only: the client factory, the agent options and the judge's HTTP client are registered, and the agent factory is built over them
    [Fact]
    public void TheFactoryIsBuiltOverTheClientFactoryTheAgentOptionsAndTheJudgeClient()
    {
        using var provider = Build(new MiniOptions { NetHome = TestEnvironment.Home });

        Assert.NotNull(provider.GetRequiredService<IAgentFactory>());
        Assert.Same(provider.GetRequiredService<AnthropicClientFactory>(), provider.GetRequiredService<AnthropicClientFactory>());
        Assert.Same(provider.GetRequiredService<IOptions<AgentOptions>>().Value, provider.GetRequiredService<AgentOptions>());
        Assert.Equal(0, provider.GetRequiredService<AgentOptions>().MaxTokens); // the model's own max_output
        Assert.IsType<JevJudge>(provider.GetRequiredService<IApprovalJudge>());
        Assert.Equal(TimeSpan.FromSeconds(20), provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(JevJudge)).Timeout); // the typed client AddHttpClient<JevJudge> registered
    }

    // C#-only: the factory reads its variables through the delegate it was given, not from the process environment directly
    [Fact]
    public void TheClientFactoryReadsTheEnvironmentThroughItsDelegate()
    {
        var withKey = new AnthropicClientFactory(name => name == "ANTHROPIC_API_KEY" ? "sk-ant-from-delegate" : null);
        Assert.NotNull(withKey.Create("claude-opus-5-5", null));

        var without = new AnthropicClientFactory(_ => null);
        Assert.Throws<MissingCredentialsException>(() => without.Create("claude-opus-5-5", null));
    }
}
