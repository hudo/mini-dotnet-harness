// Python: loop.py Agent.run_subagent
namespace Mini.Core.Tools.Agentic;

/// <summary>Runs a prompt in a fresh agent with an empty context and returns its final answer.</summary>
public interface ISubagentRunner
{
    Task<string> RunAsync(string prompt, string? model, CancellationToken ct);
}
