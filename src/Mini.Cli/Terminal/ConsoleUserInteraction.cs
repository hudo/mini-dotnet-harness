// Python: tools.py Approver.ok (the prompt), loop.py Agent.dispatch (ask_user)
namespace Mini.Cli.Terminal;

/// <summary>The terminal's answers to <see cref="IUserInteraction"/>.</summary>
public sealed class ConsoleUserInteraction(IAgentOutput ui) : IUserInteraction
{
    // Python: tools.py Approver.ok (the prompt)
    public Task<ApprovalDecision> ConfirmAsync(ApprovalPrompt prompt, CancellationToken ct)
    {
        var kind = prompt.Kind.Wire();
        var write = prompt.Kind == ApprovalKind.Write;
        // Every row carries a marker content can't fake, and nothing taller than a screen is shown at all.
        var (width, height) = GetTerminalSize();
        var cols = Math.Max(width - 6, 20);
        var rows = new List<string>();
        foreach (var r in Printable.Of(prompt.Detail, write).Split('\n'))
        {
            for (var i = 0; i < Math.Max(r.Length, 1); i += cols)
                rows.Add(r.Substring(i, Math.Min(cols, r.Length - i)));
        }

        if (rows.Count > Math.Max(Math.Min(40, height - 3), 10)) // the top must stay on screen
            return Task.FromResult(ApprovalDecision.Deny(" Too long to review on one screen: split it, or write a short file and extend it with edit."));
        var head = Printable.Of(write ? prompt.Detail.Split('\n', 2)[0] : kind); // the path, by the answer
        Console.Out.Write($"\e[!p\e7\e[r\e8\e[0m\e[?7h\e[33m  allow {kind}:"
            + string.Concat(rows.Select(r => $"\n  │ {r}"))
            + $"\n  [{head} · {rows.Count} rows · y/n] \e[0m");
        Console.Out.Flush();
        // shortcut: Console.ReadLine cannot be interrupted, upgrade to cancellable input when immediate prompt cancellation is needed
        var answer = Console.ReadLine();
        ct.ThrowIfCancellationRequested();
        if (answer is null) // Python's input() raises EOFError here
            throw new EndOfStreamException("EOF when reading a line");
        return Task.FromResult(answer.Trim().ToLowerInvariant() is "y" or "yes" ? ApprovalDecision.Allow : ApprovalDecision.Deny(""));
    }

    // Python: loop.py Agent.dispatch (ask_user: ui.end_message())
    public void BeginQuestion() => ui.EndMessage();

    // Python: loop.py Agent.dispatch (ask_user: the prompt and the input() call)
    public Task<string> AskAsync(UserQuestion question, CancellationToken ct)
    {
        var q = question.Text + string.Concat(question.Options.Select((o, i) => $"\n    {i + 1}. {o}"));
        Console.Write($"\u001b[33m  ? {Printable.Control.Replace(q, "").Replace('│', '|')}\n  > \u001b[0m");
        // shortcut: Console.ReadLine cannot be interrupted, so a Ctrl-C only takes effect once a line (or EOF) arrives, and
        // that line is dropped; upgrade to an async stdin read if ask_user must cancel instantly.
        var line = Console.ReadLine();
        ct.ThrowIfCancellationRequested(); // before the answer is recorded or returned: an interrupted ask leaves no answer event
        return Task.FromResult((line ?? throw new EndOfStreamException("EOF when reading a line")).Trim());
    }

    // shutil.get_terminal_size: 80x24 when there is no terminal.
    // Python: tools.py Approver.ok (the shutil.get_terminal_size calls)
    private static TerminalSize GetTerminalSize()
    {
        try { return new(Console.WindowWidth > 0 ? Console.WindowWidth : 80, Console.WindowHeight > 0 ? Console.WindowHeight : 24); }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException) { return new(80, 24); }
    }
}
