// Python: none (C#-only: Python's Agent.dispatch is a getattr on LocalTools; each tool here carries its own definition and behavior)
namespace Mini.Core.Tools;

/// <summary>One thing the model can call. The registry finds it by <see cref="ToolDefinition.Name"/>, so adding a tool never edits a switch.</summary>
public interface ITool
{
    /// <summary>What the model is told about it; frozen into the session's tool list when a session starts.</summary>
    ToolDefinition Definition { get; }

    /// <summary>Runs one call. A failure the model should read is a <see cref="ToolException"/>; anything else is reported by the executor too.</summary>
    Task<ToolOutput> InvokeAsync(ToolCall call, CancellationToken ct);
}
