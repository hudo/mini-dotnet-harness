// Python: loop.py Agent.request_args, Agent.call, Agent.turn, Agent.run_tools (the calls and results, not the loop)
namespace Mini.Core.Agents;

/// <summary>One user turn: ask the model, run the tools it asks for, and ask again, until it stops asking. Every outcome leaves the
/// transcript valid: a failure or an interrupt is answered with a placeholder, every tool call with a result.</summary>
/// <param name="transcript">The conversation: the one place messages are added.</param>
/// <param name="client">The transport, with the mid-stream retries.</param>
/// <param name="executor">Runs the tool calls of a reply.</param>
/// <param name="usage">Prices and logs each response.</param>
/// <param name="compactor">Compacts a long tool loop before it outgrows the window.</param>
/// <param name="claim">What goal_done claimed; cleared at the start of each turn.</param>
/// <param name="manifest">The frozen model, system prompt and tools every request carries.</param>
/// <param name="profile">The provider's own request shape.</param>
/// <param name="maxTokens">The most a reply may have.</param>
/// <param name="effort">The effort every request asks for.</param>
/// <param name="ui">Where the reply, and what went wrong, are shown.</param>
public sealed class TurnRunner(Transcript transcript, IModelClient client, ToolExecutor executor, UsageTracker usage, Compactor compactor, GoalClaimSlot claim,
    SessionManifest manifest, IProviderProfile profile, int maxTokens, EffortSetting effort, IAgentOutput ui)
{
    // Everything shared is copied by the request's JSON writer: a JsonNode has one parent, and the transcript must stay untouched.
    // Python: loop.py Agent.request_args
    private MessageRequest BuildRequest(IReadOnlyList<Message> paused)
    {
        var request = new MessageRequestBuilder()
            .Model(manifest.Model)
            .MaxTokens(maxTokens)
            .System(manifest.System)
            .Tools(manifest.Tools)
            .Messages(transcript.Messages.Concat(paused))
            .CacheControl()
            .Effort(effort.Value);
        profile.Shape(request); // thinking, betas and fallbacks are Claude's; the other providers add nothing
        return request.Build();
    }

    /// <summary>Stream one response; the client adds the mid-stream retries.</summary>
    // Python: loop.py Agent.call
    private Task<ModelResponse> CallAsync(IReadOnlyList<Message> paused, CancellationToken ct) =>
        client.StreamAsync(BuildRequest(paused), new OutputStreamObserver(ui), ct);

    /// <summary>Run until the model stops asking for tools. Returns the stop reason.</summary>
    // Python: loop.py Agent.turn
    public async Task<StopReason> TurnAsync(string content, bool harness = false, CancellationToken ct = default)
    {
        claim.Clear();
        transcript.Repair(); // closes anything an earlier Ctrl-C left open; only appends
        transcript.AddUser(content, harness);
        var paused = new List<Message>(); // replies that paused on a server tool, committed with the one that completes them
        var cutOffs = 0;
        while (true)
        {
            ModelResponse resp;
            try
            {
                resp = await CallAsync(paused, ct);
            }
            catch (Exception) when (ct.IsCancellationRequested) // the SDK and MCP can wrap cancellation in their own exceptions
            {
                transcript.Placeholder("interrupted by the user");
                ui.Warn("interrupted");
                return StopReason.Interrupted;
            }
            catch (Exception e) // the transcript must stay valid whatever failed
            {
                transcript.Placeholder($"request failed: {e.GetType().Name}");
                ui.Warn($"request failed: {e.Message}");
                return StopReason.Error;
            }
            var stop = resp.StopReason ?? new StopReason("");
            var blocks = FallbackSanitizer.Clean(resp.Content);
            usage.Track(resp);
            if (stop == StopReason.PauseTurn) // a server tool paused; send it back, unchanged, to continue
            {
                paused.Add(Message.Assistant(blocks));
                if (paused.Count < 5) continue;
                transcript.Placeholder("a server tool kept pausing; stopped");
                return StopReason.PauseTurn;
            }
            if (stop == StopReason.Refusal || blocks.Count == 0)
            {
                // a refused partial is discarded; an empty assistant message would 400 every later request
                transcript.Placeholder(blocks.Count == 0 ? $"no content: {stop.Value}" : "declined");
                blocks = []; // nothing of the declined reply is in the transcript, so no tool calls to answer
            }
            else
            {
                transcript.CommitPaused(paused, Message.Assistant(blocks));
            }
            paused = [];
            ui.EndMessage();
            var uses = blocks.OfType<ToolUseBlock>().ToList();
            cutOffs = stop == StopReason.MaxTokens ? cutOffs + 1 : 0; // in a row: any other reply resets it
            var cut = cutOffs is > 0 and <= 2;
            if (uses.Count > 0)
            {
                var run = await executor.RunAsync(uses.Select(ToolCall.From).ToList(), stop, ct);
                transcript.AddUser(run.Results.Cast<ContentBlock>().Concat(cut ? [TextBlock.Create(HarnessPrompts.CutOff["tool"])] : []));
                if (run.Interrupted) return StopReason.Interrupted;
                if (stop == StopReason.ToolUse)
                {
                    if (compactor.ShouldCompact && await compactor.CompactAsync(ct)) // long tool loops must not outgrow the window
                        transcript.AddUser("Context was compacted. Continue the current task from the summary.", true);
                    continue;
                }
            }
            if (stop == StopReason.MaxTokens) // the reply is kept as returned; a note says what happened
            {
                if (cut)
                {
                    ui.Note("the reply hit the output limit; asking the model to go on");
                    if (uses.Count == 0) // with a call, the note went in with its result
                        transcript.AddUser(HarnessPrompts.CutOff[blocks.OfType<TextBlock>().Any(b => b.Text.Trim().Length > 0) ? "text" : "thinking"], true);
                    continue;
                }
                ui.Warn("the reply hit the output limit three times in a row; stopped");
            }
            if (stop == StopReason.Refusal)
                ui.Warn($"refused: {resp.RefusalCategory}");
            return stop;
        }
    }
}
