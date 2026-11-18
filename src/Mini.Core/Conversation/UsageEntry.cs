// Python: loop.py Agent.track, Agent.price (the fields they read from usage and from each iteration)
namespace Mini.Core.Conversation;

/// <summary>One priced slice of a response: the response itself, or one of its iterations (a compaction, a declined attempt, a fallback).</summary>
/// <param name="Type">"message", "fallback_message", "compaction" and so on; null for the response's own usage.</param>
/// <param name="Model">The model that served it; null or empty means the response's model.</param>
/// <param name="InputTokens">New input tokens, not read from or written to the cache.</param>
/// <param name="OutputTokens">Tokens the model wrote.</param>
/// <param name="CacheReadInputTokens">Input tokens read from the cache.</param>
/// <param name="CacheCreationInputTokens">Input tokens written to the cache.</param>
public sealed record UsageEntry(string? Type, string? Model, long InputTokens, long OutputTokens, long CacheReadInputTokens, long CacheCreationInputTokens)
{
    /// <summary>What the model read: new input plus both kinds of cache.</summary>
    public long PromptTokens => InputTokens + CacheReadInputTokens + CacheCreationInputTokens;

    /// <summary>The conversation's size after this slice: everything read plus what it wrote.</summary>
    public long ContextTokens => PromptTokens + OutputTokens;
}
