// Python: tools.py Approver.ok (the y/n prompt), loop.py Agent.dispatch (the ask_user prompt)
namespace Mini.Core.Interaction;

/// <summary>Where the program asks the user something. The console implements it; nothing in the approval or tool code touches the terminal.</summary>
public interface IUserInteraction
{
    /// <summary>Shows what is about to happen and asks y/n. A denial may carry its own reason (the action was too tall to show); a plain "no" has none.</summary>
    Task<ApprovalDecision> ConfirmAsync(ApprovalPrompt prompt, CancellationToken ct);

    /// <summary>Closes the model's open line of streamed text, so a question starts on a fresh one.</summary>
    void BeginQuestion();

    /// <summary>Asks the agent's question and returns the user's line, trimmed (possibly empty). End of input throws <see cref="EndOfStreamException"/>.</summary>
    Task<string> AskAsync(UserQuestion question, CancellationToken ct);
}
