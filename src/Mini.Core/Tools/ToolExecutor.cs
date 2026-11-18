using System.Text.Json.Nodes;

// Python: loop.py Agent.run_tools, Agent.dispatch (the call by name and the unknown-tool error)
namespace Mini.Core.Tools;

/// <summary>Runs one reply's tool calls and answers every one of them: one tool_result per tool_use, in order, errors flagged,
/// and calls after an interrupt (or after a reply that ended before they were complete) answered with "Not run".</summary>
public sealed class ToolExecutor(ToolRegistry registry, IAgentOutput ui)
{
    /// <summary>`stop` is the response's stop reason: only "tool_use" means the calls are complete.</summary>
    // Python: loop.py Agent.run_tools
    public async Task<ToolRunResult> RunAsync(IReadOnlyList<ToolCall> calls, StopReason stop, CancellationToken ct)
    {
        var results = new List<ToolResultBlock>();
        var interrupted = false;
        foreach (var call in calls)
        {
            ui.ToolCall(call.Name, call.Input);
            ToolOutput output;
            bool err;
            if (stop != StopReason.ToolUse)
            {
                (output, err) = (ToolOutput.Of($"Not run: the response ended ({stop.Value}) before this call was complete."), true);
            }
            else if (interrupted)
            {
                (output, err) = (ToolOutput.Of("Not run: interrupted by the user."), true);
            }
            else
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    (output, err) = (await InvokeAsync(call, ct), false);
                }
                catch (Exception) when (ct.IsCancellationRequested) // any failure while the user is cancelling counts as the interrupt
                {
                    (interrupted, output, err) = (true, ToolOutput.Of("Interrupted by the user."), true);
                }
                catch (ToolException e)
                {
                    (output, err) = (ToolOutput.Of(e.Message), true);
                }
                catch (Exception e)
                {
                    (output, err) = (ToolOutput.Of($"{e.GetType().Name}: {e.Message}"), true);
                }
            }
            ui.ToolResult(output.Summary, err); // a list: read's text and image
            results.Add(output.Text is { } text
                ? ToolResultBlock.Create(call.Id, OutputLimit.Truncate(text), err)
                : ToolResultBlock.Create(call.Id, output.Blocks!, err));
        }
        return new ToolRunResult(results, interrupted);
    }

    /// <summary>Runs one call by name. An unknown name, or a call whose arguments are bad, is a <see cref="ToolException"/>.</summary>
    // Python: loop.py Agent.dispatch
    public Task<ToolOutput> InvokeAsync(ToolCall call, CancellationToken ct) =>
        registry.TryGet(call.Name, out var tool)
            ? tool.InvokeAsync(call, ct) // bad arguments come back as ToolException
            : throw new ToolException($"Tool {call.Name} is not available in this session.");
}
