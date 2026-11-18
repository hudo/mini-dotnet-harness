namespace Mini.Core.Models;

/// <summary>Another provider behind an Anthropic-format endpoint (models.json): thinking is on by default there, so the request has no thinking binding, betas or fallbacks; compaction asks the model for a summary; tokens are estimated.</summary>
// Python: loop.py Agent.request_args, Agent.compact (the `other` branches)
// Python: cli.py context_report (the `other` branch)
public sealed class CompatibleProfile : IProviderProfile
{
    /// <summary>The profile keeps no state, so one is enough.</summary>
    public static CompatibleProfile Instance { get; } = new();

    CompatibleProfile() { }

    /// <inheritdoc/>
    public void Shape(MessageRequestBuilder request) { }

    /// <inheritdoc/>
    public CompactionMode Compaction => CompactionMode.Summary;

    /// <inheritdoc/>
    public bool CanCountTokens => false;
}
