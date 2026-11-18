// Python: loop.py Agent.dispatch (ask_user's question and options)
namespace Mini.Core.Interaction;

/// <summary>A question from the model, with the choices it listed (the user picks a number or types an answer).</summary>
public sealed record UserQuestion(string Text, IReadOnlyList<string> Options);
