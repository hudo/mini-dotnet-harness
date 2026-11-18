namespace Mini.Core.Models;

/// <summary>The system prompt of a request. It is sent as one text block, except in the summary request of a provider without server-side compaction, which sends the bare string.</summary>
/// <param name="Text">The frozen prompt of the session.</param>
/// <param name="AsPlainString">Send the text as a string, not as a text block.</param>
// Python: loop.py Agent.request_args, Agent.summarize (the "system" key: a list of one text block, or the plain string)
public sealed record SystemPrompt(string Text, bool AsPlainString = false);
