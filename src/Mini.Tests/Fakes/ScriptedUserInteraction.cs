namespace Mini.Tests.Fakes;

/// <summary>An <see cref="IUserInteraction"/> that answers from a script and records what it was asked: no terminal.</summary>
public sealed class ScriptedUserInteraction(IEnumerable<string>? answers = null, bool allow = false) : IUserInteraction
{
    readonly Queue<string> _answers = new(answers ?? []);

    /// <summary>Every approval prompt shown.</summary>
    public List<ApprovalPrompt> Prompts { get; } = [];

    /// <summary>Every question asked.</summary>
    public List<UserQuestion> Questions { get; } = [];

    /// <summary>How often a question was about to be asked (the model's open line was closed).</summary>
    public int Begun { get; private set; }

    /// <summary>What <see cref="ConfirmAsync"/> returns; a denial may carry a reason of its own.</summary>
    public ApprovalDecision Confirmation { get; set; } = allow ? ApprovalDecision.Allow : ApprovalDecision.Deny("");

    public Task<ApprovalDecision> ConfirmAsync(ApprovalPrompt prompt, CancellationToken ct)
    {
        Prompts.Add(prompt);
        return Task.FromResult(Confirmation);
    }

    public void BeginQuestion() => Begun++;

    public Task<string> AskAsync(UserQuestion question, CancellationToken ct)
    {
        Questions.Add(question);
        return _answers.Count > 0 ? Task.FromResult(_answers.Dequeue()) : throw new EndOfStreamException("EOF when reading a line");
    }
}
