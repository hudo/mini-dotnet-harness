namespace Mini.Core.Models;

/// <summary>
/// Collects the parts of a <see cref="MessageRequest"/>. The caller sets what every provider gets; the provider's profile
/// then adds what is its own (<see cref="IProviderProfile.Shape"/>), so no code picks keys by provider.
/// </summary>
// Python: loop.py Agent.request_args (the body it assembles)
public sealed class MessageRequestBuilder
{
    string? _model;
    int? _maxTokens;
    SystemPrompt? _system;
    IReadOnlyList<ToolDefinition>? _tools;
    IReadOnlyList<Message>? _messages;
    bool _serverCompaction, _cacheControl;
    ThinkingOptions? _thinking;
    string? _effort;
    readonly List<string> _betas = [];
    string? _fallbacks;

    public MessageRequestBuilder Model(string model) { _model = model; return this; }

    public MessageRequestBuilder MaxTokens(int maxTokens) { _maxTokens = maxTokens; return this; }

    /// <summary>The system prompt, as one text block.</summary>
    public MessageRequestBuilder System(string text) { _system = new SystemPrompt(text); return this; }

    /// <summary>The system prompt as a bare string (the summary request of a provider without server-side compaction).</summary>
    public MessageRequestBuilder SystemAsString(string text) { _system = new SystemPrompt(text, AsPlainString: true); return this; }

    public MessageRequestBuilder Tools(IEnumerable<ToolDefinition> tools) { _tools = tools.ToList(); return this; }

    public MessageRequestBuilder Messages(IEnumerable<Message> messages) { _messages = messages.ToList(); return this; }

    public MessageRequestBuilder ServerCompaction() { _serverCompaction = true; return this; }

    public MessageRequestBuilder CacheControl() { _cacheControl = true; return this; }

    public MessageRequestBuilder Thinking(ThinkingOptions thinking) { _thinking = thinking; return this; }

    public MessageRequestBuilder Effort(string effort) { _effort = effort; return this; }

    public MessageRequestBuilder Betas(IEnumerable<string> betas) { _betas.AddRange(betas); return this; }

    public MessageRequestBuilder AddBeta(string beta) { _betas.Add(beta); return this; }

    public MessageRequestBuilder Fallbacks(string setting) { _fallbacks = setting; return this; }

    /// <summary>The request. The model and the messages are required.</summary>
    public MessageRequest Build() => new()
    {
        Model = _model ?? throw new InvalidOperationException("a request needs a model"),
        Messages = _messages ?? throw new InvalidOperationException("a request needs messages"),
        MaxTokens = _maxTokens,
        System = _system,
        Tools = _tools,
        ServerCompaction = _serverCompaction,
        CacheControl = _cacheControl,
        Thinking = _thinking,
        Effort = _effort,
        Betas = _betas.ToList(),
        Fallbacks = _fallbacks,
    };
}
