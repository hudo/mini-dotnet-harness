namespace Mini.Core.Models;

/// <summary>
/// One request to the Messages API, typed. The transport writes it as the JSON body (<see cref="MessageRequestJson"/>), in the order the
/// harness always sent the keys. Built with <see cref="MessageRequestBuilder"/>. It holds no transcript state: the messages are a
/// snapshot, and the blocks in them keep the JSON they came with.
/// </summary>
// Python: loop.py Agent.request_args, Agent.server_summary, Agent.summarize (the request each builds)
public sealed record MessageRequest
{
    /// <summary>The model asked for.</summary>
    public required string Model { get; init; }

    /// <summary>The output limit; null: not sent (a token count has none).</summary>
    public int? MaxTokens { get; init; }

    /// <summary>The system prompt; null: not sent.</summary>
    public SystemPrompt? System { get; init; }

    /// <summary>The tools; null: not sent.</summary>
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }

    /// <summary>The conversation so far.</summary>
    public required IReadOnlyList<Message> Messages { get; init; }

    /// <summary>Ask the server to write the summary (<c>"compaction": {"type": "summarize"}</c>).</summary>
    public bool ServerCompaction { get; init; }

    /// <summary>Cache the prefix (<c>"cache_control": {"type": "ephemeral"}</c>).</summary>
    public bool CacheControl { get; init; }

    /// <summary>Thinking options; null: not sent (thinking is on by default on other providers).</summary>
    public ThinkingOptions? Thinking { get; init; }

    /// <summary>The effort level (<c>"output_config": {"effort": …}</c>); null: not sent.</summary>
    public string? Effort { get; init; }

    /// <summary>The beta features; the SDK sends them as the <c>anthropic-beta</c> header.</summary>
    public IReadOnlyList<string> Betas { get; init; } = [];

    /// <summary>The server-side fallback setting ("default"); null: none.</summary>
    public string? Fallbacks { get; init; }
}
