namespace Mini.Core.Models;

/// <summary>What differs between Claude and another provider's Anthropic-format endpoint, in one place: the shape of a request, how
/// a long conversation is compacted, whether tokens can be counted. A strategy, so no code asks "is this another provider" key by key.</summary>
// Python: loop.py Agent.request_args, Agent.compact (the `other` branches)
// Python: cli.py context_report (the `other` branch)
public interface IProviderProfile
{
    /// <summary>Adds what is the provider's own to a conversation request: thinking, betas, fallbacks.</summary>
    // Python: loop.py Agent.request_args (the `other` branch)
    void Shape(MessageRequestBuilder request);

    /// <summary>Who writes the summary when the conversation is compacted.</summary>
    // Python: loop.py Agent.compact
    CompactionMode Compaction { get; }

    /// <summary>Whether the provider has the free token-counting endpoint (nothing goes to the model).</summary>
    // Python: cli.py context_report
    bool CanCountTokens { get; }

    /// <summary>The profile of a model: Claude's, or the compatible one when the model comes from models.json with its own endpoint.</summary>
    /// <param name="other">The model's entry when it has its own endpoint (<c>ModelCatalog.Other</c>); null for Claude.</param>
    /// <param name="config">The settings of the model (for Claude: whether refusals fall back to another model).</param>
    static IProviderProfile For(ModelConfig? other, ModelConfig config) =>
        other is null ? new AnthropicProfile(config.Fallbacks) : CompatibleProfile.Instance;
}
