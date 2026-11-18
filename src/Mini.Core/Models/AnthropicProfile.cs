// Python: loop.py BETAS, FALLBACK_BETA, Agent.request_args (the Claude branch)
namespace Mini.Core.Models;

/// <summary>Claude: thinking with binding controls, the compaction and thinking betas, optional server-side fallbacks, server-side compaction and token counting.</summary>
/// <param name="fallbacks">Refusals fall back to another model server-side (the model's <c>fallbacks</c> setting).</param>
public sealed class AnthropicProfile(bool fallbacks) : IProviderProfile
{
    // The server-side compaction request uses them too.
    // Python: loop.py BETAS
    internal static readonly string[] Betas = ["compact-2026-09-04", "thinking-binding-controls-2026-08-01"];

    // Python: loop.py FALLBACK_BETA
    internal const string FallbackBeta = "server-side-fallback-2026-07-01";

    /// <inheritdoc/>
    // Python: loop.py Agent.request_args (the Claude branch)
    public void Shape(MessageRequestBuilder request)
    {
        request.Thinking(ThinkingOptions.Adaptive).Betas(Betas);
        if (!fallbacks) return;
        request.Fallbacks("default").AddBeta(FallbackBeta); // refusals fall back to another model server-side
    }

    /// <inheritdoc/>
    public CompactionMode Compaction => CompactionMode.Server;

    /// <inheritdoc/>
    public bool CanCountTokens => true;
}
