// Python: session.py list_sessions (the rows it returns)
namespace Mini.Core.Sessions;

/// <summary>One line of /sessions.</summary>
/// <param name="Id">The session id (its file name without the extension).</param>
/// <param name="Turns">The prompts the user typed (the harness's own do not count).</param>
/// <param name="Cost">What the session has cost, in dollars.</param>
/// <param name="Title">The first prompt, on one line and cut to 50 characters.</param>
/// <param name="Updated">When the file last changed, UTC.</param>
public sealed record SessionSummary(string Id, int Turns, double Cost, string Title, DateTime Updated);
